using Universal.Torrent.Client.Peers;

namespace Universal.Torrent.Client.Args
{
    public class AttemptConnectionEventArgs : System.EventArgs
    {
        public AttemptConnectionEventArgs(Peer peer)
        {
            Peer = peer;
        }

        public bool BanPeer { get; set; }

        public Peer Peer { get; }
    }
}