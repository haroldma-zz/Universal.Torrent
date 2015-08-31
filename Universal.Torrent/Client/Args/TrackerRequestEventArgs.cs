using System;
using Universal.Torrent.Client.Tracker;

namespace Universal.Torrent.Client.Args
{
    public abstract class TrackerResponseEventArgs : System.EventArgs
    {
        protected TrackerResponseEventArgs(Tracker.Tracker tracker, object state, bool successful)
        {
            if (tracker == null)
                throw new ArgumentNullException(nameof(tracker));
            if (!(state is TrackerConnectionID))
                throw new ArgumentException("The state object must be the same object as in the call to Announce",
                    nameof(state));
            Id = (TrackerConnectionID) state;
            Successful = successful;
            Tracker = tracker;
        }

        internal TrackerConnectionID Id { get; }

        public object State => Id;

        /// <summary>
        ///     True if the request completed successfully
        /// </summary>
        public bool Successful { get; set; }

        /// <summary>
        ///     The tracker which the request was sent to
        /// </summary>
        public Tracker.Tracker Tracker { get; protected set; }
    }
}