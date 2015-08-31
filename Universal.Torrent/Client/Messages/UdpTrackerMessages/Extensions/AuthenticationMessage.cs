using System;
using System.Text;

namespace Universal.Torrent.Client.Messages.UdpTrackerMessages.Extensions
{
    internal class AuthenticationMessage : Message
    {
        private byte[] _password;
        private string _username;
        private byte _usernameLength;

        public override int ByteLength => 4 + _usernameLength + 8;

        public override void Decode(byte[] buffer, int offset, int length)
        {
            _usernameLength = buffer[offset];
            offset++;
            _username = Encoding.ASCII.GetString(buffer, offset, _usernameLength);
            offset += _usernameLength;
            _password = new byte[8];
            Buffer.BlockCopy(buffer, offset, _password, 0, _password.Length);
        }

        public override int Encode(byte[] buffer, int offset)
        {
            var written = Write(buffer, offset, _usernameLength);
            var name = Encoding.ASCII.GetBytes(_username);
            written += Write(buffer, offset, name, 0, name.Length);
            written += Write(buffer, offset, _password, 0, _password.Length);

            CheckWritten(written);
            return written;
        }
    }
}