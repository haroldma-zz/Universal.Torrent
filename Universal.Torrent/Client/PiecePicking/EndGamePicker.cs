//
// EndgamePicker.cs
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
    // Keep a list of all the pieces which have not yet being fully downloaded
    // From this list we will make requests for all the blocks until the piece is complete.
    public class EndGamePicker : PiecePicker
    {
        private static readonly Predicate<Request> TimedOut = r => r.Block.RequestTimedOut;

        // This list stores all the pieces which have not yet been completed. If a piece is *not* in this list
        // we don't need to download it.
        private List<Piece> _pieces;

        // These are all the requests for the individual blocks
        private readonly List<Request> _requests;

        public EndGamePicker()
            : base(null)
        {
            _requests = new List<Request>();
        }

        // Cancels a pending request when the predicate returns 'true'
        private void CancelWhere(Predicate<Request> predicate, bool sendCancel)
        {
            foreach (var r in _requests)
            {
                if (predicate(r))
                {
                    r.Peer.AmRequestingPiecesCount--;
                    if (sendCancel)
                        r.Peer.Enqueue(new CancelMessage(r.Block.PieceIndex, r.Block.StartOffset, r.Block.RequestLength));
                }
            }
            _requests.RemoveAll(predicate);
        }

        public override void CancelTimedOutRequests()
        {
            CancelWhere(TimedOut, false);
        }

        public override RequestMessage ContinueExistingRequest(PeerId peer)
        {
            return null;
        }

        public override int CurrentRequestCount()
        {
            return _requests.Count;
        }

        public override List<Piece> ExportActiveRequests()
        {
            return new List<Piece>(_pieces);
        }

        public override void Initialise(BitField bitfield, TorrentFile[] files, IEnumerable<Piece> requests)
        {
            // 'Requests' should contain a list of all the pieces we need to complete
            _pieces = new List<Piece>(requests);
            foreach (var piece in _pieces)
            {
                for (var i = 0; i < piece.BlockCount; i++)
                    if (piece.Blocks[i].RequestedOff != null)
                        _requests.Add(new Request(piece.Blocks[i].RequestedOff, piece.Blocks[i]));
            }
        }

        public override bool IsInteresting(BitField bitfield)
        {
            return !bitfield.AllFalse;
        }

        public override MessageBundle PickPiece(PeerId id, BitField peerBitfield, List<PeerId> otherPeers, int count,
            int startIndex, int endIndex)
        {
            // Only request 2 pieces at a time in endgame mode
            // to prevent a *massive* overshoot
            if (id.IsChoking || id.AmRequestingPiecesCount > 2)
                return null;

            LoadPieces(id, peerBitfield);

            // 1) See if there are any blocks which have not been requested at all. Request the block if the peer has it
            foreach (var p in _pieces)
            {
                if (!peerBitfield[p.Index] || p.AllBlocksRequested)
                    continue;

                for (var i = 0; i < p.BlockCount; i++)
                {
                    if (p.Blocks[i].Requested)
                        continue;
                    p.Blocks[i].Requested = true;
                    var request = new Request(id, p.Blocks[i]);
                    _requests.Add(request);
                    return new MessageBundle(request.Block.CreateRequest(id));
                }
            }

            // 2) For each block with an existing request, add another request. We do a search from the start
            //    of the list to the end. So when we add a duplicate request, move both requests to the end of the list
            foreach (var p in _pieces)
            {
                if (!peerBitfield[p.Index])
                    continue;

                for (var i = 0; i < p.BlockCount; i++)
                {
                    if (p.Blocks[i].Received || AlreadyRequested(p.Blocks[i], id))
                        continue;

                    var c = _requests.Count;
                    for (var j = 0; j < _requests.Count - 1 && (c-- > 0); j++)
                    {
                        if (_requests[j].Block.PieceIndex == p.Index &&
                            _requests[j].Block.StartOffset == p.Blocks[i].StartOffset)
                        {
                            var r = _requests[j];
                            _requests.RemoveAt(j);
                            _requests.Add(r);
                            j--;
                        }
                    }
                    p.Blocks[i].Requested = true;
                    var request = new Request(id, p.Blocks[i]);
                    _requests.Add(request);
                    return new MessageBundle(request.Block.CreateRequest(id));
                }
            }

            return null;
        }

        private void LoadPieces(PeerId id, BitField b)
        {
            var length = b.Length;
            for (var i = b.FirstTrue(0, length); i != -1; i = b.FirstTrue(i + 1, length))
                if (!_pieces.Exists(p => p.Index == i))
                    _pieces.Add(new Piece(i, id.TorrentManager.Torrent.PieceLength, id.TorrentManager.Torrent.Size));
        }

        private bool AlreadyRequested(Block block, PeerId id)
        {
            var b = _requests.Exists(r => r.Block.PieceIndex == block.PieceIndex &&
                                          r.Block.StartOffset == block.StartOffset &&
                                          Equals(r.Peer, id));
            return b;
        }

        public override void Reset()
        {
            // Though if you reset an EndGamePicker it really means that you should be using a regular picker now
            _requests.Clear();
        }

        public override void CancelRequest(PeerId peer, int piece, int startOffset, int length)
        {
            CancelWhere(r => r.Block.PieceIndex == piece &&
                             r.Block.StartOffset == startOffset &&
                             r.Block.RequestLength == length &&
                             peer.Equals(r.Peer), false);
        }

        public override void CancelRequests(PeerId peer)
        {
            CancelWhere(r => Equals(r.Peer, peer), false);
        }

        public override bool ValidatePiece(PeerId peer, int pieceIndex, int startOffset, int length, out Piece piece)
        {
            foreach (var r in _requests)
            {
                // When we get past this block, it means we've found a valid request for this piece
                if (r.Block.PieceIndex != pieceIndex || r.Block.StartOffset != startOffset ||
                    r.Block.RequestLength != length || !Equals(r.Peer, peer))
                    continue;

                // All the other requests for this block need to be cancelled.
                foreach (var p in _pieces)
                {
                    if (p.Index != pieceIndex)
                        continue;

                    CancelWhere(req => req.Block.PieceIndex == pieceIndex &&
                                       req.Block.StartOffset == startOffset &&
                                       req.Block.RequestLength == length &&
                                       !Equals(req.Peer, peer), true);

                    // Mark the block as received
                    p.Blocks[startOffset/Piece.BlockSize].Received = true;

                    // Once a piece is completely received, remove it from our list.
                    // If a piece *fails* the hashcheck, we need to add it back into the list so
                    // we download it again.
                    if (p.AllBlocksReceived)
                        _pieces.Remove(p);

                    _requests.Remove(r);
                    piece = p;
                    peer.AmRequestingPiecesCount--;
                    return true;
                }
            }

            // The request was not valid
            piece = null;
            return false;
        }

        // Struct to link a request for a block to a peer
        // This way we can have multiple requests for the same block
        private class Request
        {
            public Block Block;
            public readonly PeerId Peer;

            public Request(PeerId peer, Block block)
            {
                Peer = peer;
                Block = block;
            }
        }
    }
}