//
// HTTPTracker.cs
//
// Authors:
//   Eric Butler eric@extremeboredom.net
//   Alan McGovern alan.mcgovern@gmail.com
//
// Copyright (C) 2007 Eric Butler
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using Universal.Torrent.Bencoding;
using Universal.Torrent.Client.Args;
using Universal.Torrent.Client.Peers;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.Tracker
{
    // ReSharper disable once InconsistentNaming
    public class HTTPTracker : Tracker
    {
        private static readonly Random Random = new Random();
        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

        private string _trackerId;

        public HTTPTracker(Uri announceUrl)
            : base(announceUrl)
        {
            CanAnnounce = true;
            var index = announceUrl.OriginalString.LastIndexOf('/');
            var part = (index + 9 <= announceUrl.OriginalString.Length)
                ? announceUrl.OriginalString.Substring(index + 1, 8)
                : "";
            if (part.Equals("announce", StringComparison.OrdinalIgnoreCase))
            {
                CanScrape = true;
                var r = new Regex("announce");
                ScrapeUri = new Uri(r.Replace(announceUrl.OriginalString, "scrape", 1, index));
            }

            var passwordKey = new byte[8];
            lock (Random)
                Random.NextBytes(passwordKey);
            Key = UriHelper.UrlEncode(passwordKey);
        }

        public string Key { get; }

        public Uri ScrapeUri { get; }

        public override async void Announce(AnnounceParameters parameters, object state)
        {
            var peers = new List<Peer>();
            var requestUri = CreateAnnounceString(parameters);

            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.Timeout = RequestTimeout;
                    var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
                    request.Headers.UserAgent.ParseAdd(VersionInfo.ClientVersion);

                    RaiseBeforeAnnounce();
                    var httpResponseMessage = await httpClient.SendAsync(request);
                    if (httpResponseMessage.StatusCode == HttpStatusCode.OK)
                    {
                        FailureMessage = "";
                        WarningMessage = "";
                        try
                        {
                            var stream = await httpResponseMessage.Content.ReadAsStreamAsync();
                            if (stream.Length > 0L && parameters.ClientEvent != TorrentEvent.Stopped)
                                HandleAnnounce(DecodeResponse(stream), peers);
                            Status = TrackerState.Ok;
                        }
                        catch
                        {
                            Status = TrackerState.InvalidResponse;
                            FailureMessage = "Failed to open tracker response.";
                        }
                    }
                    else
                    {
                        FailureMessage = string.Format("The tracker could not be contacted. {0}",
                            httpResponseMessage.StatusCode);
                        Status = TrackerState.Offline;
                    }
                }
            }
            catch (WebException)
            {
                Status = TrackerState.Offline;
                FailureMessage = "The tracker could not be contacted";
            }
            catch
            {
                Status = TrackerState.InvalidResponse;
                FailureMessage = "The tracker returned an invalid or incomplete response";
            }
            finally
            {
                RaiseAnnounceComplete(new AnnounceResponseEventArgs(this, state, string.IsNullOrEmpty(FailureMessage), peers));
            }
        }

        private Uri CreateAnnounceString(AnnounceParameters parameters)
        {
            var b = new UriQueryBuilder(Uri);
            b.Add("info_hash", parameters.InfoHash.UrlEncode())
                .Add("peer_id", parameters.PeerId)
                .Add("port", parameters.Port)
                .Add("uploaded", parameters.BytesUploaded)
                .Add("downloaded", parameters.BytesDownloaded)
                .Add("left", parameters.BytesLeft)
                .Add("compact", 1)
                .Add("numwant", 100);

            if (parameters.SupportsEncryption)
                b.Add("supportcrypto", 1);
            if (parameters.RequireEncryption)
                b.Add("requirecrypto", 1);
            if (!b.Contains("key"))
                b.Add("key", Key);
            if (!string.IsNullOrEmpty(parameters.Ipaddress))
                b.Add("ip", parameters.Ipaddress);

            // If we have not successfully sent the started event to this tier, override the passed in started event
            // Otherwise append the event if it is not "none"
            //if (!parameters.Id.Tracker.Tier.SentStartedEvent)
            //{
            //    sb.Append("&event=started");
            //    parameters.Id.Tracker.Tier.SendingStartedEvent = true;
            //}
            if (parameters.ClientEvent != TorrentEvent.None)
                b.Add("event", parameters.ClientEvent.ToString().ToLower());

            if (!string.IsNullOrEmpty(_trackerId))
                b.Add("trackerid", _trackerId);

            return b.ToUri();
        }

        private BEncodedDictionary DecodeResponse(Stream stream)
        {
            return (BEncodedDictionary) BEncodedValue.Decode(stream);
        }

        public override bool Equals(object obj)
        {
            var tracker = obj as HTTPTracker;

            // If the announce URL matches, then CanScrape and the scrape URL must match too
            return tracker != null && (Uri.Equals(tracker.Uri));
        }

        public override int GetHashCode()
        {
            return Uri.GetHashCode();
        }

        private void HandleAnnounce(BEncodedDictionary dict, List<Peer> peers)
        {
            foreach (var keypair in dict)
            {
                switch (keypair.Key.Text)
                {
                    case ("complete"):
                        Complete = Convert.ToInt32(keypair.Value.ToString());
                        break;

                    case ("incomplete"):
                        Incomplete = Convert.ToInt32(keypair.Value.ToString());
                        break;

                    case ("downloaded"):
                        Downloaded = Convert.ToInt32(keypair.Value.ToString());
                        break;

                    case ("tracker id"):
                        _trackerId = keypair.Value.ToString();
                        break;

                    case ("min interval"):
                        MinUpdateInterval = TimeSpan.FromSeconds(int.Parse(keypair.Value.ToString()));
                        break;

                    case ("interval"):
                        UpdateInterval = TimeSpan.FromSeconds(int.Parse(keypair.Value.ToString()));
                        break;

                    case ("peers"):
                        if (keypair.Value is BEncodedList) // Non-compact response
                            peers.AddRange(Peer.Decode((BEncodedList) keypair.Value));
                        else if (keypair.Value is BEncodedString) // Compact response
                            peers.AddRange(Peer.Decode((BEncodedString) keypair.Value));
                        break;

                    case ("failure reason"):
                        FailureMessage = keypair.Value.ToString();
                        break;

                    case ("warning message"):
                        WarningMessage = keypair.Value.ToString();
                        break;

                    default:
                        Debug.WriteLine("HttpTracker - Unknown announce tag received: Key {0}  Value: {1}",
                            keypair.Key, keypair.Value);
                        break;
                }
            }
        }

        public override async void Scrape(ScrapeParameters parameters, object state)
        {
            var message = "";
            try
            {
                var url = ScrapeUri.OriginalString;
                // If you want to scrape the tracker for *all* torrents, don't append the info_hash.
                if (url.IndexOf('?') == -1)
                    url += "?info_hash=" + parameters.InfoHash.UrlEncode();
                else
                    url += "&info_hash=" + parameters.InfoHash.UrlEncode();

                using (var httpClient = new HttpClient())
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, url);
                    request.Headers.UserAgent.ParseAdd(VersionInfo.ClientVersion);

                    var httpResponseMessage = await httpClient.SendAsync(request);
                    if (httpResponseMessage.StatusCode == HttpStatusCode.OK)
                    {
                        var stream = await httpResponseMessage.Content.ReadAsStreamAsync();
                        var dict = DecodeResponse(stream);
                        // FIXME: Log the failure?
                        if (!dict.ContainsKey("files"))
                        {
                            message = "Response contained no data";
                        }
                        else
                        {
                            var files = (BEncodedDictionary) dict["files"];
                            foreach (var kp in files.Select(keypair => (BEncodedDictionary) keypair.Value).SelectMany(d => d))
                            {
                                switch (kp.Key.ToString())
                                {
                                    case ("complete"):
                                        Complete = (int) ((BEncodedNumber) kp.Value).Number;
                                        break;

                                    case ("downloaded"):
                                        Downloaded = (int) ((BEncodedNumber) kp.Value).Number;
                                        break;

                                    case ("incomplete"):
                                        Incomplete = (int) ((BEncodedNumber) kp.Value).Number;
                                        break;

                                    default:
                                        Debug.WriteLine(null,
                                            "HttpTracker - Unknown scrape tag received: Key {0}  Value {1}", kp.Key,
                                            kp.Value);
                                        break;
                                }
                            }
                        }
                    }
                    else
                        message = string.Format("The tracker could not be contacted {0}", httpResponseMessage.StatusCode);
                }
            }
            catch (WebException)
            {
                message = "The tracker could not be contacted";
            }
            catch (IOException ex)
            {
                message = ex.Message;
            }
            catch
            {
                message = "The tracker returned an invalid or incomplete response";
            }
            finally
            {
                RaiseScrapeComplete(new ScrapeResponseEventArgs(this, state, string.IsNullOrEmpty(message)));
            }
        }

        public override string ToString()
        {
            return Uri.ToString();
        }
    }
}