#if !DISABLE_DHT
using System.Collections.Generic;
using System.Net;
using Universal.Torrent.Common;
using Universal.Torrent.Dht.EventArgs;
using Universal.Torrent.Dht.Messages.Queries;
using Universal.Torrent.Dht.Messages.Responses;
using Universal.Torrent.Dht.Nodes;
using Universal.Torrent.Dht.RoutingTable;

namespace Universal.Torrent.Dht.Tasks
{
    internal class InitialiseTask : Task
    {
        private readonly SortedList<NodeId, NodeId> _nodes = new SortedList<NodeId, NodeId>();
        private int _activeRequests;
        private DhtEngine _engine;
        private List<Node> _initialNodes;

        public InitialiseTask(DhtEngine engine)
        {
            Initialise(engine, null);
        }

        public InitialiseTask(DhtEngine engine, byte[] initialNodes)
        {
            Initialise(engine, initialNodes == null ? null : Node.FromCompactNode(initialNodes));
        }

        public InitialiseTask(DhtEngine engine, IEnumerable<Node> nodes)
        {
            Initialise(engine, nodes);
        }

        private void Initialise(DhtEngine engine, IEnumerable<Node> nodes)
        {
            _engine = engine;
            _initialNodes = new List<Node>();
            if (nodes != null)
                _initialNodes.AddRange(nodes);
        }

        public override void Execute()
        {
            if (Active)
                return;

            Active = true;

            // If we were given a list of nodes to load at the start, use them
            if (_initialNodes.Count > 0)
            {
                foreach (var node in _initialNodes)
                    _engine.Add(node);
                SendFindNode(_initialNodes);
            }
            else
            {
                try
                {
                    var utorrent = new Node(NodeId.Create(),
                        new IPEndPoint(Dns.ResolveAddress("router.bittorrent.com"), 6881));
                    SendFindNode(new[] {utorrent});
                }
                catch
                {
                    RaiseComplete(new TaskCompleteEventArgs(this));
                }
            }
        }

        private void FindNodeComplete(object sender, TaskCompleteEventArgs e)
        {
            e.Task.Completed -= FindNodeComplete;
            _activeRequests--;

            var args = (SendQueryEventArgs) e;
            if (!args.TimedOut)
            {
                var response = (FindNodeResponse) args.Response;
                SendFindNode(Node.FromCompactNode(response.Nodes));
            }
            
            if (_activeRequests == 0)
                RaiseComplete(new TaskCompleteEventArgs(this));
        }

        protected override void RaiseComplete(TaskCompleteEventArgs e)
        {
            if (!Active)
                return;

            // If we were given a list of initial nodes and they were all dead,
            // initialise again except use the utorrent router.
            if (_initialNodes.Count > 0 && _engine.RoutingTable.CountNodes() < 10)
            {
                new InitialiseTask(_engine).Execute();
            }
            else
            {
                _engine.RaiseStateChanged(DhtState.Ready);
            }

            Active = false;
            base.RaiseComplete(e);
        }

        private void SendFindNode(IEnumerable<Node> newNodes)
        {
            foreach (var node in Node.CloserNodes(_engine.LocalId, _nodes, newNodes, Bucket.MaxCapacity))
            {
                _activeRequests++;
                var request = new FindNode(_engine.LocalId, _engine.LocalId);
                var task = new SendQueryTask(_engine, request, node);
                task.Completed += FindNodeComplete;
                task.Execute();
            }
        }
    }
}

#endif