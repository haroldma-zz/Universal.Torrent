//
// PeerAEncryption.cs
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
using System.Text;
using Universal.Torrent.Client.Managers;
using Universal.Torrent.Client.Messages;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.Encryption
{
    /// <summary>
    ///     Class to handle message stream encryption for initiating connections
    /// </summary>
    internal class PeerAEncryption : EncryptedSocket
    {
        private readonly AsyncCallback _gotPadDCallback;

        private readonly AsyncCallback _gotVerificationCallback;
        private byte[] _b;
        private byte[] _verifyBytes;

        public PeerAEncryption(InfoHash infoHash, EncryptionTypes allowedEncryption)
            : base(allowedEncryption)
        {
            _gotVerificationCallback = GotVerification;
            _gotPadDCallback = GotPadD;

            Skey = infoHash;
        }

        protected override void DoneReceiveY()
        {
            try
            {
                base.DoneReceiveY(); // 2 B->A: Diffie Hellman Yb, PadB

                StepThree();
            }
            catch (Exception ex)
            {
                AsyncResult.Complete(ex);
            }
        }

        private void StepThree()
        {
            try
            {
                CreateCryptors("keyA", "keyB");

                // 3 A->B: HASH('req1', S)
                var req1 = Hash(Encoding.ASCII.GetBytes("req1"), S);

                // ... HASH('req2', SKEY)
                var req2 = Hash(Encoding.ASCII.GetBytes("req2"), Skey.Hash);

                // ... HASH('req3', S)
                var req3 = Hash(Encoding.ASCII.GetBytes("req3"), S);

                // HASH('req2', SKEY) xor HASH('req3', S)
                for (var i = 0; i < req2.Length; i++)
                    req2[i] ^= req3[i];

                var padC = GeneratePad();

                // 3 A->B: HASH('req1', S), HASH('req2', SKEY) xor HASH('req3', S), ENCRYPT(VC, crypto_provide, len(PadC), ...
                var buffer = new byte[req1.Length + req2.Length + VerificationConstant.Length + CryptoProvide.Length
                                      + 2 + padC.Length + 2 + InitialPayload.Length];

                var offset = 0;
                offset += Message.Write(buffer, offset, req1);
                offset += Message.Write(buffer, offset, req2);
                offset += Message.Write(buffer, offset, DoEncrypt(VerificationConstant));
                offset += Message.Write(buffer, offset, DoEncrypt(CryptoProvide));
                offset += Message.Write(buffer, offset, DoEncrypt(Len(padC)));
                offset += Message.Write(buffer, offset, DoEncrypt(padC));

                // ... PadC, len(IA)), ENCRYPT(IA)
                offset += Message.Write(buffer, offset, DoEncrypt(Len(InitialPayload)));
                Message.Write(buffer, offset, DoEncrypt(InitialPayload));

                // Send the entire message in one go
                SendMessage(buffer);
                InitialPayload = BufferManager.EmptyBuffer;

                Synchronize(DoDecrypt(VerificationConstant), 616); // 4 B->A: ENCRYPT(VC)
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
                base.DoneSynchronize(); // 4 B->A: ENCRYPT(VC, ...

                _verifyBytes = new byte[4 + 2];
                ReceiveMessage(_verifyBytes, _verifyBytes.Length, _gotVerificationCallback);
                // crypto_select, len(padD) ...
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
                var myCs = new byte[4];
                var lenPadD = new byte[2];

                DoDecrypt(_verifyBytes, 0, _verifyBytes.Length);

                Array.Copy(_verifyBytes, 0, myCs, 0, myCs.Length); // crypto_select

                //SelectCrypto(myCS);
                _b = myCs;
                Array.Copy(_verifyBytes, myCs.Length, lenPadD, 0, lenPadD.Length); // len(padD)

                PadD = new byte[DeLen(lenPadD)];

                ReceiveMessage(PadD, PadD.Length, _gotPadDCallback);
            }
            catch (Exception ex)
            {
                AsyncResult.Complete(ex);
            }
        }

        private void GotPadD(IAsyncResult result)
        {
            try
            {
                DoDecrypt(PadD, 0, PadD.Length); // padD
                SelectCrypto(_b, true);
                Ready();
            }
            catch (Exception ex)
            {
                AsyncResult.Complete(ex);
            }
        }
    }
}