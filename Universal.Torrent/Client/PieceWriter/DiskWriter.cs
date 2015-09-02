using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage;
using Windows.Storage.Streams;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.PieceWriter
{
    public class DiskWriter : PieceWriter
    {
        private readonly FileStreamBuffer _streamsBuffer;

        public DiskWriter()
            : this(512)
        {
        }

        public DiskWriter(int maxOpenFiles)
        {
            _streamsBuffer = new FileStreamBuffer(maxOpenFiles);
        }

        public override void Close(TorrentFile file)
        {
            _streamsBuffer.CloseStream(file.FullPath);
        }

        public override void CancelOperations(TorrentFile file)
        {
            _streamsBuffer.CancelTorrentFile(file);
        }

        public override void Dispose()
        {
            _streamsBuffer.Dispose();
            base.Dispose();
        }

        internal TorrentFileStream GetStream(TorrentFile file, FileAccessMode access)
        {
            if (access == FileAccessMode.Read && !Exists(file))
                return null;
            return _streamsBuffer.GetStream(file, access);
        }

        public override void Move(TorrentFile file, StorageFolder newRoot, bool ignoreExisting)
        {
            Check.File(file);
            Check.SaveFolder(newRoot);
            if (file.TargetFolder == newRoot)
                return;
            try
            {
                Flush(file);
                _streamsBuffer.Move(file, newRoot, ignoreExisting);
            }
            catch (Exception ex)
            {
                if (!(ex.InnerException is FileNotFoundException))
                {
                    if (ex.InnerException.HResult != -2147467259)
                        throw;
                }
            }
            file.TargetFolder = newRoot;
        }

        public override int Read(TorrentFile file, long offset, byte[] buffer, int bufferOffset, int count)
        {
            Check.File(file);
            Check.Buffer(buffer);
            if (offset < 0L || offset + count > file.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            var s = GetStream(file, FileAccessMode.Read);
            if (s == null || s.Size < (ulong) offset + (ulong) count)
                return 0;

            s.Seek((ulong) offset);
            var buff = buffer.AsBuffer(bufferOffset, count);
            s.ReadAsync(buff, (uint) count, InputStreamOptions.Partial).AsTask().Wait();
            return count;
        }

        public override void Write(TorrentFile file, long offset, byte[] buffer, int bufferOffset, int count)
        {
            Check.File(file);
            Check.Buffer(buffer);
            if (offset < 0L || offset + count > file.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            var s = GetStream(file, FileAccessMode.ReadWrite);
            s.Seek((ulong) offset);
            s.WriteAsync(buffer.AsBuffer(bufferOffset, count)).AsTask().Wait();
        }

        public override bool Exists(TorrentFile file)
        {
            if (!file.Exists.HasValue)
                file.Exists = InternalExists(file);
            return file.Exists.Value;
        }

        internal bool InternalExists(TorrentFile file)
        {
            try
            {
                return File.Exists(Path.Combine(file.TargetFolder.Path, file.FullPath));
            }
            catch
            {
                return false;
            }
        }

        public override void Flush(TorrentFile file)
        {
            var s = _streamsBuffer.FindStream(file.FullPath);
            if (s == null)
                return;
            if (!s.CanWrite)
                return;
            s.FlushAsync().AsTask().Wait();
        }
    }
}