using Universal.Torrent.Common;

namespace Universal.Torrent.Client.Tracker
{
    public class ScrapeParameters
    {
        public ScrapeParameters(InfoHash infoHash)
        {
            InfoHash = infoHash;
        }


        public InfoHash InfoHash { get; }
    }
}