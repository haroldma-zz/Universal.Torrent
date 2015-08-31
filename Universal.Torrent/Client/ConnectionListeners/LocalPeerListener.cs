//
// LocalPeerListener.cs
//
// Authors:
//   Jared Hendry hendry.jared@gmail.com
//
// Copyright (C) 2008 Jared Hendry
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
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Windows.Networking;
using Windows.Networking.Sockets;
using Universal.Torrent.Client.Args;
using Universal.Torrent.Client.Encryption;
using Universal.Torrent.Client.Managers;
using Universal.Torrent.Client.Peers;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.ConnectionListeners
{
    internal class LocalPeerListener : Listener
    {
        private const int MulticastPort = 6771;
        private static readonly HostName MulticastIpAddress = new HostName("239.192.152.143");

        private readonly ClientEngine _engine;
        private DatagramSocket _datagramSocket;

        public LocalPeerListener(ClientEngine engine)
            : base(new IPEndPoint(IPAddress.Any, 6771))
        {
            _engine = engine;
        }

        public override async void Start()
        {
            if (Status == ListenerStatus.Listening)
                return;
            try
            {
                _datagramSocket = new DatagramSocket();
                _datagramSocket.MessageReceived += DatagramSocketOnMessageReceived;
                await _datagramSocket.BindServiceNameAsync(MulticastPort.ToString());
                _datagramSocket.JoinMulticastGroup(MulticastIpAddress);
                RaiseStatusChanged(ListenerStatus.Listening);
            }
            catch
            {
                RaiseStatusChanged(ListenerStatus.PortNotFree);
            }
        }

        private void DatagramSocketOnMessageReceived(DatagramSocket sender, DatagramSocketMessageReceivedEventArgs args)
        {
            try
            {
                var dataReader = args.GetDataReader();
                var receiveBytes = new byte[dataReader.UnconsumedBufferLength];
                dataReader.ReadBytes(receiveBytes);
                var receiveString = Encoding.ASCII.GetString(receiveBytes);
                var exp = new Regex(
                    "BT-SEARCH \\* HTTP/1.1\\r\\nHost: 239.192.152.143:6771\\r\\nPort: (?<port>[^@]+)\\r\\nInfohash: (?<hash>[^@]+)\\r\\n\\r\\n\\r\\n");
                var match = exp.Match(receiveString);

                if (!match.Success)
                    return;

                var portcheck = Convert.ToInt32(match.Groups["port"].Value);
                if (portcheck < 0 || portcheck > 65535)
                    return;

                TorrentManager manager = null;
                var matchHash = InfoHash.FromHex(match.Groups["hash"].Value);
                for (var i = 0; manager == null && i < _engine.Torrents.Count; i++)
                    if (_engine.Torrents[i].InfoHash == matchHash)
                        manager = _engine.Torrents[i];

                if (manager == null)
                    return;

                var uri = new Uri("tcp://" + args.RemoteAddress.RawName + ':' + match.Groups["port"].Value);
                var peer = new Peer("", uri, EncryptionTypes.All);

                // Add new peer to matched Torrent
                if (manager.HasMetadata && manager.Torrent.IsPrivate)
                    return;
                ClientEngine.MainLoop.Queue(delegate
                {
                    var count = manager.AddPeersCore(peer);
                    manager.RaisePeersFound(new LocalPeersAdded(manager, count, 1));
                });
            }
            catch
            {
                // ignored
            }
        }

        public override void Stop()
        {
            if (Status == ListenerStatus.NotListening)
                return;

            RaiseStatusChanged(ListenerStatus.NotListening);
            _datagramSocket?.Dispose();
            _datagramSocket = null;
        }
    }
}