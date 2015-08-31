//
// MessageEventArgs.cs
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


using Universal.Torrent.Client.Args;
using Universal.Torrent.Client.Messages;
using Universal.Torrent.Client.PeerConnections;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.Managers
{
    /// <summary>
    ///     Provides the data needed to handle a PeerMessage event
    /// </summary>
    public class PeerMessageEventArgs : TorrentEventArgs
    {
        #region Constructors

        /// <summary>
        /// Creates a new PeerMessageEventArgs
        /// </summary>
        /// <param name="manager">The manager.</param>
        /// <param name="message">The peer message involved</param>
        /// <param name="direction">The direction of the message</param>
        /// <param name="id">The identifier.</param>
        internal PeerMessageEventArgs(TorrentManager manager, PeerMessage message, Direction direction, PeerId id)
            : base(manager)
        {
            Direction = direction;
            ID = id;
            Message = message;
        }

        #endregion

        #region Member Variables

        /// <summary>
        ///     The Peer message that was just sent/Received
        /// </summary>
        public PeerMessage Message { get; }

        /// <summary>
        ///     The direction of the message (outgoing/incoming)
        /// </summary>
        public Direction Direction { get; }

        public PeerId ID { get; }

        #endregion
    }
}