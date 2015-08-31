//
// MainLoop.cs
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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client
{
    public delegate void MainLoopResult(object result);

    public delegate object MainLoopJob();

    public delegate void MainLoopTask();

    public delegate bool TimeoutTask();

    public class MainLoop
    {
        private readonly ICache<DelegateTask> _cache = new Cache<DelegateTask>(true).Synchronize();

        private readonly TimeoutDispatcher _dispatcher = new TimeoutDispatcher();
        private readonly AutoResetEvent _handle = new AutoResetEvent(false);
        private readonly Queue<DelegateTask> _tasks = new Queue<DelegateTask>();
        private int _currentManagedThreadId;

        public MainLoop()
        {
            Task.Factory.StartNew(Loop, TaskCreationOptions.LongRunning);
        }

        private void Loop()
        {
            _currentManagedThreadId = Environment.CurrentManagedThreadId;
            while (true)
            {
                try
                {
                    DelegateTask task = null;
                    lock (_tasks)
                    {
                        if (_tasks.Count > 0)
                            task = _tasks.Dequeue();
                    }

                    if (task == null)
                    {
                        _handle.WaitOne();
                    }
                    else
                    {
                        var reuse = !task.IsBlocking;
                        task.Execute();
                        if (reuse)
                            _cache.Enqueue(task);
                    }
                }
                catch
                {
                    // ignored
                }
            }
            // ReSharper disable once FunctionNeverReturns
        }

        private void Queue(DelegateTask task)
        {
            lock (_tasks)
            {
                _tasks.Enqueue(task);
                _handle.Set();
            }
        }

        public void Queue(MainLoopTask task)
        {
            var dTask = _cache.Dequeue();
            dTask.Task = task;
            Queue(dTask);
        }

        public void QueueWait(MainLoopTask task)
        {
            if (IsInCurrentThread())
            {
                task();
                return;
            }

            var dTask = _cache.Dequeue();
            dTask.Task = task;
            try
            {
                QueueWait(dTask);
            }
            finally
            {
                _cache.Enqueue(dTask);
            }
        }

        public object QueueWait(MainLoopJob task)
        {
            if (IsInCurrentThread())
                return task();

            var dTask = _cache.Dequeue();
            dTask.Job = task;

            try
            {
                QueueWait(dTask);
                return dTask.JobResult;
            }
            finally
            {
                _cache.Enqueue(dTask);
            }
        }

        private void QueueWait(DelegateTask t)
        {
            t.WaitHandle.Reset();
            t.IsBlocking = true;

            Queue(t);

            t.WaitHandle.WaitOne();

            if (t.StoredException != null)
                throw new TorrentException("Exception in mainloop", t.StoredException);
        }

        public bool IsInCurrentThread()
        {
            return Environment.CurrentManagedThreadId == _currentManagedThreadId;
        }

        public uint QueueTimeout(TimeSpan span, TimeoutTask task)
        {
            var dTask = _cache.Dequeue();
            dTask.Timeout = task;

            return _dispatcher.Add(span, delegate
            {
                QueueWait(dTask);
                return dTask.TimeoutResult;
            });
        }

        public AsyncCallback Wrap(AsyncCallback callback)
        {
            return delegate(IAsyncResult result) { Queue(delegate { callback(result); }); };
        }

        private class DelegateTask : ICacheable
        {
            public DelegateTask()
            {
                WaitHandle = new ManualResetEvent(false);
            }

            public bool IsBlocking { get; set; }

            public MainLoopJob Job { private get; set; }

            public Exception StoredException { get; private set; }

            public MainLoopTask Task { private get; set; }

            public TimeoutTask Timeout { private get; set; }

            public object JobResult { get; private set; }

            public bool TimeoutResult { get; private set; }

            public ManualResetEvent WaitHandle { get; }

            public void Initialise()
            {
                IsBlocking = false;
                Job = null;
                JobResult = null;
                StoredException = null;
                Task = null;
                Timeout = null;
                TimeoutResult = false;
            }

            public void Execute()
            {
                try
                {
                    if (Job != null)
                        JobResult = Job();
                    else if (Task != null)
                        Task();
                    else if (Timeout != null)
                        TimeoutResult = Timeout();
                }
                catch (Exception ex)
                {
                    StoredException = ex;

                    // FIXME: I assume this case can't happen. The only user interaction
                    // with the mainloop is with blocking tasks. Internally it's a big bug
                    // if i allow an exception to propagate to the mainloop.
                    if (!IsBlocking)
                        throw;
                }
                finally
                {
                    WaitHandle.Set();
                }
            }
        }
    }
}