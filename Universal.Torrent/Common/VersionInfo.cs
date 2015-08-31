//
// VersionInfo.cs
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

namespace Universal.Torrent.Common
{
    public static class VersionInfo
    {
        /// <summary>
        ///     Protocol string for version 1.0 of Bittorrent Protocol
        /// </summary>
        public static readonly string ProtocolStringV100 = "BitTorrent protocol";

        /// <summary>
        ///     The current version of the client
        /// </summary>
        public static readonly string ClientVersion = CreateClientVersion();

        public static readonly string DhtClientVersion = "UW06";

        internal static Version Version;

        private static string CreateClientVersion()
        {
            Version = new Version(1, 0);

            // 'UW' for Universal.Torrent then four digit version number
            var version =
                $"{Math.Max(Version.Major, 0)}{Math.Max(Version.Minor, 0)}{Math.Max(Version.Build, 0)}{Math.Max(Version.Revision, 0)}";
            version = version.Length > 4 ? version.Substring(0, 4) : version.PadRight(4, '0');
            return string.Format("-UW{0}-", version);
        }
    }
}