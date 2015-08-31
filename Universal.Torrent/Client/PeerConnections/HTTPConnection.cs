//
// HTTPConnection.cs
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
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Universal.Torrent.Client.Managers;
using Universal.Torrent.Client.Messages;
using Universal.Torrent.Client.Messages.StandardMessages;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.PeerConnections
{
    public partial class HttpConnection : IConnection
    {
        private static readonly MethodInfo Method = typeof (WebHeaderCollection).GetMethod
            ("AddWithoutValidate", BindingFlags.Instance | BindingFlags.NonPublic);

        #region Constructors

        public HttpConnection(Uri uri)
        {
            if (uri == null)
                throw new ArgumentNullException(nameof(uri));
            if (!string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Scheme is not http");

            Uri = uri;

            ConnectionTimeout = TimeSpan.FromSeconds(10);
            _getResponseCallback = ClientEngine.MainLoop.Wrap(GotResponse);
            _receivedChunkCallback = ClientEngine.MainLoop.Wrap(ReceivedChunk);
            _requestMessages = new List<RequestMessage>();
            _webRequests = new Queue<KeyValuePair<WebRequest, int>>();
        }

        #endregion Constructors

        public Task ConnectAsync(CancellationToken token)
        {
            throw new NotImplementedException();
        }

        public IAsyncResult BeginConnect(AsyncCallback callback, object state)
        {
            var result = new AsyncResult(callback, state);
            result.Complete();
            return result;
        }

        public void EndConnect(IAsyncResult result)
        {
            // Do nothing
        }

        public Task<uint> ReceiveAsync(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public IAsyncResult BeginReceive(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            if (_receiveResult != null)
                throw new InvalidOperationException("Cannot call BeginReceive twice");

            _receiveResult = new HttpResult(callback, state, buffer, offset, count);
            try
            {
                // BeginReceive has been called *before* we have sent a piece request.
                // Wait for a piece request to be sent before allowing this to complete.
                if (_dataStream == null)
                    return _receiveResult;

                DoReceive();
                return _receiveResult;
            }
            catch (Exception ex)
            {
                _sendResult?.Complete(ex);

                _receiveResult?.Complete(ex);
            }
            return _receiveResult;
        }

        public int EndReceive(IAsyncResult result)
        {
            var r = CompleteTransfer(result, _receiveResult);
            _receiveResult = null;
            return r;
        }

        public Task<uint> SendAsync(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public IAsyncResult BeginSend(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            if (_sendResult != null)
                throw new InvalidOperationException("Cannot call BeginSend twice");
            _sendResult = new HttpResult(callback, state, buffer, offset, count);

            try
            {
                var bundle = DecodeMessages(buffer, offset, count);
                if (bundle == null)
                {
                    _sendResult.Complete(count);
                }
                else if (bundle.TrueForAll(m => m is RequestMessage))
                {
                    _requestMessages.AddRange(bundle.Cast<RequestMessage>());
                    // The RequestMessages are always sequential
                    var start = (RequestMessage) bundle[0];
                    var end = (RequestMessage) bundle[bundle.Count - 1];
                    CreateWebRequests(start, end);

                    var r = _webRequests.Dequeue();
                    _totalExpected = r.Value;
                    BeginGetResponse(r.Key, _getResponseCallback, r.Key);
                }
                else
                {
                    _sendResult.Complete(count);
                }
            }
            catch (Exception ex)
            {
                _sendResult.Complete(ex);
            }

            return _sendResult;
        }

        public int EndSend(IAsyncResult result)
        {
            var r = CompleteTransfer(result, _sendResult);
            _sendResult = null;
            return r;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _dataStream?.Dispose();
            _dataStream = null;
        }

        private List<PeerMessage> DecodeMessages(byte[] buffer, int offset, int count)
        {
            var off = offset;
            var c = count;

            try
            {
                if (_sendBuffer != BufferManager.EmptyBuffer)
                {
                    Buffer.BlockCopy(buffer, offset, _sendBuffer, _sendBufferCount, count);
                    _sendBufferCount += count;

                    off = 0;
                    c = _sendBufferCount;
                }
                var messages = new List<PeerMessage>();
                for (var i = off; i < off + c;)
                {
                    var message = PeerMessage.DecodeMessage(buffer, i, c + off - i, null);
                    messages.Add(message);
                    i += message.ByteLength;
                }
                ClientEngine.BufferManager.FreeBuffer(ref _sendBuffer);
                return messages;
            }
            catch (Exception)
            {
                if (_sendBuffer == BufferManager.EmptyBuffer)
                {
                    ClientEngine.BufferManager.GetBuffer(ref _sendBuffer, 16*1024);
                    Buffer.BlockCopy(buffer, offset, _sendBuffer, 0, count);
                    _sendBufferCount = count;
                }
                return null;
            }
        }


        private void ReceivedChunk(IAsyncResult result)
        {
            if (_disposed)
                return;

            try
            {
                var received = ((Task<int>) result).Result;
                if (received == 0)
                    throw new WebException("No futher data is available");

                _receiveResult.BytesTransferred += received;
                _currentRequest.TotalReceived += received;

                // We've received everything for this piece, so null it out
                if (_currentRequest.Complete)
                    _currentRequest = null;

                _totalExpected -= received;
                _receiveResult.Complete();
            }
            catch (Exception ex)
            {
                _receiveResult.Complete(ex);
            }
            finally
            {
                // If there are no more requests pending, complete the Send call
                if (_currentRequest == null && _requestMessages.Count == 0)
                    RequestCompleted();
            }
        }

        private void RequestCompleted()
        {
            _dataStream.Dispose();
            _dataStream = null;

            // Let MonoTorrent know we've finished requesting everything it asked for
            _sendResult?.Complete(_sendResult.Count);
        }

        private int CompleteTransfer(IAsyncResult supplied, HttpResult expected)
        {
            if (supplied == null)
                throw new ArgumentNullException(nameof(supplied));

            if (supplied != expected)
                throw new ArgumentException("Invalid IAsyncResult supplied");

            if (!expected.IsCompleted)
                expected.AsyncWaitHandle.WaitOne();

            if (expected.SavedException != null)
                throw expected.SavedException;

            return expected.BytesTransferred;
        }

        private void CreateWebRequests(RequestMessage start, RequestMessage end)
        {
            // Properly handle the case where we have multiple files
            // This is only implemented for single file torrents
            var uri = Uri;

            if (Uri.OriginalString.EndsWith("/"))
                uri = new Uri(uri, Manager.Torrent.Name + "/");

            // startOffset and endOffset are *inclusive*. I need to subtract '1' from the end index so that i
            // stop at the correct byte when requesting the byte ranges from the server
            var startOffset = (long) start.PieceIndex*Manager.Torrent.PieceLength + start.StartOffset;
            var endOffset = (long) end.PieceIndex*Manager.Torrent.PieceLength + end.StartOffset + end.RequestLength;

            foreach (var file in Manager.Torrent.Files)
            {
                var u = uri;
                if (Manager.Torrent.Files.Length > 1)
                    u = new Uri(u, file.Path);
                if (endOffset == 0)
                    break;

                // We want data from a later file
                if (startOffset >= file.Length)
                {
                    startOffset -= file.Length;
                    endOffset -= file.Length;
                }
                // We want data from the end of the current file and from the next few files
                else if (endOffset >= file.Length)
                {
                    var request = (HttpWebRequest) WebRequest.Create(u);
                    AddRange(request, startOffset, file.Length - 1);
                    _webRequests.Enqueue(new KeyValuePair<WebRequest, int>(request, (int) (file.Length - startOffset)));
                    startOffset = 0;
                    endOffset -= file.Length;
                }
                // All the data we want is from within this file
                else
                {
                    var request = (HttpWebRequest) WebRequest.Create(u);
                    AddRange(request, startOffset, endOffset - 1);
                    _webRequests.Enqueue(new KeyValuePair<WebRequest, int>(request, (int) (endOffset - startOffset)));
                    endOffset = 0;
                }
            }
        }

        private static void AddRange(WebRequest request, long startOffset, long endOffset)
        {
            Method.Invoke(request.Headers,
                new object[] {"Range", string.Format("bytes={0}-{1}", startOffset, endOffset)});
        }

        private void DoReceive()
        {
            var buffer = _receiveResult.Buffer;
            var offset = _receiveResult.Offset;
            var count = _receiveResult.Count;

            if (_currentRequest == null && _requestMessages.Count > 0)
            {
                _currentRequest = new HttpRequestData(_requestMessages[0]);
                _requestMessages.RemoveAt(0);
            }

            if (_totalExpected == 0)
            {
                if (_webRequests.Count == 0)
                {
                    _sendResult.Complete(_sendResult.Count);
                }
                else
                {
                    var r = _webRequests.Dequeue();
                    _totalExpected = r.Value;
                    BeginGetResponse(r.Key, _getResponseCallback, r.Key);
                }
                return;
            }

            if (_currentRequest != null && !_currentRequest.SentLength)
            {
                // The message length counts as the first four bytes
                _currentRequest.SentLength = true;
                _currentRequest.TotalReceived += 4;
                Message.Write(_receiveResult.Buffer, _receiveResult.Offset,
                    _currentRequest.TotalToReceive - _currentRequest.TotalReceived);
                _receiveResult.Complete(4);
                return;
            }
            if (_currentRequest != null && !_currentRequest.SentHeader)
            {
                _currentRequest.SentHeader = true;

                // We have *only* written the messageLength to the stream
                // Now we need to write the rest of the PieceMessage header
                var written = 0;
                written += Message.Write(buffer, offset + written, PieceMessage.MessageId);
                written += Message.Write(buffer, offset + written, _currentRequest.Request.PieceIndex);
                written += Message.Write(buffer, offset + written, _currentRequest.Request.StartOffset);
                count -= written;
                offset += written;
                _receiveResult.BytesTransferred += written;
                _currentRequest.TotalReceived += written;
            }

            _dataStream.ReadAsync(buffer, offset, count).ContinueWith(p => _receivedChunkCallback(p));
        }

        private void BeginGetResponse(WebRequest request, AsyncCallback callback, object state)
        {
            var result = request.BeginGetResponse(callback, state);
            ClientEngine.MainLoop.QueueTimeout(ConnectionTimeout, delegate
            {
                if (!result.IsCompleted)
                    request.Abort();
                return false;
            });
        }

        private void GotResponse(IAsyncResult result)
        {
            var r = (WebRequest) result.AsyncState;
            try
            {
                var response = r.EndGetResponse(result);
                _dataStream = response.GetResponseStream();

                if (_receiveResult != null)
                    DoReceive();
            }
            catch (Exception ex)
            {
                _sendResult?.Complete(ex);

                _receiveResult?.Complete(ex);
            }
        }

        private class HttpResult : AsyncResult
        {
            public readonly byte[] Buffer;
            public int BytesTransferred;
            public readonly int Count;
            public readonly int Offset;

            public HttpResult(AsyncCallback callback, object state, byte[] buffer, int offset, int count)
                : base(callback, state)
            {
                Buffer = buffer;
                Offset = offset;
                Count = count;
            }

            public void Complete(int bytes)
            {
                BytesTransferred = bytes;
                base.Complete();
            }
        }

        #region Member Variables

        private byte[] _sendBuffer = BufferManager.EmptyBuffer;
        private int _sendBufferCount;
        private Stream _dataStream;
        private HttpRequestData _currentRequest;
        private bool _disposed;
        private readonly AsyncCallback _getResponseCallback;
        private readonly AsyncCallback _receivedChunkCallback;
        private HttpResult _receiveResult;
        private readonly List<RequestMessage> _requestMessages;
        private HttpResult _sendResult;
        private int _totalExpected;
        private readonly Queue<KeyValuePair<WebRequest, int>> _webRequests;

        public byte[] AddressBytes => new byte[4];

        public bool CanReconnect => false;

        public bool Connected => true;

        internal TimeSpan ConnectionTimeout { get; set; }

        EndPoint IConnection.EndPoint => null;

        public bool IsIncoming => false;

        public TorrentManager Manager { get; set; }

        public Uri Uri { get; }

        #endregion
    }
}