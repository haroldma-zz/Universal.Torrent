using System.Collections.Generic;
using Universal.Torrent.Client.Managers;
using Universal.Torrent.Client.PeerConnections;
using Universal.Torrent.Common;

// ReSharper disable once CheckNamespace
namespace Universal.Torrent.Client
{
    public partial class DiskManager
    {
        public class BufferedIO : ICacheable
        {
            internal byte[] InternalBuffer;

            public int ActualCount { get; set; }

            public int BlockIndex => PieceOffset/Piece.BlockSize;

            public byte[] Buffer => InternalBuffer;

            internal DiskIOCallback Callback { get; set; }

            public int Count { get; set; }

            internal PeerId Id { get; set; }

            public int PieceIndex => (int) (Offset/PieceLength);

            public int PieceLength { get; private set; }

            public int PieceOffset => (int) (Offset%PieceLength);

            public long Offset { get; set; }

            public IList<TorrentFile> Files { get; private set; }

            internal TorrentManager Manager { get; private set; }

            public bool Complete { get; set; }

            public void Initialise()
            {
                Initialise(null, BufferManager.EmptyBuffer, 0, 0, 0, null);
            }

            public void Initialise(TorrentManager manager, byte[] buffer, long offset, int count, int pieceLength,
                IList<TorrentFile> files)
            {
                ActualCount = 0;
                InternalBuffer = buffer;
                Callback = null;
                Complete = false;
                Count = count;
                Files = files;
                Manager = manager;
                Offset = offset;
                Id = null;
                PieceLength = pieceLength;
            }

            public override string ToString()
            {
                return string.Format("Piece: {0} Block: {1} Count: {2}", PieceIndex, BlockIndex, Count);
            }
        }
    }
}