#if !DISABLE_DHT
using System;
using Universal.Torrent.Dht.EventArgs;
using Universal.Torrent.Dht.Messages.Queries;
using Universal.Torrent.Dht.Nodes;
using Universal.Torrent.Dht.RoutingTable;

namespace Universal.Torrent.Dht.Tasks
{
    internal class ReplaceNodeTask : Task
    {
        private readonly Bucket _bucket;
        private readonly DhtEngine _engine;
        private readonly Node _newNode;

        public ReplaceNodeTask(DhtEngine engine, Bucket bucket, Node newNode)
        {
            _engine = engine;
            _bucket = bucket;
            _newNode = newNode;
        }

        public override void Execute()
        {
            DhtEngine.MainLoop.Queue(delegate
            {
                if (_bucket.Nodes.Count == 0)
                {
                    RaiseComplete(new TaskCompleteEventArgs(this));
                    return;
                }

                SendPingToOldest();
            });
        }

        private void SendPingToOldest()
        {
            _bucket.LastChanged = DateTime.UtcNow;
            _bucket.SortBySeen();

            if ((DateTime.UtcNow - _bucket.Nodes[0].LastSeen) < TimeSpan.FromMinutes(3))
            {
                RaiseComplete(new TaskCompleteEventArgs(this));
            }
            else
            {
                var oldest = _bucket.Nodes[0];
                var task = new SendQueryTask(_engine, new Ping(_engine.LocalId), oldest);
                task.Completed += TaskComplete;
                task.Execute();
            }
        }

        private void TaskComplete(object sender, TaskCompleteEventArgs e)
        {
            e.Task.Completed -= TaskComplete;

            // I should raise the event with some eventargs saying which node was dead
            var args = (SendQueryEventArgs) e;

            if (args.TimedOut)
            {
                // If the node didn't respond and it's no longer in our bucket,
                // we need to send a ping to the oldest node in the bucket
                // Otherwise if we have a non-responder and it's still there, replace it!
                var index = _bucket.Nodes.IndexOf(((SendQueryTask) e.Task).Target);
                if (index < 0)
                {
                    SendPingToOldest();
                }
                else
                {
                    _bucket.Nodes[index] = _newNode;
                    RaiseComplete(new TaskCompleteEventArgs(this));
                }
            }
            else
            {
                SendPingToOldest();
            }
        }
    }
}

#endif