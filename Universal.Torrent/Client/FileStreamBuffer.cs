using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Streams;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client
{
    internal class FileStreamBuffer : IDisposable
    {
        private readonly Dictionary<string, AsynTokens> _dictionary;
        private readonly object _locker = new object();
        // A list of currently open filestreams. Note: The least recently used is at position 0
        // The most recently used is at the last position in the array
        private readonly int _maxStreams;
        private readonly List<TorrentFileStream> _streams;


        public FileStreamBuffer(int maxStreams)
        {
            _maxStreams = maxStreams;
            _streams = new List<TorrentFileStream>(maxStreams);
            _dictionary = new Dictionary<string, AsynTokens>();
        }

        public int Count => _streams.Count;

        #region IDisposable Members

        public void Dispose()
        {
            lock (_locker)
            {
                _streams.ForEach(delegate(TorrentFileStream s) { s.Dispose(); });
                _streams.Clear();
            }
        }

        #endregion

        private void Add(TorrentFileStream stream)
        {
            lock (_locker)
            {
                Debug.WriteLine("Opening filestream: {0}", stream.Path);

                // If we have our maximum number of streams open, just dispose and dump the least recently used one
                if (_maxStreams != 0 && _streams.Count >= _streams.Capacity)
                {
                    Debug.WriteLine("We've reached capacity: {0}", _streams.Count);
                    var first = _streams.FirstOrDefault(p => p.TorrentFile.Priority != Priority.Immediate);
                    if (first != null)
                        CloseAndRemove(first);
                }
                _streams.Add(stream);
            }
        }

        public TorrentFileStream FindStream(string path)
        {
            lock (_locker)
            {
                return _streams.FirstOrDefault(p => p.Path == path);
            }
        }

        internal TorrentFileStream GetStream(TorrentFile file, FileAccessMode access)
        {
            var fullPath = file.FullPath;
            var asyncTokens = GetAsyncTokens(fullPath);
            try
            {
                asyncTokens.CancellationTokenSource.Token.ThrowIfCancellationRequested();
                var s = FindStream(fullPath);
                if (s != null)
                {
                    // If we are requesting write access and the current stream does not have it
                    if (access == FileAccessMode.ReadWrite && !s.CanWrite)
                    {
                        Debug.WriteLine("Didn't have write permission - reopening");
                        CloseAndRemove(s);
                    }
                    else
                    {
                        lock (_locker)
                        {
                            // Place the filestream at the end so we know it's been recently used
                            _streams.Remove(s);
                            _streams.Add(s);
                        }
                        return s;
                    }
                }

                try
                {
                    var result = OpenStreamAsync(file, access, asyncTokens).Result;
                    file.Exists = true;
                    s = new TorrentFileStream(file, result)
                    {
                        Size = (ulong) file.Length
                    };
                    Add(s);
                }
                catch (AggregateException ex)
                {
                    if (ex.InnerException is OperationCanceledException ||
                        ex.InnerException is UnauthorizedAccessException)
                        throw ex.InnerException;
                    throw;
                }
                return s;
            }
            finally
            {
                if (asyncTokens != null)
                {
                    asyncTokens.SemaphoreSlim.Release();
                    if (asyncTokens.CancellationTokenSource.IsCancellationRequested)
                        Clear(fullPath);
                }
            }
        }

        internal void CancelTorrentFile(TorrentFile file)
        {
            lock (_locker)
            {
                if (file == null)
                {
                    foreach (var asynTokens in _dictionary.Values)
                        asynTokens.CancellationTokenSource.Cancel(true);
                }
                else
                {
                    AsynTokens asynTokens;
                    if (!_dictionary.TryGetValue(file.FullPath, out asynTokens))
                        return;
                    asynTokens.CancellationTokenSource.Cancel(true);
                }
            }
        }

        internal void Move(TorrentFile file, StorageFolder folder, bool ignoreExisting)
        {
            var asyncTokens = GetAsyncTokens(file.FullPath);
            try
            {
                CloseStream(file.FullPath);
                var result = file.TargetFolder.GetFileAsync(file.FullPath).AsTask().Result;
                var directoryName = Path.GetDirectoryName(file.FullPath);
                char[] separator = {'\\'};
                folder = directoryName.Split(separator, StringSplitOptions.RemoveEmptyEntries)
                    .Aggregate(folder,
                        (current, desiredName) =>
                            current.CreateFolderAsync(desiredName, CreationCollisionOption.OpenIfExists).AsTask().Result);
                result.MoveAsync(folder, result.Name,
                    ignoreExisting ? NameCollisionOption.ReplaceExisting : NameCollisionOption.FailIfExists).AsTask().Wait();
            }
            finally
            {
                asyncTokens?.SemaphoreSlim.Release();
            }
        }

        internal bool Clear(string path)
        {
            var asyncTokens = GetAsyncTokens(path);
            try
            {
                var stream = FindStream(path);
                if (stream != null)
                    CloseAndRemove(stream);
                return stream != null;
            }
            finally
            {
                if (asyncTokens != null)
                {
                    asyncTokens.SemaphoreSlim.Release();
                    CloseAndRemove(path);
                }
            }
        }


        private void CloseAndRemove(string path)
        {
            lock (_locker)
            {
                AsynTokens asynTokens;
                if (!_dictionary.TryGetValue(path, out asynTokens))
                    return;
                _dictionary.Remove(path);
                asynTokens.Dispose();
            }
        }

        private AsynTokens GetAsyncTokens(string path)
        {
            AsynTokens asynTokens;
            lock (_locker)
            {
                if (!_dictionary.TryGetValue(path, out asynTokens))
                {
                    asynTokens = new AsynTokens();
                    _dictionary.Add(path, asynTokens);
                }
            }

            asynTokens.SemaphoreSlim.Wait();
            return asynTokens;
        }

        private Task<IRandomAccessStream> OpenStreamAsync(TorrentFile file, FileAccessMode access, AsynTokens asynTokens)
        {
            return Task.Run(async () =>
            {
                var token = asynTokens.CancellationTokenSource.Token;
                var fullPath = file.FullPath;
                var storageFile = await StorageHelper.CreateFileAsync(fullPath, file.TargetFolder);
                if (access == FileAccessMode.ReadWrite)
                {
                    var stream = File.Open(storageFile.Path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
                    var randomAccessStream = stream.AsRandomAccessStream();
                    try
                    {
                        var size = (long) randomAccessStream.Size;
                        var length = file.Length - size;
                        if (length > 0L)
                        {
                            var buffer = ClientEngine.BufferManager.GetBuffer((int) Math.Min(length, 524288L));
                            try
                            {
                                randomAccessStream.Seek((ulong) size);
                                for (var i = size;
                                    i < file.Length;
                                    i = i + (long) buffer.Length)
                                {
                                    length = length - await randomAccessStream.WriteAsync(
                                        buffer.AsBuffer(0,
                                            (int) Math.Min(length, buffer.Length)));
                                    token.ThrowIfCancellationRequested();
                                }
                            }
                            finally
                            {
                                ClientEngine.BufferManager.FreeBuffer(ref buffer);
                            }
                        }
                    }
                    finally
                    {
                        randomAccessStream?.Dispose();
                    }
                }
                return await storageFile.OpenAsync(access);
            });
        }

        internal bool CloseStream(string path)
        {
            var s = FindStream(path);
            if (s != null)
                CloseAndRemove(s);

            return s != null;
        }


        private void CloseAndRemove(TorrentFileStream s)
        {
            lock (_locker)
            {
                Debug.WriteLine("Closing and removing: {0}", s.Path);
                if (_streams.Remove(s))
                    s.Dispose();
            }
        }


        private class AsynTokens : IDisposable
        {
            public readonly CancellationTokenSource CancellationTokenSource;
            public readonly SemaphoreSlim SemaphoreSlim;

            public AsynTokens()
            {
                SemaphoreSlim = new SemaphoreSlim(1, 1);
                CancellationTokenSource = new CancellationTokenSource();
            }

            public void Dispose()
            {
                CancellationTokenSource.Dispose();
                SemaphoreSlim.Dispose();
            }
        }
    }
}