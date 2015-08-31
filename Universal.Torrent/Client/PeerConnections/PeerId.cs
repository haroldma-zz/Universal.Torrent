//
// PeerConnectionId.cs
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
using System.Collections.Generic;
using System.Diagnostics;
using Universal.Torrent.Client.Encryption.IEncryption;
using Universal.Torrent.Client.Managers;
using Universal.Torrent.Client.Messages;
using Universal.Torrent.Client.Messages.LibtorrentMessages;
using Universal.Torrent.Client.Messages.StandardMessages;
using Universal.Torrent.Client.Peers;
using Universal.Torrent.Client.RateLimiters;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.PeerConnections
{
    public class PeerId //: IComparable<PeerIdInternal>
    {
        #region Constructors

        internal PeerId(Peer peer, TorrentManager manager)
        {
            if (peer == null)
                throw new ArgumentNullException(nameof(peer));

            SuggestedPieces = new MonoTorrentCollection<int>();
            AmChoking = true;
            IsChoking = true;

            IsAllowedFastPieces = new MonoTorrentCollection<int>();
            AmAllowedFastPieces = new MonoTorrentCollection<int>();
            LastMessageReceived = DateTime.Now;
            LastMessageSent = DateTime.Now;
            Peer = peer;
            MaxPendingRequests = 2;
            MaxSupportedPendingRequests = 50;
            Monitor = new ConnectionMonitor();
            _sendQueue = new MonoTorrentCollection<PeerMessage>(12);
            ExtensionSupports = new ExtensionSupports();
            TorrentManager = manager;
            InitializeTyrant();
        }

        #endregion

        internal void TryProcessAsyncReads()
        {
            foreach (var message in PieceReads)
                Enqueue(message);
            PieceReads.Clear();
            return;
            // We only allow 2 simultaenous PieceMessages in a peers send queue.
            // This way if the peer requests 100 pieces, we don't bloat our memory
            // usage unnecessarily. Once the first message is sent, we read data
            // for the *next* message asynchronously and then add it to the queue.
            // While this is happening, we send data from the second PieceMessage in
            // the queue, thus the queue should rarely be empty.
           /* var existingReads = 0;
            if (CurrentlySendingMessage is PieceMessage)
                existingReads++;

            for (var i = 0; existingReads < 2 && i < sendQueue.Count; i++)
                if (sendQueue[i] is PieceMessage)
                    existingReads++;

            if (existingReads >= 2)
                return;

            PieceMessage m = null;
            for (var i = 0; m == null && i < PieceReads.Count; i++)
                if (PieceReads[i].Data == BufferManager.EmptyBuffer)
                    m = PieceReads[i];

            if (m == null)
                return;

            var offset = (long) m.PieceIndex*torrentManager.Torrent.PieceLength + m.StartOffset;
            ClientEngine.BufferManager.GetBuffer(ref m.Data, m.RequestLength);
            Engine.DiskManager.QueueRead(torrentManager, offset, m.Data, m.RequestLength, delegate
            {
                ClientEngine.MainLoop.Queue(delegate
                {
                    if (!PieceReads.Contains(m))
                        ClientEngine.BufferManager.FreeBuffer(ref m.Data);
                    else
                    {
                        PieceReads.Remove(m);
                        Enqueue(m);
                    }
                    TryProcessAsyncReads();
                });
            });*/
        }

        #region Choke/Unchoke

        internal DateTime? LastUnchoked { get; set; } = null;

        internal long BytesDownloadedAtLastReview { get; set; } = 0;

        internal long BytesUploadedAtLastReview { get; set; } = 0;

        public IConnection Connection { get; internal set; }

        internal double LastReviewDownloadRate { get; set; } = 0;

        internal double LastReviewUploadRate { get; set; } = 0;

        internal bool FirstReviewPeriod { get; set; }

        internal DateTime LastBlockReceived { get; set; } = DateTime.Now;

        //downloaded during a review period

        #endregion

        #region Member Variables

        public List<PieceMessage> PieceReads = new List<PieceMessage>();

        private readonly MonoTorrentCollection<PeerMessage> _sendQueue; // This holds the peermessages waiting to be sent
        private TorrentManager _torrentManager;

        #endregion Member Variables

        #region Properties

        internal byte[] AddressBytes => Connection.AddressBytes;

        /// <summary>
        ///     The remote peer can request these and we'll fulfill the request if we're choking them
        /// </summary>
        internal MonoTorrentCollection<int> AmAllowedFastPieces { get; set; }

        public bool AmChoking { get; internal set; }

        public bool AmInterested { get; internal set; }

        public int AmRequestingPiecesCount { get; set; }

        public BitField BitField { get; set; }

        public Software ClientApp { get; internal set; }

        internal ConnectionManager ConnectionManager => Engine.ConnectionManager;

        internal PeerMessage CurrentlySendingMessage { get; set; }

        internal IEncryption Decryptor { get; set; }

        internal string DisconnectReason { get; set; }

        public IEncryption Encryptor { get; set; }

        public ClientEngine Engine { get; private set; }

        internal ExtensionSupports ExtensionSupports { get; set; }

        public int HashFails => Peer.TotalHashFails;

        internal MonoTorrentCollection<int> IsAllowedFastPieces { get; set; }

        public bool IsChoking { get; internal set; }

        public bool IsConnected => Connection != null;

        public bool IsInterested { get; internal set; }

        public bool IsSeeder => BitField.AllTrue || Peer.IsSeeder;

        public int IsRequestingPiecesCount { get; set; }

        internal DateTime LastMessageReceived { get; set; }

        internal DateTime LastMessageSent { get; set; }

        internal DateTime WhenConnected { get; set; }

        internal int MaxPendingRequests { get; set; }

        internal int MaxSupportedPendingRequests { get; set; }

        internal MessagingCallback MessageSentCallback { get; set; }

        internal MessagingCallback MessageReceivedCallback { get; set; }

        public ConnectionMonitor Monitor { get; }

        internal Peer Peer { get; set; }

        internal PeerExchangeManager PeerExchangeManager { get; set; }

        public string PeerID => Peer.PeerId;

        public int PiecesSent { get; internal set; }

        public int PiecesReceived { get; internal set; }

        internal ushort Port { get; set; }

        internal bool ProcessingQueue { get; set; }

        public bool SupportsFastPeer { get; internal set; }

        public bool SupportsLTMessages { get; internal set; }

        internal MonoTorrentCollection<int> SuggestedPieces { get; }

        public TorrentManager TorrentManager
        {
            get { return _torrentManager; }
            set
            {
                _torrentManager = value;
                if (value != null)
                {
                    Engine = value.Engine;
                    if (value.HasMetadata)
                        BitField = new BitField(value.Torrent.Pieces.Count);
                }
            }
        }

        public Uri Uri => Peer.ConnectionUri;

        #endregion Properties

        #region Methods

        public void CloseConnection()
        {
            ClientEngine.MainLoop.QueueWait(delegate { Connection?.Dispose(); });
        }

        internal PeerMessage Dequeue()
        {
            return _sendQueue.Dequeue();
        }

        internal void Enqueue(PeerMessage msg)
        {
            _sendQueue.Add(msg);
            if (!ProcessingQueue)
            {
                ProcessingQueue = true;
                ConnectionManager.ProcessQueue(this);
            }
        }

        internal void EnqueueAt(PeerMessage message, int index)
        {
            if (_sendQueue.Count == 0 || index >= _sendQueue.Count)
                Enqueue(message);
            else
                _sendQueue.Insert(index, message);
        }

        public override bool Equals(object obj)
        {
            var id = obj as PeerId;
            return id != null && Peer.Equals(id.Peer);
        }

        public override int GetHashCode()
        {
            return Peer.ConnectionUri.GetHashCode();
        }

        internal int QueueLength => _sendQueue.Count;

        public void SendMessage(PeerMessage message)
        {
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            ClientEngine.MainLoop.QueueWait(delegate
            {
                if (Connection == null)
                    return;

                Enqueue(message);
            });
        }

        public override string ToString()
        {
            return Peer.ConnectionUri.ToString();
        }

        #endregion

        #region BitTyrantasaurus implementation

        private const int MarketRate = 7000; // taken from reference BitTyrant implementation
        private DateTime _lastRateReductionTime; // last time we reduced rate of this peer
        private int _lastMeasuredDownloadRate; // last download rate measured
        private long _startTime;

        // stats
        private int _maxObservedDownloadSpeed;

        private void InitializeTyrant()
        {
            _haveMessagesReceived = 0;
            _startTime = Stopwatch.GetTimestamp();

            RateLimiter = new RateLimiter();
            _uploadRateForRecip = MarketRate;
            _lastRateReductionTime = DateTime.Now;
            _lastMeasuredDownloadRate = 0;

            _maxObservedDownloadSpeed = 0;
            RoundsChoked = 0;
            RoundsUnchoked = 0;
        }

        /// <summary>
        ///     Measured from number of Have messages
        /// </summary>
        private int _haveMessagesReceived;

        /// <summary>
        ///     how much we have to send to this peer to guarantee reciprocation
        ///     TODO: Can't allow upload rate to exceed this
        /// </summary>
        private int _uploadRateForRecip;


        internal int HaveMessagesReceived
        {
            get { return _haveMessagesReceived; }
            set { _haveMessagesReceived = value; }
        }

        /// <summary>
        ///     This is Up
        /// </summary>
        internal int UploadRateForRecip => _uploadRateForRecip;


        /// <summary>
        ///     TGS CHANGE: Get the estimated download rate of this peer based on the rate at which he sends
        ///     us Have messages. Note that this could be false if the peer has a malicious client.
        ///     Units: Bytes/s
        /// </summary>
        internal int EstimatedDownloadRate
        {
            get
            {
                var timeElapsed = (int) new TimeSpan(Stopwatch.GetTimestamp() - _startTime).TotalSeconds;
                return
                    (int)
                        (timeElapsed == 0
                            ? 0
                            : ((long) _haveMessagesReceived*TorrentManager.Torrent.PieceLength)/timeElapsed);
            }
        }

        /// <summary>
        ///     This is the ratio of Dp to Up
        /// </summary>
        internal float Ratio
        {
            get
            {
                float downloadRate = GetDownloadRate();
                return downloadRate/_uploadRateForRecip;
            }
        }

        /// <summary>
        ///     Last time we looked that this peer was choking us
        /// </summary>
        internal DateTime LastChokedTime { get; private set; }

        /// <summary>
        ///     Used to check how much upload capacity we are giving this peer
        /// </summary>
        internal RateLimiter RateLimiter { get; private set; }

        internal short RoundsChoked { get; private set; }

        internal short RoundsUnchoked { get; private set; }

        /// <summary>
        ///     Get our download rate from this peer -- this is Dp.
        ///     1. If we are not choked by this peer, return the actual measure download rate.
        ///     2. If we are choked, then attempt to make an educated guess at the download rate using the following steps
        ///     - use the rate of Have messages received from this peer as an estimate of its download rate
        ///     - assume that its upload rate is equivalent to its estimated download rate
        ///     - divide this upload rate by the standard implementation's active set size for that rate
        /// </summary>
        /// <returns></returns>
        internal int GetDownloadRate()
        {
            if (_lastMeasuredDownloadRate > 0)
            {
                return _lastMeasuredDownloadRate;
            }
            // assume that his upload rate will match his estimated download rate, and 
            // get the estimated active set size
            var estimatedDownloadRate = EstimatedDownloadRate;
            var activeSetSize = GetActiveSetSize(estimatedDownloadRate);

            return estimatedDownloadRate/activeSetSize;
        }


        /// <summary>
        ///     Should be called by ChokeUnchokeManager.ExecuteReview
        ///     Logic taken from BitTyrant implementation
        /// </summary>
        internal void UpdateTyrantStats()
        {
            // if we're still being choked, set the time of our last choking
            if (IsChoking)
            {
                RoundsChoked++;

                LastChokedTime = DateTime.Now;
            }
            else
            {
                RoundsUnchoked++;

                if (AmInterested)
                {
                    //if we are interested and unchoked, update last measured download rate, unless it is 0
                    if (Monitor.DownloadSpeed > 0)
                    {
                        _lastMeasuredDownloadRate = Monitor.DownloadSpeed;

                        _maxObservedDownloadSpeed = Math.Max(_lastMeasuredDownloadRate, _maxObservedDownloadSpeed);
                    }
                }
            }

            // last rate wasn't sufficient to achieve reciprocation
            if (!AmChoking && IsChoking && IsInterested)
                // only increase upload rate if he's interested, otherwise he won't request any pieces
            {
                _uploadRateForRecip = (_uploadRateForRecip*12)/10;
            }

            // we've been unchoked by this guy for a while....
            if (!IsChoking && !AmChoking
                && (DateTime.Now - LastChokedTime).TotalSeconds > 30
                && (DateTime.Now - _lastRateReductionTime).TotalSeconds > 30) // only do rate reduction every 30s
            {
                _uploadRateForRecip = (_uploadRateForRecip*9)/10;
                _lastRateReductionTime = DateTime.Now;
            }
        }


        /// <summary>
        ///     Compares the actual upload rate with the upload rate that we are supposed to be limiting them to
        ///     (UploadRateForRecip)
        /// </summary>
        /// <returns>True if the upload rate for recip is greater than the actual upload rate</returns>
        internal bool IsUnderUploadLimit()
        {
            return _uploadRateForRecip > Monitor.UploadSpeed;
        }


        /// <summary>
        ///     Stolen from reference BitTyrant implementation (see org.gudy.azureus2.core3.peer.TyrantStats)
        /// </summary>
        /// <param name="uploadRate">Upload rate of peer</param>
        /// <returns>Estimated active set size of peer</returns>
        internal static int GetActiveSetSize(int uploadRate)
        {
            if (uploadRate < 11)
                return 2;
            if (uploadRate < 35)
                return 3;
            if (uploadRate < 80)
                return 4;
            if (uploadRate < 200)
                return 5;
            if (uploadRate < 350)
                return 6;
            if (uploadRate < 600)
                return 7;
            if (uploadRate < 900)
                return 8;
            return 9;
        }

        #endregion BitTyrant
    }
}