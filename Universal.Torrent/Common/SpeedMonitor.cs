//
// SpeedMonitor.cs
//
// Authors:
//   Alan McGovern alan.mcgovern@gmail.com
//
// Copyright (C) 2010 Alan McGovern
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

namespace Universal.Torrent.Common
{
    public class SpeedMonitor
    {
        private const int DefaultAveragePeriod = 12;
        private readonly int[] _speeds;
        private DateTime _lastUpdated;
        private int _speedsIndex;
        private long _tempRecvCount;


        public SpeedMonitor()
            : this(DefaultAveragePeriod)
        {
        }

        public SpeedMonitor(int averagingPeriod)
        {
            if (averagingPeriod < 0)
                throw new ArgumentOutOfRangeException(nameof(averagingPeriod));

            _lastUpdated = DateTime.UtcNow;
            _speeds = new int[Math.Max(1, averagingPeriod)];
            _speedsIndex = -_speeds.Length;
        }


        public int Rate { get; private set; }

        public long Total { get; private set; }


        public void AddDelta(int speed)
        {
            Total += speed;
            _tempRecvCount += speed;
        }

        public void AddDelta(long speed)
        {
            Total += speed;
            _tempRecvCount += speed;
        }

        public void Reset()
        {
            Total = 0;
            Rate = 0;
            _tempRecvCount = 0;
            _lastUpdated = DateTime.UtcNow;
            _speedsIndex = -_speeds.Length;
        }

        private void TimePeriodPassed(int difference)
        {
            var currSpeed = (int) (_tempRecvCount*1000/difference);
            _tempRecvCount = 0;

            int speedsCount;
            if (_speedsIndex < 0)
            {
                //speeds array hasn't been filled yet

                var idx = _speeds.Length + _speedsIndex;

                _speeds[idx] = currSpeed;
                speedsCount = idx + 1;

                _speedsIndex++;
            }
            else
            {
                //speeds array is full, keep wrapping around overwriting the oldest value
                _speeds[_speedsIndex] = currSpeed;
                speedsCount = _speeds.Length;

                _speedsIndex = (_speedsIndex + 1)%_speeds.Length;
            }

            var total = _speeds[0];
            for (var i = 1; i < speedsCount; i++)
                total += _speeds[i];

            Rate = total/speedsCount;
        }


        public void Tick()
        {
            var old = _lastUpdated;
            _lastUpdated = DateTime.UtcNow;
            var difference = (int) (_lastUpdated - old).TotalMilliseconds;

            if (difference > 800)
                TimePeriodPassed(difference);
        }

        // Used purely for unit testing purposes.
        internal void Tick(int difference)
        {
            _lastUpdated = DateTime.UtcNow;
            TimePeriodPassed(difference);
        }
    }
}