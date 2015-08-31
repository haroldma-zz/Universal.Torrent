//
// ConnectionMonitor.cs
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


using Universal.Torrent.Common;

namespace Universal.Torrent.Client.Managers
{
    /// <summary>
    ///     This class is used to track upload/download speed and bytes uploaded/downloaded for each connection
    /// </summary>
    public class ConnectionMonitor
    {
        #region Member Variables

        private readonly SpeedMonitor _dataDown;
        private readonly SpeedMonitor _dataUp;
        private readonly object _locker = new object();
        private readonly SpeedMonitor _protocolDown;
        private readonly SpeedMonitor _protocolUp;

        #endregion Member Variables

        #region Public Properties

        public long DataBytesDownloaded => _dataDown.Total;

        public long DataBytesUploaded => _dataUp.Total;

        public int DownloadSpeed => _dataDown.Rate + _protocolDown.Rate;

        public long ProtocolBytesDownloaded => _protocolDown.Total;

        public long ProtocolBytesUploaded => _protocolUp.Total;

        public int UploadSpeed => _dataUp.Rate + _protocolUp.Rate;

        #endregion Public Properties

        #region Constructors

        internal ConnectionMonitor()
            : this(12)
        {
        }

        internal ConnectionMonitor(int averagingPeriod)
        {
            _dataDown = new SpeedMonitor(averagingPeriod);
            _dataUp = new SpeedMonitor(averagingPeriod);
            _protocolDown = new SpeedMonitor(averagingPeriod);
            _protocolUp = new SpeedMonitor(averagingPeriod);
        }

        #endregion

        #region Methods

        internal void BytesSent(int bytesUploaded, TransferType type)
        {
            lock (_locker)
            {
                if (type == TransferType.Data)
                    _dataUp.AddDelta(bytesUploaded);
                else
                    _protocolUp.AddDelta(bytesUploaded);
            }
        }

        internal void BytesReceived(int bytesDownloaded, TransferType type)
        {
            lock (_locker)
            {
                if (type == TransferType.Data)
                    _dataDown.AddDelta(bytesDownloaded);
                else
                    _protocolDown.AddDelta(bytesDownloaded);
            }
        }

        internal void Reset()
        {
            lock (_locker)
            {
                _dataDown.Reset();
                _dataUp.Reset();
                _protocolDown.Reset();
                _protocolUp.Reset();
            }
        }

        internal void Tick()
        {
            lock (_locker)
            {
                _dataDown.Tick();
                _dataUp.Tick();
                _protocolDown.Tick();
                _protocolUp.Tick();
            }
        }

        #endregion
    }
}