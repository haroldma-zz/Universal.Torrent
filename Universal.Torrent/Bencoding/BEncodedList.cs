//
// BEncodedList.cs
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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Universal.Torrent.Bencoding
{
    /// <summary>
    ///     Class representing a BEncoded list
    /// </summary>
    public class BEncodedList : BEncodedValue, IList<BEncodedValue>
    {
        #region Member Variables

        private readonly List<BEncodedValue> _list;

        #endregion

        #region Helper Methods

        /// <summary>
        ///     Returns the size of the list in bytes
        /// </summary>
        /// <returns></returns>
        public override int LengthInBytes()
        {
            var length = 0;

            length += 1; // Lists start with 'l'
            length += _list.Sum(t => t.LengthInBytes());

            length += 1; // Lists end with 'e'
            return length;
        }

        #endregion

        #region Constructors

        /// <summary>
        ///     Create a new BEncoded List with default capacity
        /// </summary>
        public BEncodedList()
            : this(new List<BEncodedValue>())
        {
        }

        /// <summary>
        ///     Create a new BEncoded List with the supplied capacity
        /// </summary>
        /// <param name="capacity">The initial capacity</param>
        public BEncodedList(int capacity)
            : this(new List<BEncodedValue>(capacity))
        {
        }

        public BEncodedList(IEnumerable<BEncodedValue> list)
        {
            if (list == null)
                throw new ArgumentNullException(nameof(list));

            _list = new List<BEncodedValue>(list);
        }

        private BEncodedList(List<BEncodedValue> value)
        {
            _list = value;
        }

        #endregion

        #region Encode/Decode Methods

        /// <summary>
        ///     Encodes the list to a byte[]
        /// </summary>
        /// <param name="buffer">The buffer to encode the list to</param>
        /// <param name="offset">The offset to start writing the data at</param>
        /// <returns></returns>
        public override int Encode(byte[] buffer, int offset)
        {
            var written = 0;
            buffer[offset] = (byte) 'l';
            written++;
            written = _list.Aggregate(written, (current, t) => current + t.Encode(buffer, offset + current));
            buffer[offset + written] = (byte) 'e';
            written++;
            return written;
        }

        /// <summary>
        ///     Decodes a BEncodedList from the given StreamReader
        /// </summary>
        /// <param name="reader"></param>
        internal override void DecodeInternal(RawReader reader)
        {
            if (reader.ReadByte() != 'l') // Remove the leading 'l'
                throw new BEncodingException("Invalid data found. Aborting");

            while ((reader.PeekByte() != -1) && (reader.PeekByte() != 'e'))
                _list.Add(Decode(reader));

            if (reader.ReadByte() != 'e') // Remove the trailing 'e'
                throw new BEncodingException("Invalid data found. Aborting");
        }

        #endregion

        #region Overridden Methods

        public override bool Equals(object obj)
        {
            var other = obj as BEncodedList;

            if (other == null)
                return false;

            return !_list.Where((t, i) => !t.Equals(other._list[i])).Any();
        }


        public override int GetHashCode()
        {
            return _list.Aggregate(0, (current, t) => current ^ t.GetHashCode());
        }


        public override string ToString()
        {
            return Encoding.UTF8.GetString(Encode());
        }

        #endregion

        #region IList methods

        public void Add(BEncodedValue item)
        {
            _list.Add(item);
        }

        public void AddRange(IEnumerable<BEncodedValue> collection)
        {
            _list.AddRange(collection);
        }

        public void Clear()
        {
            _list.Clear();
        }

        public bool Contains(BEncodedValue item)
        {
            return _list.Contains(item);
        }

        public void CopyTo(BEncodedValue[] array, int arrayIndex)
        {
            _list.CopyTo(array, arrayIndex);
        }

        public int Count => _list.Count;

        public int IndexOf(BEncodedValue item)
        {
            return _list.IndexOf(item);
        }

        public void Insert(int index, BEncodedValue item)
        {
            _list.Insert(index, item);
        }

        public bool IsReadOnly => false;

        public bool Remove(BEncodedValue item)
        {
            return _list.Remove(item);
        }

        public void RemoveAt(int index)
        {
            _list.RemoveAt(index);
        }

        public BEncodedValue this[int index]
        {
            get { return _list[index]; }
            set { _list[index] = value; }
        }

        public IEnumerator<BEncodedValue> GetEnumerator()
        {
            return _list.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion
    }
}