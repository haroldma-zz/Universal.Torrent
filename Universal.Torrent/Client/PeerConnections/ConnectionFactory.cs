using System;
using System.Collections.Generic;

namespace Universal.Torrent.Client.PeerConnections
{
    public static class ConnectionFactory
    {
        private static readonly object Locker = new object();
        private static readonly Dictionary<string, Type> TrackerTypes = new Dictionary<string, Type>();

        static ConnectionFactory()
        {
            RegisterTypeForProtocol("tcp", typeof (TCPConnection));
            RegisterTypeForProtocol("ipv6", typeof (TCPConnection));
            RegisterTypeForProtocol("http", typeof (HttpConnection));
        }

        public static void RegisterTypeForProtocol(string protocol, Type connectionType)
        {
            if (string.IsNullOrEmpty(protocol))
                throw new ArgumentException("cannot be null or empty", nameof(protocol));
            if (connectionType == null)
                throw new ArgumentNullException(nameof(connectionType));

            lock (Locker)
                TrackerTypes[protocol] = connectionType;
        }

        public static IConnection Create(Uri connectionUri)
        {
            if (connectionUri == null)
                throw new ArgumentNullException(nameof(connectionUri));

            Type type;
            lock (Locker)
                if (!TrackerTypes.TryGetValue(connectionUri.Scheme, out type))
                    return null;

            try
            {
                return (IConnection) Activator.CreateInstance(type, connectionUri);
            }
            catch
            {
                return null;
            }
        }
    }
}