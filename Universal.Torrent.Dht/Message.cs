#if !DISABLE_DHT
//
// Message.cs
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


using Universal.Torrent.Bencoding;
using Universal.Torrent.Common;
using Universal.Torrent.Dht.Nodes;

namespace Universal.Torrent.Dht
{
    internal abstract class Message : Universal.Torrent.Client.Messages.Message
    {
        internal static bool UseVersionKey = true;

        private static readonly BEncodedString EmptyString = "";
        protected static readonly BEncodedString IdKey = "id";
        private static readonly BEncodedString TransactionIdKey = "t";
        private static readonly BEncodedString VersionKey = "v";
        private static readonly BEncodedString MessageTypeKey = "y";
        private static readonly BEncodedString DhtVersion = VersionInfo.DhtClientVersion;

        protected BEncodedDictionary Properties = new BEncodedDictionary();


        protected Message(BEncodedString messageType)
        {
            Properties.Add(TransactionIdKey, null);
            Properties.Add(MessageTypeKey, messageType);
            if (UseVersionKey)
                Properties.Add(VersionKey, DhtVersion);
        }

        protected Message(BEncodedDictionary dictionary)
        {
            Properties = dictionary;
        }

        public BEncodedString ClientVersion
        {
            get
            {
                BEncodedValue val;
                if (Properties.TryGetValue(VersionKey, out val))
                    return (BEncodedString) val;
                return EmptyString;
            }
        }

        internal abstract NodeId Id { get; }

        public BEncodedString MessageType => (BEncodedString) Properties[MessageTypeKey];

        public BEncodedValue TransactionId
        {
            get { return Properties[TransactionIdKey]; }
            set { Properties[TransactionIdKey] = value; }
        }

        public override int ByteLength => Properties.LengthInBytes();

        public override void Decode(byte[] buffer, int offset, int length)
        {
            Properties = BEncodedValue.Decode<BEncodedDictionary>(buffer, offset, length, false);
        }

        public override int Encode(byte[] buffer, int offset)
        {
            return Properties.Encode(buffer, offset);
        }

        public virtual void Handle(DhtEngine engine, Node node)
        {
            node.Seen();
        }
    }
}

#endif