#if !DISABLE_DHT
//
// TokenManager.cs
//
// Authors:
//   Olivier Dufour <olivier.duff@gmail.com>
//
// Copyright (C) 2008 Olivier Dufour
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
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Universal.Torrent.Bencoding;

namespace Universal.Torrent.Dht.Nodes
{
    internal class TokenManager
    {
        private readonly byte[] _previousSecret;
        private byte[] _secret;
        private DateTime _lastSecretGeneration;
        private CryptographicHash _hash;

        public TokenManager()
        {
            _lastSecretGeneration = DateTime.MinValue; //in order to force the update
            _hash = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Sha1).CreateHash();
            _secret = CryptographicBuffer.GenerateRandom(10).ToArray();
            _previousSecret = CryptographicBuffer.GenerateRandom(10).ToArray();
        }

        internal TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(5);

        public BEncodedString GenerateToken(Node node)
        {
            return GetToken(node, _secret);
        }

        public bool VerifyToken(Node node, BEncodedString token)
        {
            return (token.Equals(GetToken(node, _secret)) || token.Equals(GetToken(node, _previousSecret)));
        }

        private BEncodedString GetToken(Node node, byte[] s)
        {
            //refresh secret needed
            if (_lastSecretGeneration.Add(Timeout) < DateTime.UtcNow)
            {
                _lastSecretGeneration = DateTime.UtcNow;
                _secret.CopyTo(_previousSecret, 0);
                _secret = CryptographicBuffer.GenerateRandom((uint) _secret.Length).ToArray();
            }

            var n = node.CompactPort().TextBytes;
            _hash.Append(n.AsBuffer());
            _hash.Append(s.AsBuffer());
            
            return _hash.GetValueAndReset().ToArray();
        }
    }
}

#endif