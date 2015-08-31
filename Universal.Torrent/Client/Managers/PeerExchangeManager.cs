//
// PeerExchangeManager.cs
//
// Authors:
//   Olivier Dufour olivier.duff@gmail.com
//
// Copyright (C) 2006 Olivier Dufour
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
using Universal.Torrent.Client.Args;
using Universal.Torrent.Client.Encryption;
using Universal.Torrent.Client.Messages.uTorrent;
using Universal.Torrent.Client.PeerConnections;
using Universal.Torrent.Client.Peers;

namespace Universal.Torrent.Client.Managers
{
    /// <summary>
    ///     This class is used to send each minute a peer excahnge message to peer who have enable this protocol
    /// </summary>
    public class PeerExchangeManager : IDisposable
    {
        #region Member Variables

        private readonly PeerId _id;
        private readonly List<Peer> _addedPeers;
        private readonly List<Peer> _droppedPeers;
        private bool _disposed;
        private const int MaxPeers = 50;

        #endregion Member Variables

        #region Constructors

        internal PeerExchangeManager(PeerId id)
        {
            _id = id;

            _addedPeers = new List<Peer>();
            _droppedPeers = new List<Peer>();
            id.TorrentManager.OnPeerFound += OnAdd;
            Start();
        }

        internal void OnAdd(object source, PeerAddedEventArgs e)
        {
            _addedPeers.Add(e.Peer);
        }

        // TODO onDropped!

        #endregion

        #region Methods

        internal void Start()
        {
            ClientEngine.MainLoop.QueueTimeout(TimeSpan.FromMinutes(1), delegate
            {
                if (!_disposed)
                    OnTick();
                return !_disposed;
            });
        }

        internal void OnTick()
        {
            if (!_id.TorrentManager.Settings.EnablePeerExchange)
                return;

            var len = (_addedPeers.Count <= MaxPeers) ? _addedPeers.Count : MaxPeers;
            var added = new byte[len*6];
            var addedDotF = new byte[len];
            for (var i = 0; i < len; i++)
            {
                _addedPeers[i].CompactPeer(added, i*6);
                if ((_addedPeers[i].Encryption & (EncryptionTypes.RC4Full | EncryptionTypes.RC4Header)) !=
                    EncryptionTypes.None)
                {
                    addedDotF[i] = 0x01;
                }
                else
                {
                    addedDotF[i] = 0x00;
                }

                addedDotF[i] |= (byte) (_addedPeers[i].IsSeeder ? 0x02 : 0x00);
            }
            _addedPeers.RemoveRange(0, len);

            len = Math.Min(MaxPeers - len, _droppedPeers.Count);

            var dropped = new byte[len*6];
            for (var i = 0; i < len; i++)
                _droppedPeers[i].CompactPeer(dropped, i*6);

            _droppedPeers.RemoveRange(0, len);
            _id.Enqueue(new PeerExchangeMessage(_id, added, addedDotF, dropped));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _id.TorrentManager.OnPeerFound -= OnAdd;
        }

        #endregion
    }
}