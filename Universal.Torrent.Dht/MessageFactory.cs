#if !DISABLE_DHT
//
// MessageFactory.cs
//
// Authors:
//   Alan McGovern <alan.mcgovern@gmail.com>
//
// Copyright (C) 2008 Alan McGovern
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


using System.Collections.Generic;
using Universal.Torrent.Bencoding;
using Universal.Torrent.Dht.Messages.Errors;
using Universal.Torrent.Dht.Messages.Queries;

namespace Universal.Torrent.Dht
{
    internal delegate Message Creator(BEncodedDictionary dictionary);

    internal delegate Message ResponseCreator(BEncodedDictionary dictionary, QueryMessage message);

    internal static class MessageFactory
    {
        private static readonly string QueryNameKey = "q";
        private static readonly BEncodedString MessageTypeKey = "y";
        private static readonly BEncodedString TransactionIdKey = "t";

        private static readonly Dictionary<BEncodedValue, QueryMessage> Messages =
            new Dictionary<BEncodedValue, QueryMessage>();

        private static readonly Dictionary<BEncodedString, Creator> QueryDecoders =
            new Dictionary<BEncodedString, Creator>();

        static MessageFactory()
        {
            QueryDecoders.Add("announce_peer", d => new AnnouncePeer(d));
            QueryDecoders.Add("find_node", d => new FindNode(d));
            QueryDecoders.Add("get_peers", d => new GetPeers(d));
            QueryDecoders.Add("ping", d => new Ping(d));
        }

        public static int RegisteredMessages => Messages.Count;

        internal static bool IsRegistered(BEncodedValue transactionId)
        {
            return Messages.ContainsKey(transactionId);
        }

        public static void RegisterSend(QueryMessage message)
        {
            Messages.Add(message.TransactionId, message);
        }

        public static bool UnregisterSend(QueryMessage message)
        {
            return Messages.Remove(message.TransactionId);
        }

        public static Message DecodeMessage(BEncodedDictionary dictionary)
        {
            Message message;
            string error;

            if (!TryDecodeMessage(dictionary, out message, out error))
                throw new MessageException(ErrorCode.GenericError, error);

            return message;
        }

        public static bool TryDecodeMessage(BEncodedDictionary dictionary, out Message message)
        {
            string error;
            return TryDecodeMessage(dictionary, out message, out error);
        }

        public static bool TryDecodeMessage(BEncodedDictionary dictionary, out Message message, out string error)
        {
            message = null;
            error = null;

            if (dictionary[MessageTypeKey].Equals(QueryMessage.QueryType))
            {
                message = QueryDecoders[(BEncodedString) dictionary[QueryNameKey]](dictionary);
            }
            else if (dictionary[MessageTypeKey].Equals(ErrorMessage.ErrorType))
            {
                message = new ErrorMessage(dictionary);
            }
            else
            {
                QueryMessage query;
                var key = (BEncodedString) dictionary[TransactionIdKey];
                if (Messages.TryGetValue(key, out query))
                {
                    Messages.Remove(key);
                    try
                    {
                        message = query.ResponseCreator(dictionary, query);
                    }
                    catch
                    {
                        error = "Response dictionary was invalid";
                    }
                }
                else
                {
                    error = "Response had bad transaction ID";
                }
            }

            return error == null && message != null;
        }
    }
}

#endif