#if !DISABLE_DHT
using Universal.Torrent.Bencoding;

namespace Universal.Torrent.Dht
{
    internal static class TransactionId
    {
        private static readonly byte[] Current = new byte[2];

        public static BEncodedString NextId()
        {
            lock (Current)
            {
                var result = new BEncodedString((byte[]) Current.Clone());
                if (Current[0]++ == 255)
                    Current[1]++;
                return result;
            }
        }
    }
}

#endif