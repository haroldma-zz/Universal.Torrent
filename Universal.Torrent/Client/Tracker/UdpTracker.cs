using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Universal.Torrent.Client.Args;
using Universal.Torrent.Client.Messages.UdpTrackerMessages;
using Universal.Torrent.Client.Peers;

namespace Universal.Torrent.Client.Tracker
{
    public class UdpTracker : Tracker
    {
        private readonly SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);
        private TaskCompletionSource<byte[]> _taskCompletionSource;
        private long _connectionId;
        private bool _hasConnected;
        internal TimeSpan RetryDelay;
        private readonly object _lock = new object();
        private int _timeout;
        private readonly DatagramSocket _tracker;

        public UdpTracker(Uri announceUrl)
            : base(announceUrl)
        {
            CanScrape = true;
            CanAnnounce = true;
            RetryDelay = TimeSpan.FromSeconds(15);
            _tracker = new DatagramSocket();
            _tracker.MessageReceived += TrackerOnMessageReceived;
        }

        private void TrackerOnMessageReceived(DatagramSocket sender, DatagramSocketMessageReceivedEventArgs args)
        {
            if (_taskCompletionSource == null || _taskCompletionSource.Task.IsCompleted)
                return;
            try
            {
                var dataReader = args.GetDataReader();
                var result = new byte[dataReader.UnconsumedBufferLength];
                dataReader.ReadBytes(result);
                _taskCompletionSource.SetResult(result);
            }
            catch (Exception ex)
            {
                lock (_lock)
                    _timeout = 0;
                _taskCompletionSource.TrySetException(ex);
            }
        }

        private async Task ConnectAsync()
        {
            var connectMessage = new ConnectMessage();
            var port = Uri.IsDefaultPort ? 80 : Uri.Port;

            await _tracker.ConnectAsync(new HostName(Uri.Host), port.ToString());

            var responseBytes = await SendAndReceiveAsync(connectMessage);
            var msg = Receive(connectMessage, responseBytes);

            if (msg == null)
                throw new Exception(FailureMessage);

            var rsp = msg as ConnectResponseMessage;
            if (rsp == null)
            {
                FailureMessage = ((ErrorMessage) msg).Error;
                throw new Exception(FailureMessage);
            }
            _connectionId = rsp.ConnectionId;
            _hasConnected = true;
        }

        public override string ToString()
        {
            return "udptracker:" + _connectionId;
        }

        #region announce

        public override async void Announce(AnnounceParameters parameters, object state)
        {
            try
            {
                await _semaphoreSlim.WaitAsync();
                try
                {
                    if (!_hasConnected)
                        await ConnectAsync();
                    await DoAnnounceAsync(parameters, state);
                }
                finally
                {
                    _semaphoreSlim.Release();
                }
            }
            catch (Exception ex)
            {
                FailureMessage = ex.Message;
                DoAnnounceComplete(false, state, new List<Peer>());
            }
        }

        private async Task DoAnnounceAsync(AnnounceParameters parameter, object state)
        {
            var announceMessage = new AnnounceMessage(DateTime.Now.GetHashCode(), _connectionId, parameter);
            var responseBytes = await SendAndReceiveAsync(announceMessage);

            var msg = Receive(announceMessage, responseBytes);

            if (!(msg is AnnounceResponseMessage))
                throw new Exception(FailureMessage);

            MinUpdateInterval = ((AnnounceResponseMessage) msg).Interval;
            CompleteAnnounce(msg, state);
        }

        private void CompleteAnnounce(UdpTrackerMessage message, object state)
        {
            var error = message as ErrorMessage;
            if (error != null)
            {
                FailureMessage = error.Error;
                DoAnnounceComplete(false, state, new List<Peer>());
            }
            else
            {
                var response = (AnnounceResponseMessage) message;
                DoAnnounceComplete(true, state, response.Peers);

                //TODO seeders and leechers is not used in event.
            }
        }

        private void DoAnnounceComplete(bool successful, object state, List<Peer> peers)
        {
            RaiseAnnounceComplete(new AnnounceResponseEventArgs(this, state, successful, peers));
        }

        #endregion

        #region scrape

        public override async void Scrape(ScrapeParameters parameters, object state)
        {
            try
            {
                await _semaphoreSlim.WaitAsync();
                try
                {
                    if (!_hasConnected)
                        await ConnectAsync();
                    await DoScrapeAsync(parameters, state);
                }
                finally
                {
                    _semaphoreSlim.Release();
                }
            }
            catch
            {
                DoScrapeComplete(false, state);
            }
        }

        private async Task DoScrapeAsync(ScrapeParameters parameters, object state)
        {
            //strange because here only one infohash???
            //or get all torrent infohash so loop on torrents of client engine
            var infohashs = new List<byte[]>(1) {parameters.InfoHash.Hash};
            var message = new ScrapeMessage(DateTime.Now.GetHashCode(), _connectionId, infohashs);

            var responseBytes = await SendAndReceiveAsync(message);
            var udpTrackerMessage = Receive(message, responseBytes);

            if (!(udpTrackerMessage is ScrapeResponseMessage))
                DoScrapeComplete(false, state);
            else
                CompleteScrape(udpTrackerMessage, state);
        }

        private void CompleteScrape(UdpTrackerMessage message, object state)
        {
            var error = message as ErrorMessage;
            if (error != null)
            {
                FailureMessage = error.Error;
                DoScrapeComplete(false, state);
            }
            else
            {
                //response.Scrapes not used for moment
                //ScrapeResponseMessage response = (ScrapeResponseMessage)message;
                DoScrapeComplete(true, state);
            }
        }

        private void DoScrapeComplete(bool successful, object state)
        {
            var e = new ScrapeResponseEventArgs(this, state, successful);
            RaiseScrapeComplete(e);
        }

        #endregion

        #region TimeOut System

        private async Task<byte[]> SendAndReceiveAsync(UdpTrackerMessage message)
        {
            lock (_lock)
                _timeout = 1;
            _taskCompletionSource = new TaskCompletionSource<byte[]>();
            await SendRequestAsync(message);
            return await _taskCompletionSource.Task;
        }

        private async Task SendRequestAsync(UdpTrackerMessage message)
        {
            var encodedMessage = message.Encode();
            var dataWriter = new DataWriter(_tracker.OutputStream);
            try
            {
                dataWriter.WriteBytes(encodedMessage);
                await dataWriter.StoreAsync();
            }
            finally
            {
                dataWriter.DetachStream();
            }
            
            // TODO: queue timeout
            if (_timeout <= 1)
            {
                // send message again
            }
        }

        private UdpTrackerMessage Receive(UdpTrackerMessage originalMessage, byte[] receivedMessage)
        {
            _timeout = 0; //we have receive so unactive the timeout
            var data = receivedMessage;
            var rsp = UdpTrackerMessage.DecodeMessage(data, 0, data.Length, MessageType.Response);

            if (originalMessage.TransactionId != rsp.TransactionId)
            {
                FailureMessage = "Invalid transaction Id in response from udp tracker!";
                return null; //to raise event fail outside
            }
            return rsp;
        }

        #endregion
    }
}