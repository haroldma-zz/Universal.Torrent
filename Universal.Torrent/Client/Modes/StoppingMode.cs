using System.Linq;
using Universal.Torrent.Client.Managers;
using Universal.Torrent.Client.PeerConnections;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.Modes
{
    internal class StoppingMode : Mode
    {
        private readonly WaitHandleGroup _handle = new WaitHandleGroup();

        public StoppingMode(TorrentManager manager)
            : base(manager)
        {
            CanAcceptConnections = false;
            var engine = manager.Engine;
            if (manager.Mode is HashingMode)
                _handle.AddHandle(((HashingMode) manager.Mode).HashingWaitHandle, "Hashing");

            if (manager.TrackerManager.CurrentTracker != null &&
                manager.TrackerManager.CurrentTracker.Status == TrackerState.Ok)
                _handle.AddHandle(manager.TrackerManager.Announce(TorrentEvent.Stopped), "Announcing");

            foreach (var id in manager.Peers.ConnectedPeers.Where(id => id.Connection != null))
                id.Connection.Dispose();

            manager.Peers.ClearAll();

            _handle.AddHandle(engine.DiskManager.CloseFileStreams(manager), "DiskManager");

            manager.Monitor.Reset();
            manager.PieceManager.Reset();
            engine.ConnectionManager.CancelPendingConnects(manager);
            engine.Stop();
        }

        public override TorrentState State => TorrentState.Stopping;

        public override void HandlePeerConnected(PeerId id, Direction direction)
        {
            id.CloseConnection();
        }

        public override void Tick(int counter)
        {
            if (_handle.WaitOne(0))
            {
                _handle.Dispose();
                Manager.Mode = new StoppedMode(Manager);
            }
        }
    }
}