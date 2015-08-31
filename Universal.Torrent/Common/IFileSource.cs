using System.Collections.Generic;

namespace Universal.Torrent.Common
{
    public interface ITorrentFileSource
    {
        IEnumerable<FileMapping> Files { get; }
        string TorrentName { get; }
    }
}