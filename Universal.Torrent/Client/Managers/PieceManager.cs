//
// PieceManager.cs
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
using System.Linq;
using Universal.Torrent.Client.Args;
using Universal.Torrent.Client.Messages;
using Universal.Torrent.Client.Messages.StandardMessages;
using Universal.Torrent.Client.PeerConnections;
using Universal.Torrent.Client.PiecePicking;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.Managers
{
    /// <summary>
    ///     Contains the logic for choosing what piece to download next
    /// </summary>
    public class PieceManager
    {
        internal PieceManager()
        {
            Picker = new NullPicker();
            UnhashedPieces = new BitField(0);
        }

        internal PiecePicker Picker { get; private set; }

        internal BitField UnhashedPieces { get; private set; }

        public void PieceDataReceived(PeerId peer, PieceMessage message)
        {
            Piece piece;
            if (Picker.ValidatePiece(peer, message.PieceIndex, message.StartOffset, message.RequestLength, out piece))
            {
                var id = peer;
                var manager = id.TorrentManager;
                var block = piece.Blocks[message.StartOffset/Piece.BlockSize];
                var offset = (long) message.PieceIndex*id.TorrentManager.Torrent.PieceLength + message.StartOffset;

                id.LastBlockReceived = DateTime.Now;
                id.TorrentManager.PieceManager.RaiseBlockReceived(new BlockEventArgs(manager, block, piece, id));
                id.TorrentManager.Engine.DiskManager.QueueWrite(manager, offset, message.Data, message.RequestLength,
                    delegate
                    {
                        piece.Blocks[message.StartOffset/Piece.BlockSize].Written = true;
                        ClientEngine.BufferManager.FreeBuffer(ref message.Data);
                        // If we haven't written all the pieces to disk, there's no point in hash checking
                        if (!piece.AllBlocksWritten)
                            return;

                        // Hashcheck the piece as we now have all the blocks.
                        id.Engine.DiskManager.BeginGetHash(id.TorrentManager, piece.Index, delegate(object o)
                        {
                            var hash = (byte[]) o;
                            var result = hash != null && id.TorrentManager.Torrent.Pieces.IsValid(hash, piece.Index);
                            id.TorrentManager.Bitfield[message.PieceIndex] = result;

                            ClientEngine.MainLoop.Queue(delegate
                            {
                                id.TorrentManager.PieceManager.UnhashedPieces[piece.Index] = false;

                                id.TorrentManager.HashedPiece(new PieceHashedEventArgs(id.TorrentManager, piece.Index,
                                    result));
                                var peers = new List<PeerId>(piece.Blocks.Length);
                                foreach (var t in piece.Blocks.Where(t => t.RequestedOff != null &&
                                                                          !peers.Contains(t.RequestedOff)))
                                    peers.Add(t.RequestedOff);

                                foreach (var t in peers.Where(t => t.Connection != null))
                                {
                                    t.Peer.HashedPiece(result);
                                    if (t.Peer.TotalHashFails == 5)
                                        t.ConnectionManager.CleanupSocket(id, "Too many hash fails");
                                }

                                // If the piece was successfully hashed, enqueue a new "have" message to be sent out
                                if (result)
                                    id.TorrentManager.FinishedPieces.Enqueue(piece.Index);
                            });
                        });
                    });

                if (piece.AllBlocksReceived)
                    UnhashedPieces[message.PieceIndex] = true;
            }
        }

        internal void AddPieceRequests(PeerId id)
        {
            PeerMessage msg;
            var maxRequests = id.MaxPendingRequests;

            if (id.AmRequestingPiecesCount >= maxRequests)
                return;

            var count = 1;
            if (id.Connection is HttpConnection)
            {
                // How many whole pieces fit into 2MB
                count = (2*1024*1024)/id.TorrentManager.Torrent.PieceLength;

                // Make sure we have at least one whole piece
                count = Math.Max(count, 1);

                count *= id.TorrentManager.Torrent.PieceLength/Piece.BlockSize;
            }

            if (!id.IsChoking || id.SupportsFastPeer)
            {
                while (id.AmRequestingPiecesCount < maxRequests)
                {
                    msg = Picker.ContinueExistingRequest(id);
                    if (msg != null)
                        id.Enqueue(msg);
                    else
                        break;
                }
            }

            if (!id.IsChoking || (id.SupportsFastPeer && id.IsAllowedFastPieces.Count > 0))
            {
                while (id.AmRequestingPiecesCount < maxRequests)
                {
                    msg = Picker.PickPiece(id, id.TorrentManager.Peers.ConnectedPeers, count);
                    if (msg != null)
                        id.Enqueue(msg);
                    else
                        break;
                }
            }
        }

        internal bool IsInteresting(PeerId id)
        {
            // If i have completed the torrent, then no-one is interesting
            if (id.TorrentManager.Complete)
                return false;

            // If the peer is a seeder, then he is definately interesting
            if ((id.Peer.IsSeeder = id.BitField.AllTrue))
                return true;

            // Otherwise we need to do a full check
            return Picker.IsInteresting(id.BitField);
        }

        internal void ChangePicker(PiecePicker picker, BitField bitfield, TorrentFile[] files)
        {
            if (UnhashedPieces.Length != bitfield.Length)
                UnhashedPieces = new BitField(bitfield.Length);

            picker = new IgnoringPicker(bitfield, picker);
            picker = new IgnoringPicker(UnhashedPieces, picker);
            IEnumerable<Piece> pieces = Picker == null ? new List<Piece>() : Picker.ExportActiveRequests();
            picker.Initialise(bitfield, files, pieces);
            Picker = picker;
        }

        internal void Reset()
        {
            UnhashedPieces.SetAll(false);
            Picker?.Reset();
        }

        internal int CurrentRequestCount()
        {
            return
                (int) ClientEngine.MainLoop.QueueWait(() => Picker.CurrentRequestCount());
        }

        #region Old

        // For every 10 kB/sec upload a peer has, we request one extra piece above the standard amount him
        internal const int BonusRequestPerKb = 10;
        internal const int NormalRequestAmount = 2;
        internal const int MaxEndGameRequests = 2;

        public event EventHandler<BlockEventArgs> BlockReceived;
        public event EventHandler<BlockEventArgs> BlockRequested;
        public event EventHandler<BlockEventArgs> BlockRequestCancelled;

        internal void RaiseBlockReceived(BlockEventArgs args)
        {
            Toolbox.RaiseAsyncEvent(BlockReceived, args.TorrentManager, args);
        }

        internal void RaiseBlockRequested(BlockEventArgs args)
        {
            Toolbox.RaiseAsyncEvent(BlockRequested, args.TorrentManager, args);
        }

        internal void RaiseBlockRequestCancelled(BlockEventArgs args)
        {
            Toolbox.RaiseAsyncEvent(BlockRequestCancelled, args.TorrentManager, args);
        }

        #endregion Old
    }
}