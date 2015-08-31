#if !DISABLE_DHT
using System.Collections.Generic;
using System.Diagnostics;
using Universal.Torrent.Client.Peers;
using Universal.Torrent.Common;
using Universal.Torrent.Dht.EventArgs;
using Universal.Torrent.Dht.Messages.Queries;
using Universal.Torrent.Dht.Messages.Responses;
using Universal.Torrent.Dht.Nodes;
using Universal.Torrent.Dht.RoutingTable;

namespace Universal.Torrent.Dht.Tasks
{
    internal class GetPeersTask : Task
    {
        private readonly SortedList<NodeId, NodeId> _closestNodes;
        private readonly DhtEngine _engine;
        private readonly NodeId _infoHash;
        private int _activeQueries;

        public GetPeersTask(DhtEngine engine, InfoHash infohash)
            : this(engine, new NodeId(infohash))
        {
        }

        public GetPeersTask(DhtEngine engine, NodeId infohash)
        {
            _engine = engine;
            _infoHash = infohash;
            _closestNodes = new SortedList<NodeId, NodeId>(Bucket.MaxCapacity);
            ClosestActiveNodes = new SortedList<NodeId, Node>(Bucket.MaxCapacity*2);
        }

        internal SortedList<NodeId, Node> ClosestActiveNodes { get; }

        public override void Execute()
        {
            if (Active)
                return;

            Active = true;
            DhtEngine.MainLoop.Queue(delegate
            {
                IEnumerable<Node> newNodes = _engine.RoutingTable.GetClosest(_infoHash);
                foreach (var n in Node.CloserNodes(_infoHash, _closestNodes, newNodes, Bucket.MaxCapacity))
                    SendGetPeers(n);
            });
        }

        private void SendGetPeers(Node n)
        {
            var distance = n.Id.Xor(_infoHash);
            if (ClosestActiveNodes.ContainsKey(distance))
                return;
            ClosestActiveNodes.Add(distance, n);

            _activeQueries++;
            var m = new GetPeers(_engine.LocalId, _infoHash);
            var task = new SendQueryTask(_engine, m, n);
            task.Completed += GetPeersCompleted;
            task.Execute();
        }

        private void GetPeersCompleted(object o, TaskCompleteEventArgs e)
        {
            try
            {
                _activeQueries--;
                e.Task.Completed -= GetPeersCompleted;

                var args = (SendQueryEventArgs) e;

                // We want to keep a list of the top (K) closest nodes which have responded
                var target = ((SendQueryTask) args.Task).Target;
                
                //var index = ClosestActiveNodes.Values.IndexOf(target);

                var key = target.Id.Xor(_infoHash);
                int index = 0;
                foreach (var obj3 in this.ClosestActiveNodes.Keys)
                  {
                    if (!(obj3 == key))
                        ++index;
                    else
                        break;
                }

                if (index >= Bucket.MaxCapacity || args.TimedOut)
                    ClosestActiveNodes.RemoveAt(index);

                if (args.TimedOut)
                    return;

                var response = (GetPeersResponse) args.Response;

                // Ensure that the local Node object has the token. There may/may not be
                // an additional copy in the routing table depending on whether or not
                // it was able to fit into the table.
                target.Token = response.Token;
                if (response.Values != null)
                {
                    // We have actual peers!
                    Debug.WriteLine("Found peers");
                    _engine.RaisePeersFound(_infoHash, Peer.Decode(response.Values));
                }
                else if (response.Nodes != null)
                {
                    if (!Active)
                        return;
                    // We got a list of nodes which are closer
                    var newNodes = Node.FromCompactNode(response.Nodes);
                    foreach (var n in Node.CloserNodes(_infoHash, _closestNodes, newNodes, Bucket.MaxCapacity))
                        SendGetPeers(n);
                }
            }
            finally
            {
                if (_activeQueries == 0)
                    RaiseComplete(new TaskCompleteEventArgs(this));
            }
        }

        protected override void RaiseComplete(TaskCompleteEventArgs e)
        {
            if (!Active)
                return;

            Active = false;
            base.RaiseComplete(e);
        }
    }
}

#endif