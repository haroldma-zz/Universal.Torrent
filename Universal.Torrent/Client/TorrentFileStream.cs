using Windows.Foundation;
using Windows.Storage.Streams;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client
{
    internal class TorrentFileStream : IRandomAccessStream
    {
        private readonly IRandomAccessStream _randomAccessStream;

        public TorrentFileStream(TorrentFile torrentFile, IRandomAccessStream randomAccessStream)
        {
            TorrentFile = torrentFile;
            _randomAccessStream = randomAccessStream;
        }

        public TorrentFile TorrentFile { get; }

        public string Path => TorrentFile.FullPath;

        public void Dispose()
        {
            _randomAccessStream.Dispose();
        }

        public IAsyncOperation<bool> FlushAsync()
        {
            return _randomAccessStream.FlushAsync();
        }

        public IAsyncOperationWithProgress<uint, uint> WriteAsync(IBuffer buffer)
        {
            return _randomAccessStream.WriteAsync(buffer);
        }

        public bool CanRead => _randomAccessStream.CanRead;

        public bool CanWrite => _randomAccessStream.CanWrite;

        public ulong Position => _randomAccessStream.Position;

        public ulong Size
        {
            get { return _randomAccessStream.Size; }
            set { _randomAccessStream.Size = value; }
        }


        public IRandomAccessStream CloneStream()
        {
            return _randomAccessStream.CloneStream();
        }

        public IInputStream GetInputStreamAt(ulong position)
        {
            return _randomAccessStream.GetInputStreamAt(position);
        }

        public IOutputStream GetOutputStreamAt(ulong position)
        {
            return _randomAccessStream.GetOutputStreamAt(position);
        }

        public void Seek(ulong position)
        {
            _randomAccessStream.Seek(position);
        }

        public IAsyncOperationWithProgress<IBuffer, uint> ReadAsync(IBuffer buffer, uint count,
            InputStreamOptions options)
        {
            return _randomAccessStream.ReadAsync(buffer, count, options);
        }
    }
}