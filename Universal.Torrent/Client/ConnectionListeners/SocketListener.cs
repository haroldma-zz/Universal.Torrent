//
// ConnectionListener.cs
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
using System.Net;
using Windows.Networking.Sockets;
using Universal.Torrent.Client.Encryption;
using Universal.Torrent.Client.PeerConnections;
using Universal.Torrent.Client.Peers;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.ConnectionListeners
{
    /// <summary>
    ///     Accepts incoming connections and passes them off to the right TorrentManager
    /// </summary>
    public class SocketListener : PeerListener
    {
        private StreamSocketListener _listener;

        public SocketListener(IPEndPoint endpoint)
            : base(endpoint)
        {
        }

        public override async void Start()
        {
            if (Status == ListenerStatus.Listening)
                return;

            try
            {
                _listener = new StreamSocketListener();
                _listener.ConnectionReceived += ListenerOnConnectionReceived;
                await _listener.BindServiceNameAsync(Endpoint.Port.ToString());
                RaiseStatusChanged(ListenerStatus.Listening);
            }
            catch
            {
                RaiseStatusChanged(ListenerStatus.PortNotFree);
            }
        }

        private void ListenerOnConnectionReceived(StreamSocketListener sender,
            StreamSocketListenerConnectionReceivedEventArgs args)
        {
            var peerSocket = args.Socket;
            try
            {
                if (peerSocket == null)
                    return;

                var uri = new Uri("tcp://" + peerSocket.Information.RemoteHostName.RawName + ':' +
                                  peerSocket.Information.RemoteServiceName);
                var peer = new Peer("", uri, EncryptionTypes.All);
                IConnection connection = new TCPConnection(peerSocket, true);


                RaiseConnectionReceived(peer, connection, null);
            }
            catch
            {
                if (peerSocket == null)
                    return;
                peerSocket.Dispose();
            }
        }

        public override void Stop()
        {
            RaiseStatusChanged(ListenerStatus.NotListening);
            _listener?.Dispose();
            _listener = null;
        }
    }
}