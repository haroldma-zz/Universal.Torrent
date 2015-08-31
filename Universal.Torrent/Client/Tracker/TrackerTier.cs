using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;

namespace Universal.Torrent.Client.Tracker
{
    public class TrackerTier : IEnumerable<Tracker>
    {
        #region Constructors

        internal TrackerTier(IEnumerable<string> trackerUrls)
        {
            var trackerList = new List<Tracker>();

            foreach (var trackerUrl in trackerUrls)
            {
                // FIXME: Debug spew?
                Uri result;
                if (!Uri.TryCreate(trackerUrl, UriKind.Absolute, out result))
                {
                    Debug.WriteLine("TrackerTier - Invalid tracker Url specified: {0}", trackerUrl);
                    continue;
                }

                var tracker = TrackerFactory.Create(result);
                if (tracker != null)
                {
                    trackerList.Add(tracker);
                }
                else
                {
                    Debug.WriteLine("Unsupported protocol {0}", result); // FIXME: Debug spew?
                }
            }

            Trackers = trackerList;
        }

        #endregion Constructors

        #region Private Fields

        #endregion Private Fields

        #region Properties

        public bool SendingStartedEvent { get; set; }

        public bool SentStartedEvent { get; set; }

        public List<Tracker> Trackers { get; }

        #endregion Properties

        #region Methods

        internal int IndexOf(Tracker tracker)
        {
            return Trackers.IndexOf(tracker);
        }

        public IEnumerator<Tracker> GetEnumerator()
        {
            return Trackers.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public List<Tracker> GetTrackers()
        {
            return new List<Tracker>(Trackers);
        }

        #endregion Methods
    }
}