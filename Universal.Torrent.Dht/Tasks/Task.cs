#if !DISABLE_DHT
using System;
using Universal.Torrent.Dht.EventArgs;

namespace Universal.Torrent.Dht.Tasks
{
    internal abstract class Task : ITask
    {
        public event EventHandler<TaskCompleteEventArgs> Completed;

        public bool Active { get; protected set; }

        public abstract void Execute();

        protected virtual void RaiseComplete(TaskCompleteEventArgs e)
        {
            Completed?.Invoke(this, e);
        }
    }
}

#endif