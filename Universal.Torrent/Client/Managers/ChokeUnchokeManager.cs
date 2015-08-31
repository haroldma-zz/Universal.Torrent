using System;
using System.Collections.Generic;
using System.Diagnostics;
using Universal.Torrent.Client.Messages.FastPeerExtensions;
using Universal.Torrent.Client.Messages.StandardMessages;
using Universal.Torrent.Client.PeerConnections;
using Universal.Torrent.Client.Peers;
using Universal.Torrent.Client.Unchokers;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.Managers
{
    internal class ChokeUnchokeManager : IUnchoker
    {
        #region Constructors

        /// <summary>
        ///     Initializes a new instance of the <see cref="ChokeUnchokeManager" /> class.
        /// </summary>
        /// <param name="torrentManager">The torrent manager.</param>
        /// <param name="minimumTimeBetweenReviews">The minimum time between reviews.</param>
        /// <param name="percentOfMaxRateToSkipReview">The percent of maximum rate to skip review.</param>
        public ChokeUnchokeManager(TorrentManager torrentManager, int minimumTimeBetweenReviews,
            int percentOfMaxRateToSkipReview)
        {
            _owningTorrent = torrentManager;
            _minimumTimeBetweenReviews = minimumTimeBetweenReviews;
            _percentOfMaxRateToSkipReview = percentOfMaxRateToSkipReview;
        }

        #endregion

        #region IUnchoker Members

        public void UnchokeReview()
        {
            TimePassed();
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Executed each tick of the client engine
        /// </summary>
        public void TimePassed()
        {
            //Start by identifying:
            //  the choked and interested peers
            //  the number of unchoked peers
            //Choke peers that have become disinterested at the same time
            var chokedInterestedPeers = new List<PeerId>();
            var interestedCount = 0;
            var unchokedCount = 0;

            var skipDownload = (_isDownloading &&
                                (_owningTorrent.Monitor.DownloadSpeed <
                                 (_owningTorrent.Settings.MaxDownloadSpeed*_percentOfMaxRateToSkipReview/100.0)));
            var skipUpload = (!_isDownloading &&
                              (_owningTorrent.Monitor.UploadSpeed <
                               (_owningTorrent.Settings.MaxUploadSpeed*_percentOfMaxRateToSkipReview/100.0)));

            skipDownload = skipDownload && _owningTorrent.Settings.MaxDownloadSpeed > 0;
            skipUpload = skipUpload && _owningTorrent.Settings.MaxUploadSpeed > 0;

            foreach (var connectedPeer in _owningTorrent.Peers.ConnectedPeers)
            {
                if (connectedPeer.Connection == null)
                    continue;

                //If the peer is a seeder and we are not currently interested in it, put that right
                if (connectedPeer.Peer.IsSeeder && !connectedPeer.AmInterested)
                {
                    // FIXME - Is this necessary anymore? I don't think so
                    //owningTorrent.Mode.SetAmInterestedStatus(connectedPeer, true);
                    //Send2Log("Forced AmInterested: " + connectedPeer.Peer.Location);
                }

                // If the peer is interesting try to queue up some piece requests off him
                // If he is choking, we will only queue a piece if there is a FastPiece we can choose
                if (connectedPeer.AmInterested)
                    _owningTorrent.PieceManager.AddPieceRequests(connectedPeer);

                if (!connectedPeer.Peer.IsSeeder)
                {
                    if (!connectedPeer.IsInterested && !connectedPeer.AmChoking)
                        //This peer is disinterested and unchoked; choke it
                        Choke(connectedPeer);

                    else if (connectedPeer.IsInterested)
                    {
                        interestedCount++;
                        if (!connectedPeer.AmChoking) //This peer is interested and unchoked, count it
                            unchokedCount++;
                        else
                            chokedInterestedPeers.Add(connectedPeer);
                        //This peer is interested and choked, remember it and count it
                    }
                }
            }

            if (_firstCall)
            {
                //This is the first time we've been called for this torrent; set current status and run an initial review
                _isDownloading = !_owningTorrent.Complete; //If progress is less than 100% we must be downloading
                _firstCall = false;
                ExecuteReview();
            }
            else if (_isDownloading && _owningTorrent.Complete)
            {
                //The state has changed from downloading to seeding; set new status and run an initial review
                _isDownloading = false;
                ExecuteReview();
            }

            else if (interestedCount <= _owningTorrent.Settings.UploadSlots)
                //Since we have enough slots to satisfy everyone that's interested, unchoke them all; no review needed
                UnchokePeerList(chokedInterestedPeers);

            else if (_minimumTimeBetweenReviews > 0 &&
                     (SecondsBetween(_timeOfLastReview, DateTime.Now) >= _minimumTimeBetweenReviews) &&
                     (skipDownload || skipUpload))
                //Based on the time of the last review, a new review is due
                //There are more interested peers than available upload slots
                //If we're downloading, the download rate is insufficient to skip the review
                //If we're seeding, the upload rate is insufficient to skip the review
                //So, we need a review
                ExecuteReview();

            else
            //We're not going to do a review this time
            //Allocate any available slots based on the results of the last review
                AllocateSlots(unchokedCount);
        }

        #endregion

        #region Private Fields

        private readonly int _minimumTimeBetweenReviews;
        //seconds.  Minimum time that needs to pass before we execute a review

        private readonly int _percentOfMaxRateToSkipReview;
        //If the latest download/upload rate is >= to this percentage of the maximum rate we should skip the review

        private DateTime _timeOfLastReview; //When we last reviewed the choke/unchoke position
        private bool _firstCall = true; //Indicates the first call to the TimePassed method
        private bool _isDownloading = true; //Allows us to identify change in state from downloading to seeding
        private readonly TorrentManager _owningTorrent; //The torrent to which this manager belongs
        private PeerId _optimisticUnchokePeer; //This is the peer we have optimistically unchoked, or null

        //Lists of peers held by the choke/unchoke manager
        private readonly PeerList _nascentPeers = new PeerList(PeerListType.NascentPeers);
        //Peers that have yet to be unchoked and downloading for a full review period

        private readonly PeerList _candidatePeers = new PeerList(PeerListType.CandidatePeers);
        //Peers that are candidates for unchoking based on past performance

        private readonly PeerList _optimisticUnchokeCandidates =
            new PeerList(PeerListType.OptimisticUnchokeCandidatePeers);

        //Peers that are candidates for unchoking in case they perform well

        /// <summary>
        ///     Number of peer reviews that have been conducted
        /// </summary>
        internal int ReviewsExecuted { get; private set; }

        #endregion Private Fields

        #region Private Methods

        private IEnumerable<PeerList> AllLists()
        {
            yield return _nascentPeers;
            yield return _candidatePeers;
            yield return _optimisticUnchokeCandidates;
        }

        private void AllocateSlots(int alreadyUnchoked)
        {
            PeerId peer;

            //Allocate interested peers to slots based on the latest review results
            //First determine how many slots are available to be allocated
            var availableSlots = _owningTorrent.Settings.UploadSlots - alreadyUnchoked;

            // If there are no slots, just return
            if (availableSlots <= 0)
                return;

            // Check the peer lists (nascent, then candidate then optimistic unchoke)
            // for an interested choked peer, if one is found, unchoke it.
            foreach (var list in AllLists())
                while ((peer = list.GetFirstInterestedChokedPeer()) != null && (availableSlots-- > 0))
                    Unchoke(peer);

            // In the time that has passed since the last review we might have connected to more peers
            // that don't appear in AllLists.  It's also possible we have not yet run a review in
            // which case AllLists will be empty.  Fill remaining slots with unchoked, interested peers
            // from the full list.
            while (availableSlots-- > 0)
            {
                //No peers left, look for any interested choked peers
                var peerFound = false;
                foreach (var connectedPeer in _owningTorrent.Peers.ConnectedPeers)
                {
                    if (connectedPeer.Connection != null)
                    {
                        if (connectedPeer.IsInterested && connectedPeer.AmChoking)
                        {
                            Unchoke(connectedPeer);
                            peerFound = true;
                            break;
                        }
                    }
                }
                if (!peerFound)
                    //No interested choked peers anywhere, we're done
                    break;
            }
        }


        public void Choke(PeerId peer)
        {
            //Choke the supplied peer

            if (peer.AmChoking)
                //We're already choking this peer, nothing to do
                return;

            peer.AmChoking = true;
            _owningTorrent.UploadingTo--;
            RejectPendingRequests(peer);
            peer.EnqueueAt(new ChokeMessage(), 0);
            Debug.WriteLine(peer.Connection, "Choking");
            //			Send2Log("Choking: " + PeerToChoke.Location);
        }

        private void ExecuteReview()
        {
            //Review current choke/unchoke position and adjust as necessary
            //Start by populating the lists of peers, then allocate available slots oberving the unchoke limit

            //Clear the lists to start with
            _nascentPeers.Clear();
            _candidatePeers.Clear();
            _optimisticUnchokeCandidates.Clear();

            //No review needed or disabled by the torrent settings

            /////???Remove when working
            ////Log peer status - temporary
            //if (isLogging)
            //{
            //    StringBuilder logEntry = new StringBuilder(1000);
            //    logEntry.Append(B2YN(owningTorrent.State == TorrentState.Seeding) + timeOfLastReview.ToString() + "," + DateTime.Now.ToString() + ";");
            //    foreach (PeerIdInternal connectedPeer in owningTorrent.Peers.ConnectedPeers)
            //    {
            //        if (connectedPeer.Connection != null)
            //            if (!connectedPeer.Peer.IsSeeder)
            //            {
            //                {
            //                    logEntry.Append(
            //                        B2YN(connectedPeer.Peer.IsSeeder) +
            //                        B2YN(connectedPeer.AmChoking) +
            //                        B2YN(connectedPeer.AmInterested) +
            //                        B2YN(connectedPeer.IsInterested) +
            //                        B2YN(connectedPeer.Peer.FirstReviewPeriod) +
            //                        connectedPeer.Connection.Monitor.DataBytesDownloaded.ToString() + "," +
            //                        connectedPeer.Peer.BytesDownloadedAtLastReview.ToString() + "," +
            //                        connectedPeer.Connection.Monitor.DataBytesUploaded.ToString() + "," +
            //                        connectedPeer.Peer.BytesUploadedAtLastReview.ToString() + "," +
            //                        connectedPeer.Peer.Location);
            //                    DateTime? lastUnchoked = connectedPeer.Peer.LastUnchoked;
            //                    if (lastUnchoked.HasValue)
            //                        logEntry.Append(
            //                            "," +
            //                            lastUnchoked.ToString() + "," +
            //                            SecondsBetween(lastUnchoked.Value, DateTime.Now).ToString());
            //                    logEntry.Append(";");
            //                }
            //            }
            //    }
            //    Send2Log(logEntry.ToString());
            //}

            //Scan the peers building the lists as we go and count number of unchoked peers

            var unchokedPeers = 0;

            foreach (var connectedPeer in _owningTorrent.Peers.ConnectedPeers)
            {
                if (connectedPeer.Connection != null)
                {
                    if (!connectedPeer.Peer.IsSeeder)
                    {
                        //Determine common values for use in this routine
                        var timeSinceLastReview = SecondsBetween(_timeOfLastReview, DateTime.Now);
                        double timeUnchoked = 0;
                        if (!connectedPeer.AmChoking)
                        {
                            if (connectedPeer.LastUnchoked != null)
                                timeUnchoked = SecondsBetween(connectedPeer.LastUnchoked.Value, DateTime.Now);
                            unchokedPeers++;
                        }
                        long bytesTransferred;
                        if (!_isDownloading)
                            //We are seeding the torrent; determine bytesTransferred as bytes uploaded
                            bytesTransferred = connectedPeer.Monitor.DataBytesUploaded -
                                               connectedPeer.BytesUploadedAtLastReview;
                        else
                        //The peer is unchoked and we are downloading the torrent; determine bytesTransferred as bytes downloaded
                            bytesTransferred = connectedPeer.Monitor.DataBytesDownloaded -
                                               connectedPeer.BytesDownloadedAtLastReview;

                        //Reset review up and download rates to zero; peers are therefore non-responders unless we determine otherwise
                        connectedPeer.LastReviewDownloadRate = 0;
                        connectedPeer.LastReviewUploadRate = 0;

                        if (!connectedPeer.AmChoking &&
                            (timeUnchoked < _minimumTimeBetweenReviews ||
                             (connectedPeer.FirstReviewPeriod && bytesTransferred > 0)))
                            //The peer is unchoked but either it has not been unchoked for the warm up interval,
                            // or it is the first full period and only just started transferring data
                            _nascentPeers.Add(connectedPeer);

                        else if ((timeUnchoked >= _minimumTimeBetweenReviews) && bytesTransferred > 0)
                            //The peer is unchoked, has been for the warm up period and has transferred data in the period
                        {
                            //Add to peers that are candidates for unchoking based on their performance
                            _candidatePeers.Add(connectedPeer);
                            //Calculate the latest up/downloadrate
                            connectedPeer.LastReviewUploadRate = (connectedPeer.Monitor.DataBytesUploaded -
                                                                  connectedPeer.BytesUploadedAtLastReview)/
                                                                 timeSinceLastReview;
                            connectedPeer.LastReviewDownloadRate = (connectedPeer.Monitor.DataBytesDownloaded -
                                                                    connectedPeer.BytesDownloadedAtLastReview)/
                                                                   timeSinceLastReview;
                        }

                        else if (_isDownloading && connectedPeer.IsInterested && connectedPeer.AmChoking &&
                                 bytesTransferred > 0)
                            //A peer is optimistically unchoking us.  Take the maximum of their current download rate and their download rate over the
                            //	review period since they might have only just unchoked us and we don't want to miss out on a good opportunity.  Upload
                            // rate is less important, so just take an average over the period.
                        {
                            //Add to peers that are candidates for unchoking based on their performance
                            _candidatePeers.Add(connectedPeer);
                            //Calculate the latest up/downloadrate
                            connectedPeer.LastReviewUploadRate = (connectedPeer.Monitor.DataBytesUploaded -
                                                                  connectedPeer.BytesUploadedAtLastReview)/
                                                                 timeSinceLastReview;
                            connectedPeer.LastReviewDownloadRate =
                                Math.Max(
                                    (connectedPeer.Monitor.DataBytesDownloaded -
                                     connectedPeer.BytesDownloadedAtLastReview)/timeSinceLastReview,
                                    connectedPeer.Monitor.DownloadSpeed);
                        }

                        else if (connectedPeer.IsInterested)
                            //All other interested peers are candidates for optimistic unchoking
                            _optimisticUnchokeCandidates.Add(connectedPeer);

                        //Remember the number of bytes up and downloaded for the next review
                        connectedPeer.BytesUploadedAtLastReview = connectedPeer.Monitor.DataBytesUploaded;
                        connectedPeer.BytesDownloadedAtLastReview = connectedPeer.Monitor.DataBytesDownloaded;

                        //If the peer has been unchoked for longer than one review period, unset FirstReviewPeriod
                        if (timeUnchoked >= _minimumTimeBetweenReviews)
                            connectedPeer.FirstReviewPeriod = false;
                    }
                }
            }
            //				Send2Log(nascentPeers.Count.ToString() + "," + candidatePeers.Count.ToString() + "," + optimisticUnchokeCandidates.Count.ToString());

            //Now sort the lists of peers so we are ready to reallocate them
            _nascentPeers.Sort(_owningTorrent.State == TorrentState.Seeding);
            _candidatePeers.Sort(_owningTorrent.State == TorrentState.Seeding);
            _optimisticUnchokeCandidates.Sort(_owningTorrent.State == TorrentState.Seeding);
            //				if (isLogging)
            //				{
            //					string x = "";
            //					while (optimisticUnchokeCandidates.MorePeers)
            //						x += optimisticUnchokeCandidates.GetNextPeer().Location + ";";
            //					Send2Log(x);
            //					optimisticUnchokeCandidates.StartScan();
            //				}

            //If there is an optimistic unchoke peer and it is nascent, we should reallocate all the available slots
            //Otherwise, if all the slots are allocated to nascent peers, don't try an optimistic unchoke this time
            if (_nascentPeers.Count >= _owningTorrent.Settings.UploadSlots ||
                _nascentPeers.Includes(_optimisticUnchokePeer))
                ReallocateSlots(_owningTorrent.Settings.UploadSlots, unchokedPeers);
            else
            {
                //We should reallocate all the slots but one and allocate the last slot to the next optimistic unchoke peer
                ReallocateSlots(_owningTorrent.Settings.UploadSlots - 1, unchokedPeers);
                //In case we don't find a suitable peer, make the optimistic unchoke peer null
                var oup = _optimisticUnchokeCandidates.GetOUPeer();
                if (oup != null)
                {
                    //						Send2Log("OUP: " + oup.Location);
                    Unchoke(oup);
                    _optimisticUnchokePeer = oup;
                }
            }

            //Finally, deallocate (any) remaining peers from the three lists
            while (_nascentPeers.MorePeers)
            {
                var nextPeer = _nascentPeers.GetNextPeer();
                if (!nextPeer.AmChoking)
                    Choke(nextPeer);
            }
            while (_candidatePeers.MorePeers)
            {
                var nextPeer = _candidatePeers.GetNextPeer();
                if (!nextPeer.AmChoking)
                    Choke(nextPeer);
            }
            while (_optimisticUnchokeCandidates.MorePeers)
            {
                var nextPeer = _optimisticUnchokeCandidates.GetNextPeer();
                if (!nextPeer.AmChoking)
                    //This peer is currently unchoked, choke it unless it is the optimistic unchoke peer
                    if (_optimisticUnchokePeer == null)
                        //There isn't an optimistic unchoke peer
                        Choke(nextPeer);
                    else if (!nextPeer.Equals(_optimisticUnchokePeer))
                        //This isn't the optimistic unchoke peer
                        Choke(nextPeer);
            }

            _timeOfLastReview = DateTime.Now;
            ReviewsExecuted++;
        }


        /// <summary>
        ///     Review method for BitTyrant Choking/Unchoking Algorithm
        /// </summary>
        private void ExecuteTyrantReview()
        {
            // if we are seeding, don't deal with it - just send it to old method
            if (!_isDownloading)
                ExecuteReview();

            var sortedPeers = new List<PeerId>();
            int uploadBandwidthUsed;

            foreach (var connectedPeer in _owningTorrent.Peers.ConnectedPeers)
            {
                if (connectedPeer.Connection != null)
                {
                    // update tyrant stats
                    connectedPeer.UpdateTyrantStats();
                    sortedPeers.Add(connectedPeer);
                }
            }

            // sort the list by BitTyrant ratio
            sortedPeers.Sort((p1, p2) => p2.Ratio.CompareTo(p1.Ratio));

            //TODO: Make sure that lan-local peers always get unchoked. Perhaps an implementation like AZInstanceManager
            //(in com.aelitis.azureus.core.instancemanager)


            // After this is complete, sort them and and unchoke until upload capcity is met
            // TODO: Should we consider some extra measures, like nascent peers, candidatePeers, optimisticUnchokeCandidates ETC.

            uploadBandwidthUsed = 0;
            foreach (var pid in sortedPeers)
            {
                // unchoke the top interested peers till we reach the max bandwidth allotted.
                if (uploadBandwidthUsed < _owningTorrent.Settings.MaxUploadSpeed && pid.IsInterested)
                {
                    Unchoke(pid);

                    uploadBandwidthUsed += pid.UploadRateForRecip;
                }
                else
                {
                    Choke(pid);
                }
            }

            _timeOfLastReview = DateTime.Now;
            ReviewsExecuted++;
        }


        /// <summary>
        ///     Reallocates the specified number of upload slots
        /// </summary>
        /// <param name="numberOfSlots"></param>
        /// <param name="numberOfUnchokedPeers"></param>
        /// The number of slots we should reallocate
        private void ReallocateSlots(int numberOfSlots, int numberOfUnchokedPeers)
        {
            //First determine the maximum number of peers we can unchoke in this review = maximum of:
            //  half the number of upload slots; and
            //  slots not already unchoked
            var maximumUnchokes = numberOfSlots/2;
            maximumUnchokes = Math.Max(maximumUnchokes, numberOfSlots - numberOfUnchokedPeers);

            //Now work through the lists of peers in turn until we have allocated all the slots
            while (numberOfSlots > 0)
            {
                if (_nascentPeers.MorePeers)
                    ReallocateSlot(ref numberOfSlots, ref maximumUnchokes, _nascentPeers.GetNextPeer());
                else if (_candidatePeers.MorePeers)
                    ReallocateSlot(ref numberOfSlots, ref maximumUnchokes, _candidatePeers.GetNextPeer());
                else if (_optimisticUnchokeCandidates.MorePeers)
                    ReallocateSlot(ref numberOfSlots, ref maximumUnchokes, _optimisticUnchokeCandidates.GetNextPeer());
                else
                //No more peers left, we're done
                    break;
            }
        }

        /// <summary>
        ///     Reallocates the next slot with the specified peer if we can
        /// </summary>
        /// <param name="numberOfSlots"></param>
        /// The number of slots left to reallocate
        /// <param name="maximumUnchokes"></param>
        /// The number of peers we can unchoke
        /// <param name="peer"></param>
        /// The peer to consider for reallocation
        private void ReallocateSlot(ref int numberOfSlots, ref int maximumUnchokes, PeerId peer)
        {
            if (!peer.AmChoking)
            {
                //This peer is already unchoked, just decrement the number of slots
                numberOfSlots--;
                //				Send2Log("Leave: " + peer.Location);
            }
            else if (maximumUnchokes > 0)
            {
                //This peer is choked and we've not yet reached the limit of unchokes, unchoke it
                Unchoke(peer);
                maximumUnchokes--;
                numberOfSlots--;
            }
        }

        /// <summary>
        ///     Checks the send queue of the peer to see if there are any outstanding pieces which they requested
        ///     and rejects them as necessary
        /// </summary>
        /// <param name="peer"></param>
        private void RejectPendingRequests(PeerId peer)
        {
            var length = peer.QueueLength;

            for (var i = 0; i < length; i++)
            {
                var message = peer.Dequeue();
                if (!(message is PieceMessage))
                {
                    peer.Enqueue(message);
                    continue;
                }

                var pieceMessage = (PieceMessage) message;

                // If the peer doesn't support fast peer, then we will never requeue the message
                if (!(peer.SupportsFastPeer && ClientEngine.SupportsFastPeer))
                {
                    peer.IsRequestingPiecesCount--;
                    continue;
                }

                // If the peer supports fast peer, queue the message if it is an AllowedFast piece
                // Otherwise send a reject message for the piece
                if (peer.AmAllowedFastPieces.Contains(pieceMessage.PieceIndex))
                    peer.Enqueue(pieceMessage);
                else
                {
                    peer.IsRequestingPiecesCount--;
                    peer.Enqueue(new RejectRequestMessage(pieceMessage));
                }
            }
        }

        private static double SecondsBetween(DateTime firstTime, DateTime secondTime)
        {
            //Calculate the number of seconds and fractions of a second that have elapsed between the first time and the second
            var difference = secondTime.Subtract(firstTime);
            return difference.TotalMilliseconds/1000;
        }

        public void Unchoke(PeerId peerToUnchoke)
        {
            //Unchoke the supplied peer

            if (!peerToUnchoke.AmChoking)
                //We're already unchoking this peer, nothing to do
                return;

            peerToUnchoke.AmChoking = false;
            _owningTorrent.UploadingTo++;
            peerToUnchoke.EnqueueAt(new UnchokeMessage(), 0);
            peerToUnchoke.LastUnchoked = DateTime.Now;
            peerToUnchoke.FirstReviewPeriod = true;
            Debug.WriteLine(peerToUnchoke.Connection, "Unchoking");
            //			Send2Log("Unchoking: " + PeerToUnchoke.Location);
        }

        private void UnchokePeerList(List<PeerId> peerList)
        {
            //Unchoke all the peers in the supplied list
            peerList.ForEach(Unchoke);
        }

        #endregion

        #region Temporary stuff for logging

        //FileStream logStream;
        //StreamWriter logStreamWriter;
        //bool isLogging = true;

        //private void Send2Log(string LogEntry)
        //{
        //    if (isLogging)
        //    {
        //        if (logStream == null)
        //        {
        //            string logFileName = owningTorrent.Torrent.Name + ".ChokeUnchoke.Log";
        //            logStream = new FileStream(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal),logFileName),FileMode.Append);
        //            logStreamWriter = new StreamWriter(logStream, System.Text.Encoding.ASCII);
        //            logStreamWriter.AutoFlush=true;
        //        }
        //        logStreamWriter.WriteLine(DateTime.Now.ToString() + ":" + LogEntry);
        //    }
        //}

        //private string B2YN(bool Boolean)
        //{
        //    if (Boolean)
        //        return "Y,";
        //    else
        //        return "N,";
        //}

        #endregion
    }
}