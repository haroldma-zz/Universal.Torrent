using Universal.Torrent.Client.Managers;

namespace Universal.Torrent.Client.Args
{
    public class TorrentEventArgs : System.EventArgs
    {
        public TorrentEventArgs(TorrentManager manager)
        {
            TorrentManager = manager;
        }


        public TorrentManager TorrentManager { get; protected set; }
    }
}