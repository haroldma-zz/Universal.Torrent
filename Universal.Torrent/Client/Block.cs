//
// Block.cs
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
using Universal.Torrent.Client.Messages.StandardMessages;
using Universal.Torrent.Client.PeerConnections;

namespace Universal.Torrent.Client
{
    /// <summary>
    /// </summary>
    public struct Block
    {
        #region Private Fields

        private readonly Piece _piece;
        private bool _requested;
        private bool _received;
        private bool _written;

        #endregion Private Fields

        #region Properties

        public int PieceIndex => _piece.Index;

        public bool Received
        {
            get { return _received; }
            internal set
            {
                if (value && !_received)
                    _piece.TotalReceived++;

                else if (!value && _received)
                    _piece.TotalReceived--;

                _received = value;
            }
        }

        public bool Requested
        {
            get { return _requested; }
            internal set
            {
                if (value && !_requested)
                    _piece.TotalRequested++;

                else if (!value && _requested)
                    _piece.TotalRequested--;

                _requested = value;
            }
        }

        public int RequestLength { get; }

        public bool RequestTimedOut => !Received && RequestedOff != null &&
                                       (DateTime.Now - RequestedOff.LastMessageReceived) > TimeSpan.FromMinutes(1);

        internal PeerId RequestedOff { get; set; }

        public int StartOffset { get; }

        public bool Written
        {
            get { return _written; }
            internal set
            {
                if (value && !_written)
                    _piece.TotalWritten++;

                else if (!value && _written)
                    _piece.TotalWritten--;

                _written = value;
            }
        }

        #endregion Properties

        #region Constructors

        internal Block(Piece piece, int startOffset, int requestLength)
        {
            RequestedOff = null;
            _piece = piece;
            _received = false;
            _requested = false;
            RequestLength = requestLength;
            StartOffset = startOffset;
            _written = false;
        }

        #endregion

        #region Methods

        internal RequestMessage CreateRequest(PeerId id)
        {
            Requested = true;
            RequestedOff = id;
            RequestedOff.AmRequestingPiecesCount++;
            return new RequestMessage(PieceIndex, StartOffset, RequestLength);
        }

        internal void CancelRequest()
        {
            Requested = false;
            RequestedOff.AmRequestingPiecesCount--;
            RequestedOff = null;
        }

        public override bool Equals(object obj)
        {
            if (!(obj is Block))
                return false;

            var other = (Block) obj;
            return PieceIndex == other.PieceIndex && StartOffset == other.StartOffset &&
                   RequestLength == other.RequestLength;
        }

        public override int GetHashCode()
        {
            return PieceIndex ^ RequestLength ^ StartOffset;
        }

        internal static int IndexOf(Block[] blocks, int startOffset, int blockLength)
        {
            var index = startOffset/Piece.BlockSize;
            if (blocks[index].StartOffset != startOffset || blocks[index].RequestLength != blockLength)
                return -1;
            return index;
        }

        #endregion
    }
}