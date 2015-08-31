using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Universal.Torrent.Common
{
    public class MagnetLink
    {
        public MagnetLink(string url)
        {
            Check.Url(url);
            AnnounceUrls = new RawTrackerTier();
            Webseeds = new List<string>();

            ParseMagnetLink(url);
        }

        public RawTrackerTier AnnounceUrls { get; }

        public InfoHash InfoHash { get; private set; }

        public string Name { get; private set; }

        public List<string> Webseeds { get; }

        private void ParseMagnetLink(string url)
        {
            var splitStr = url.Split('?');
            if (splitStr.Length == 0 || splitStr[0] != "magnet:")
                throw new FormatException("The magnet link must start with 'magnet:?'.");

            if (splitStr.Length == 1)
                return; //no parametter

            var parameters = splitStr[1].Split('&', ';');

            foreach (var keyval in parameters.Select(t => t.Split('=')))
            {
                if (keyval.Length != 2)
                    throw new FormatException("A field-value pair of the magnet link contain more than one equal'.");
                switch (keyval[0].Substring(0, 2))
                {
                    case "xt": //exact topic
                        if (InfoHash != null)
                            throw new FormatException("More than one infohash in magnet link is not allowed.");

                        var val = keyval[1].Substring(9);
                        if (keyval[1].Substring(0, 9) == "urn:sha1:" || keyval[1].Substring(0, 9) == "urn:btih:")
                        {
                            switch (val.Length)
                            {
                                case 32:
                                    InfoHash = InfoHash.FromBase32(val);
                                    break;
                                case 40:
                                    InfoHash = InfoHash.FromHex(val);
                                    break;
                                default:
                                    throw new FormatException("Infohash must be base32 or hex encoded.");
                            }
                        }
                        break;
                    case "tr": //address tracker
                        var bytes = UriHelper.UrlDecode(keyval[1]);
                        AnnounceUrls.Add(Encoding.UTF8.GetString(bytes));
                        break;
                    case "as": //Acceptable Source
                        Webseeds.Add(keyval[1]);
                        break;
                    case "dn": //display name
                        var name = UriHelper.UrlDecode(keyval[1]);
                        Name = Encoding.UTF8.GetString(name);
                        break;
                    case "xl": //exact length
                    case "xs": // eXact Source - P2P link.
                    case "kt": //keyword topic
                    case "mt": //manifest topic
                        //not supported for moment
                        break;
                    default:
                        //not supported
                        break;
                }
            }
        }
    }
}