//
// ClientEngine.cs
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
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Text;
using Universal.Torrent.Client.Args;
using Universal.Torrent.Client.ConnectionListeners;
using Universal.Torrent.Client.Managers;
using Universal.Torrent.Client.PieceWriter;
using Universal.Torrent.Client.RateLimiters;
using Universal.Torrent.Client.Settings;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client
{
    /// <summary>
    ///     The Engine that contains the TorrentManagers
    /// </summary>
    public class ClientEngine : IDisposable
    {
        internal static MainLoop MainLoop = new MainLoop();
        private static readonly Random Random = new Random();

        #region Global Constants

        // To support this I need to ensure that the transition from
        // InitialSeeding -> Regular seeding either closes all existing
        // connections or sends HaveAll messages, or sends HaveMessages.
        public static readonly bool SupportsInitialSeed = true;
        public static readonly bool SupportsLocalPeerDiscovery = true;
        public static readonly bool SupportsWebSeed = true;
        public static readonly bool SupportsExtended = true;
        public static readonly bool SupportsFastPeer = true;
        public static readonly bool SupportsEncryption = true;
        public static readonly bool SupportsEndgameMode = true;
#if !DISABLE_DHT
        public static readonly bool SupportsDht = true;
#else
        public static readonly bool SupportsDht = false;
#endif
        internal const int TickLength = 500; // A logic tick will be performed every TickLength miliseconds

        #endregion

        #region Events

        public event EventHandler<StatsUpdateEventArgs> StatsUpdate;
        public event EventHandler<CriticalExceptionEventArgs> CriticalException;

        public event EventHandler<TorrentEventArgs> TorrentRegistered;
        public event EventHandler<TorrentEventArgs> TorrentUnregistered;

        #endregion

        #region Member Variables

        internal static readonly BufferManager BufferManager = new BufferManager();

        private readonly ListenManager _listenManager;
        // Listens for incoming connections and passes them off to the correct TorrentManager

        private readonly LocalPeerManager _localPeerManager;
        private readonly LocalPeerListener _localPeerListener;
        private int _tickCount;
        private readonly List<TorrentManager> _torrents;
        private readonly ReadOnlyCollection<TorrentManager> _torrentsReadonly;
        private RateLimiterGroup _uploadLimiter;
        private RateLimiterGroup _downloadLimiter;

        #endregion

        #region Properties

        public ConnectionManager ConnectionManager { get; }

#if !DISABLE_DHT
        public IDhtEngine DhtEngine { get; private set; }
#endif
        public DiskManager DiskManager { get; }

        public bool Disposed { get; private set; }

        public PeerListener Listener { get; }

        public bool LocalPeerSearchEnabled
        {
            get { return _localPeerListener.Status != ListenerStatus.NotListening; }
            set
            {
                if (value && !LocalPeerSearchEnabled)
                    _localPeerListener.Start();
                else if (!value && LocalPeerSearchEnabled)
                    _localPeerListener.Stop();
            }
        }

        public bool IsRunning { get; private set; }

        public string PeerId { get; }

        public EngineSettings Settings { get; }

        public IList<TorrentManager> Torrents => _torrentsReadonly;

        #endregion

        #region Constructors

        public ClientEngine(EngineSettings settings)
            : this(settings, new DiskWriter())
        {
        }

        public ClientEngine(EngineSettings settings, PieceWriter.PieceWriter writer)
            : this(settings, new SocketListener(new IPEndPoint(IPAddress.Any, 0)), writer)

        {
        }

        public ClientEngine(EngineSettings settings, PeerListener listener)
            : this(settings, listener, new DiskWriter())
        {
        }

        public ClientEngine(EngineSettings settings, PeerListener listener, PieceWriter.PieceWriter writer)
        {
            Check.Settings(settings);
            Check.Listener(listener);
            Check.Writer(writer);

            Listener = listener;
            Settings = settings;

            ConnectionManager = new ConnectionManager(this);
            RegisterDht(new NullDhtEngine());
            DiskManager = new DiskManager(this, writer);
            _listenManager = new ListenManager(this);
            MainLoop.QueueTimeout(TimeSpan.FromMilliseconds(TickLength), delegate
            {
                if (IsRunning && !Disposed)
                    LogicTick();
                return !Disposed;
            });
            _torrents = new List<TorrentManager>();
            _torrentsReadonly = new ReadOnlyCollection<TorrentManager>(_torrents);
            CreateRateLimiters();
            PeerId = GeneratePeerId();

            _localPeerListener = new LocalPeerListener(this);
            _localPeerManager = new LocalPeerManager();
            LocalPeerSearchEnabled = SupportsLocalPeerDiscovery;
            _listenManager.Register(listener);
            // This means we created the listener in the constructor
            if (listener.Endpoint.Port == 0)
                listener.ChangeEndpoint(new IPEndPoint(IPAddress.Any, settings.ListenPort));
        }

        private void CreateRateLimiters()
        {
            var downloader = new RateLimiter();
            _downloadLimiter = new RateLimiterGroup();
            _downloadLimiter.Add(new DiskWriterLimiter(DiskManager));
            _downloadLimiter.Add(downloader);

            var uploader = new RateLimiter();
            _uploadLimiter = new RateLimiterGroup();
            _downloadLimiter.Add(new DiskWriterLimiter(DiskManager));
            _uploadLimiter.Add(uploader);

            MainLoop.QueueTimeout(TimeSpan.FromSeconds(1), delegate
            {
                downloader.UpdateChunks(Settings.GlobalMaxDownloadSpeed, TotalDownloadSpeed);
                uploader.UpdateChunks(Settings.GlobalMaxUploadSpeed, TotalUploadSpeed);
                return !Disposed;
            });
        }

        #endregion

        #region Methods

        public void ChangeListenEndpoint(IPEndPoint endpoint)
        {
            Check.Endpoint(endpoint);

            Settings.ListenPort = endpoint.Port;
            Listener.ChangeEndpoint(endpoint);
        }

        private void CheckDisposed()
        {
            if (Disposed)
                throw new ObjectDisposedException(GetType().Name);
        }

        public bool Contains(InfoHash infoHash)
        {
            CheckDisposed();
            if (infoHash == null)
                return false;

            return _torrents.Exists(m => m.InfoHash.Equals(infoHash));
        }

        public bool Contains(Common.Torrent torrent)
        {
            CheckDisposed();
            if (torrent == null)
                return false;

            return Contains(torrent.InfoHash);
        }

        public bool Contains(TorrentManager manager)
        {
            CheckDisposed();
            if (manager == null)
                return false;

            return Contains(manager.Torrent);
        }

        public void Dispose()
        {
            if (Disposed)
                return;

            Disposed = true;
            MainLoop.QueueWait(delegate
            {
                this.DhtEngine.Dispose();
                this.DiskManager.Dispose();
                this._listenManager.Dispose();
                this._localPeerListener.Stop();
                this._localPeerManager.Dispose();
            });
        }

        private static string GeneratePeerId()
        {
            var sb = new StringBuilder(20);

            sb.Append(VersionInfo.ClientVersion);
            lock (Random)
                while (sb.Length < 20)
                    sb.Append(Random.Next(0, 9));

            return sb.ToString();
        }

        public void PauseAll()
        {
            CheckDisposed();
            MainLoop.QueueWait(delegate
            {
                foreach (var manager in _torrents)
                    manager.Pause();
            });
        }

        public void Register(TorrentManager manager)
        {
            CheckDisposed();
            Check.Manager(manager);

            MainLoop.QueueWait(delegate
            {
                if (manager.Engine != null)
                    throw new TorrentException("This manager has already been registered");

                if (Contains(manager.Torrent))
                    throw new TorrentException("A manager for this torrent has already been registered");
                this._torrents.Add(manager);
                manager.PieceHashed += PieceHashed;
                manager.Engine = this;
                manager.DownloadLimiter.Add(_downloadLimiter);
                manager.UploadLimiter.Add(_uploadLimiter);
                if (DhtEngine != null && manager.Torrent?.Nodes != null && DhtEngine.State != DhtState.Ready)
                {
                    try
                    {
                        DhtEngine.Add(manager.Torrent.Nodes);
                    }
                    catch
                    {
                        // FIXME: Should log this somewhere, though it's not critical
                    }
                }
            });

            TorrentRegistered?.Invoke(this, new TorrentEventArgs(manager));
        }

        public void RegisterDht(IDhtEngine engine)
        {
            MainLoop.QueueWait(delegate
            {
                if (DhtEngine != null)
                {
                    DhtEngine.StateChanged -= DhtEngineStateChanged;
                    DhtEngine.Stop();
                    DhtEngine.Dispose();
                }
                DhtEngine = engine ?? new NullDhtEngine();
            });

            DhtEngine.StateChanged += DhtEngineStateChanged;
        }

        private void DhtEngineStateChanged(object o, EventArgs e)
        {
            if (DhtEngine.State != DhtState.Ready)
                return;

            MainLoop.Queue(delegate
            {
                foreach (var manager in _torrents.Where(manager => manager.CanUseDht))
                {
                    DhtEngine.Announce(manager.InfoHash, Listener.Endpoint.Port);
                    DhtEngine.GetPeers(manager.InfoHash);
                }
            });
        }

        public void StartAll()
        {
            CheckDisposed();
            MainLoop.QueueWait(delegate
            {
                foreach (var t in _torrents)
                    t.Start();
            });
        }

        public void StopAll()
        {
            CheckDisposed();

            MainLoop.QueueWait(delegate
            {
                foreach (var t in _torrents)
                    t.Stop();
            });
        }

        public int TotalDownloadSpeed
        {
            get
            {
                return
                    (int) Toolbox.Accumulate(_torrents, m => m.Monitor.DownloadSpeed);
            }
        }

        public int TotalUploadSpeed
        {
            get { return (int) Toolbox.Accumulate(_torrents, m => m.Monitor.UploadSpeed); }
        }

        public void Unregister(TorrentManager manager)
        {
            CheckDisposed();
            Check.Manager(manager);

            MainLoop.QueueWait(delegate
            {
                if (manager.Engine != this)
                    throw new TorrentException("The manager has not been registered with this engine");

                if (manager.State != TorrentState.Stopped)
                    throw new TorrentException("The manager must be stopped before it can be unregistered");

                this._torrents.Remove(manager);

                manager.PieceHashed -= PieceHashed;
                manager.Engine = null;
                manager.DownloadLimiter.Remove(_downloadLimiter);
                manager.UploadLimiter.Remove(_uploadLimiter);
            });

            TorrentUnregistered?.Invoke(this, new TorrentEventArgs(manager));
        }

        #endregion

        #region Private/Internal methods

        internal void Broadcast(TorrentManager manager)
        {
            if (LocalPeerSearchEnabled)
                _localPeerManager.Broadcast(manager);
        }

        private void LogicTick()
        {
            _tickCount++;

            if (_tickCount%(1000/TickLength) == 0)
            {
                DiskManager.WriteLimiter.UpdateChunks(Settings.MaxWriteRate, DiskManager.WriteRate);
                DiskManager.ReadLimiter.UpdateChunks(Settings.MaxReadRate, DiskManager.ReadRate);
            }

            ConnectionManager.TryConnect();
            foreach (var t in _torrents)
                t.Mode.Tick(_tickCount);

            RaiseStatsUpdate(new StatsUpdateEventArgs());
        }

        internal void RaiseCriticalException(CriticalExceptionEventArgs e)
        {
            Toolbox.RaiseAsyncEvent(CriticalException, this, e);
        }

        private void PieceHashed(object sender, PieceHashedEventArgs e)
        {
            if (e.TorrentManager.State != TorrentState.Hashing)
                DiskManager.QueueFlush(e.TorrentManager, e.PieceIndex);
        }

        internal void RaiseStatsUpdate(StatsUpdateEventArgs args)
        {
            Toolbox.RaiseAsyncEvent(StatsUpdate, this, args);
        }


        internal void Start()
        {
            CheckDisposed();
            IsRunning = true;
            if (Listener.Status == ListenerStatus.NotListening)
                Listener.Start();
        }


        internal void Stop()
        {
            CheckDisposed();
            // If all the torrents are stopped, stop ticking
            IsRunning = _torrents.Exists(m => m.State != TorrentState.Stopped);
            if (!IsRunning)
                Listener.Stop();
        }

        #endregion
    }
}