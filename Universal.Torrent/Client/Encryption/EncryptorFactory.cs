//
// EncryptorFactory.cs
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
using Universal.Torrent.Client.Encryption.IEncryption;
using Universal.Torrent.Client.Messages.StandardMessages;
using Universal.Torrent.Client.PeerConnections;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.Encryption
{
    internal static class EncryptorFactory
    {
        private static readonly AsyncCallback CompletedEncryptedHandshakeCallback = CompletedEncryptedHandshake;
        private static readonly AsyncIOCallback HandshakeReceivedCallback = HandshakeReceived;

        private static EncryptionTypes CheckRc4(PeerId id)
        {
            // If the connection is *not* incoming, then it will be associated with an Engine
            // so we can check what encryption levels the engine allows.
            var t = id.Connection.IsIncoming ? EncryptionTypes.All : id.TorrentManager.Engine.Settings.AllowedEncryption;

            // We're allowed use encryption if the engine settings allow it and the peer supports it
            // Binary AND both the engine encryption and peer encryption and check what levels are supported
            t &= id.Peer.Encryption;
            return t;
        }

        internal static IAsyncResult BeginCheckEncryption(PeerId id, int bytesToReceive, AsyncCallback callback,
            object state)
        {
            return BeginCheckEncryption(id, bytesToReceive, callback, state, null);
        }

        internal static IAsyncResult BeginCheckEncryption(PeerId id, int bytesToReceive, AsyncCallback callback,
            object state, InfoHash[] sKeys)
        {
            var result = new EncryptorAsyncResult(id, callback, state) {SKeys = sKeys};

            var c = id.Connection;
            ClientEngine.MainLoop.QueueTimeout(TimeSpan.FromSeconds(10), delegate
            {
                if (id.Encryptor == null || id.Decryptor == null)
                    id.CloseConnection();
                return false;
            });

            try
            {
                // If the connection is incoming, receive the handshake before
                // trying to decide what encryption to use
                if (id.Connection.IsIncoming)
                {
                    result.Buffer = new byte[bytesToReceive];
                    NetworkIO.EnqueueReceive(c, result.Buffer, 0, result.Buffer.Length, null, null, null,
                        HandshakeReceivedCallback, result);
                }
                else
                {
                    var usable = CheckRc4(id);
                    var hasPlainText = Toolbox.HasEncryption(usable, EncryptionTypes.PlainText);
                    var hasRc4 = Toolbox.HasEncryption(usable, EncryptionTypes.RC4Full) ||
                                 Toolbox.HasEncryption(usable, EncryptionTypes.RC4Header);
                    if (id.Engine.Settings.PreferEncryption)
                    {
                        if (hasRc4)
                        {
                            result.EncSocket = new PeerAEncryption(id.TorrentManager.InfoHash, usable);
                            result.EncSocket.BeginHandshake(id.Connection, CompletedEncryptedHandshakeCallback, result);
                        }
                        result.Complete();
                    }
                    else
                    {
                        if (hasPlainText)
                        {
                            result.Complete();
                        }
                        else
                        {
                            result.EncSocket = new PeerAEncryption(id.TorrentManager.InfoHash, usable);
                            result.EncSocket.BeginHandshake(id.Connection, CompletedEncryptedHandshakeCallback, result);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Complete(ex);
            }
            return result;
        }

        internal static void EndCheckEncryption(IAsyncResult result, out byte[] initialData)
        {
            var r = (EncryptorAsyncResult) result;

            if (!r.IsCompleted)
                r.AsyncWaitHandle.WaitOne();

            if (r == null)
                throw new ArgumentException("Invalid async result");

            if (r.SavedException != null)
                throw r.SavedException;

            r.Id.Encryptor = r.Encryptor;
            r.Id.Decryptor = r.Decryptor;
            initialData = r.InitialData;

            r.AsyncWaitHandle.Dispose();
        }

        private static void HandshakeReceived(bool succeeded, int count, object state)
        {
            var result = (EncryptorAsyncResult) state;
            var connection = result.Id.Connection;

            try
            {
                if (!succeeded)
                    throw new EncryptionException("Couldn't receive the handshake");

                result.Available += count;
                var message = new HandshakeMessage();
                message.Decode(result.Buffer, 0, result.Buffer.Length);
                var valid = message.ProtocolString == VersionInfo.ProtocolStringV100;
                var usable = CheckRc4(result.Id);

                var canUseRc4 = Toolbox.HasEncryption(usable, EncryptionTypes.RC4Header) ||
                                Toolbox.HasEncryption(usable, EncryptionTypes.RC4Full);
                // If encryption is disabled and we received an invalid handshake - abort!
                if (valid)
                {
                    result.InitialData = result.Buffer;
                    result.Complete();
                    return;
                }
                if (!canUseRc4)
                {
                    result.Complete(new EncryptionException("Invalid handshake received and no decryption works"));
                    return;
                }
                // The data we just received was part of an encrypted handshake and was *not* the BitTorrent handshake
                result.EncSocket = new PeerBEncryption(result.SKeys, EncryptionTypes.All);
                result.EncSocket.BeginHandshake(connection, result.Buffer, 0, result.Buffer.Length,
                    CompletedEncryptedHandshakeCallback, result);
            }
            catch (Exception ex)
            {
                result.Complete(ex);
            }
        }

        private static void CompletedEncryptedHandshake(IAsyncResult result)
        {
            var r = (EncryptorAsyncResult) result.AsyncState;
            try
            {
                r.EncSocket.EndHandshake(result);

                r.Decryptor = r.EncSocket.Decryptor;
                r.Encryptor = r.EncSocket.Encryptor;
                r.InitialData = r.EncSocket.InitialData;
            }
            catch (Exception ex)
            {
                r.SavedException = ex;
            }

            r.Complete();

            result.AsyncWaitHandle.Dispose();
            //r.AsyncWaitHandle.Close();
        }

        private class EncryptorAsyncResult : AsyncResult
        {
            public readonly PeerId Id;
            public int Available;
            public byte[] Buffer;
            public IEncryption.IEncryption Decryptor;
            public IEncryption.IEncryption Encryptor;
            public IEncryptor.IEncryptor EncSocket;
            public byte[] InitialData;
            public InfoHash[] SKeys;


            public EncryptorAsyncResult(PeerId id, AsyncCallback callback, object state)
                : base(callback, state)
            {
                Id = id;
                Decryptor = new PlainTextEncryption();
                Encryptor = new PlainTextEncryption();
            }
        }
    }
}