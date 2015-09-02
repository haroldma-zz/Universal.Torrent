//
// AnnouceResponseMessage.cs
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
using System.Net;
using Universal.Torrent.Client.Peers;

namespace Universal.Torrent.Client.Messages.UdpTrackerMessages
{
    internal class AnnounceResponseMessage : UdpTrackerMessage
    {
        public AnnounceResponseMessage()
            : this(0, TimeSpan.Zero, 0, 0, new List<Peer>())
        {
        }

        public AnnounceResponseMessage(int transactionId, TimeSpan interval, int leechers, int seeders, List<Peer> peers)
            : base(1, transactionId)
        {
            Interval = interval;
            Leechers = leechers;
            Seeders = seeders;
            Peers = peers;
        }

        public override int ByteLength => (4*5 + Peers.Count*6);

        public int Leechers { get; private set; }

        public TimeSpan Interval { get; private set; }

        public int Seeders { get; private set; }

        public List<Peer> Peers { get; }

        public override void Decode(byte[] buffer, int offset, int length)
        {
            if (Action != ReadInt(buffer, offset))
                ThrowInvalidActionException();
            TransactionId = ReadInt(buffer, offset + 4);
            Interval = TimeSpan.FromSeconds(ReadInt(buffer, offset + 8));
            Leechers = ReadInt(buffer, offset + 12);
            Seeders = ReadInt(buffer, offset + 16);

            LoadPeerDetails(buffer, 20);
        }

        private void LoadPeerDetails(byte[] buffer, int offset)
        {
            while (offset <= (buffer.Length - 6))
            {
                var ip = ReadInt(buffer, ref offset);
                var port = (ushort) ReadShort(buffer, ref offset);
                try
                {
                    Peers.Add(new Peer("", new Uri("tcp://" + new IPEndPoint(new IPAddress(ip), port))));
                }
                catch
                {


                    // ignored
                }
            }
        }

    public override int Encode(byte[] buffer, int offset)
        {
            var written = offset;

            written += Write(buffer, written, Action);
            written += Write(buffer, written, TransactionId);
            written += Write(buffer, written, (int) Interval.TotalSeconds);
            written += Write(buffer, written, Leechers);
            written += Write(buffer, written, Seeders);

            for (var i = 0; i < Peers.Count; i++)
                Peers[i].CompactPeer(buffer, written + (i*6));

            return written - offset;
        }
    }
}