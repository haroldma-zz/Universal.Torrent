//
// IgnoringPicker.cs
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


using System.Collections.Generic;
using Universal.Torrent.Client.Messages;
using Universal.Torrent.Client.PeerConnections;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.PiecePicking
{
    public class IgnoringPicker : PiecePicker
    {
        private readonly BitField _bitfield;
        private readonly BitField _temp;

        public IgnoringPicker(BitField bitfield, PiecePicker picker)
            : base(picker)
        {
            _bitfield = bitfield;
            _temp = new BitField(bitfield.Length);
        }

        public override MessageBundle PickPiece(PeerId id, BitField peerBitfield, List<PeerId> otherPeers, int count,
            int startIndex, int endIndex)
        {
            // Invert 'bitfield' and AND it with the peers bitfield
            // Any pieces which are 'true' in the bitfield will not be downloaded
            _temp.From(peerBitfield).NAnd(_bitfield);
            if (_temp.AllFalse)
                return null;
            return base.PickPiece(id, _temp, otherPeers, count, startIndex, endIndex);
        }

        public override bool IsInteresting(BitField bitfield)
        {
            _temp.From(bitfield).NAnd(_bitfield);
            if (_temp.AllFalse)
                return false;
            return base.IsInteresting(_temp);
        }
    }
}