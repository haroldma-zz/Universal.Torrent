#if !DISABLE_DHT
using System.Linq;
using Universal.Torrent.Common;
using Universal.Torrent.Dht.EventArgs;
using Universal.Torrent.Dht.Messages.Queries;
using Universal.Torrent.Dht.Nodes;

namespace Universal.Torrent.Dht.Tasks
{
    internal class AnnounceTask : Task
    {
        private readonly DhtEngine _engine;
        private readonly NodeId _infoHash;
        private readonly int _port;
        private int _activeAnnounces;

        public AnnounceTask(DhtEngine engine, InfoHash infoHash, int port)
            : this(engine, new NodeId(infoHash), port)
        {
        }

        public AnnounceTask(DhtEngine engine, NodeId infoHash, int port)
        {
            _engine = engine;
            _infoHash = infoHash;
            _port = port;
        }

        public override void Execute()
        {
            var task = new GetPeersTask(_engine, _infoHash);
            task.Completed += GotPeers;
            task.Execute();
        }

        private void GotPeers(object o, TaskCompleteEventArgs e)
        {
            e.Task.Completed -= GotPeers;
            var getpeers = (GetPeersTask) e.Task;
            foreach (var task in from n in getpeers.ClosestActiveNodes.Values
                where n.Token != null
                let query = new AnnouncePeer(_engine.LocalId, _infoHash, _port, n.Token)
                select new SendQueryTask(_engine, query, n))
            {
                task.Completed += SentAnnounce;
                task.Execute();
                _activeAnnounces++;
            }

            if (_activeAnnounces == 0)
                RaiseComplete(new TaskCompleteEventArgs(this));
        }

        private void SentAnnounce(object o, TaskCompleteEventArgs e)
        {
            e.Task.Completed -= SentAnnounce;
            _activeAnnounces--;

            if (_activeAnnounces == 0)
                RaiseComplete(new TaskCompleteEventArgs(this));
        }
    }
}

#endif