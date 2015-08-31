//
// NetworkIO.cs
//
// Authors:
//   Alan McGovern alan.mcgovern@gmail.com
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
using System.Collections.Generic;
using System.Threading;
using Universal.Torrent.Client.Managers;
using Universal.Torrent.Client.Messages;
using Universal.Torrent.Client.PeerConnections;
using Universal.Torrent.Client.Peers;
using Universal.Torrent.Client.RateLimiters;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client
{
    public delegate void AsyncIOCallback(bool succeeded, int transferred, object state);

    public delegate void AsyncMessageReceivedCallback(bool succeeded, PeerMessage message, object state);

    internal class AsyncConnectState
    {
        public IConnection Connection;
        public TorrentManager Manager;
        public Peer Peer;

        public AsyncConnectState(TorrentManager manager, Peer peer, IConnection connection)
        {
            Manager = manager;
            Peer = peer;
            Connection = connection;
        }
    }

    internal partial class NetworkIO
    {
        // The biggest message is a PieceMessage which is 16kB + some overhead
        // so send in chunks of 2kB + a little so we do 8 transfers per piece.
        private const int ChunkLength = 2048 + 32;

        private static readonly Queue<AsyncIOState> ReceiveQueue = new Queue<AsyncIOState>();
        private static readonly Queue<AsyncIOState> SendQueue = new Queue<AsyncIOState>();

        private static readonly ICache<AsyncConnectState> ConnectCache =
            new Cache<AsyncConnectState>(true).Synchronize();

        private static readonly ICache<AsyncIOState> TransferCache = new Cache<AsyncIOState>(true).Synchronize();

        private static int _halfOpens;

        static NetworkIO()
        {
            ClientEngine.MainLoop.QueueTimeout(TimeSpan.FromMilliseconds(100), delegate
            {
                lock (SendQueue)
                {
                    var count = SendQueue.Count;
                    for (var i = 0; i < count; i++)
                        SendOrEnqueue(SendQueue.Dequeue());
                }
                lock (ReceiveQueue)
                {
                    var count = ReceiveQueue.Count;
                    for (var i = 0; i < count; i++)
                        ReceiveOrEnqueue(ReceiveQueue.Dequeue());
                }
                return true;
            });
        }

        public static int HalfOpens => _halfOpens;

        public static async void EnqueueConnect(IConnection connection, AsyncIOCallback callback, object state)
        {
            var data = ConnectCache.Dequeue().Initialise(connection, callback, state);

            try
            {
                using (var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(30.0)))
                {
                    Interlocked.Increment(ref _halfOpens);
                    await connection.ConnectAsync(cancellationTokenSource.Token).ConfigureAwait(false);
                }
                FinishConnect(data);
            }
            catch
            {
                Interlocked.Decrement(ref _halfOpens);
                callback(false, 0, state);
                ConnectCache.Enqueue(data);
            }
        }

        private static void FinishConnect(AsyncConnectState data)
        {
            try
            {
                Interlocked.Decrement(ref _halfOpens);
                data.Callback(true, 0, data.State);
            }
            catch
            {
                data.Callback(false, 0, data.State);
            }
            finally
            {
                ConnectCache.Enqueue(data);
            }
        }

        public static void EnqueueReceive(IConnection connection, byte[] buffer, int offset, int count,
            IRateLimiter rateLimiter, ConnectionMonitor peerMonitor, ConnectionMonitor managerMonitor,
            AsyncIOCallback callback, object state)
        {
            var data = TransferCache.Dequeue()
                .Initialise(connection, buffer, offset, count, callback, state, rateLimiter, peerMonitor, managerMonitor);
            lock (ReceiveQueue)
                ReceiveOrEnqueue(data);
        }

        public static void EnqueueSend(IConnection connection, byte[] buffer, int offset, int count,
            IRateLimiter rateLimiter, ConnectionMonitor peerMonitor, ConnectionMonitor managerMonitor,
            AsyncIOCallback callback, object state)
        {
            var data = TransferCache.Dequeue()
                .Initialise(connection, buffer, offset, count, callback, state, rateLimiter, peerMonitor, managerMonitor);
            lock (SendQueue)
                SendOrEnqueue(data);
        }


        private static void FinishReceive(AsyncIOState data)
        {
            try
            {
                var transferred = (int) data.Transferred;
                if (transferred == 0)
                {
                    data.Callback(false, 0, data.State);
                    TransferCache.Enqueue(data);
                }
                else
                {
                    data.PeerMonitor?.BytesReceived(transferred, data.TransferType);
                    data.ManagerMonitor?.BytesReceived(transferred, data.TransferType);

                    data.Offset += transferred;
                    data.Remaining -= transferred;
                    if (data.Remaining == 0)
                    {
                        data.Callback(true, data.Count, data.State);
                        TransferCache.Enqueue(data);
                    }
                    else
                    {
                        lock (ReceiveQueue)
                            ReceiveOrEnqueue(data);
                    }
                }
            }
            catch
            {
                data.Callback(false, 0, data.State);
                TransferCache.Enqueue(data);
            }
        }

        private static void FinishSend(AsyncIOState data)
        {
            try
            {
                var transferred = (int) data.Transferred;
                if (transferred == 0)
                {
                    data.Callback(false, 0, data.State);
                    TransferCache.Enqueue(data);
                }
                else
                {
                    data.PeerMonitor?.BytesSent(transferred, data.TransferType);
                    data.ManagerMonitor?.BytesSent(transferred, data.TransferType);

                    data.Offset += transferred;
                    data.Remaining -= transferred;
                    if (data.Remaining == 0)
                    {
                        data.Callback(true, data.Count, data.State);
                        TransferCache.Enqueue(data);
                    }
                    else
                    {
                        lock (SendQueue)
                            SendOrEnqueue(data);
                    }
                }
            }
            catch
            {
                data.Callback(false, 0, data.State);
                TransferCache.Enqueue(data);
            }
        }

        private static async void ReceiveOrEnqueue(AsyncIOState data)
        {
            var count = Math.Min(ChunkLength, data.Remaining);
            if (data.RateLimiter == null || data.RateLimiter.TryProcess(1))
            {
                try
                {
                    data.Transferred = await data.Connection.ReceiveAsync(data.Buffer, data.Offset, count);
                    FinishReceive(data);
                }
                catch
                {
                    data.Callback(false, 0, data.State);
                    TransferCache.Enqueue(data);
                }
            }
            else
            {
                ReceiveQueue.Enqueue(data);
            }
        }

        private static async void SendOrEnqueue(AsyncIOState data)
        {
            var count = Math.Min(ChunkLength, data.Remaining);
            if (data.RateLimiter == null || data.RateLimiter.TryProcess(1))
            {
                try
                {
                    data.Transferred = await data.Connection.SendAsync(data.Buffer, data.Offset, count);
                    FinishSend(data);
                }
                catch
                {
                    data.Callback(false, 0, data.State);
                    TransferCache.Enqueue(data);
                }
            }
            else
            {
                SendQueue.Enqueue(data);
            }
        }
    }
}