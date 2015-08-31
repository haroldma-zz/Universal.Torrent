//
// BufferManager.cs
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
using System.Collections.Generic;
using System.Diagnostics;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.Managers
{
    public enum BufferType
    {
        SmallMessageBuffer,
        MediumMessageBuffer,
        LargeMessageBuffer,
        MassiveBuffer
    }

    public class BufferManager
    {
        internal static readonly int SmallMessageBufferSize = 1 << 8; // 256 bytes
        internal static readonly int MediumMessageBufferSize = 1 << 11; // 2048 bytes

        internal static readonly int LargeMessageBufferSize = Piece.BlockSize + 32;
        // 16384 bytes + 32. Enough for a complete piece aswell as the overhead

        public static readonly byte[] EmptyBuffer = new byte[0];

        private readonly Queue<byte[]> _largeMessageBuffers;
        private readonly Queue<byte[]> _massiveBuffers;
        private readonly Queue<byte[]> _mediumMessageBuffers;
        private readonly Queue<byte[]> _smallMessageBuffers;

        /// <summary>
        ///     The class that controls the allocating and deallocating of all byte[] buffers used in the engine.
        /// </summary>
        public BufferManager()
        {
            _massiveBuffers = new Queue<byte[]>();
            _largeMessageBuffers = new Queue<byte[]>();
            _mediumMessageBuffers = new Queue<byte[]>();
            _smallMessageBuffers = new Queue<byte[]>();

            // Preallocate some of each buffer to help avoid heap fragmentation due to pinning
            AllocateBuffers(4, BufferType.LargeMessageBuffer);
            AllocateBuffers(4, BufferType.MediumMessageBuffer);
            AllocateBuffers(4, BufferType.SmallMessageBuffer);
        }

        /// <summary>
        ///     Allocates an existing buffer from the pool
        /// </summary>
        /// <param name="buffer">The byte[]you want the buffer to be assigned to</param>
        /// <param name="type">The type of buffer that is needed</param>
        private void GetBuffer(ref byte[] buffer, BufferType type)
        {
            // We check to see if the buffer already there is the empty buffer. If it isn't, then we have
            // a buffer leak somewhere and the buffers aren't being freed properly.
            if (buffer != EmptyBuffer)
                throw new TorrentException("The old Buffer should have been recovered before getting a new buffer");

            // If we're getting a small buffer and there are none in the pool, just return a new one.
            // Otherwise return one from the pool.
            switch (type)
            {
                case BufferType.SmallMessageBuffer:
                    lock (_smallMessageBuffers)
                    {
                        if (_smallMessageBuffers.Count == 0)
                            AllocateBuffers(5, BufferType.SmallMessageBuffer);
                        buffer = _smallMessageBuffers.Dequeue();
                    }
                    break;
                case BufferType.MediumMessageBuffer:
                    lock (_mediumMessageBuffers)
                    {
                        if (_mediumMessageBuffers.Count == 0)
                            AllocateBuffers(5, BufferType.MediumMessageBuffer);
                        buffer = _mediumMessageBuffers.Dequeue();
                    }
                    break;
                case BufferType.LargeMessageBuffer:
                    lock (_largeMessageBuffers)
                    {
                        if (_largeMessageBuffers.Count == 0)
                            AllocateBuffers(5, BufferType.LargeMessageBuffer);
                        buffer = _largeMessageBuffers.Dequeue();
                    }
                    break;
                default:
                    throw new TorrentException("You cannot directly request a massive buffer");
            }
        }


        public byte[] GetBuffer(int minCapacity)
        {
            var buffer = EmptyBuffer;
            GetBuffer(ref buffer, minCapacity);
            return buffer;
        }

        /// <summary>
        ///     Allocates an existing buffer from the pool
        /// </summary>
        /// <param name="buffer">The byte[]you want the buffer to be assigned to</param>
        /// <param name="minCapacity">The minimum capacity.</param>
        /// <exception cref="TorrentException">The old Buffer should have been recovered before getting a new buffer</exception>
        public void GetBuffer(ref byte[] buffer, int minCapacity)
        {
            if (buffer != EmptyBuffer)
                throw new TorrentException("The old Buffer should have been recovered before getting a new buffer");

            if (minCapacity <= SmallMessageBufferSize)
                GetBuffer(ref buffer, BufferType.SmallMessageBuffer);

            else if (minCapacity <= MediumMessageBufferSize)
                GetBuffer(ref buffer, BufferType.MediumMessageBuffer);

            else if (minCapacity <= LargeMessageBufferSize)
                GetBuffer(ref buffer, BufferType.LargeMessageBuffer);

            else
            {
                lock (_massiveBuffers)
                {
                    for (var i = 0; i < _massiveBuffers.Count; i++)
                        if ((buffer = _massiveBuffers.Dequeue()).Length >= minCapacity)
                            return;
                        else
                            _massiveBuffers.Enqueue(buffer);

                    buffer = new byte[minCapacity];
                }
            }
        }


        public void FreeBuffer(byte[] buffer)
        {
            FreeBuffer(ref buffer);
        }

        public void FreeBuffer(ref byte[] buffer)
        {
            if (buffer == EmptyBuffer)
                return;

            if (buffer.Length == SmallMessageBufferSize)
                lock (_smallMessageBuffers)
                    _smallMessageBuffers.Enqueue(buffer);

            else if (buffer.Length == MediumMessageBufferSize)
                lock (_mediumMessageBuffers)
                    _mediumMessageBuffers.Enqueue(buffer);

            else if (buffer.Length == LargeMessageBufferSize)
                lock (_largeMessageBuffers)
                    _largeMessageBuffers.Enqueue(buffer);

            else if (buffer.Length > LargeMessageBufferSize)
                lock (_massiveBuffers)
                    _massiveBuffers.Enqueue(buffer);

            // All buffers should be allocated in this class, so if something else is passed in that isn't the right size
            // We just throw an exception as someone has done something wrong.
            else
                throw new TorrentException("That buffer wasn't created by this manager");

            buffer = EmptyBuffer; // After recovering the buffer, we send the "EmptyBuffer" back as a placeholder
        }


        private void AllocateBuffers(int number, BufferType type)
        {
            Debug.WriteLine("BufferManager - Allocating {0} buffers of type {1}", number, type);
            switch (type)
            {
                case BufferType.LargeMessageBuffer:
                    while (number-- > 0)
                        _largeMessageBuffers.Enqueue(new byte[LargeMessageBufferSize]);
                    break;
                case BufferType.MediumMessageBuffer:
                    while (number-- > 0)
                        _mediumMessageBuffers.Enqueue(new byte[MediumMessageBufferSize]);
                    break;
                case BufferType.SmallMessageBuffer:
                    while (number-- > 0)
                        _smallMessageBuffers.Enqueue(new byte[SmallMessageBufferSize]);
                    break;
                default:
                    throw new ArgumentException("Unsupported BufferType detected");
            }
        }
    }
}