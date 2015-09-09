using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Universal.Torrent.Client.Args;
using Universal.Torrent.Client.ConnectionListeners;
using Universal.Torrent.Client.Encryption;
using Universal.Torrent.Client.Encryption.IEncryption;
using Universal.Torrent.Client.Exceptions;
using Universal.Torrent.Client.Messages;
using Universal.Torrent.Client.Messages.StandardMessages;
using Universal.Torrent.Client.PeerConnections;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.Managers
{
    /// <summary>
    ///     Instance methods of this class are threadsafe
    /// </summary>
    public class ListenManager : IDisposable
    {
        #region Constructors

        internal ListenManager(ClientEngine engine)
        {
            Engine = engine;
            Listeners = new MonoTorrentCollection<PeerListener>();
            _endCheckEncryptionCallback = ClientEngine.MainLoop.Wrap(EndCheckEncryption);
            _handshakeReceivedCallback =
                (a, b, c) => ClientEngine.MainLoop.Queue(() => OnPeerHandshakeReceived(a, b, c));
        }

        #endregion Constructors

        private void ConnectionReceived(object sender, NewConnectionEventArgs e)
        {
            if (Engine.ConnectionManager.ShouldBanPeer(e.Peer))
            {
                e.Connection.Dispose();
                return;
            }
            var id = new PeerId(e.Peer, e.TorrentManager) {Connection = e.Connection};

            //Debug.WriteLine("ListenManager - ConnectionReceived: {0}", id.Connection);

            if (id.Connection.IsIncoming)
            {
                var skeys = new List<InfoHash>();

                ClientEngine.MainLoop.QueueWait(delegate { skeys.AddRange(Engine.Torrents.Select(t => t.InfoHash)); });

                EncryptorFactory.BeginCheckEncryption(id, HandshakeMessage.HandshakeLength, _endCheckEncryptionCallback,
                    id, skeys.ToArray());
            }
            else
            {
                ClientEngine.MainLoop.Queue(delegate { Engine.ConnectionManager.ProcessFreshConnection(id); });
            }
        }

        private void EndCheckEncryption(IAsyncResult result)
        {
            var id = (PeerId) result.AsyncState;
            try
            {
                byte[] initialData;
                EncryptorFactory.EndCheckEncryption(result, out initialData);

                if (initialData != null && initialData.Length == HandshakeMessage.HandshakeLength)
                {
                    var message = new HandshakeMessage();
                    message.Decode(initialData, 0, initialData.Length);
                    HandleHandshake(id, message);
                }
                else if (initialData == null || initialData.Length > 0)
                {
                    throw new Exception("Argh. I can't handle this scenario. It also shouldn't happen. Ever.");
                }
                else
                {
                    PeerIO.EnqueueReceiveHandshake(id.Connection, id.Decryptor, _handshakeReceivedCallback, id);
                }
            }
            catch
            {
                id.Connection.Dispose();
            }
        }


        private void HandleHandshake(PeerId id, HandshakeMessage message)
        {
            TorrentManager man = null;
            try
            {
                if (message.ProtocolString != VersionInfo.ProtocolStringV100)
                    throw new ProtocolException("Invalid protocol string in handshake");
            }
            catch (Exception ex)
            {
                Debug.WriteLine(id.Connection, ex.Message);
                id.Connection.Dispose();
                return;
            }

            man = Engine.Torrents.FirstOrDefault(t => message.InfoHash == t.InfoHash);
            ClientEngine.MainLoop.QueueWait(delegate
            {
                foreach (var t in Engine.Torrents.Where(t => message.InfoHash == t.InfoHash))
                    man = t;
            });

            //FIXME: #warning FIXME: Don't stop the message loop until Dispose() and track all incoming connections
            if (man == null) // We're not hosting that torrent
            {
                //Debug.WriteLine("ListenManager - Handshake requested nonexistant torrent");
                id.Connection.Dispose();
                return;
            }
            if (man.State == TorrentState.Stopped)
            {
                Debug.WriteLine("ListenManager - Handshake requested for torrent which is not running");
                id.Connection.Dispose();
                return;
            }
            if (!man.Mode.CanAcceptConnections)
            {
                Debug.WriteLine("ListenManager - Current mode does not support connections");
                id.Connection.Dispose();
                return;
            }

            id.Peer.PeerId = message.PeerId;
            id.TorrentManager = man;

            // If the handshake was parsed properly without encryption, then it definitely was not encrypted. If this is not allowed, abort
            if ((id.Encryptor is PlainTextEncryption &&
                 !Toolbox.HasEncryption(Engine.Settings.AllowedEncryption, EncryptionTypes.PlainText)) &&
                ClientEngine.SupportsEncryption)
            {
                Debug.WriteLine("ListenManager - Encryption is required but was not active");
                id.Connection.Dispose();
                return;
            }

            message.Handle(id);
            Debug.WriteLine("ListenManager - Handshake successful handled");

            id.ClientApp = new Software(message.PeerId);

            message = new HandshakeMessage(id.TorrentManager.InfoHash, Engine.PeerId, VersionInfo.ProtocolStringV100);
            var callback = Engine.ConnectionManager.IncomingConnectionAcceptedCallback;
            PeerIO.EnqueueSendMessage(id.Connection, id.Encryptor, message, id.TorrentManager.UploadLimiter,
                id.Monitor, id.TorrentManager.Monitor, callback, id);
        }

        /// <summary>
        ///     Called when [peer handshake received].
        /// </summary>
        /// <param name="succeeded">if set to <c>true</c> [succeeded].</param>
        /// <param name="message">The message.</param>
        /// <param name="state">The state.</param>
        private void OnPeerHandshakeReceived(bool succeeded, PeerMessage message, object state)
        {
            var id = (PeerId) state;

            try
            {
                if (succeeded)
                    HandleHandshake(id, (HandshakeMessage) message);
                else
                    id.Connection.Dispose();
            }
            catch (Exception)
            {
                Debug.WriteLine(id.Connection, "ListenManager - Socket exception receiving handshake");
                id.Connection.Dispose();
            }
        }

        #region Member Variables

        private readonly AsyncCallback _endCheckEncryptionCallback;
        private readonly AsyncMessageReceivedCallback _handshakeReceivedCallback;

        #endregion Member Variables

        #region Properties

        public MonoTorrentCollection<PeerListener> Listeners { get; }

        internal ClientEngine Engine { get; }

        #endregion Properties

        #region Public Methods

        public void Dispose()
        {
        }

        public void Register(PeerListener listener)
        {
            listener.ConnectionReceived += ConnectionReceived;
        }

        public void Unregister(PeerListener listener)
        {
            listener.ConnectionReceived -= ConnectionReceived;
        }

        #endregion Public Methods
    }
}