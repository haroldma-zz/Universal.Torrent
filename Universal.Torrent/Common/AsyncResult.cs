//
// AsyncResult.cs
//
// Authors:
//   Alan McGovern alan.mcgovern@gmail.com
//
// Copyright (C) 2008 Alan McGovern
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
using System.Threading;

namespace Universal.Torrent.Common
{
    public class AsyncResult : IAsyncResult
    {
        #region Constructors

        public AsyncResult(AsyncCallback callback, object asyncState)
        {
            AsyncState = asyncState;
            Callback = callback;
            AsyncWaitHandle = new ManualResetEvent(false);
        }

        #endregion Constructors

        #region Member Variables

        #endregion Member Variables

        #region Properties

        public object AsyncState { get; }

        WaitHandle IAsyncResult.AsyncWaitHandle => AsyncWaitHandle;

        protected internal ManualResetEvent AsyncWaitHandle { get; }

        internal AsyncCallback Callback { get; }

        public bool CompletedSynchronously { get; protected internal set; }

        public bool IsCompleted { get; protected internal set; }

        protected internal Exception SavedException { get; set; }

        #endregion Properties

        #region Methods

        protected internal void Complete()
        {
            Complete(SavedException);
        }

        protected internal void Complete(Exception ex)
        {
            // Ensure we only complete once - Needed because in encryption there could be
            // both a pending send and pending receive so if there is an error, both will
            // attempt to complete the encryption handshake meaning this is called twice.
            if (IsCompleted)
                return;

            SavedException = ex;
            CompletedSynchronously = false;
            IsCompleted = true;
            AsyncWaitHandle.Set();

            Callback?.Invoke(this);
        }

        #endregion Methods
    }
}