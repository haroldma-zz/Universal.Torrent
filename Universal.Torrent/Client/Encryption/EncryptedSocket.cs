//
// EncryptedSocket.cs
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
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Security.Cryptography;
using Universal.Torrent.Client.Encryption.IEncryption;
using Universal.Torrent.Client.Exceptions;
using Universal.Torrent.Client.Managers;
using Universal.Torrent.Client.Messages;
using Universal.Torrent.Client.PeerConnections;
using Universal.Torrent.Common;

namespace Universal.Torrent.Client.Encryption
{
    public class EncryptionException : TorrentException
    {
        public EncryptionException()
        {
        }

        public EncryptionException(string message)
            : base(message)
        {
        }

        public EncryptionException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>
    ///     The class that handles.Message Stream Encryption for a connection
    /// </summary>
    internal class EncryptedSocket : IEncryptor.IEncryptor
    {
        protected AsyncResult AsyncResult;

        public EncryptedSocket(EncryptionTypes allowedEncryption)
        {
            GenerateX();
            GenerateY();

            InitialPayload = BufferManager.EmptyBuffer;
            RemoteInitialPayload = BufferManager.EmptyBuffer;

            _doneSendCallback = DoneSend;
            _doneReceiveCallback = DoneReceive;
            _doneReceiveYCallback = delegate { DoneReceiveY(); };
            _doneSynchronizeCallback = delegate { DoneSynchronize(); };
            _fillSynchronizeBytesCallback = FillSynchronizeBytes;

            _bytesReceived = 0;

            SetMinCryptoAllowed(allowedEncryption);
        }

        public IEncryption.IEncryption Encryptor { get; private set; }

        public IEncryption.IEncryption Decryptor { get; private set; }

        public byte[] InitialData => RemoteInitialPayload;

        public void AddPayload(byte[] buffer)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            AddPayload(buffer, 0, buffer.Length);
        }

        public void AddPayload(byte[] buffer, int offset, int count)
        {
            var newBuffer = new byte[InitialPayload.Length + count];

            Message.Write(newBuffer, 0, InitialPayload);
            Message.Write(newBuffer, InitialPayload.Length, buffer, offset, count);

            InitialPayload = buffer;
        }

        #region Private members

        // Cryptors for the handshaking
        private RC4 _encryptor;
        private RC4 _decryptor;

        // Cryptors for the data transmission

        private EncryptionTypes _allowedEncryption;

        private byte[] _x; // A 160 bit random integer
        private byte[] _y; // 2^X mod P
        private byte[] _otherY;

        private IConnection _socket;

        // Data to be passed to initial ReceiveMessage requests
        private byte[] _initialBuffer;
        private int _initialBufferOffset;
        private int _initialBufferCount;

        // State information to be checked against abort conditions
        private int _bytesReceived;

        // Callbacks
        private readonly AsyncIOCallback _doneSendCallback;
        private readonly AsyncIOCallback _doneReceiveCallback;
        private readonly AsyncCallback _doneReceiveYCallback;
        private readonly AsyncCallback _doneSynchronizeCallback;
        private readonly AsyncIOCallback _fillSynchronizeBytesCallback;

        // State information for synchronization
        private byte[] _synchronizeData;
        private byte[] _synchronizeWindow;
        private int _syncStopPoint;

        #endregion

        #region Protected members

        protected byte[] S;
        protected InfoHash Skey = null;

        protected byte[] PadC = null;
        protected byte[] PadD = null;

        protected byte[] VerificationConstant = new byte[8];

        protected byte[] CryptoProvide = {0x00, 0x00, 0x00, 0x03};

        protected byte[] InitialPayload;
        protected byte[] RemoteInitialPayload;

        protected byte[] CryptoSelect;

        #endregion

        #region Interface implementation

        /// <summary>
        ///     Begins the message stream encryption handshaking process
        /// </summary>
        /// <param name="socket">The socket to perform handshaking with</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns></returns>
        public virtual IAsyncResult BeginHandshake(IConnection socket, AsyncCallback callback, object state)
        {
            if (socket == null)
                throw new ArgumentNullException(nameof(socket));

            if (AsyncResult != null)
                throw new ArgumentException("BeginHandshake has already been called");

            AsyncResult = new AsyncResult(callback, state);

            try
            {
                _socket = socket;

                // Either "1 A->B: Diffie Hellman Ya, PadA" or "2 B->A: Diffie Hellman Yb, PadB"
                // These two steps will be done simultaneously to save time due to latency
                SendY();
                ReceiveY();
            }
            catch (Exception ex)
            {
                AsyncResult.Complete(ex);
            }
            return AsyncResult;
        }

        public void EndHandshake(IAsyncResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            if (result != AsyncResult)
                throw new ArgumentException("Wrong IAsyncResult supplied");

            if (!result.IsCompleted)
                result.AsyncWaitHandle.WaitOne();

            if (AsyncResult.SavedException != null)
                throw AsyncResult.SavedException;
        }

        /// <summary>
        ///     Begins the message stream encryption handshaking process, beginning with some data
        ///     already received from the socket.
        /// </summary>
        /// <param name="socket">The socket to perform handshaking with</param>
        /// <param name="initialBuffer">Buffer containing soome data already received from the socket</param>
        /// <param name="offset">Offset to begin reading in initialBuffer</param>
        /// <param name="count">Number of bytes to read from initialBuffer</param>
        /// <param name="callback">The callback.</param>
        /// <param name="state">The state.</param>
        /// <returns></returns>
        public virtual IAsyncResult BeginHandshake(IConnection socket, byte[] initialBuffer, int offset, int count,
            AsyncCallback callback, object state)
        {
            _initialBuffer = initialBuffer;
            _initialBufferOffset = offset;
            _initialBufferCount = count;
            return BeginHandshake(socket, callback, state);
        }


        /// <summary>
        ///     Encrypts some data (should only be called after onEncryptorReady)
        /// </summary>
        /// <param name="data">The data.</param>
        /// <param name="offset">Offset to begin encryption</param>
        /// <param name="length">The length.</param>
        public void Encrypt(byte[] data, int offset, int length)
        {
            Encryptor.Encrypt(data, offset, data, offset, length);
        }

        /// <summary>
        ///     Decrypts some data (should only be called after onEncryptorReady)
        /// </summary>
        /// <param name="data">The data.</param>
        /// <param name="offset">Offset to begin decryption</param>
        /// <param name="length">The length.</param>
        public void Decrypt(byte[] data, int offset, int length)
        {
            Decryptor.Decrypt(data, offset, data, offset, length);
        }

        private int RandomNumber(int max)
        {
            var b = CryptographicBuffer.GenerateRandom(4).ToArray();
            var val = BitConverter.ToUInt32(b, 0);
            return (int) (val%max);
        }

        #endregion

        #region Diffie-Hellman Key Exchange Functions

        /// <summary>
        ///     Send Y to the remote client, with a random padding that is 0 to 512 bytes long
        ///     (Either "1 A->B: Diffie Hellman Ya, PadA" or "2 B->A: Diffie Hellman Yb, PadB")
        /// </summary>
        protected void SendY()
        {
            var toSend = CryptographicBuffer.GenerateRandom(96 + (uint) RandomNumber(512)).ToArray();

            Buffer.BlockCopy(_y, 0, toSend, 0, 96);

            SendMessage(toSend);
        }

        /// <summary>
        ///     Receive the first 768 bits of the transmission from the remote client, which is Y in the protocol
        ///     (Either "1 A->B: Diffie Hellman Ya, PadA" or "2 B->A: Diffie Hellman Yb, PadB")
        /// </summary>
        protected void ReceiveY()
        {
            _otherY = new byte[96];
            ReceiveMessage(_otherY, 96, _doneReceiveYCallback);
        }

        protected virtual void DoneReceiveY()
        {
            S = ModuloCalculator.Calculate(_otherY, _x);
        }

        #endregion

        #region Synchronization functions

        /// <summary>
        ///     Read data from the socket until the byte string in syncData is read, or until syncStopPoint
        ///     is reached (in that case, there is an EncryptionError).
        ///     (Either "3 A->B: HASH('req1', S)" or "4 B->A: ENCRYPT(VC)")
        /// </summary>
        /// <param name="syncData">Buffer with the data to synchronize to</param>
        /// <param name="syncStopPoint">
        ///     Maximum number of bytes (measured from the total received from the socket since connection)
        ///     to read before giving up
        /// </param>
        protected void Synchronize(byte[] syncData, int syncStopPoint)
        {
            try
            {
                // The strategy here is to create a window the size of the data to synchronize and just refill that until its contents match syncData
                _synchronizeData = syncData;
                _synchronizeWindow = new byte[syncData.Length];
                _syncStopPoint = syncStopPoint;

                if (_bytesReceived > syncStopPoint)
                    AsyncResult.Complete(new EncryptionException("Couldn't synchronise 1"));
                else
                    NetworkIO.EnqueueReceive(_socket, _synchronizeWindow, 0, _synchronizeWindow.Length, null, null, null,
                        _fillSynchronizeBytesCallback, 0);
            }
            catch (Exception ex)
            {
                AsyncResult.Complete(ex);
            }
        }

        protected void FillSynchronizeBytes(bool succeeded, int count, object state)
        {
            try
            {
                if (!succeeded)
                    throw new MessageException("Could not fill sync. bytes");

                _bytesReceived += count;
                var filled = (int) state + count; // count of the bytes currently in synchronizeWindow
                var matched = true;
                for (var i = 0; i < filled && matched; i++)
                    matched &= _synchronizeData[i] == _synchronizeWindow[i];

                if (matched) // the match started in the beginning of the window, so it must be a full match
                {
                    _doneSynchronizeCallback(null);
                }
                else
                {
                    if (_bytesReceived > _syncStopPoint)
                        throw new EncryptionException("Could not resyncronise the stream");

                    // See if the current window contains the first byte of the expected synchronize data
                    // No need to check synchronizeWindow[0] as otherwise we could loop forever receiving 0 bytes
                    var shift = -1;
                    for (var i = 1; i < _synchronizeWindow.Length && shift == -1; i++)
                        if (_synchronizeWindow[i] == _synchronizeData[0])
                            shift = i;

                    // The current data is all useless, so read an entire new window of data
                    if (shift == -1)
                    {
                        NetworkIO.EnqueueReceive(_socket, _synchronizeWindow, 0, _synchronizeWindow.Length, null, null,
                            null, _fillSynchronizeBytesCallback, 0);
                    }
                    else
                    {
                        // Shuffle everything left by 'shift' (the first good byte) and fill the rest of the window
                        Buffer.BlockCopy(_synchronizeWindow, shift, _synchronizeWindow, 0,
                            _synchronizeWindow.Length - shift);
                        NetworkIO.EnqueueReceive(_socket, _synchronizeWindow, _synchronizeWindow.Length - shift,
                            shift, null, null, null, _fillSynchronizeBytesCallback, _synchronizeWindow.Length - shift);
                    }
                }
            }
            catch (Exception ex)
            {
                AsyncResult.Complete(ex);
            }
        }

        protected virtual void DoneSynchronize()
        {
            // do nothing for now
        }

        #endregion

        #region I/O Functions

        protected void ReceiveMessage(byte[] buffer, int length, AsyncCallback callback)
        {
            try
            {
                if (length == 0)
                {
                    callback(null);
                    return;
                }
                if (_initialBuffer != null)
                {
                    var toCopy = Math.Min(_initialBufferCount, length);
                    Array.Copy(_initialBuffer, _initialBufferOffset, buffer, 0, toCopy);
                    _initialBufferOffset += toCopy;
                    _initialBufferCount -= toCopy;

                    if (toCopy == _initialBufferCount)
                    {
                        _initialBufferCount = 0;
                        _initialBufferOffset = 0;
                        _initialBuffer = BufferManager.EmptyBuffer;
                    }

                    if (toCopy == length)
                        callback(null);
                    else
                        NetworkIO.EnqueueReceive(_socket, buffer, toCopy, length - toCopy, null, null, null,
                            _doneReceiveCallback, callback);
                }
                else
                {
                    NetworkIO.EnqueueReceive(_socket, buffer, 0, length, null, null, null, _doneReceiveCallback,
                        callback);
                }
            }
            catch (Exception ex)
            {
                AsyncResult.Complete(ex);
            }
        }

        private void DoneReceive(bool succeeded, int count, object state)
        {
            try
            {
                var callback = (AsyncCallback) state;
                if (!succeeded)
                    throw new MessageException("Could not receive");

                _bytesReceived += count;
                callback(null);
            }
            catch (Exception ex)
            {
                AsyncResult.Complete(ex);
            }
        }

        protected void SendMessage(byte[] toSend)
        {
            try
            {
                if (toSend.Length > 0)
                    NetworkIO.EnqueueSend(_socket, toSend, 0, toSend.Length, null, null, null, _doneSendCallback, null);
            }
            catch (Exception ex)
            {
                AsyncResult.Complete(ex);
            }
        }

        private void DoneSend(bool succeeded, int count, object state)
        {
            if (!succeeded)
                AsyncResult.Complete(new MessageException("Could not send required data"));
        }

        #endregion

        #region Cryptography Setup

        /// <summary>
        ///     Generate a 160 bit random number for X
        /// </summary>
        private void GenerateX()
        {
            _x = CryptographicBuffer.GenerateRandom(20).ToArray();
        }

        /// <summary>
        ///     Calculate 2^X mod P
        /// </summary>
        private void GenerateY()
        {
            _y = ModuloCalculator.Calculate(ModuloCalculator.Two, _x);
        }

        /// <summary>
        ///     Instantiate the cryptors with the keys: Hash(encryptionSalt, S, SKEY) for the encryptor and
        ///     Hash(encryptionSalt, S, SKEY) for the decryptor.
        ///     (encryptionSalt should be "keyA" if you're A, "keyB" if you're B, and reverse for decryptionSalt)
        /// </summary>
        /// <param name="encryptionSalt">The salt to calculate the encryption key with</param>
        /// <param name="decryptionSalt">The salt to calculate the decryption key with</param>
        protected void CreateCryptors(string encryptionSalt, string decryptionSalt)
        {
            _encryptor = new RC4(Hash(Encoding.ASCII.GetBytes(encryptionSalt), S, Skey.Hash));
            _decryptor = new RC4(Hash(Encoding.ASCII.GetBytes(decryptionSalt), S, Skey.Hash));
        }

        /// <summary>
        ///     Sets CryptoSelect and initializes the stream encryptor and decryptor based on the selected method.
        /// </summary>
        /// <param name="remoteCryptoBytes">
        ///     The cryptographic methods supported/wanted by the remote client in CryptoProvide
        ///     format. The highest order one available will be selected
        /// </param>
        /// <param name="replace">if set to <c>true</c> [replace].</param>
        /// <returns></returns>
        protected virtual int SelectCrypto(byte[] remoteCryptoBytes, bool replace)
        {
            CryptoSelect = new byte[remoteCryptoBytes.Length];

            // '2' corresponds to RC4Full
            if ((remoteCryptoBytes[3] & 2) == 2 && Toolbox.HasEncryption(_allowedEncryption, EncryptionTypes.RC4Full))
            {
                CryptoSelect[3] |= 2;
                if (replace)
                {
                    Encryptor = _encryptor;
                    Decryptor = _decryptor;
                }
                return 2;
            }

            // '1' corresponds to RC4Header
            if ((remoteCryptoBytes[3] & 1) == 1 && Toolbox.HasEncryption(_allowedEncryption, EncryptionTypes.RC4Header))
            {
                CryptoSelect[3] |= 1;
                if (replace)
                {
                    Encryptor = new RC4Header();
                    Decryptor = new RC4Header();
                }
                return 1;
            }

            throw new EncryptionException("No valid encryption method detected");
        }

        #endregion

        #region Utility Functions

        /// <summary>
        ///     Concatenates several byte buffers
        /// </summary>
        /// <param name="data">Buffers to concatenate</param>
        /// <returns>Resulting concatenated buffer</returns>
        protected byte[] Combine(params byte[][] data)
        {
            var totalLength = data.Sum(datum => datum.Length);

            var combined = new byte[totalLength];
            data.Aggregate(0, (current, t) => current + Message.Write(combined, current, t));

            return combined;
        }

        /// <summary>
        ///     Hash some data with SHA1
        /// </summary>
        /// <param name="data">Buffers to hash</param>
        /// <returns>20-byte hash</returns>
        protected byte[] Hash(params byte[][] data)
        {
            return SHA1Helper.ComputeHash(Combine(data));
        }

        /// <summary>
        ///     Converts a 2-byte big endian integer into an int (reverses operation of Len())
        /// </summary>
        /// <param name="data">2 byte buffer</param>
        /// <returns>int</returns>
        protected int DeLen(byte[] data)
        {
            return (data[0] << 8) + data[1];
        }

        /// <summary>
        ///     Returns a 2-byte buffer with the length of data
        /// </summary>
        protected byte[] Len(byte[] data)
        {
            var lenBuffer = new byte[2];
            lenBuffer[0] = (byte) ((data.Length >> 8) & 0xff);
            lenBuffer[1] = (byte) ((data.Length) & 0xff);
            return lenBuffer;
        }

        /// <summary>
        ///     Returns a 0 to 512 byte 0-filled pad.
        /// </summary>
        protected byte[] GeneratePad()
        {
            return new byte[RandomNumber(512)];
        }

        #endregion

        #region Miscellaneous

        protected byte[] DoEncrypt(byte[] data)
        {
            var d = (byte[]) data.Clone();
            _encryptor.Encrypt(d);
            return d;
        }

        /// <summary>
        ///     Encrypts some data with the RC4 encryptor used in handshaking
        /// </summary>
        /// <param name="data">The data.</param>
        /// <param name="offset">Offset to begin encryption</param>
        /// <param name="length">The length.</param>
        protected void DoEncrypt(byte[] data, int offset, int length)
        {
            _encryptor.Encrypt(data, offset, data, offset, length);
        }

        /// <summary>
        ///     Decrypts some data with the RC4 encryptor used in handshaking
        /// </summary>
        /// <param name="data">Buffers with the data to decrypt</param>
        /// <returns>Buffer with decrypted data</returns>
        protected byte[] DoDecrypt(byte[] data)
        {
            var d = (byte[]) data.Clone();
            _decryptor.Decrypt(d);
            return d;
        }

        /// <summary>
        ///     Decrypts some data with the RC4 decryptor used in handshaking
        /// </summary>
        /// <param name="data">The data.</param>
        /// <param name="offset">Offset to begin decryption</param>
        /// <param name="length">The length.</param>
        protected void DoDecrypt(byte[] data, int offset, int length)
        {
            _decryptor.Decrypt(data, offset, data, offset, length);
        }

        /// <summary>
        ///     Signal that the cryptor is now in a state ready to encrypt and decrypt payload data
        /// </summary>
        protected void Ready()
        {
            AsyncResult.Complete();
        }

        protected void SetMinCryptoAllowed(EncryptionTypes allowedEncryption)
        {
            _allowedEncryption = allowedEncryption;

            // EncryptionType is basically a bit position starting from the right.
            // This sets all bits in CryptoProvide 0 that is to the right of minCryptoAllowed.
            CryptoProvide[0] = CryptoProvide[1] = CryptoProvide[2] = CryptoProvide[3] = 0;

            if (Toolbox.HasEncryption(allowedEncryption, EncryptionTypes.RC4Full))
                CryptoProvide[3] |= 1 << 1;

            if (Toolbox.HasEncryption(allowedEncryption, EncryptionTypes.RC4Header))
                CryptoProvide[3] |= 1;
        }

        #endregion
    }
}