namespace Universal.Torrent.Client.Args
{
    public class ScrapeResponseEventArgs : TrackerResponseEventArgs
    {
        public ScrapeResponseEventArgs(Tracker.Tracker tracker, object state, bool successful)
            : base(tracker, state, successful)
        {
        }
    }
}