//
// BEncodedDictionary.cs
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


using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Universal.Torrent.Bencoding
{
    /// <summary>
    ///     Class representing a BEncoded Dictionary
    /// </summary>
    public class BEncodedDictionary : BEncodedValue, IDictionary<BEncodedString, BEncodedValue>
    {
        #region Member Variables

        private readonly SortedDictionary<BEncodedString, BEncodedValue> _dictionary;

        #endregion

        #region Constructors

        /// <summary>
        ///     Create a new BEncodedDictionary
        /// </summary>
        public BEncodedDictionary()
        {
            _dictionary = new SortedDictionary<BEncodedString, BEncodedValue>();
        }

        #endregion

        #region Helper Methods

        /// <summary>
        ///     Returns the size of the dictionary in bytes using UTF8 encoding
        /// </summary>
        /// <returns></returns>
        public override int LengthInBytes()
        {
            var length = 0;
            length += 1; // Dictionaries start with 'd'

            foreach (var keypair in _dictionary)
            {
                length += keypair.Key.LengthInBytes();
                length += keypair.Value.LengthInBytes();
            }
            length += 1; // Dictionaries end with 'e'
            return length;
        }

        #endregion

        #region Encode/Decode Methods

        /// <summary>
        ///     Encodes the dictionary to a byte[]
        /// </summary>
        /// <param name="buffer">The buffer to encode the data to</param>
        /// <param name="offset">The offset to start writing the data to</param>
        /// <returns></returns>
        public override int Encode(byte[] buffer, int offset)
        {
            var written = 0;

            //Dictionaries start with 'd'
            buffer[offset] = (byte) 'd';
            written++;

            foreach (var keypair in this)
            {
                written += keypair.Key.Encode(buffer, offset + written);
                written += keypair.Value.Encode(buffer, offset + written);
            }

            // Dictionaries end with 'e'
            buffer[offset + written] = (byte) 'e';
            written++;
            return written;
        }


        /// <summary>
        /// </summary>
        /// <param name="reader"></param>
        internal override void DecodeInternal(RawReader reader)
        {
            DecodeInternal(reader, reader.StrictDecoding);
        }

        private void DecodeInternal(RawReader reader, bool strictDecoding)
        {
            BEncodedString oldkey = null;

            if (reader.ReadByte() != 'd')
                throw new BEncodingException("Invalid data found. Aborting"); // Remove the leading 'd'

            while ((reader.PeekByte() != -1) && (reader.PeekByte() != 'e'))
            {
                var key = (BEncodedString) Decode(reader);

                if (oldkey != null && oldkey.CompareTo(key) > 0)
                    if (strictDecoding)
                        throw new BEncodingException(string.Format(
                            "Illegal BEncodedDictionary. The attributes are not ordered correctly. Old key: {0}, New key: {1}",
                            oldkey, key));

                oldkey = key;
                var value = Decode(reader);
                _dictionary.Add(key, value);
            }

            if (reader.ReadByte() != 'e') // remove the trailing 'e'
                throw new BEncodingException("Invalid data found. Aborting");
        }

        public static BEncodedDictionary DecodeTorrent(byte[] bytes)
        {
            return DecodeTorrent(new MemoryStream(bytes));
        }

        public static BEncodedDictionary DecodeTorrent(Stream s)
        {
            return DecodeTorrent(new RawReader(s));
        }


        /// <summary>
        ///     Special decoding method for torrent files - allows dictionary attributes to be out of order for the
        ///     overall torrent file, but imposes strict rules on the info dictionary.
        /// </summary>
        /// <returns></returns>
        public static BEncodedDictionary DecodeTorrent(RawReader reader)
        {
            var torrent = new BEncodedDictionary();
            if (reader.ReadByte() != 'd')
                throw new BEncodingException("Invalid data found. Aborting"); // Remove the leading 'd'

            while ((reader.PeekByte() != -1) && (reader.PeekByte() != 'e'))
            {
                var key = (BEncodedString) Decode(reader);

                BEncodedValue value;
                if (reader.PeekByte() == 'd')
                {
                    value = new BEncodedDictionary();
                    ((BEncodedDictionary) value).DecodeInternal(reader, key.Text.ToLower().Equals("info"));
                }
                else
                    value = Decode(reader); // the value is a BEncoded value

                torrent._dictionary.Add(key, value);
            }

            if (reader.ReadByte() != 'e') // remove the trailing 'e'
                throw new BEncodingException("Invalid data found. Aborting");

            return torrent;
        }

        #endregion

        #region Overridden Methods

        public override bool Equals(object obj)
        {
            var other = obj as BEncodedDictionary;

            if (_dictionary.Count != other?._dictionary.Count)
                return false;

            foreach (var keypair in _dictionary)
            {
                BEncodedValue val;
                if (!other.TryGetValue(keypair.Key, out val))
                    return false;

                if (!keypair.Value.Equals(val))
                    return false;
            }

            return true;
        }

        public override int GetHashCode()
        {
            var result = 0;
            foreach (var keypair in _dictionary)
            {
                result ^= keypair.Key.GetHashCode();
                result ^= keypair.Value.GetHashCode();
            }

            return result;
        }

        public override string ToString()
        {
            return Encoding.UTF8.GetString(Encode());
        }

        #endregion

        #region IDictionary and IList methods

        public void Add(BEncodedString key, BEncodedValue value)
        {
            _dictionary.Add(key, value);
        }

        public void Add(KeyValuePair<BEncodedString, BEncodedValue> item)
        {
            _dictionary.Add(item.Key, item.Value);
        }

        public void Clear()
        {
            _dictionary.Clear();
        }

        public bool Contains(KeyValuePair<BEncodedString, BEncodedValue> item)
        {
            return _dictionary.ContainsKey(item.Key) && _dictionary[item.Key].Equals(item.Value);
        }

        public bool ContainsKey(BEncodedString key)
        {
            return _dictionary.ContainsKey(key);
        }

        public void CopyTo(KeyValuePair<BEncodedString, BEncodedValue>[] array, int arrayIndex)
        {
            _dictionary.CopyTo(array, arrayIndex);
        }

        public int Count => _dictionary.Count;

        //public int IndexOf(KeyValuePair<BEncodedString, IBEncodedValue> item)
        //{
        //    return this.dictionary.IndexOf(item);
        //}

        //public void Insert(int index, KeyValuePair<BEncodedString, IBEncodedValue> item)
        //{
        //    this.dictionary.Insert(index, item);
        //}

        public bool IsReadOnly => false;

        public bool Remove(BEncodedString key)
        {
            return _dictionary.Remove(key);
        }

        public bool Remove(KeyValuePair<BEncodedString, BEncodedValue> item)
        {
            return _dictionary.Remove(item.Key);
        }

        //public void RemoveAt(int index)
        //{
        //    this.dictionary.RemoveAt(index);
        //}

        public bool TryGetValue(BEncodedString key, out BEncodedValue value)
        {
            return _dictionary.TryGetValue(key, out value);
        }

        public BEncodedValue this[BEncodedString key]
        {
            get { return _dictionary[key]; }
            set { _dictionary[key] = value; }
        }

        //public KeyValuePair<BEncodedString, IBEncodedValue> this[int index]
        //{
        //    get { return this.dictionary[index]; }
        //    set { this.dictionary[index] = value; }
        //}

        public ICollection<BEncodedString> Keys => _dictionary.Keys;

        public ICollection<BEncodedValue> Values => _dictionary.Values;

        public IEnumerator<KeyValuePair<BEncodedString, BEncodedValue>> GetEnumerator()
        {
            return _dictionary.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _dictionary.GetEnumerator();
        }

        #endregion
    }
}