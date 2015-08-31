//
// ReceiveMessageState.cs
//
// Authors:
//   Alan McGovern alan.mcgovern@gmail.com
//
// Copyright (C) 2010 Alan McGovern
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

using Universal.Torrent.Client.Encryption.IEncryption;
using Universal.Torrent.Client.Managers;
using Universal.Torrent.Client.PeerConnections;
using Universal.Torrent.Client.RateLimiters;
using Universal.Torrent.Common;

// ReSharper disable once CheckNamespace
namespace Universal.Torrent.Client
{
    internal static partial class PeerIO
    {
        // ReSharper disable once ClassNeverInstantiated.Local
        private class ReceiveMessageState : ICacheable
        {
            public byte[] Buffer { get; set; }

            public AsyncMessageReceivedCallback Callback { get; private set; }

            public IConnection Connection { get; private set; }

            public IEncryption Decryptor { get; private set; }

            public IRateLimiter RateLimiter { get; private set; }

            public TorrentManager Manager { get; private set; }

            public ConnectionMonitor ManagerMonitor { get; private set; }

            public ConnectionMonitor PeerMonitor { get; private set; }

            public object State { get; private set; }

            public void Initialise()
            {
                Initialise(null, null, null, null, null, null, null, null);
            }

            public ReceiveMessageState Initialise(IConnection connection, IEncryption decryptor, IRateLimiter limiter,
                ConnectionMonitor peerMonitor, TorrentManager manager, byte[] buffer,
                AsyncMessageReceivedCallback callback, object state)
            {
                Connection = connection;
                Decryptor = decryptor;
                Manager = manager;
                Buffer = buffer;
                PeerMonitor = peerMonitor;
                RateLimiter = limiter;
                ManagerMonitor = manager?.Monitor;
                Callback = callback;
                State = state;
                return this;
            }
        }
    }
}