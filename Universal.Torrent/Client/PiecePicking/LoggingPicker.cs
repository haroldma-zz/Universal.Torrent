//
// LoggingPicker.cs
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
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Universal.Torrent.Client.Messages;
using Universal.Torrent.Client.Messages.StandardMessages;
using Universal.Torrent.Client.PeerConnections;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.PiecePicking
{
    internal class LoggingPicker : PiecePicker
    {
        private readonly SortList<Request> _requests = new SortList<Request>();

        public LoggingPicker(PiecePicker picker)
            : base(picker)
        {
        }

        public override RequestMessage ContinueExistingRequest(PeerId peer)
        {
            var m = base.ContinueExistingRequest(peer);
            if (m != null)
                HandleRequest(peer, m);
            return m;
        }

        public override MessageBundle PickPiece(PeerId id, BitField peerBitfield, List<PeerId> otherPeers, int count,
            int startIndex, int endIndex)
        {
            var bundle = base.PickPiece(id, peerBitfield, otherPeers, count, startIndex, endIndex);
            if (bundle != null)
            {
                foreach (var m in bundle.Messages.Cast<RequestMessage>())
                {
                    HandleRequest(id, m);
                }
            }

            return bundle;
        }

        private void HandleRequest(PeerId id, RequestMessage m)
        {
            var r = new Request
            {
                PieceIndex = m.PieceIndex,
                RequestedOff = id,
                RequestLength = m.RequestLength,
                StartOffset = m.StartOffset
            };
            var current = _requests.FindAll(req => req.CompareTo(r) == 0);
            if (current.Count > 0)
            {
                foreach (var request in current.Where(request => request.Verified))
                {
                    if (id.TorrentManager.Bitfield[request.PieceIndex])
                    {
                        Debug.WriteLine("Double request: {0}", m);
                        Debug.WriteLine("From: {0} and {1}", id.PeerID, r.RequestedOff.PeerID);
                    }
                    else
                    {
                        // The piece failed a hashcheck, so ignore it this time
                        _requests.Remove(request);
                    }
                }
            }
            _requests.Add(r);
        }

        public override bool ValidatePiece(PeerId peer, int pieceIndex, int startOffset, int length, out Piece piece)
        {
            var validatedOk = base.ValidatePiece(peer, pieceIndex, startOffset, length, out piece);

            var list = _requests.FindAll(r => r.PieceIndex == pieceIndex &&
                                              r.RequestLength == length &&
                                              r.StartOffset == startOffset);

            if (list.Count == 0)
            {
                Debug.WriteLine("Piece was not requested from anyone: {1}-{2}", peer.PeerID, pieceIndex, startOffset);
            }
            else if (list.Count == 1)
            {
                if (list[0].Verified)
                {
                    if (validatedOk)
                        Debug.WriteLine("The piece should not have validated");
                    Debug.WriteLine("Piece already verified: Orig: {0} Current: {3} <> {1}-{2}",
                        list[0].RequestedOff.PeerID, pieceIndex, startOffset, peer.PeerID);
                }
            }
            else
            {
                var alreadyVerified = list.FindAll(r => r.Verified);
                if (alreadyVerified.Count > 0)
                {
                    if (validatedOk)
                        Debug.WriteLine("The piece should not have validated 2");
                    Debug.WriteLine("Piece has already been verified {0} times", alreadyVerified.Count);
                }
            }

            foreach (var request in list.Where(request => Equals(request.RequestedOff, peer)))
            {
                if (!request.Verified)
                {
                    if (!validatedOk)
                        Debug.WriteLine("The piece should have validated");
                    request.Verified = true;
                }
                else
                {
                    if (validatedOk)
                        Debug.WriteLine("The piece should not have validated 3");
                    Debug.WriteLine("This peer has already sent and verified this piece. {0} <> {1}-{2}",
                        peer.PeerID, pieceIndex, startOffset);
                }
            }

            return validatedOk;
        }

        private class Request : IComparable<Request>
        {
            public int PieceIndex;
            public PeerId RequestedOff;
            public int RequestLength;
            public int StartOffset;
            public bool Verified;

            public int CompareTo(Request other)
            {
                int difference;
                if ((difference = PieceIndex.CompareTo(other.PieceIndex)) != 0)
                    return difference;
                if ((difference = StartOffset.CompareTo(other.StartOffset)) != 0)
                    return difference;
                return RequestLength.CompareTo(other.RequestLength);
            }
        }
    }
}