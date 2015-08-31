using System;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.Exceptions
{
    public class MessageException : TorrentException
    {
        public MessageException()
            : base()
        {
        }


        public MessageException(string message)
            : base(message)
        {
        }


        public MessageException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
