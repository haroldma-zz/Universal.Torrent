//
// EngineSettings.cs
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


using System.Net;
using Windows.Storage;
using Universal.Torrent.Client.Encryption;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.Settings
{
    /// <summary>
    ///     Represents the Settings which need to be passed to the engine
    /// </summary>
    public class EngineSettings : ICloneable
    {
        #region Properties                 

        // The minimum encryption level to use. "None" corresponds to no encryption.
        public EncryptionTypes AllowedEncryption { get; set; }

        // True if you want to enable have surpression
        public bool HaveSupressionEnabled { get; set; }

        // The maximum number of connections that can be opened
        public int GlobalMaxConnections { get; set; }

        // The maximum number of simultaenous 1/2 open connections
        public int GlobalMaxHalfOpenConnections { get; set; }

        // The maximum combined download speed
        public int GlobalMaxDownloadSpeed { get; set; }

        // The maximum combined upload speed
        public int GlobalMaxUploadSpeed { get; set; }

        // The port to listen to incoming connections on]
        public int ListenPort { get; set; }

        // The maximum number of simultaenous open filestreams
        public int MaxOpenFiles { get; set; } = 15;
        // The maximum read rate from the harddisk (for all active torrentmanagers)
        public int MaxReadRate { get; set; }
        // The maximum write rate to the harddisk (for all active torrentmanagers)
        public int MaxWriteRate { get; set; }
        // The IPEndpoint reported to the tracker
        public IPEndPoint ReportedAddress { get; set; }
        // If encrypted and unencrypted connections are enabled, specifies if encryption should be chosen first
        public bool PreferEncryption { get; set; }
        // The path that torrents will be downloaded to by default
        public StorageFolder SaveFolder { get; set; }

        #endregion Properties

        #region Defaults

        private const bool DefaultEnableHaveSupression = false;
        private static readonly StorageFolder DefaultSavePath = ApplicationData.Current.LocalFolder;
        private const int DefaultMaxConnections = 150;
        private const int DefaultMaxDownloadSpeed = 0;
        private const int DefaultMaxUploadSpeed = 0;
        private const int DefaultMaxHalfOpenConnections = 5;
        private const EncryptionTypes DefaultAllowedEncryption = EncryptionTypes.All;
        private const int DefaultListenPort = 52139;

        #endregion

        #region Constructors

        public EngineSettings()
            : this(DefaultSavePath, DefaultListenPort, DefaultMaxConnections, DefaultMaxHalfOpenConnections,
                DefaultMaxDownloadSpeed, DefaultMaxUploadSpeed, DefaultAllowedEncryption)
        {
        }

        public EngineSettings(StorageFolder defaultSaveFolder, int listenPort)
            : this(
                defaultSaveFolder, listenPort, DefaultMaxConnections, DefaultMaxHalfOpenConnections,
                DefaultMaxDownloadSpeed, DefaultMaxUploadSpeed, DefaultAllowedEncryption)
        {
        }

        public EngineSettings(StorageFolder defaultSaveFolder, int listenPort, int globalMaxConnections)
            : this(
                defaultSaveFolder, listenPort, globalMaxConnections, DefaultMaxHalfOpenConnections,
                DefaultMaxDownloadSpeed, DefaultMaxUploadSpeed, DefaultAllowedEncryption)
        {
        }

        public EngineSettings(StorageFolder defaultSaveFolder, int listenPort, int globalMaxConnections,
            int globalHalfOpenConnections)
            : this(
                defaultSaveFolder, listenPort, globalMaxConnections, globalHalfOpenConnections, DefaultMaxDownloadSpeed,
                DefaultMaxUploadSpeed, DefaultAllowedEncryption)
        {
        }

        public EngineSettings(StorageFolder defaultSaveFolder, int listenPort, int globalMaxConnections,
            int globalHalfOpenConnections, int globalMaxDownloadSpeed, int globalMaxUploadSpeed,
            EncryptionTypes allowedEncryption)
        {
            GlobalMaxConnections = globalMaxConnections;
            GlobalMaxDownloadSpeed = globalMaxDownloadSpeed;
            GlobalMaxUploadSpeed = globalMaxUploadSpeed;
            GlobalMaxHalfOpenConnections = globalHalfOpenConnections;
            ListenPort = listenPort;
            AllowedEncryption = allowedEncryption;
            SaveFolder = defaultSaveFolder;
            HaveSupressionEnabled = DefaultEnableHaveSupression;
        }

        #endregion

        #region Methods

        object ICloneable.Clone()
        {
            return Clone();
        }

        public EngineSettings Clone()
        {
            return (EngineSettings) MemberwiseClone();
        }

        public override bool Equals(object obj)
        {
            var settings = obj as EngineSettings;
            return (settings != null) && (GlobalMaxConnections == settings.GlobalMaxConnections &&
                                          GlobalMaxDownloadSpeed == settings.GlobalMaxDownloadSpeed &&
                                          GlobalMaxHalfOpenConnections == settings.GlobalMaxHalfOpenConnections &&
                                          GlobalMaxUploadSpeed == settings.GlobalMaxUploadSpeed &&
                                          ListenPort == settings.ListenPort &&
                                          AllowedEncryption == settings.AllowedEncryption &&
                                          SaveFolder == settings.SaveFolder);
        }

        public override int GetHashCode()
        {
            return GlobalMaxConnections +
                   GlobalMaxDownloadSpeed +
                   GlobalMaxHalfOpenConnections +
                   GlobalMaxUploadSpeed +
                   ListenPort.GetHashCode() +
                   AllowedEncryption.GetHashCode() +
                   SaveFolder.GetHashCode();
        }

        #endregion Methods
    }
}