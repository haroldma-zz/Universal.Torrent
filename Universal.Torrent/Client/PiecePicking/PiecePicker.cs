//
// PiecePicker.cs
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
using Universal.Torrent.Client.Messages;
using Universal.Torrent.Client.Messages.StandardMessages;
using Universal.Torrent.Client.PeerConnections;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.PiecePicking
{
    public abstract class PiecePicker
    {
        protected static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(45);

        private readonly PiecePicker _picker;

        protected PiecePicker(PiecePicker picker)
        {
            _picker = picker;
        }

        private void CheckOverriden()
        {
            if (_picker == null)
                throw new InvalidOperationException("This method must be overridden");
        }

        public virtual void CancelRequest(PeerId peer, int piece, int startOffset, int length)
        {
            CheckOverriden();
            _picker.CancelRequest(peer, piece, startOffset, length);
        }

        public virtual void CancelRequests(PeerId peer)
        {
            CheckOverriden();
            _picker.CancelRequests(peer);
        }

        public virtual void CancelTimedOutRequests()
        {
            CheckOverriden();
            _picker.CancelTimedOutRequests();
        }

        public virtual RequestMessage ContinueExistingRequest(PeerId peer)
        {
            CheckOverriden();
            return _picker.ContinueExistingRequest(peer);
        }

        public virtual int CurrentRequestCount()
        {
            CheckOverriden();
            return _picker.CurrentRequestCount();
        }

        public virtual List<Piece> ExportActiveRequests()
        {
            CheckOverriden();
            return _picker.ExportActiveRequests();
        }

        public virtual void Initialise(BitField bitfield, TorrentFile[] files, IEnumerable<Piece> requests)
        {
            CheckOverriden();
            _picker.Initialise(bitfield, files, requests);
        }

        public virtual bool IsInteresting(BitField bitfield)
        {
            CheckOverriden();
            return _picker.IsInteresting(bitfield);
        }

        public RequestMessage PickPiece(PeerId peer, List<PeerId> otherPeers)
        {
            var bundle = PickPiece(peer, otherPeers, 1);
            return (RequestMessage) bundle?.Messages[0];
        }

        public MessageBundle PickPiece(PeerId peer, List<PeerId> otherPeers, int count)
        {
            return PickPiece(peer, peer.BitField, otherPeers, count, 0, peer.BitField.Length);
        }

        public virtual MessageBundle PickPiece(PeerId id, BitField peerBitfield, List<PeerId> otherPeers, int count,
            int startIndex, int endIndex)
        {
            CheckOverriden();
            return _picker.PickPiece(id, peerBitfield, otherPeers, count, startIndex, endIndex);
        }

        public virtual void Reset()
        {
            CheckOverriden();
            _picker.Reset();
        }

        public virtual bool ValidatePiece(PeerId peer, int pieceIndex, int startOffset, int length, out Piece piece)
        {
            CheckOverriden();
            return _picker.ValidatePiece(peer, pieceIndex, startOffset, length, out piece);
        }
    }
}