using Universal.Torrent.Client.Messages.StandardMessages;

namespace Universal.Torrent.Client.PeerConnections
{
    public partial class HttpConnection
    {
        private class HttpRequestData
        {
            public RequestMessage Request;
            public bool SentHeader;
            public bool SentLength;
            public int TotalReceived;
            public readonly int TotalToReceive;

            public HttpRequestData(RequestMessage request)
            {
                Request = request;
                var m = new PieceMessage(request.PieceIndex, request.StartOffset, request.RequestLength);
                TotalToReceive = m.ByteLength;
            }

            public bool Complete => TotalToReceive == TotalReceived;
        }
    }
}