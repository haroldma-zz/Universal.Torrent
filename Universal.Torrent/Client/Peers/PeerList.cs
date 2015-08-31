using System;
using System.Collections.Generic;
using System.Linq;
using Universal.Torrent.Client.PeerConnections;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.Peers
{
    internal class PeerList
    {
        #region Constructors

        public PeerList(PeerListType listType)
        {
            _peers = new List<PeerId>();
            _listType = listType;
        }

        #endregion

        #region Private Fields

        private readonly List<PeerId> _peers; //Peers held
        private readonly PeerListType _listType; //The type of list this represents
        private int _scanIndex; //Position in the list when scanning peers

        #endregion Private Fields

        #region Public Properties

        public int Count => _peers.Count;

        public bool MorePeers
        {
            get
            {
                if (_scanIndex < _peers.Count)
                    return true;
                return false;
            }
        }

        public int UnchokedPeers
        {
            get { return _peers.Count(peer => !peer.AmChoking); }
        }

        #endregion

        #region Public Methods

        public void Add(PeerId peer)
        {
            _peers.Add(peer);
        }

        public void Clear()
        {
            _peers.Clear();
            _scanIndex = 0;
        }

        public PeerId GetNextPeer()
        {
            if (_scanIndex < _peers.Count)
            {
                _scanIndex++;
                return _peers[_scanIndex - 1];
            }
            return null;
        }

        public PeerId GetFirstInterestedChokedPeer()
        {
            //Look for a choked peer
            return
                _peers.Where(peer => peer.Connection != null)
                    .FirstOrDefault(peer => peer.IsInterested && peer.AmChoking);
            //None found, return null
        }

        public PeerId GetOUPeer()
        {
            //Look for an untried peer that we haven't unchoked, or else return the choked peer with the longest unchoke interval
            PeerId longestIntervalPeer = null;
            double longestIntervalPeerTime = 0;
            foreach (var peer in _peers)
                if (peer.Connection != null)
                    if (peer.AmChoking)
                    {
                        if (!peer.LastUnchoked.HasValue)
                            //This is an untried peer that we haven't unchoked, return it
                            return peer;
                        //This is an unchoked peer that we have unchoked in the past
                        //If this is the first one we've found, remember it
                        if (longestIntervalPeer == null)
                            longestIntervalPeer = peer;
                        else
                        {
                            //Compare dates to determine whether the new one has a longer interval (but halve the interval
                            //  if the peer has never sent us any data)
                            var newInterval = SecondsBetween(peer.LastUnchoked.Value, DateTime.Now);
                            if (peer.Monitor.DataBytesDownloaded == 0)
                                newInterval = newInterval/2;
                            if (newInterval > longestIntervalPeerTime)
                            {
                                //The new peer has a longer interval than the current one, replace it
                                longestIntervalPeer = peer;
                                longestIntervalPeerTime = newInterval;
                            }
                        }
                    }
            //Return the peer with the longest interval since it was unchoked, or null if none found
            return longestIntervalPeer;
        }

        public bool Includes(PeerId peer)
        {
            //Return false if the supplied peer is null
            if (peer == null)
                return false;
            return _peers.Contains(peer);
        }

        public void Sort(bool isSeeding)
        {
            switch (_listType)
            {
                case (PeerListType.NascentPeers):
                    _peers.Sort(CompareNascentPeers);
                    break;

                case (PeerListType.CandidatePeers):
                    if (isSeeding)
                        _peers.Sort(CompareCandidatePeersWhileSeeding);
                    else
                        _peers.Sort(CompareCandidatePeersWhileDownloading);
                    break;

                case (PeerListType.OptimisticUnchokeCandidatePeers):
                    if (isSeeding)
                        _peers.Sort(CompareOptimisticUnchokeCandidatesWhileSeeding);
                    else
                        _peers.Sort(CompareOptimisticUnchokeCandidatesWhileDownloading);
                    break;
            }
        }

        public void StartScan()
        {
            _scanIndex = 0;
        }

        #endregion

        #region Private Methods

        private static int CompareCandidatePeersWhileDownloading(PeerId p1, PeerId p2)
        {
            //Comparer for candidate peers for use when the torrent is downloading
            //First sort Am interested before !AmInterested
            if (p1.AmInterested && !p2.AmInterested)
                return -1;
            if (!p1.AmInterested && p2.AmInterested)
                return 1;

            //Both have the same AmInterested status, sort by download rate highest first
            return p2.LastReviewDownloadRate.CompareTo(p1.LastReviewDownloadRate);
        }

        private static int CompareCandidatePeersWhileSeeding(PeerId p1, PeerId p2)
        {
            //Comparer for candidate peers for use when the torrent is seeding
            //Sort by upload rate, largest first
            return p2.LastReviewUploadRate.CompareTo(p1.LastReviewUploadRate);
        }

        private static int CompareNascentPeers(PeerId p1, PeerId p2)
        {
            //Comparer for nascent peers
            //Sort most recent first
            if (p1.LastUnchoked > p2.LastUnchoked)
                return -1;
            if (p1.LastUnchoked < p2.LastUnchoked)
                return 1;
            return 0;
        }

        private static int CompareOptimisticUnchokeCandidatesWhileDownloading(PeerId p1, PeerId p2)
        {
            //Comparer for optimistic unchoke candidates

            //Start by sorting peers that have given us most data before to the top
            if (p1.Monitor.DataBytesDownloaded > p2.Monitor.DataBytesDownloaded)
                return -1;
            if (p1.Monitor.DataBytesDownloaded < p2.Monitor.DataBytesDownloaded)
                return 1;

            //Amount of data sent is equal (and probably 0), sort untried before tried
            if (!p1.LastUnchoked.HasValue && p2.LastUnchoked.HasValue)
                return -1;
            if (p1.LastUnchoked.HasValue && !p2.LastUnchoked.HasValue)
                return 1;
            if (!p1.LastUnchoked.HasValue && !p2.LastUnchoked.HasValue)
                //Both untried, nothing to choose between them
                return 0;

            //Both peers have been unchoked
            //Sort into descending order (most recent first)
            if (p1.LastUnchoked > p2.LastUnchoked)
                return -1;
            if (p1.LastUnchoked < p2.LastUnchoked)
                return 1;
            return 0;
        }

        private static int CompareOptimisticUnchokeCandidatesWhileSeeding(PeerId p1, PeerId p2)
        {
            //Comparer for optimistic unchoke candidates

            //Start by sorting peers that we have sent most data to before to the top
            if (p1.Monitor.DataBytesUploaded > p2.Monitor.DataBytesUploaded)
                return -1;
            if (p1.Monitor.DataBytesUploaded < p2.Monitor.DataBytesUploaded)
                return 1;

            //Amount of data sent is equal (and probably 0), sort untried before tried
            if (!p1.LastUnchoked.HasValue && p2.LastUnchoked.HasValue)
                return -1;
            if (p1.LastUnchoked.HasValue && !p2.LastUnchoked.HasValue)
                return 1;
            if (!p1.LastUnchoked.HasValue && p2.LastUnchoked.HasValue)
                //Both untried, nothing to choose between them
                return 0;

            //Both peers have been unchoked
            //Sort into descending order (most recent first)
            if (p1.LastUnchoked > p2.LastUnchoked)
                return -1;
            if (p1.LastUnchoked < p2.LastUnchoked)
                return 1;
            return 0;
        }

        private static double SecondsBetween(DateTime firstTime, DateTime secondTime)
        {
            //Calculate the number of seconds and fractions of a second that have elapsed between the first time and the second
            return secondTime.Subtract(firstTime).TotalMilliseconds/1000;
        }

        #endregion
    }
}