//
// PriorityPicker.cs
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
using Universal.Torrent.Client.PeerConnections;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.PiecePicking
{
    public class PriorityPicker : PiecePicker
    {
        private readonly List<Files> _files = new List<Files>();
        private Predicate<Files> _allSamePriority;
        private BitField _temp;

        public PriorityPicker(PiecePicker picker)
            : base(picker)
        {
        }

        public override MessageBundle PickPiece(PeerId id, BitField peerBitfield, List<PeerId> otherPeers, int count,
            int startIndex, int endIndex)
        {
            // Fast Path - the peer has nothing to offer
            if (peerBitfield.AllFalse)
                return null;

            if (_files.Count == 1)
            {
                if (_files[0].File.Priority == Priority.DoNotDownload)
                    return null;
                return base.PickPiece(id, peerBitfield, otherPeers, count, startIndex, endIndex);
            }

            _files.Sort();

            // Fast Path - all the files have been set to DoNotDownload
            if (_files[0].File.Priority == Priority.DoNotDownload)
                return null;

            // Fast Path - If all the files are the same priority, call straight into the base picker
            if (_files.TrueForAll(_allSamePriority))
                return base.PickPiece(id, peerBitfield, otherPeers, count, startIndex, endIndex);

            _temp.From(_files[0].Selector);
            for (var i = 1; i < _files.Count && _files[i].File.Priority != Priority.DoNotDownload; i++)
            {
                if (_files[i].File.Priority != _files[i - 1].File.Priority)
                {
                    _temp.And(peerBitfield);
                    if (!_temp.AllFalse)
                    {
                        var message = base.PickPiece(id, _temp, otherPeers, count, startIndex, endIndex);
                        if (message != null)
                            return message;
                        _temp.SetAll(false);
                    }
                }

                _temp.Or(_files[i].Selector);
            }

            if (_temp.AllFalse || _temp.And(peerBitfield).AllFalse)
                return null;
            return base.PickPiece(id, _temp, otherPeers, count, startIndex, endIndex);
        }

        public override void Initialise(BitField bitfield, TorrentFile[] files, IEnumerable<Piece> requests)
        {
            base.Initialise(bitfield, files, requests);
            _allSamePriority = f => f.File.Priority == files[0].Priority;
            _temp = new BitField(bitfield.Length);

            _files.Clear();
            foreach (var t in files)
                _files.Add(new Files(t, t.GetSelector(bitfield.Length)));
        }

        public override bool IsInteresting(BitField bitfield)
        {
            _files.Sort();
            _temp.SetAll(false);

            // OR all the files together which we want to download
            for (var i = 0; i < _files.Count; i++)
                if (_files[i].File.Priority != Priority.DoNotDownload)
                    _temp.Or(_files[i].Selector);

            _temp.And(bitfield);
            if (_temp.AllFalse)
                return false;

            return base.IsInteresting(_temp);
        }

        private struct Files : IComparable<Files>
        {
            public readonly TorrentFile File;
            public readonly BitField Selector;

            public Files(TorrentFile file, BitField selector)
            {
                File = file;
                Selector = selector;
            }

            public int CompareTo(Files other)
            {
                return (int) other.File.Priority - (int) File.Priority;
            }
        }
    }
}