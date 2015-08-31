using Universal.Torrent.Client.Managers;
using Universal.Torrent.Client.PeerConnections;

namespace Universal.Torrent.Client.Args
{
    public class BlockEventArgs : TorrentEventArgs
    {
        #region Private Fields

        private Block _block;

        #endregion

        #region Public Properties

        /// <summary>
        ///     The block whose state changed
        /// </summary>
        public Block Block => _block;


        /// <summary>
        ///     The piece that the block belongs too
        /// </summary>
        public Piece Piece { get; private set; }


        /// <summary>
        ///     The peer who the block has been requested off
        /// </summary>
        public PeerId ID { get; private set; }

        #endregion

        #region Constructors

        /// <summary>
        ///     Creates a new PeerMessageEventArgs
        /// </summary>
        /// <param name="manager">The manager.</param>
        /// <param name="block">The block.</param>
        /// <param name="piece">The piece.</param>
        /// <param name="id">The identifier.</param>
        internal BlockEventArgs(TorrentManager manager, Block block, Piece piece, PeerId id)
            : base(manager)
        {
            Init(block, piece, id);
        }

        private void Init(Block block, Piece piece, PeerId id)
        {
            _block = block;
            ID = id;
            Piece = piece;
        }

        #endregion

        #region Methods

        public override bool Equals(object obj)
        {
            var args = obj as BlockEventArgs;
            return (args != null) && (Piece.Equals(args.Piece)
                                      && ID.Equals(args.ID)
                                      && _block.Equals(args._block));
        }

        public override int GetHashCode()
        {
            return _block.GetHashCode();
        }

        #endregion Methods
    }
}