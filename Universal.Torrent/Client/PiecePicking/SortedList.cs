//
// SortedList.cs
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
using System.Collections;
using System.Collections.Generic;

namespace Universal.Torrent.Client.PiecePicking
{
    public class SortList<T> : IList<T>
    {
        private readonly List<T> _list;

        public SortList()
        {
            _list = new List<T>();
        }

        public SortList(IEnumerable<T> list)
        {
            _list = new List<T>(list);
        }

        public int IndexOf(T item)
        {
            var index = _list.BinarySearch(item);
            return index < 0 ? -1 : index;
        }

        public void Insert(int index, T item)
        {
            Add(item);
        }

        public void RemoveAt(int index)
        {
            _list.RemoveAt(index);
        }

        public T this[int index]
        {
            get { return _list[index]; }
            set { _list[index] = value; }
        }

        public void Add(T item)
        {
            var index = _list.BinarySearch(item);
            if (index < 0)
                _list.Insert(~index, item);
            else
                _list.Insert(index, item);
        }

        public void Clear()
        {
            _list.Clear();
        }

        public bool Contains(T item)
        {
            return _list.BinarySearch(item) >= 0;
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            _list.CopyTo(array, arrayIndex);
        }

        public int Count => _list.Count;

        public bool IsReadOnly => false;

        public bool Remove(T item)
        {
            var index = _list.BinarySearch(item);
            if (index < 0)
                return false;
            _list.RemoveAt(index);
            return true;
        }

        public IEnumerator<T> GetEnumerator()
        {
            return _list.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public int BinarySearch(T piece, IComparer<T> comparer)
        {
            return _list.BinarySearch(piece, comparer);
        }

        public bool Exists(Predicate<T> predicate)
        {
            return _list.Exists(predicate);
        }

        public List<T> FindAll(Predicate<T> predicate)
        {
            return _list.FindAll(predicate);
        }

        public void ForEach(Action<T> action)
        {
            _list.ForEach(action);
        }

        public int RemoveAll(Predicate<T> predicate)
        {
            return _list.RemoveAll(predicate);
        }
    }
}