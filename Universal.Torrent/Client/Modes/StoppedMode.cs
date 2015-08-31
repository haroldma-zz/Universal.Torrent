using Universal.Torrent.Client.Managers;
using Universal.Torrent.Client.PeerConnections;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.Modes
{
	class StoppedMode : Mode
	{
		public override bool CanHashCheck
		{
			get { return true; }
		}
		
		public override TorrentState State
		{
			get { return TorrentState.Stopped; }
		}

		public StoppedMode(TorrentManager manager)
			: base(manager)
		{
			CanAcceptConnections = false;
		}

		public override void HandlePeerConnected(PeerId id, Direction direction)
		{
			id.CloseConnection();
		}


		public override void Tick(int counter)
		{
			// When stopped, do nothing
		}
	}
}
