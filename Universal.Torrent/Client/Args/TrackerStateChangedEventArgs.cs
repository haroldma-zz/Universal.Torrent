//
// TrackerStateChangedEventArgs.cs
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


using Universal.Torrent.Client.Managers;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.Args
{
    /// <summary>
    ///     Provides the data needed to handle a TrackerUpdate event
    /// </summary>
    public class TrackerStateChangedEventArgs : TorrentEventArgs
    {
        #region Constructors

        /// <summary>
        /// Creates a new TrackerUpdateEventArgs
        /// </summary>
        /// <param name="manager">The manager.</param>
        /// <param name="tracker">The tracker.</param>
        /// <param name="oldState">The old state.</param>
        /// <param name="newState">The new state.</param>
        public TrackerStateChangedEventArgs(TorrentManager manager, Tracker.Tracker tracker, TrackerState oldState,
            TrackerState newState)
            : base(manager)
        {
            Tracker = tracker;
            OldState = oldState;
            NewState = newState;
        }

        #endregion

        #region Member Variables

        /// <summary>
        ///     The current status of the tracker update
        /// </summary>
        public Tracker.Tracker Tracker { get; }


        public TrackerState OldState { get; }


        public TrackerState NewState { get; }

        #endregion
    }
}