//
// EndGameSwitcher.cs
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
using System.Linq;
using Universal.Torrent.Client.Managers;
using Universal.Torrent.Client.Messages;
using Universal.Torrent.Client.Messages.StandardMessages;
using Universal.Torrent.Client.PeerConnections;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.PiecePicking
{
    public class EndGameSwitcher : PiecePicker
    {
        private const int Threshold = 20;
        private readonly int _blocksPerPiece;
        private readonly PiecePicker _endgame;
        private readonly PiecePicker _standard;
        private readonly TorrentManager _torrentManager;

        private BitField _bitfield;
        private BitField _endgameSelector;
        private TorrentFile[] _files;
        private bool _inEndgame;

        public EndGameSwitcher(StandardPicker standard, EndGamePicker endgame, int blocksPerPiece,
            TorrentManager torrentManager)
            : base(null)
        {
            _standard = standard;
            _endgame = endgame;
            _blocksPerPiece = blocksPerPiece;
            _torrentManager = torrentManager;
        }

        private PiecePicker ActivePicker => _inEndgame ? _endgame : _standard;

        public override void CancelRequest(PeerId peer, int piece, int startOffset, int length)
        {
            ActivePicker.CancelRequest(peer, piece, startOffset, length);
        }

        public override void CancelRequests(PeerId peer)
        {
            ActivePicker.CancelRequests(peer);
        }

        public override void CancelTimedOutRequests()
        {
            ActivePicker.CancelTimedOutRequests();
        }

        public override RequestMessage ContinueExistingRequest(PeerId peer)
        {
            return ActivePicker.ContinueExistingRequest(peer);
        }

        public override int CurrentRequestCount()
        {
            return ActivePicker.CurrentRequestCount();
        }

        public override List<Piece> ExportActiveRequests()
        {
            return ActivePicker.ExportActiveRequests();
        }

        public override void Initialise(BitField bitfield, TorrentFile[] files, IEnumerable<Piece> requests)
        {
            _bitfield = bitfield;
            _endgameSelector = new BitField(bitfield.Length);
            _files = files;
            _inEndgame = false;
            TryEnableEndgame();
            ActivePicker.Initialise(bitfield, files, requests);
        }

        public override bool IsInteresting(BitField bitfield)
        {
            return ActivePicker.IsInteresting(bitfield);
        }

        public override MessageBundle PickPiece(PeerId id, BitField peerBitfield, List<PeerId> otherPeers, int count,
            int startIndex, int endIndex)
        {
            var bundle = ActivePicker.PickPiece(id, peerBitfield, otherPeers, count, startIndex, endIndex);
            if (bundle == null && TryEnableEndgame())
                return ActivePicker.PickPiece(id, peerBitfield, otherPeers, count, startIndex, endIndex);
            return bundle;
        }

        private bool TryEnableEndgame()
        {
            if (_inEndgame)
                return false;

            // We need to activate endgame mode when there are less than 20 requestable blocks
            // available. We OR the bitfields of all the files which are downloadable and then
            // NAND it with the torrents bitfield to get a list of pieces which remain to be downloaded.

            // Essentially we get a list of all the pieces we're allowed download, then get a list of
            // the pieces which we still need to get and AND them together.

            // Create the bitfield of pieces which are downloadable
            _endgameSelector.SetAll(false);
            foreach (var t in _files.Where(t => t.Priority != Priority.DoNotDownload))
                _endgameSelector.Or(t.GetSelector(_bitfield.Length));

            // NAND it with the pieces we already have (i.e. AND it with the pieces we still need to receive)
            _endgameSelector.NAnd(_bitfield);

            // If the total number of blocks remaining is less than Threshold, activate Endgame mode.
            var pieces = _standard.ExportActiveRequests();
            var count = pieces.Sum(t => t.TotalReceived);
            _inEndgame = Math.Max(_blocksPerPiece, (_endgameSelector.TrueCount*_blocksPerPiece)) - count < Threshold;
            if (_inEndgame)
            {
                _endgame.Initialise(_bitfield, _files, _standard.ExportActiveRequests());
                // Set torrent's IsInEndGame flag
                _torrentManager.InternalIsInEndGame = true;
            }
            return _inEndgame;
        }

        public override void Reset()
        {
            _inEndgame = false;
            _torrentManager.InternalIsInEndGame = false;
            _standard.Reset();
            _endgame.Reset();
        }

        public override bool ValidatePiece(PeerId peer, int pieceIndex, int startOffset, int length, out Piece piece)
        {
            return ActivePicker.ValidatePiece(peer, pieceIndex, startOffset, length, out piece);
        }
    }
}