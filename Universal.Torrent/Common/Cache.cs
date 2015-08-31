//
// Cache.cs
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


using System.Collections.Generic;

namespace Universal.Torrent.Common
{
    internal interface ICache<T>
    {
        int Count { get; }
        T Dequeue();
        void Enqueue(T instance);
    }

    internal class Cache<T> : ICache<T>
        where T : class, ICacheable, new()
    {
        private readonly bool _autoCreate;
        private readonly Queue<T> _cache;

        public Cache()
            : this(false)
        {
        }

        public Cache(bool autoCreate)
        {
            _autoCreate = autoCreate;
            _cache = new Queue<T>();
        }

        public int Count => _cache.Count;

        public T Dequeue()
        {
            if (_cache.Count > 0)
                return _cache.Dequeue();
            return _autoCreate ? new T() : null;
        }

        public void Enqueue(T instance)
        {
            instance.Initialise();
            _cache.Enqueue(instance);
        }

        public ICache<T> Synchronize()
        {
            return new SynchronizedCache<T>(this);
        }
    }

    internal class SynchronizedCache<T> : ICache<T>
    {
        private readonly ICache<T> _cache;

        public SynchronizedCache(ICache<T> cache)
        {
            Check.Cache(cache);
            _cache = cache;
        }

        public int Count => _cache.Count;

        public T Dequeue()
        {
            lock (_cache)
                return _cache.Dequeue();
        }

        public void Enqueue(T instance)
        {
            lock (_cache)
                _cache.Enqueue(instance);
        }
    }
}