#if !DISABLE_DHT
using System.Diagnostics;
using Universal.Torrent.Dht.EventArgs;
using Universal.Torrent.Dht.Messages.Queries;
using Universal.Torrent.Dht.Nodes;
using Universal.Torrent.Dht.RoutingTable;

namespace Universal.Torrent.Dht.Tasks
{
    internal class RefreshBucketTask : Task
    {
        private readonly Bucket _bucket;
        private readonly DhtEngine _engine;
        private FindNode _message;
        private Node _node;
        private SendQueryTask _task;

        public RefreshBucketTask(DhtEngine engine, Bucket bucket)
        {
            _engine = engine;
            _bucket = bucket;
        }

        public override void Execute()
        {
            if (_bucket.Nodes.Count == 0)
            {
                RaiseComplete(new TaskCompleteEventArgs(this));
                return;
            }

            Debug.WriteLine("Choosing first from: {0}", _bucket.Nodes.Count);
            _bucket.SortBySeen();
            QueryNode(_bucket.Nodes[0]);
        }

        private void TaskComplete(object o, TaskCompleteEventArgs e)
        {
            _task.Completed -= TaskComplete;

            var args = (SendQueryEventArgs) e;
            if (args.TimedOut)
            {
                _bucket.SortBySeen();
                var index = _bucket.Nodes.IndexOf(_node);
                if (index == -1 || (++index < _bucket.Nodes.Count))
                {
                    QueryNode(_bucket.Nodes[0]);
                }
                else
                {
                    RaiseComplete(new TaskCompleteEventArgs(this));
                }
            }
            else
            {
                RaiseComplete(new TaskCompleteEventArgs(this));
            }
        }

        private void QueryNode(Node node)
        {
            _node = node;
            _message = new FindNode(_engine.LocalId, node.Id);
            _task = new SendQueryTask(_engine, _message, node);
            _task.Completed += TaskComplete;
            _task.Execute();
        }
    }
}

#endif