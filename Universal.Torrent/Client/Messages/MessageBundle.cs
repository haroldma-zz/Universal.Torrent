using System;
using System.Collections.Generic;
using System.Linq;

namespace Universal.Torrent.Client.Messages
{
    public class MessageBundle : PeerMessage
    {
        public MessageBundle()
        {
            Messages = new List<PeerMessage>();
        }

        public MessageBundle(PeerMessage message)
            : this()
        {
            Messages.Add(message);
        }

        public List<PeerMessage> Messages { get; }

        public override int ByteLength
        {
            get
            {
                return Messages.Sum(t => t.ByteLength);
            }
        }

        public override void Decode(byte[] buffer, int offset, int length)
        {
            throw new InvalidOperationException();
        }

        public override int Encode(byte[] buffer, int offset)
        {
            var written = Messages.Aggregate(offset, (current, t) => current + t.Encode(buffer, current));

            return CheckWritten(written - offset);
        }
    }
}