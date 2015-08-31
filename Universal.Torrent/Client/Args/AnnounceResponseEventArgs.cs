using System.Collections.Generic;
using Universal.Torrent.Client.Peers;

namespace Universal.Torrent.Client.Args
{
    public class AnnounceResponseEventArgs : TrackerResponseEventArgs
    {
        public AnnounceResponseEventArgs(Tracker.Tracker tracker, object state, bool successful)
            : this(tracker, state, successful, new List<Peer>())
        {
        }

        public AnnounceResponseEventArgs(Tracker.Tracker tracker, object state, bool successful, List<Peer> peers)
            : base(tracker, state, successful)
        {
            Peers = peers;
        }

        public List<Peer> Peers { get; }
    }
}