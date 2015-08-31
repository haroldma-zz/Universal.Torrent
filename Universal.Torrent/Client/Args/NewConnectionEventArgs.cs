using System;
using Universal.Torrent.Client.Managers;
using Universal.Torrent.Client.PeerConnections;
using Universal.Torrent.Client.Peers;

namespace Universal.Torrent.Client.Args
{
    public class NewConnectionEventArgs : TorrentEventArgs
    {
        private IConnection connection;
        private Peer peer;
        public IConnection Connection
        {
            get { return connection; }
        }

        public Peer Peer
        {
            get { return peer; }
        }

        public NewConnectionEventArgs(Peer peer, IConnection connection, TorrentManager manager)
            : base(manager)
        {
            if (!connection.IsIncoming && manager == null)
                throw new InvalidOperationException("An outgoing connection must specify the torrent manager it belongs to");

            this.connection = connection;
            this.peer = peer;
        }
    }
}
