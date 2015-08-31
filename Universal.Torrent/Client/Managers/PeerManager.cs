using System.Collections.Generic;
using System.Linq;
using Universal.Torrent.Client.PeerConnections;
using Universal.Torrent.Client.Peers;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.Managers
{
    public class PeerManager
    {
        #region Constructors

        public PeerManager()
        {
            ActivePeers = new List<Peer>();
            AvailablePeers = new List<Peer>();
            BannedPeers = new List<Peer>();
            BusyPeers = new List<Peer>();
        }

        #endregion Constructors

        #region Member Variables

        internal List<PeerId> ConnectedPeers = new List<PeerId>();
        internal List<Peer> ConnectingToPeers = new List<Peer>();

        internal List<Peer> ActivePeers;
        internal List<Peer> AvailablePeers;
        internal List<Peer> BannedPeers;
        internal List<Peer> BusyPeers;

        #endregion Member Variables

        #region Properties

        public int Available => AvailablePeers.Count;

        /// <summary>
        ///     Returns the number of Leechs we are currently connected to
        /// </summary>
        /// <returns></returns>
        public int Leechs
        {
            get
            {
                return
                    (int)
                        ClientEngine.MainLoop.QueueWait(
                                delegate
                                {
                                    return Toolbox.Count(ActivePeers, p => !p.IsSeeder);
                                });
            }
        }

        /// <summary>
        ///     Returns the number of Seeds we are currently connected to
        /// </summary>
        /// <returns></returns>
        public int Seeds
        {
            get
            {
                return
                    (int)
                        ClientEngine.MainLoop.QueueWait(
                                delegate { return Toolbox.Count(ActivePeers, p => p.IsSeeder); });
            }
        }

        #endregion

        #region Methods

        internal IEnumerable<Peer> AllPeers()
        {
            foreach (var t in AvailablePeers)
                yield return t;

            foreach (var t in ActivePeers)
                yield return t;

            foreach (var t in BannedPeers)
                yield return t;

            foreach (var t in BusyPeers)
                yield return t;
        }

        internal void ClearAll()
        {
            ActivePeers.Clear();
            AvailablePeers.Clear();
            BannedPeers.Clear();
            BusyPeers.Clear();
        }

        internal bool Contains(Peer peer)
        {
            return AllPeers().Any(peer.Equals);
        }

        #endregion Methods
    }
}