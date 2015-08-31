using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using Windows.Security.Cryptography.Core;
using Windows.Storage;
using Universal.Torrent.Client.Managers;
using Universal.Torrent.Client.Modes;
using Universal.Torrent.Client.RateLimiters;
using Universal.Torrent.Common;

// ReSharper disable once CheckNamespace

namespace Universal.Torrent.Client
{
    public delegate void DiskIOCallback(bool successful);

    public partial class DiskManager : IDisposable
    {
        private static readonly MainLoop IoLoop = new MainLoop();

        #region Constructors

        internal DiskManager(ClientEngine engine, PieceWriter.PieceWriter writer)
        {
            _bufferedReads = new Queue<BufferedIO>();
            _bufferedWrites = new Queue<BufferedIO>();
            _cache = new Cache<BufferedIO>(true).Synchronize();
            _engine = engine;
            ReadLimiter = new RateLimiter();
            _readMonitor = new SpeedMonitor();
            _writeMonitor = new SpeedMonitor();
            WriteLimiter = new RateLimiter();
            Writer = writer;

            _loopTask = delegate
            {
                if (Disposed)
                    return;

                while (this._bufferedWrites.Count > 0 &&
                       WriteLimiter.TryProcess(_bufferedWrites.Peek().InternalBuffer.Length/2048))
                {
                    BufferedIO write;
                    lock (_bufferLock)
                        write = this._bufferedWrites.Dequeue();
                    try
                    {
                        PerformWrite(write);
                        _cache.Enqueue(write);
                    }
                    catch (Exception ex)
                    {
                        if (write.Manager != null)
                            SetError(write.Manager, Reason.WriteFailure, ex);
                    }
                }

                while (this._bufferedReads.Count > 0 && ReadLimiter.TryProcess(_bufferedReads.Peek().Count/2048))
                {
                    BufferedIO read;
                    lock (_bufferLock)
                        read = this._bufferedReads.Dequeue();

                    try
                    {
                        PerformRead(read);
                        _cache.Enqueue(read);
                    }
                    catch (Exception ex)
                    {
                        if (read.Manager != null)
                            SetError(read.Manager, Reason.ReadFailure, ex);
                    }
                }
            };

            IoLoop.QueueTimeout(TimeSpan.FromSeconds(1), delegate
            {
                if (Disposed)
                    return false;

                _readMonitor.Tick();
                _writeMonitor.Tick();
                _loopTask();
                return true;
            });
        }

        #endregion Constructors

        internal void MoveFile(TorrentManager manager, TorrentFile file, StorageFolder path)
        {
            IoLoop.QueueWait(delegate
            {
                try
                {
                    Writer.Move(file, path, false);
                    file.TargetFolder = path;
                }
                catch (Exception ex)
                {
                    SetError(manager, Reason.WriteFailure, ex);
                }
            });
        }

        internal void MoveFiles(TorrentManager manager, StorageFolder newRoot, bool overWriteExisting)
        {
            IoLoop.QueueWait(delegate
            {
                try
                {
                    Writer.Move(newRoot, manager.Torrent.Files, overWriteExisting);
                }
                catch (Exception ex)
                {
                    SetError(manager, Reason.WriteFailure, ex);
                }
            });
        }

        #region Member Variables

        private readonly object _bufferLock = new object();
        private readonly Queue<BufferedIO> _bufferedReads;
        private readonly Queue<BufferedIO> _bufferedWrites;
        private readonly ICache<BufferedIO> _cache;
        private readonly ClientEngine _engine;
        private readonly MainLoopTask _loopTask;

        private readonly SpeedMonitor _readMonitor;
        private readonly SpeedMonitor _writeMonitor;

        internal RateLimiter ReadLimiter;
        internal RateLimiter WriteLimiter;

        #endregion Member Variables

        #region Properties

        public bool Disposed { get; private set; }

        public int QueuedWrites => _bufferedWrites.Count;

        public int ReadRate => _readMonitor.Rate;

        public int WriteRate => _writeMonitor.Rate;

        public long TotalRead => _readMonitor.Total;

        public long TotalWritten => _writeMonitor.Total;

        internal PieceWriter.PieceWriter Writer { get; set; }

        #endregion Properties

        #region Methods

        internal WaitHandle CloseFileStreams(TorrentManager manager)
        {
            var handle = new ManualResetEvent(false);

            IoLoop.Queue(delegate
            {
                // Process all pending reads/writes then close any open streams
                try
                {
                    _loopTask();
                    Writer.Close(manager.Torrent.Files);
                }
                catch (Exception ex)
                {
                    SetError(manager, Reason.WriteFailure, ex);
                }
                finally
                {
                    handle.Set();
                }
            });

            return handle;
        }

        public void Dispose()
        {
            if (Disposed)
                return;

            Disposed = true;
            // FIXME: Ensure everything is written to disk before killing the mainloop.
            IoLoop.QueueWait((MainLoopTask) Writer.Dispose);
        }

        public void Flush()
        {
            IoLoop.QueueWait(delegate
            {
                foreach (var manager in _engine.Torrents)
                    Writer.Flush(manager.Torrent.Files);
            });
        }

        public void Flush(TorrentManager manager)
        {
            Check.Manager(manager);
            IoLoop.QueueWait(delegate { Writer.Flush(manager.Torrent.Files); });
        }

        private void PerformWrite(BufferedIO io)
        {
            // Find the block that this data belongs to and set it's state to "Written"
            var index = io.PieceOffset/Piece.BlockSize;
            try
            {
                // Perform the actual write
                Writer.Write(io.Files, io.Offset, io.InternalBuffer, 0, io.Count, io.PieceLength,
                    io.Manager.Torrent.Size);
                _writeMonitor.AddDelta(io.Count);
            }
            finally
            {
                io.Complete = true;
                io.Callback?.Invoke(true);
            }
        }

        private void PerformRead(BufferedIO io)
        {
            try
            {
                io.ActualCount = Writer.Read(io.Files, io.Offset, io.InternalBuffer, 0, io.Count, io.PieceLength,
                    io.Manager.Torrent.Size)
                    ? io.Count
                    : 0;
                _readMonitor.AddDelta(io.ActualCount);
            }
            finally
            {
                io.Complete = true;
                io.Callback?.Invoke(io.ActualCount == io.Count);
            }
        }

        internal void QueueFlush(TorrentManager manager, int index)
        {
            IoLoop.Queue(delegate
            {
                try
                {
                    foreach (
                        var file in
                            manager.Torrent.Files.Where(
                                file => file.StartPieceIndex >= index && file.EndPieceIndex <= index))
                        Writer.Flush(file);
                }
                catch (Exception ex)
                {
                    SetError(manager, Reason.WriteFailure, ex);
                }
            });
        }

        internal void QueueRead(TorrentManager manager, long offset, byte[] buffer, int count, DiskIOCallback callback)
        {
            var io = _cache.Dequeue();
            io.Initialise(manager, buffer, offset, count, manager.Torrent.PieceLength, manager.Torrent.Files);
            QueueRead(io, callback);
        }

        private void QueueRead(BufferedIO io, DiskIOCallback callback)
        {
            io.Callback = callback;
            if (IoLoop.IsInCurrentThread())
            {
                PerformRead(io);
                _cache.Enqueue(io);
            }
            else
                lock (_bufferLock)
                {
                    _bufferedReads.Enqueue(io);
                    if (_bufferedReads.Count == 1)
                        IoLoop.Queue(_loopTask);
                }
        }

        internal void QueueWrite(TorrentManager manager, long offset, byte[] buffer, int count, DiskIOCallback callback)
        {
            var io = _cache.Dequeue();
            io.Initialise(manager, buffer, offset, count, manager.Torrent.PieceLength, manager.Torrent.Files);
            QueueWrite(io, callback);
        }

        private void QueueWrite(BufferedIO io, DiskIOCallback callback)
        {
            io.Callback = callback;
            if (IoLoop.IsInCurrentThread())
            {
                PerformWrite(io);
                _cache.Enqueue(io);
            }
            else
                lock (_bufferLock)
                {
                    _bufferedWrites.Enqueue(io);
                    if (_bufferedWrites.Count == 1)
                        IoLoop.Queue(_loopTask);
                }
        }

        internal bool CheckAnyFilesExist(TorrentManager manager)
        {
            var result = false;
            IoLoop.QueueWait(delegate
            {
                try
                {
                    for (var i = 0; i < manager.Torrent.Files.Length && !result; i++)
                        result = Writer.Exists(manager.Torrent.Files[i]);
                }
                catch (Exception ex)
                {
                    SetError(manager, Reason.ReadFailure, ex);
                }
            });
            return result;
        }

        internal bool CheckFileExists(TorrentManager manager, TorrentFile file)
        {
            var result = false;
            IoLoop.QueueWait(delegate
            {
                try
                {
                    result = Writer.Exists(file);
                }
                catch (Exception ex)
                {
                    SetError(manager, Reason.ReadFailure, ex);
                }
            });
            return result;
        }

        private void SetError(TorrentManager manager, Reason reason, Exception ex)
        {
            ClientEngine.MainLoop.Queue(delegate
            {
                if (manager.Mode is ErrorMode)
                    return;

                manager.Error = new Error(reason, ex);
                manager.Mode = new ErrorMode(manager);
            });
        }

        internal void BeginGetHash(TorrentManager manager, int pieceIndex, MainLoopResult callback)
        {
            var count = 0;
            var offset = (long) manager.Torrent.PieceLength*pieceIndex;
            var endOffset = Math.Min(offset + manager.Torrent.PieceLength, manager.Torrent.Size);

            var hashBuffer = BufferManager.EmptyBuffer;
            ClientEngine.BufferManager.GetBuffer(ref hashBuffer, Piece.BlockSize);

            var hashProvider = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Sha1);
            var hasher = hashProvider.CreateHash();

            DiskIOCallback readCallback = null;
            readCallback = delegate(bool successful)
            {
                // TODO [uwp]: make sure this works
                if (successful)
                    hasher.Append(hashBuffer.AsBuffer(0, count));
                offset += count;

                if (!successful || offset == endOffset)
                {
                    object hash = null;
                    if (successful)
                    {
                        hash = hasher.GetValueAndReset().ToArray();
                    }
                    ClientEngine.BufferManager.FreeBuffer(ref hashBuffer);
                    ClientEngine.MainLoop.Queue(delegate { callback(hash); });
                }
                else
                {
                    count = (int) Math.Min(Piece.BlockSize, endOffset - offset);
                    QueueRead(manager, offset, hashBuffer, count, readCallback);
                }
            };

            count = (int) Math.Min(Piece.BlockSize, endOffset - offset);
            QueueRead(manager, offset, hashBuffer, count, readCallback);
        }

        #endregion
    }
}