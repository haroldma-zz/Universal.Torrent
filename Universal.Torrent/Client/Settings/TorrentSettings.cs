//
// TorrentSettings.cs
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
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.Settings
{
    public class TorrentSettings : ICloneable
    {
        #region Member Variables

        public bool EnablePeerExchange { get; set; } = true;

        public bool InitialSeedingEnabled { get; set; }

        public int MaxDownloadSpeed { get; set; }

        public int MaxUploadSpeed { get; set; }

        public int MaxConnections { get; set; }

        public int UploadSlots
        {
            get { return _uploadSlots; }
            set
            {
                if (value < 1)
                    throw new ArgumentOutOfRangeException(nameof(value), "You must use at least 1 upload slot");
                _uploadSlots = value;
            }
        }

        private int _uploadSlots;

        /// <summary>
        ///     The choke/unchoke manager reviews how each torrent is making use of its upload slots.  If appropriate, it releases
        ///     one of the available slots and uses it to try a different peer
        ///     in case it gives us more data.  This value determines how long (in seconds) needs to expire between reviews.  If
        ///     set too short, peers will have insufficient time to start
        ///     downloading data and the choke/unchoke manager will choke them too early.  If set too long, we will spend more time
        ///     than is necessary waiting for a peer to give us data.
        ///     The default is 30 seconds.  A value of 0 disables the choke/unchoke manager altogether.
        /// </summary>
        public int MinimumTimeBetweenReviews { get; set; } = 30;

        /// <summary>
        ///     A percentage between 0 and 100; default 90.
        ///     When downloading, the choke/unchoke manager doesn't make any adjustments if the download speed is greater than this
        ///     percentage of the maximum download rate.
        ///     That way it will not try to improve download speed when the only likley effect will be to reduce download speeds.
        ///     When uploading, the choke/unchoke manager doesn't make any adjustments if the upload speed is greater than this
        ///     percentage of the maximum upload rate.
        /// </summary>
        public int PercentOfMaxRateToSkipReview
        {
            get { return _percentOfMaxRateToSkipReview; }
            set
            {
                if (value < 0 || value > 100)
                    throw new ArgumentOutOfRangeException();
                _percentOfMaxRateToSkipReview = value;
            }
        }

        private int _percentOfMaxRateToSkipReview = 90;

        /// <summary>
        ///     The time, in seconds, the inactivity manager should wait until it can consider a peer eligible for disconnection.
        ///     Peers are disconnected only if they have not provided
        ///     any data.  Default is 600.  A value of 0 disables the inactivity manager.
        /// </summary>
        public TimeSpan TimeToWaitUntilIdle
        {
            get { return _timeToWaitUntilIdle; }
            set
            {
                if (value.TotalSeconds < 0)
                    throw new ArgumentOutOfRangeException();
                _timeToWaitUntilIdle = value;
            }
        }

        private TimeSpan _timeToWaitUntilIdle = TimeSpan.FromMinutes(10);

        /// <summary>
        ///     When considering peers that have given us data, the inactivity manager will wait TimeToWaiTUntilIdle plus (Number
        ///     of bytes we've been sent / ConnectionRetentionFactor) seconds
        ///     before they are eligible for disconnection.  Default value is 2000.  A value of 0 prevents the inactivity manager
        ///     from disconnecting peers that have sent data.
        /// </summary>
        public long ConnectionRetentionFactor
        {
            get { return _connectionRetentionFactor; }
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException();
                _connectionRetentionFactor = value;
            }
        }

        private long _connectionRetentionFactor = 1024;

        // TODO: This value needs to be obeyed if it's changed
        // while the torrent is running
        public bool UseDht { get; set; } = true;

        #endregion

        #region Defaults

        private const int DefaultDownloadSpeed = 0;
        private const int DefaultMaxConnections = 60;
        private const int DefaultUploadSlots = 4;
        private const int DefaultUploadSpeed = 0;
        private const bool DefaultInitialSeedingEnabled = false;

        #endregion

        #region Constructors

        public TorrentSettings()
            : this(
                DefaultUploadSlots, DefaultMaxConnections, DefaultDownloadSpeed, DefaultUploadSpeed,
                DefaultInitialSeedingEnabled)
        {
        }

        public TorrentSettings(int uploadSlots)
            : this(
                uploadSlots, DefaultMaxConnections, DefaultDownloadSpeed, DefaultUploadSpeed,
                DefaultInitialSeedingEnabled)
        {
        }

        public TorrentSettings(int uploadSlots, int maxConnections)
            : this(uploadSlots, maxConnections, DefaultDownloadSpeed, DefaultUploadSpeed, DefaultInitialSeedingEnabled)
        {
        }

        public TorrentSettings(int uploadSlots, int maxConnections, int maxDownloadSpeed, int maxUploadSpeed)
            : this(uploadSlots, maxConnections, maxDownloadSpeed, maxUploadSpeed, DefaultInitialSeedingEnabled)
        {
        }

        public TorrentSettings(int uploadSlots, int maxConnections, int maxDownloadSpeed, int maxUploadSpeed,
            bool initialSeedingEnabled)
        {
            MaxConnections = maxConnections;
            MaxDownloadSpeed = maxDownloadSpeed;
            MaxUploadSpeed = maxUploadSpeed;
            _uploadSlots = uploadSlots;
            InitialSeedingEnabled = initialSeedingEnabled;
        }

        #endregion

        #region Methods

        object ICloneable.Clone()
        {
            return Clone();
        }

        public TorrentSettings Clone()
        {
            return (TorrentSettings) MemberwiseClone();
        }

        public override bool Equals(object obj)
        {
            var settings = obj as TorrentSettings;
            return (settings != null) && (InitialSeedingEnabled == settings.InitialSeedingEnabled &&
                                          MaxConnections == settings.MaxConnections &&
                                          MaxDownloadSpeed == settings.MaxDownloadSpeed &&
                                          MaxUploadSpeed == settings.MaxUploadSpeed &&
                                          _uploadSlots == settings._uploadSlots);
        }

        public override int GetHashCode()
        {
            return InitialSeedingEnabled.GetHashCode() ^
                   MaxConnections ^
                   MaxDownloadSpeed ^
                   MaxUploadSpeed ^
                   _uploadSlots;
        }

        #endregion Methods
    }
}