using System;
using System.Linq;
using System.Net;
using Windows.Networking;
using Windows.Networking.Sockets;

namespace Universal.Torrent.Common
{
    public static class Dns
    {
        public static IPAddress ResolveAddress(string remoteHostName)
        {
            var canonicalName = ResolveDNS(remoteHostName);
            return IPAddress.Parse(canonicalName);
        }

        public static string ResolveDNS(string remoteHostName)
        {
            if (string.IsNullOrEmpty(remoteHostName))
                return string.Empty;

            var ipAddress = string.Empty;

            try
            {
                var data = DatagramSocket.GetEndpointPairsAsync(new HostName(remoteHostName), "0").AsTask().Result;

                if (data != null && data.Count > 0)
                {
                    foreach (
                        var item in
                            data.Where(
                                item => item?.RemoteHostName != null && item.RemoteHostName.Type == HostNameType.Ipv4))
                    {
                        return item.RemoteHostName.CanonicalName;
                    }
                }
            }
            catch (Exception ex)
            {
                ipAddress = ex.Message;
            }

            return ipAddress;
        }
    }
}