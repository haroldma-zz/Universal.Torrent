#if !DISABLE_DHT
using System.Net;
using Universal.Torrent.Dht.Messages.Queries;
using Universal.Torrent.Dht.Messages.Responses;

namespace Universal.Torrent.Dht.EventArgs
{
    internal class SendQueryEventArgs : TaskCompleteEventArgs
    {
        public SendQueryEventArgs(IPEndPoint endpoint, QueryMessage query, ResponseMessage response)
            : base(null)
        {
            EndPoint = endpoint;
            Query = query;
            Response = response;
        }

        public IPEndPoint EndPoint { get; }

        public QueryMessage Query { get; }

        public ResponseMessage Response { get; }

        public bool TimedOut => Response == null;
    }
}

#endif