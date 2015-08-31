//
// TrackerManager.cs
//
// Authors:
//   Alan McGovern alan.mcgovern@gmail.com
//
// Copyright (C) 2006 Alan McGovern
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//


using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Threading;
using Universal.Torrent.Client.Args;
using Universal.Torrent.Client.Encryption;
using Universal.Torrent.Client.Tracker;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.Managers
{
    /// <summary>
    ///     Represents the connection to a tracker that an TorrentManager has
    /// </summary>
    public class TrackerManager : IEnumerable<TrackerTier>
    {
        #region Constructors

        /// <summary>
        /// Creates a new TrackerConnection for the supplied torrent file
        /// </summary>
        /// <param name="manager">The TorrentManager to create the tracker connection for</param>
        /// <param name="infoHash">The information hash.</param>
        /// <param name="announces">The announces.</param>
        public TrackerManager(TorrentManager manager, InfoHash infoHash, IEnumerable<RawTrackerTier> announces)
        {
            this._manager = manager;
            this._infoHash = infoHash;

            // Check if this tracker supports scraping
            _trackerTiers = new List<TrackerTier>();
            foreach (var t in announces)
                _trackerTiers.Add(new TrackerTier(t));

            _trackerTiers.RemoveAll(t => t.Trackers.Count == 0);
            foreach (var tracker in _trackerTiers.SelectMany(tier => tier))
            {
                tracker.AnnounceComplete +=
                    delegate(object o, AnnounceResponseEventArgs e)
                    {
                        ClientEngine.MainLoop.Queue(delegate { OnAnnounceComplete(o, e); });
                    };

                tracker.ScrapeComplete +=
                    delegate(object o, ScrapeResponseEventArgs e)
                    {
                        ClientEngine.MainLoop.Queue(delegate { OnScrapeComplete(o, e); });
                    };
            }

            TrackerTiers = new ReadOnlyCollection<TrackerTier>(_trackerTiers);
        }

        #endregion

        public IEnumerator<TrackerTier> GetEnumerator()
        {
            return _trackerTiers.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #region Member Variables

        private readonly TorrentManager _manager;


        /// <summary>
        ///     Returns the tracker that is current in use by the engine
        /// </summary>
        public Tracker.Tracker CurrentTracker
        {
            get
            {
                if (_trackerTiers.Count == 0 || _trackerTiers[0].Trackers.Count == 0)
                    return null;

                return _trackerTiers[0].Trackers[0];
            }
        }


        /// <summary>
        ///     The infohash for the torrent
        /// </summary>
        private readonly InfoHash _infoHash;


        /// <summary>
        ///     True if the last update succeeded
        /// </summary>
        public bool UpdateSucceeded { get; private set; }


        /// <summary>
        ///     The time the last tracker update was sent to any tracker
        /// </summary>
        public DateTime LastUpdated { get; private set; }


        /// <summary>
        ///     The trackers available
        /// </summary>
        public IList<TrackerTier> TrackerTiers { get; }

        private readonly List<TrackerTier> _trackerTiers;

        #endregion

        #region Methods

        public WaitHandle Announce()
        {
            if (CurrentTracker == null)
                return new ManualResetEvent(true);

            return Announce(_trackerTiers[0].SentStartedEvent ? TorrentEvent.None : TorrentEvent.Started);
        }

        public WaitHandle Announce(Tracker.Tracker tracker)
        {
            Check.Tracker(tracker);
            var tier = _trackerTiers.Find(t => t.Trackers.Contains(tracker));
            if (tier == null)
                throw new ArgumentException("Tracker has not been registered with the manager", nameof(tracker));

            var tevent = tier.SentStartedEvent ? TorrentEvent.None : TorrentEvent.Started;
            return Announce(tracker, tevent, false, new ManualResetEvent(false));
        }

        internal WaitHandle Announce(TorrentEvent clientEvent)
        {
            if (CurrentTracker == null)
                return new ManualResetEvent(true);
            return Announce(CurrentTracker, clientEvent, true, new ManualResetEvent(false));
        }

        private WaitHandle Announce(Tracker.Tracker tracker, TorrentEvent clientEvent, bool trySubsequent,
            ManualResetEvent waitHandle)
        {
            ClientEngine engine = _manager.Engine;

            // If the engine is null, we have been unregistered
            if (engine == null)
            {
                waitHandle.Set();
                return waitHandle;
            }

            UpdateSucceeded = true;
            LastUpdated = DateTime.Now;

            EncryptionTypes e = engine.Settings.AllowedEncryption;
            var requireEncryption = !Toolbox.HasEncryption(e, EncryptionTypes.PlainText);
            var supportsEncryption = Toolbox.HasEncryption(e, EncryptionTypes.RC4Full) ||
                                     Toolbox.HasEncryption(e, EncryptionTypes.RC4Header);

            requireEncryption = requireEncryption && ClientEngine.SupportsEncryption;
            supportsEncryption = supportsEncryption && ClientEngine.SupportsEncryption;

            IPEndPoint reportedAddress = engine.Settings.ReportedAddress;
            var ip = reportedAddress?.Address.ToString();
            int port = reportedAddress?.Port ?? engine.Listener.Endpoint.Port;

            // FIXME: In metadata mode we need to pretend we need to download data otherwise
            // tracker optimisations might result in no peers being sent back.
            long bytesLeft = 1000;
            if (_manager.HasMetadata)
                bytesLeft = (long) ((1 - _manager.Bitfield.PercentComplete/100.0)*_manager.Torrent.Size);
            var p = new AnnounceParameters(_manager.Monitor.DataBytesDownloaded,
                _manager.Monitor.DataBytesUploaded,
                bytesLeft,
                clientEvent, _infoHash, requireEncryption, _manager.Engine.PeerId,
                ip, port) {SupportsEncryption = supportsEncryption};
            var id = new TrackerConnectionID(tracker, trySubsequent, clientEvent, waitHandle);
            tracker.Announce(p, id);
            return waitHandle;
        }

        private bool GetNextTracker(Tracker.Tracker tracker, out TrackerTier trackerTier, out Tracker.Tracker trackerReturn)
        {
            for (var i = 0; i < _trackerTiers.Count; i++)
            {
                for (var j = 0; j < _trackerTiers[i].Trackers.Count; j++)
                {
                    if (_trackerTiers[i].Trackers[j] != tracker)
                        continue;

                    // If we are on the last tracker of this tier, check to see if there are more tiers
                    if (j == (_trackerTiers[i].Trackers.Count - 1))
                    {
                        if (i == (_trackerTiers.Count - 1))
                        {
                            trackerTier = null;
                            trackerReturn = null;
                            return false;
                        }

                        trackerTier = _trackerTiers[i + 1];
                        trackerReturn = trackerTier.Trackers[0];
                        return true;
                    }

                    trackerTier = _trackerTiers[i];
                    trackerReturn = trackerTier.Trackers[j + 1];
                    return true;
                }
            }

            trackerTier = null;
            trackerReturn = null;
            return false;
        }

        private void OnScrapeComplete(object sender, ScrapeResponseEventArgs e)
        {
            e.Id.WaitHandle.Set();
        }

        private void OnAnnounceComplete(object sender, AnnounceResponseEventArgs e)
        {
            UpdateSucceeded = e.Successful;
            if (_manager.Engine == null)
            {
                e.Id.WaitHandle.Set();
                return;
            }

            if (e.Successful)
            {
                _manager.Peers.BusyPeers.Clear();
                var count = _manager.AddPeersCore(e.Peers);
                _manager.RaisePeersFound(new TrackerPeersAdded(_manager, count, e.Peers.Count, e.Tracker));

                var tier = _trackerTiers.Find(delegate(TrackerTier t) { return t.Trackers.Contains(e.Tracker); });
                if (tier != null)
                {
                    Toolbox.Switch(tier.Trackers, 0, tier.IndexOf(e.Tracker));
                    Toolbox.Switch(_trackerTiers, 0, _trackerTiers.IndexOf(tier));
                }
                e.Id.WaitHandle.Set();
            }
            else
            {
                TrackerTier tier;
                Tracker.Tracker tracker;

                if (!e.Id.TrySubsequent || !GetNextTracker(e.Tracker, out tier, out tracker))
                    e.Id.WaitHandle.Set();
                else
                    Announce(tracker, e.Id.TorrentEvent, true, e.Id.WaitHandle);
            }
        }

        public WaitHandle Scrape()
        {
            return CurrentTracker == null ? new ManualResetEvent(true) : Scrape(CurrentTracker, false);
        }

        public WaitHandle Scrape(Tracker.Tracker tracker)
        {
            var tier = _trackerTiers.Find(t => t.Trackers.Contains(tracker));
            if (tier == null)
                return new ManualResetEvent(true);

            return Scrape(tracker, false);
        }

        private WaitHandle Scrape(Tracker.Tracker tracker, bool trySubsequent)
        {
            if (tracker == null)
                throw new ArgumentNullException(nameof(tracker));

            if (!tracker.CanScrape)
                throw new TorrentException("This tracker does not support scraping");

            var id = new TrackerConnectionID(tracker, trySubsequent, TorrentEvent.None, new ManualResetEvent(false));
            tracker.Scrape(new ScrapeParameters(_infoHash), id);
            return id.WaitHandle;
        }

        #endregion
    }
}