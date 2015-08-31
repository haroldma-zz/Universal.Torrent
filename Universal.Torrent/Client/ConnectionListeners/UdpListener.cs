#if !DISABLE_DHT
//
// UdpListener.cs
//
// Authors:
//   Alan McGovern <alan.mcgovern@gmail.com>
//
// Copyright (C) 2008 Alan McGovern
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
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using Windows.Networking;
using Windows.Networking.Sockets;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.ConnectionListeners
{
    public abstract class UdpListener : Listener
    {
        private readonly SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(1, 1);
        private DatagramSocket _client;

        protected UdpListener(IPEndPoint endpoint)
            : base(endpoint)
        {
        }

        protected abstract void OnMessageReceived(byte[] buffer, IPEndPoint endpoint);

        public virtual async void Send(byte[] buffer, IPEndPoint endpoint)
        {
            try
            {
                if (!Equals(endpoint.Address, IPAddress.Any))
                {
                    await _semaphoreSlim.WaitAsync();
                    try
                    {
                        if (_client != null)
                        {
                            var outputStreamAsync =
                                await _client.GetOutputStreamAsync(new HostName(endpoint.Address.ToString()),
                                    endpoint.Port.ToString());
                            await outputStreamAsync.WriteAsync(buffer.AsBuffer());
                            await outputStreamAsync.FlushAsync();
                        }
                    }
                    finally
                    {
                        _semaphoreSlim.Release();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("UdpListener could not send message: {0}", ex);
            }
        }

        public override async void Start()
        {
            if (Status == ListenerStatus.Listening)
                return;

            try
            {
                await _semaphoreSlim.WaitAsync();
                try
                {
                    _client = new DatagramSocket();
                    _client.MessageReceived += ClientOnMessageReceived;
                    await _client.BindServiceNameAsync(Endpoint.Port.ToString());
                    RaiseStatusChanged(ListenerStatus.Listening);
                }
                finally
                {
                    _semaphoreSlim.Release();
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception)
            {
                RaiseStatusChanged(ListenerStatus.PortNotFree);
            }
        }

        private void ClientOnMessageReceived(DatagramSocket sender, DatagramSocketMessageReceivedEventArgs args)
        {
            try
            {
                var dataReader = args.GetDataReader();
                var endpoint = new IPEndPoint(IPAddress.Parse(args.RemoteAddress.RawName), int.Parse(args.RemotePort));
                var buffer = new byte[dataReader.UnconsumedBufferLength];
                dataReader.ReadBytes(buffer);
                OnMessageReceived(buffer, endpoint);
            }
            catch
            {
                // ignored
            }
        }

        public override async void Stop()
        {
            RaiseStatusChanged(ListenerStatus.NotListening);
            try
            {
                await _semaphoreSlim.WaitAsync();
                try
                {
                    if (_client != null)
                    {
                        _client.Dispose();
                        _client = null;
                    }
                }
                finally
                {
                    _semaphoreSlim.Release();
                }
            }
            catch
            {
                // ignored
            }
        }
    }
}

#endif