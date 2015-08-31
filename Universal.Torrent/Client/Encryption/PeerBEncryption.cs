//
// PeerBEncryption.cs
//
// Authors:
//   Yiduo Wang planetbeing@gmail.com
//   Alan McGovern alan.mcgovern@gmail.com
//
// Copyright (C) 2007 Yiduo Wang
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
using System.Text;
using Universal.Torrent.Client.Messages;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.Encryption
{
    /// <summary>
    ///     Class to handle message stream encryption for receiving connections
    /// </summary>
    internal class PeerBEncryption : EncryptedSocket
    {
        private readonly AsyncCallback _gotPadCCallback;

        private readonly AsyncCallback _gotVerificationCallback;
        private readonly InfoHash[] _possibleSkeYs;
        private byte[] _b;
        private byte[] _verifyBytes;

        public PeerBEncryption(InfoHash[] possibleSkeYs, EncryptionTypes allowedEncryption)
            : base(allowedEncryption)
        {
            _possibleSkeYs = possibleSkeYs;

            _gotVerificationCallback = GotVerification;
            _gotPadCCallback = GotPadC;
        }

        protected override void DoneReceiveY()
        {
            try
            {
                base.DoneReceiveY(); // 1 A->B: Diffie Hellman Ya, PadA

                var req1 = Hash(Encoding.ASCII.GetBytes("req1"), S);
                Synchronize(req1, 628); // 3 A->B: HASH('req1', S)
            }
            catch (Exception ex)
            {
                AsyncResult.Complete(ex);
            }
        }


        protected override void DoneSynchronize()
        {
            try
            {
                base.DoneSynchronize();

                _verifyBytes = new byte[20 + VerificationConstant.Length + 4 + 2];
                // ... HASH('req2', SKEY) xor HASH('req3', S), ENCRYPT(VC, crypto_provide, len(PadC), PadC, len(IA))

                ReceiveMessage(_verifyBytes, _verifyBytes.Length, _gotVerificationCallback);
            }
            catch (Exception ex)
            {
                AsyncResult.Complete(ex);
            }
        }

        private void GotVerification(IAsyncResult result)
        {
            try
            {
                var torrentHash = new byte[20];

                var myVc = new byte[8];
                var myCp = new byte[4];
                var lenPadC = new byte[2];

                Array.Copy(_verifyBytes, 0, torrentHash, 0, torrentHash.Length);
                // HASH('req2', SKEY) xor HASH('req3', S)

                if (!MatchSkey(torrentHash))
                {
                    AsyncResult.Complete(new EncryptionException("No valid SKey found"));
                    return;
                }

                CreateCryptors("keyB", "keyA");

                DoDecrypt(_verifyBytes, 20, 14); // ENCRYPT(VC, ...

                Array.Copy(_verifyBytes, 20, myVc, 0, myVc.Length);
                if (!Toolbox.ByteMatch(myVc, VerificationConstant))
                {
                    AsyncResult.Complete(new EncryptionException("Verification constant was invalid"));
                    return;
                }

                Array.Copy(_verifyBytes, 28, myCp, 0, myCp.Length); // ...crypto_provide ...

                // We need to select the crypto *after* we send our response, otherwise the wrong
                // encryption will be used on the response
                _b = myCp;
                Array.Copy(_verifyBytes, 32, lenPadC, 0, lenPadC.Length); // ... len(padC) ...
                PadC = new byte[DeLen(lenPadC) + 2];
                ReceiveMessage(PadC, PadC.Length, _gotPadCCallback); // padC            
            }
            catch (Exception ex)
            {
                AsyncResult.Complete(ex);
            }
        }

        private void GotPadC(IAsyncResult result)
        {
            try
            {
                DoDecrypt(PadC, 0, PadC.Length);

                var lenInitialPayload = new byte[2]; // ... len(IA))
                Array.Copy(PadC, PadC.Length - 2, lenInitialPayload, 0, 2);

                RemoteInitialPayload = new byte[DeLen(lenInitialPayload)]; // ... ENCRYPT(IA)
                ReceiveMessage(RemoteInitialPayload, RemoteInitialPayload.Length, GotInitialPayload);
            }
            catch (Exception ex)
            {
                AsyncResult.Complete(ex);
            }
        }

        private void GotInitialPayload(IAsyncResult result)
        {
            try
            {
                DoDecrypt(RemoteInitialPayload, 0, RemoteInitialPayload.Length); // ... ENCRYPT(IA)
                StepFour();
            }
            catch (Exception ex)
            {
                AsyncResult.Complete(ex);
            }
        }

        private void StepFour()
        {
            try
            {
                var padD = GeneratePad();
                SelectCrypto(_b, false);
                // 4 B->A: ENCRYPT(VC, crypto_select, len(padD), padD)
                var buffer = new byte[VerificationConstant.Length + CryptoSelect.Length + 2 + padD.Length];

                var offset = 0;
                offset += Message.Write(buffer, offset, VerificationConstant);
                offset += Message.Write(buffer, offset, CryptoSelect);
                offset += Message.Write(buffer, offset, Len(padD));
                Message.Write(buffer, offset, padD);

                DoEncrypt(buffer, 0, buffer.Length);
                SendMessage(buffer);

                SelectCrypto(_b, true);

                Ready();
            }

            catch (Exception ex)
            {
                AsyncResult.Complete(ex);
            }
        }


        /// <summary>
        ///     Matches a torrent based on whether the HASH('req2', SKEY) xor HASH('req3', S) matches, where SKEY is the InfoHash
        ///     of the torrent
        ///     and sets the SKEY to the InfoHash of the matched torrent.
        /// </summary>
        /// <returns>true if a match has been found</returns>
        private bool MatchSkey(IReadOnlyList<byte> torrentHash)
        {
            try
            {
                foreach (var t in _possibleSkeYs)
                {
                    var req2 = Hash(Encoding.ASCII.GetBytes("req2"), t.Hash);
                    var req3 = Hash(Encoding.ASCII.GetBytes("req3"), S);

                    var match = true;
                    for (var j = 0; j < req2.Length && match; j++)
                        match = torrentHash[j] == (req2[j] ^ req3[j]);

                    if (match)
                    {
                        Skey = t;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                AsyncResult.Complete(ex);
            }
            return false;
        }
    }
}