//
// PieceMessage.cs
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
using System.Text;
using Universal.Torrent.Client.Managers;

namespace Universal.Torrent.Client.Messages.StandardMessages
{
    public class PieceMessage : PeerMessage
    {
        private const int MessageLength = 9;
        internal static readonly byte MessageId = 7;

        #region Private Fields

        internal byte[] Data;

        #endregion

        #region Properties

        internal int BlockIndex => StartOffset/Piece.BlockSize;

        public override int ByteLength => (MessageLength + RequestLength + 4);

        internal int DataOffset { get; private set; }

        public int PieceIndex { get; private set; }

        public int StartOffset { get; private set; }

        public int RequestLength { get; private set; }

        #endregion

        #region Constructors

        public PieceMessage()
        {
            Data = BufferManager.EmptyBuffer;
        }

        public PieceMessage(int pieceIndex, int startOffset, int blockLength)
        {
            PieceIndex = pieceIndex;
            StartOffset = startOffset;
            RequestLength = blockLength;
            Data = BufferManager.EmptyBuffer;
        }

        #endregion

        #region Methods

        public override void Decode(byte[] buffer, int offset, int length)
        {
            PieceIndex = ReadInt(buffer, ref offset);
            StartOffset = ReadInt(buffer, ref offset);
            RequestLength = length - 8;

            DataOffset = offset;

            // This buffer will be freed after the PieceWriter has finished with it
            Data = BufferManager.EmptyBuffer;
            ClientEngine.BufferManager.GetBuffer(ref Data, RequestLength);
            Buffer.BlockCopy(buffer, offset, Data, 0, RequestLength);
        }

        public override int Encode(byte[] buffer, int offset)
        {
            var written = offset;

            written += Write(buffer, written, MessageLength + RequestLength);
            written += Write(buffer, written, MessageId);
            written += Write(buffer, written, PieceIndex);
            written += Write(buffer, written, StartOffset);
            written += Write(buffer, written, Data, 0, RequestLength);

            return CheckWritten(written - offset);
        }

        public override bool Equals(object obj)
        {
            var msg = obj as PieceMessage;
            return (msg != null) && (PieceIndex == msg.PieceIndex
                                     && StartOffset == msg.StartOffset
                                     && RequestLength == msg.RequestLength);
        }

        public override int GetHashCode()
        {
            return (RequestLength.GetHashCode()
                    ^ DataOffset.GetHashCode()
                    ^ PieceIndex.GetHashCode()
                    ^ StartOffset.GetHashCode());
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("PieceMessage ");
            sb.Append(" Index ");
            sb.Append(PieceIndex);
            sb.Append(" Offset ");
            sb.Append(StartOffset);
            sb.Append(" Length ");
            sb.Append(RequestLength);
            return sb.ToString();
        }

        #endregion
    }
}