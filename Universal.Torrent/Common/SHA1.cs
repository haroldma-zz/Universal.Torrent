using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Security.Cryptography.Core;

namespace Universal.Torrent.Common
{
    public static class SHA1Helper
    {
        public static byte[] ComputeHash(byte[] bytes)
        {
            var hash = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Sha1).CreateHash();
            hash.Append(bytes.AsBuffer());
            return hash.GetValueAndReset().ToArray();
        }
    }
}