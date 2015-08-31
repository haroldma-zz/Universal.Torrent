//
// LTMetadata.cs
//
// Authors:
//   Alan McGovern alan.mcgovern@gmail.com
//
// Copyright (C) 2009 Alan McGovern
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
using System.IO;
using Universal.Torrent.Bencoding;
using Universal.Torrent.Client.Exceptions;
using Universal.Torrent.Client.PeerConnections;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.Messages.LibtorrentMessages
{
    internal class LTMetadata : ExtensionMessage
    {
        public static readonly ExtensionSupport Support = CreateSupport("ut_metadata");
        private static readonly BEncodedString MessageTypeKey = "msg_type";
        private static readonly BEncodedString PieceKey = "piece";
        private static readonly BEncodedString TotalSizeKey = "total_size";
        internal static readonly int BlockSize = 16384; //16Kb

        private readonly BEncodedDictionary _dict;

        //this buffer contain all metadata when we send message 
        //and only a piece of metadata we receive message

        //only for register
        public LTMetadata()
            : base(Support.MessageId)
        {
        }

        public LTMetadata(PeerId id, eMessageType type, int piece)
            : this(id, type, piece, null)
        {
        }

        public LTMetadata(PeerId id, eMessageType type, int piece, byte[] metadata)
            : this(id.ExtensionSupports.MessageId(Support), type, piece, metadata)
        {
        }

        public LTMetadata(byte extensionId, eMessageType type, int piece, byte[] metadata)
            : this()
        {
            ExtensionId = extensionId;
            MetadataMessageType = type;
            MetadataPiece = metadata;
            Piece = piece;

            _dict = new BEncodedDictionary
            {
                {MessageTypeKey, (BEncodedNumber) (int) MetadataMessageType},
                {PieceKey, (BEncodedNumber) piece}
            };

            if (MetadataMessageType == eMessageType.Data)
            {
                Check.Metadata(metadata);
                _dict.Add(TotalSizeKey, (BEncodedNumber) metadata.Length);
            }
        }

        public int Piece { get; private set; }

        public byte[] MetadataPiece { get; private set; }

        internal eMessageType MetadataMessageType { get; private set; }

        public override int ByteLength
        {
            // 4 byte length, 1 byte BT id, 1 byte LT id, 1 byte payload
            get
            {
                var length = 4 + 1 + 1 + _dict.LengthInBytes();
                if (MetadataMessageType == eMessageType.Data)
                    length += Math.Min(MetadataPiece.Length - Piece*BlockSize, BlockSize);
                return length;
            }
        }

        public override void Decode(byte[] buffer, int offset, int length)
        {
            using (var reader = new RawReader(new MemoryStream(buffer, offset, length, false), false))
            {
                var d = BEncodedValue.Decode<BEncodedDictionary>(reader);

                BEncodedValue val;
                if (d.TryGetValue(MessageTypeKey, out val))
                    MetadataMessageType = (eMessageType) ((BEncodedNumber) val).Number;
                if (d.TryGetValue(PieceKey, out val))
                    Piece = (int) ((BEncodedNumber) val).Number;
                if (d.TryGetValue(TotalSizeKey, out val))
                {
                    var totalSize = (int) ((BEncodedNumber) val).Number;
                    MetadataPiece = new byte[Math.Min(totalSize - Piece*BlockSize, BlockSize)];
                    reader.Read(MetadataPiece, 0, MetadataPiece.Length);
                }
            }
        }

        public override int Encode(byte[] buffer, int offset)
        {
            if (!ClientEngine.SupportsFastPeer)
                throw new MessageException("Libtorrent extension messages not supported");

            var written = offset;

            written += Write(buffer, written, ByteLength - 4);
            written += Write(buffer, written, MessageId);
            written += Write(buffer, written, ExtensionId);
            written += _dict.Encode(buffer, written);
            if (MetadataMessageType == eMessageType.Data)
                written += Write(buffer, written, MetadataPiece, Piece*BlockSize,
                    Math.Min(MetadataPiece.Length - Piece*BlockSize, BlockSize));

            return CheckWritten(written - offset);
        }

        internal enum eMessageType
        {
            Request = 0,
            Data = 1,
            Reject = 2
        }
    }
}