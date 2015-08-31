//
// InitialSeedUnchoker.cs
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
using Universal.Torrent.Client.Managers;
using Universal.Torrent.Client.Messages.StandardMessages;
using Universal.Torrent.Client.PeerConnections;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.Unchokers
{
    internal class ChokeData
    {
        public BitField CurrentPieces;
        public DateTime LastChoked;
        public DateTime LastUnchoked;
        public PeerId Peer;
        public int SharedPieces;
        public int TotalPieces;

        public ChokeData(PeerId peer)
        {
            LastChoked = DateTime.Now;
            Peer = peer;
            CurrentPieces = new BitField(peer.BitField.Length);
        }

        public double ShareRatio => (SharedPieces + 1.0)/(TotalPieces + 1.0);
    }

    internal class SeededPiece
    {
        public int BlocksSent;
        public int Index;
        public PeerId Peer;
        public DateTime SeededAt;
        public int TotalBlocks;

        public SeededPiece(PeerId peer, int index, int totalBlocks)
        {
            Index = index;
            Peer = peer;
            SeededAt = DateTime.Now;
            TotalBlocks = totalBlocks;
        }
    }

    internal class InitialSeedUnchoker : Unchoker
    {
        private readonly List<SeededPiece> _advertisedPieces;
        private readonly BitField _bitfield;
        private readonly TorrentManager _manager;
        private readonly List<ChokeData> _peers;
        private readonly BitField _temp;

        public InitialSeedUnchoker(TorrentManager manager)
        {
            _advertisedPieces = new List<SeededPiece>();
            _bitfield = new BitField(manager.Bitfield.Length);
            this._manager = manager;
            _peers = new List<ChokeData>();
            _temp = new BitField(_bitfield.Length);
        }

        private bool PendingUnchoke
        {
            get { return _peers.Exists(d => d.Peer.AmChoking && d.Peer.IsInterested); }
        }

        public bool Complete => _bitfield.AllTrue;

        public int MaxAdvertised => 4;

        internal int PeerCount => _peers.Count;

        public override void Choke(PeerId id)
        {
            base.Choke(id);

            _advertisedPieces.RemoveAll(p => Equals(p.Peer, id));

            // Place the peer at the end of the list so the rest of the peers
            // will get an opportunity to unchoke before this peer gets tried again
            var data = _peers.Find(d => Equals(d.Peer, id));
            _peers.Remove(data);
            _peers.Add(data);
        }

        public void PeerConnected(PeerId id)
        {
            _peers.Add(new ChokeData(id));
        }

        public void PeerDisconnected(PeerId id)
        {
            _peers.RemoveAll(d => Equals(d.Peer, id));
            _advertisedPieces.RemoveAll(piece => Equals(piece.Peer, id));
        }

        public void ReceivedHave(PeerId peer, int pieceIndex)
        {
            _bitfield[pieceIndex] = true;

            // If a peer reports they have a piece that *isn't* the peer
            // we uploaded it to, then the peer we uploaded to has shared it
            foreach (var data in _peers)
            {
                if (data.CurrentPieces[pieceIndex] && !Equals(data.Peer, peer))
                {
                    data.CurrentPieces[pieceIndex] = false;
                    data.SharedPieces++;
                    // Give him another piece if no-one else is waiting.
                    TryAdvertisePiece(data);
                    break;
                }
            }

            foreach (var piece in _advertisedPieces)
            {
                if (piece.Index == pieceIndex)
                {
                    _advertisedPieces.Remove(piece);
                    return;
                }
            }
        }

        public void ReceivedNotInterested(PeerId id)
        {
            _advertisedPieces.RemoveAll(piece => Equals(piece.Peer, id));
        }

        public void SentBlock(PeerId peer, int pieceIndex)
        {
            var piece =
                _advertisedPieces.Find(p => Equals(p.Peer, peer) && p.Index == pieceIndex);
            if (piece == null)
                return;

            piece.SeededAt = DateTime.Now;
            piece.BlocksSent++;
            if (piece.TotalBlocks == piece.BlocksSent)
                _advertisedPieces.Remove(piece);
        }

        private void TryAdvertisePiece(ChokeData data)
        {
            // If we are seeding to this peer and we have a peer waiting to unchoke
            // don't advertise more data
            if (!data.Peer.AmChoking && PendingUnchoke)
                return;

            var advertised = _advertisedPieces.FindAll(p => Equals(p.Peer, data.Peer)).Count;
            int max;
            if (_manager.UploadingTo < _manager.Settings.UploadSlots)
                max = MaxAdvertised;
            else if (data.ShareRatio < 0.25)
                max = 1;
            else if (data.ShareRatio < 0.35)
                max = 2;
            else if (data.ShareRatio < 0.50)
                max = 3;
            else
                max = MaxAdvertised;

            if (advertised >= max)
                return;

            // List of pieces *not* in the swarm
            _temp.From(_bitfield).Not();

            // List of pieces that he wants that aren't in the swarm
            _temp.NAnd(data.Peer.BitField);

            // Ignore all the pieces we've already started sharing
            foreach (var p in _advertisedPieces)
                _temp[p.Index] = false;

            var index = 0;
            while (advertised < max)
            {
                // Get the index of the first piece we can send him
                index = _temp.FirstTrue(index, _temp.Length);
                // Looks like he's not interested in us...
                if (index == -1)
                    return;

                advertised++;
                data.TotalPieces++;
                data.CurrentPieces[index] = true;
                _advertisedPieces.Add(new SeededPiece(data.Peer, index,
                    data.Peer.TorrentManager.Torrent.PieceLength/Piece.BlockSize));
                data.Peer.Enqueue(new HaveMessage(index));
                index++;
            }
        }

        private void TryChoke(ChokeData data)
        {
            // Already choked
            if (data.Peer.AmChoking)
                return;

            if (!data.Peer.IsInterested)
            {
                // Choke him if he's not interested
                Choke(data.Peer);
            }
            else if (!_advertisedPieces.Exists(p => Equals(p.Peer, data.Peer)))
            {
                // If we have no free slots and peers are waiting, choke after 30 seconds.
                // FIXME: Choke as soon as the next piece completes *or* a larger time limit *and*
                // at least one piece has uploaded.
                data.LastChoked = DateTime.Now;
                Choke(data.Peer);
            }
        }

        private void TryUnchoke(ChokeData data)
        {
            // Already unchoked
            if (!data.Peer.AmChoking)
                return;

            // Don't unchoke if he's not interested
            if (!data.Peer.IsInterested)
                return;

            // Don't unchoke if we are have maxed our slots
            if (_manager.UploadingTo >= _manager.Settings.UploadSlots)
                return;

            data.LastUnchoked = DateTime.Now;
            Unchoke(data.Peer);
        }

        public override void UnchokeReview()
        {
            if (PendingUnchoke)
            {
                var dupePeers = new List<ChokeData>(_peers);
                foreach (var data in dupePeers)
                    TryChoke(data);

                dupePeers = new List<ChokeData>(_peers);
                // See if there's anyone interesting to unchoke
                foreach (var data in dupePeers)
                    TryUnchoke(data);
            }

            // Make sure our list of pieces available in the swarm is up to date
            foreach (var data in _peers)
                _bitfield.Or(data.Peer.BitField);

            _advertisedPieces.RemoveAll(p => _bitfield[p.Index]);

            // Send have messages to anyone that needs them
            foreach (var data in _peers)
                TryAdvertisePiece(data);
        }
    }
}