#if !DISABLE_DHT
using Universal.Torrent.Dht.Tasks;

namespace Universal.Torrent.Dht.EventArgs
{
    internal class TaskCompleteEventArgs : System.EventArgs
    {
        public TaskCompleteEventArgs(Task task)
        {
            Task = task;
        }

        public Task Task { get; protected internal set; }
    }
}

#endif