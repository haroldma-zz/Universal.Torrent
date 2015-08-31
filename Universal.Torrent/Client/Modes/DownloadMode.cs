using System.Linq;
using Universal.Torrent.Client.Args;
using Universal.Torrent.Client.Managers;
using Universal.Torrent.Client.PeerConnections;
using Universal.Torrent.Client.Peers;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.Modes
{
    internal class DownloadMode : Mode
    {
        private TorrentState state;

        public DownloadMode(TorrentManager manager)
            : base(manager)
        {
            state = manager.Complete ? TorrentState.Seeding : TorrentState.Downloading;
        }

        public override TorrentState State => state;

        public override void HandlePeerConnected(PeerId id, Direction direction)
        {
            if (!ShouldConnect(id))
                id.CloseConnection();
            base.HandlePeerConnected(id, direction);
        }

        public override bool ShouldConnect(Peer peer)
        {
            return !(peer.IsSeeder && Manager.HasMetadata && Manager.Complete);
        }

        public override void Tick(int counter)
        {
            //If download is complete, set state to 'Seeding'
            if (Manager.Complete && state == TorrentState.Downloading)
            {
                state = TorrentState.Seeding;
                Manager.RaiseTorrentStateChanged(new TorrentStateChangedEventArgs(Manager, TorrentState.Downloading,
                    TorrentState.Seeding));
                Manager.TrackerManager.Announce(TorrentEvent.Completed);
            }
            foreach (var t in Manager.Peers.ConnectedPeers.Where(t => !ShouldConnect(t)))
                t.CloseConnection();
            base.Tick(counter);
        }
    }
}