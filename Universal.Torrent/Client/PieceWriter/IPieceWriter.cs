using Universal.Torrent.Common;

namespace Universal.Torrent.Client.PieceWriter
{
    public interface IPieceWriter
    {
        void Close(TorrentFile file);
        bool Exists(TorrentFile file);
        void Flush(TorrentFile file);
        int Read(TorrentFile file, long offset, byte[] buffer, int bufferOffset, int count);
        void Write(TorrentFile file, long offset, byte[] buffer, int bufferOffset, int count);
    }
}
