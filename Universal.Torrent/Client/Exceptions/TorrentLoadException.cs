using System;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.Exceptions
{
    public class TorrentLoadException : TorrentException
    {

        public TorrentLoadException()
            : base()
        {
        }


        public TorrentLoadException(string message)
            : base(message)
        {
        }


        public TorrentLoadException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}