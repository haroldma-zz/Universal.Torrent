//
// TCPConnection.cs
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
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace Universal.Torrent.Client.PeerConnections
{
    public class TCPConnection : IConnection
    {
        private readonly StreamSocket _socket;
        private bool _connected;

        #region Member Variables

        public bool CanReconnect => !IsIncoming;

        public bool Connected
        {
            get
            {
                lock (_socket)
                    return _connected;
            }
            private set { _connected = value; }
        }

        EndPoint IConnection.EndPoint => EndPoint;

        public IPEndPoint EndPoint { get; }

        public bool IsIncoming { get; }

        public Uri Uri { get; }

        #endregion

        #region Constructors

        public TCPConnection(Uri uri)
            : this(new StreamSocket(), new IPEndPoint(IPAddress.Parse(uri.Host), uri.Port), false)
        {
            Uri = uri;
        }

        public TCPConnection(StreamSocket socket, bool isIncoming)
            : this(
                socket,
                new IPEndPoint(IPAddress.Parse(socket.Information.RemoteHostName.RawName),
                    int.Parse(socket.Information.RemoteServiceName)), isIncoming)
        {
            Connected = true;
        }


        private TCPConnection(StreamSocket socket, IPEndPoint endpoint, bool isIncoming)
        {
            _socket = socket;
            EndPoint = endpoint;
            IsIncoming = isIncoming;
        }

        #endregion

        #region Async Methods

        public byte[] AddressBytes => EndPoint.Address.GetAddressBytes();

        public async Task ConnectAsync(CancellationToken token)
        {
            var ip = EndPoint.Address.ToString();
            await _socket.ConnectAsync(new HostName(ip), EndPoint.Port.ToString()).AsTask(token);
            Connected = true;
        }

        public IAsyncResult BeginConnect(AsyncCallback peerEndCreateConnection, object state)
        {
            throw new NotImplementedException();
        }

        public async Task<uint> ReceiveAsync(byte[] buffer, int offset, int count)
        {
            uint num;
            if (!Connected)
            {
                num = 0U;
            }
            else
            {
                var ibuffer = await _socket.InputStream.ReadAsync(buffer.AsBuffer(offset, count),
                    (uint) count, InputStreamOptions.Partial);
                num = ibuffer.Length;
            }
            return num;
        }

        public IAsyncResult BeginReceive(byte[] buffer, int offset, int count, AsyncCallback asyncCallback, object state)
        {
            throw new NotImplementedException();
        }

        public async Task<uint> SendAsync(byte[] buffer, int offset, int count)
        {
            uint num1;
            if (!Connected)
            {
                num1 = 0U;
            }
            else
            {
                var num = await _socket.OutputStream.WriteAsync(buffer.AsBuffer(offset, count));
                num1 = !await _socket.OutputStream.FlushAsync() ? 0U : num;
            }
            return num1;
        }

        public IAsyncResult BeginSend(byte[] buffer, int offset, int count, AsyncCallback asyncCallback, object state)
        {
            throw new NotImplementedException();
        }

        public void Dispose()
        {
            lock (_socket)
            {
                Connected = false;
                _socket.Dispose();
            }
        }

        public void EndConnect(IAsyncResult result)
        {
            throw new NotImplementedException();
        }

        public int EndSend(IAsyncResult result)
        {
            throw new NotImplementedException();
        }

        public int EndReceive(IAsyncResult result)
        {
            throw new NotImplementedException();
        }

        #endregion
    }
}