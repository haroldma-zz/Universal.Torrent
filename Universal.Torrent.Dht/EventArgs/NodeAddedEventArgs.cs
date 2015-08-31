#if !DISABLE_DHT
using Universal.Torrent.Dht.Nodes;

namespace Universal.Torrent.Dht.EventArgs
{
    internal class NodeAddedEventArgs : System.EventArgs
    {
        public NodeAddedEventArgs(Node node)
        {
            Node = node;
        }

        public Node Node { get; }
    }
}

#endif