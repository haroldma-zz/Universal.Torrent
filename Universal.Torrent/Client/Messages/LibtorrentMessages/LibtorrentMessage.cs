//
// LibtorrentMessage.cs
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
using Universal.Torrent.Client.Exceptions;
using Universal.Torrent.Client.Managers;
using Universal.Torrent.Client.Messages.uTorrent;

namespace Universal.Torrent.Client.Messages.LibtorrentMessages
{
    public abstract class ExtensionMessage : PeerMessage
    {
        internal static readonly byte MessageId = 20;
        private static readonly Dictionary<byte, CreateMessage> MessageDict;
        private static readonly byte NextId;

        internal static readonly List<ExtensionSupport> SupportedMessages = new List<ExtensionSupport>();

        static ExtensionMessage()
        {
            MessageDict = new Dictionary<byte, CreateMessage>();

            Register(NextId++, delegate { return new ExtendedHandshakeMessage(); });

            Register(NextId, delegate { return new LTChat(); });
            SupportedMessages.Add(new ExtensionSupport("LT_chat", NextId++));

            Register(NextId, delegate { return new LTMetadata(); });
            SupportedMessages.Add(new ExtensionSupport("ut_metadata", NextId++));

            Register(NextId, delegate { return new PeerExchangeMessage(); });
            SupportedMessages.Add(new ExtensionSupport("ut_pex", NextId++));
        }

        protected ExtensionMessage(byte messageId)
        {
            ExtensionId = messageId;
        }

        public byte ExtensionId { get; protected set; }

        public static void Register(byte identifier, CreateMessage creator)
        {
            if (creator == null)
                throw new ArgumentNullException(nameof(creator));

            lock (MessageDict)
                MessageDict.Add(identifier, creator);
        }

        protected static ExtensionSupport CreateSupport(string name)
        {
            return SupportedMessages.Find(s => s.Name == name);
        }

        public new static PeerMessage DecodeMessage(byte[] buffer, int offset, int count, TorrentManager manager)
        {
            CreateMessage creator;

            if (!ClientEngine.SupportsExtended)
                throw new MessageException("Extension messages are not supported");

            lock (MessageDict)
            {
                if (!MessageDict.TryGetValue(buffer[offset], out creator))
                    throw new ProtocolException("Unknown extension message received");
            }

            var message = creator(manager);
            message.Decode(buffer, offset + 1, count - 1);
            return message;
        }
    }
}