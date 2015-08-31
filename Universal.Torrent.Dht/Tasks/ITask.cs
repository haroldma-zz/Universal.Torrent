#if !DISABLE_DHT
using System;
using Universal.Torrent.Dht.EventArgs;

namespace Universal.Torrent.Dht.Tasks
{
    internal interface ITask
    {
        bool Active { get; }
        event EventHandler<TaskCompleteEventArgs> Completed;
        void Execute();
    }
}

#endif