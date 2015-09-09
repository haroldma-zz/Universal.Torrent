//
// MetadataMode.cs
//
// Authors:
//   Olivier Dufour olivier.duff@gmail.com
//   Alan McGovern alan.mcgovern@gmail.com
// Copyright (C) 2009 Olivier Dufour
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
using System.Diagnostics;
using System.IO;
using System.Linq;
using Windows.Storage;
using Universal.Torrent.Bencoding;
using Universal.Torrent.Client.Exceptions;
using Universal.Torrent.Client.Managers;
using Universal.Torrent.Client.Messages;
using Universal.Torrent.Client.Messages.FastPeerExtensions;
using Universal.Torrent.Client.Messages.LibtorrentMessages;
using Universal.Torrent.Client.Messages.StandardMessages;
using Universal.Torrent.Client.Messages.uTorrent;
using Universal.Torrent.Client.PeerConnections;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.Modes
{
    internal class MetadataMode : Mode
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
        private BitField _bitField;
        private PeerId _currentId;
        private DateTime _requestTimeout;
        private StorageFolder _saveFolder;

        public MetadataMode(TorrentManager manager, StorageFolder saveFolder)
            : base(manager)
        {
            _saveFolder = saveFolder;
        }

        public override bool CanHashCheck => true;

        public override TorrentState State => TorrentState.Metadata;

        internal MemoryStream Stream { get; private set; }

        public override void Tick(int counter)
        {
            //if one request have been sent and we have wait more than timeout
            // request the next peer
            if (_requestTimeout < DateTime.Now)
            {
                SendRequestToNextPeer();
            }
        }

        protected override void HandlePeerExchangeMessage(PeerId id, PeerExchangeMessage message)
        {
            // Nothing
        }

        private void SendRequestToNextPeer()
        {
            NextPeer();

            if (_currentId != null)
            {
                RequestNextNeededPiece(_currentId);
            }
        }

        private void NextPeer()
        {
            var flag = false;

            foreach (var id in Manager.Peers.ConnectedPeers.Where(id => id.SupportsLTMessages && id.ExtensionSupports.Supports(LTMetadata.Support.Name)))
            {
                if (Equals(id, _currentId))
                    flag = true;
                else if (flag)
                {
                    _currentId = id;
                    return;
                }
            }
            //second pass without removing the currentid and previous ones
            foreach (var id in Manager.Peers.ConnectedPeers.Where(id => id.SupportsLTMessages && id.ExtensionSupports.Supports(LTMetadata.Support.Name)))
            {
                _currentId = id;
                return;
            }
            _currentId = null;
        }

        protected override async void HandleLtMetadataMessage(PeerId id, LTMetadata message)
        {
            base.HandleLtMetadataMessage(id, message);

            switch (message.MetadataMessageType)
            {
                case LTMetadata.eMessageType.Data:
                    if (Stream == null)
                        throw new Exception("Need extention handshake before ut_metadata message.");

                    Stream.Seek(message.Piece*LTMetadata.BlockSize, SeekOrigin.Begin);
                    Stream.Write(message.MetadataPiece, 0, message.MetadataPiece.Length);
                    _bitField[message.Piece] = true;
                    if (_bitField.AllTrue)
                    {
                        Stream.Position = 0;
                        var hash = SHA1Helper.ComputeHash(Stream.ToArray());

                        if (!Manager.InfoHash.Equals(hash))
                        {
                            _bitField.SetAll(false);
                        }
                        else
                        {
                            Common.Torrent t;
                            Stream.Position = 0;
                            var dict = new BEncodedDictionary {{"info", BEncodedValue.Decode(Stream)}};
                            // FIXME: Add the trackers too
                            if (Common.Torrent.TryLoad(dict.Encode(), out t))
                            {
                                try
                                {
                                    var file = await _saveFolder.CreateFileAsync(Manager.InfoHash.ToHex() + ".torrent",
                                        CreationCollisionOption.ReplaceExisting);
                                    File.WriteAllBytes(file.Path, dict.Encode());
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine("*METADATA EXCEPTION* - Can not write in {0} : {1}", _saveFolder,
                                        ex);
                                    Manager.Error = new Error(Reason.WriteFailure, ex);
                                    Manager.Mode = new ErrorMode(Manager);
                                    return;
                                }
                                t.TorrentPath = _saveFolder.Path;
                                Manager.Torrent = t;
                                SwitchToRegular();
                            }
                            else
                            {
                                _bitField.SetAll(false);
                            }
                        }
                    }
                    //Double test because we can change the bitfield in the other block
                    if (!_bitField.AllTrue)
                    {
                        RequestNextNeededPiece(id);
                    }
                    break;
                case LTMetadata.eMessageType.Reject:
                    //TODO
                    //Think to what we do in this situation
                    //for moment nothing ;)
                    //reject or flood?
                    break;
                case LTMetadata.eMessageType.Request: //ever done in base class but needed to avoid default
                    break;
                default:
                    throw new MessageException(string.Format("Invalid messagetype in LTMetadata: {0}",
                        message.MetadataMessageType));
            }
        }

        private void SwitchToRegular()
        {
            var torrent = Manager.Torrent;
            foreach (var peer in Manager.Peers.ConnectedPeers)
                peer.CloseConnection();
            Manager.Bitfield = new BitField(torrent.Pieces.Count);
            Manager.PieceManager.ChangePicker(Manager.CreateStandardPicker(), Manager.Bitfield, torrent.Files);
            foreach (var file in torrent.Files)
                file.TargetFolder = Manager.SaveFolder;
            Manager.Start();
        }

        protected override void HandleAllowedFastMessage(PeerId id, AllowedFastMessage message)
        {
            // Disregard these when in metadata mode as we can't request regular pieces anyway
        }

        protected override void HandleHaveAllMessage(PeerId id, HaveAllMessage message)
        {
            // Nothing
        }

        protected override void HandleHaveMessage(PeerId id, HaveMessage message)
        {
            // Nothing
        }

        protected override void HandleHaveNoneMessage(PeerId id, HaveNoneMessage message)
        {
            // Nothing
        }

        protected override void HandleInterestedMessage(PeerId id, InterestedMessage message)
        {
            // Nothing
        }

        private void RequestNextNeededPiece(PeerId id)
        {
            var index = _bitField.FirstFalse();
            if (index == -1)
                return; //throw exception or switch to regular?

            var m = new LTMetadata(id, LTMetadata.eMessageType.Request, index);
            id.Enqueue(m);
            _requestTimeout = DateTime.Now.Add(Timeout);
        }

        internal Common.Torrent GetTorrent()
        {
            var calculatedInfoHash = SHA1Helper.ComputeHash(Stream.ToArray());
            if (!Manager.InfoHash.Equals(calculatedInfoHash))
                throw new Exception("invalid metadata"); //restart ?

            var d = BEncodedValue.Decode(Stream);
            var dict = new BEncodedDictionary {{"info", d}};

            return Common.Torrent.LoadCore(dict);
        }

        protected override void AppendBitfieldMessage(PeerId id, MessageBundle bundle)
        {
            // We can't send a bitfield message in metadata mode as
            // we don't know what size the bitfield is
        }

        protected override void HandleExtendedHandshakeMessage(PeerId id, ExtendedHandshakeMessage message)
        {
            base.HandleExtendedHandshakeMessage(id, message);

            if (id.ExtensionSupports.Supports(LTMetadata.Support.Name))
            {
                Stream = new MemoryStream(new byte[message.MetadataSize], 0, message.MetadataSize, true, true);
                var size = message.MetadataSize%LTMetadata.BlockSize;
                if (size > 0)
                    size = 1;
                size += message.MetadataSize/LTMetadata.BlockSize;
                _bitField = new BitField(size);
                RequestNextNeededPiece(id);
            }
        }

        protected override void SetAmInterestedStatus(PeerId id, bool interesting)
        {
            // Never set a peer as interesting when in metadata mode
            // we don't want to try download any data
        }
    }
}