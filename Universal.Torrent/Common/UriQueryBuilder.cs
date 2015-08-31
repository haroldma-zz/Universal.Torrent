//
// UriQueryBuilder.cs
//
// Authors:
//   Olivier Dufour <olivier.duff@gmail.com>
//   Alan McGovern <alan.mcgovern@gmail.com>
//
// Copyright (C) 2009 Olivier Dufour
//                    Alan McGovern
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
using System.Linq;

namespace Universal.Torrent.Common
{
    public class UriQueryBuilder
    {
        private readonly UriBuilder _builder;
        private readonly Dictionary<string, string> _queryParams;

        public UriQueryBuilder(string uri)
            : this(new Uri(uri))

        {
        }

        public UriQueryBuilder(Uri uri)
        {
            _builder = new UriBuilder(uri);
            _queryParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            ParseParameters();
        }

        public string this[string key]
        {
            get { return _queryParams[key]; }
            set { _queryParams[key] = value; }
        }

        public UriQueryBuilder Add(string key, object value)
        {
            Check.Key(key);
            Check.Value(value);

            _queryParams[key] = value.ToString();
            return this;
        }

        public bool Contains(string key)
        {
            return _queryParams.ContainsKey(key);
        }

        private void ParseParameters()
        {
            if (_builder.Query.Length == 0 || !_builder.Query.StartsWith("?"))
                return;

            var strs = _builder.Query.Remove(0, 1).Split('&');
            foreach (var kv in strs.Select(t => t.Split('=')).Where(kv => kv.Length == 2))
            {
                _queryParams.Add(kv[0].Trim(), kv[1].Trim());
            }
        }

        public override string ToString()
        {
            return ToUri().OriginalString;
        }

        public Uri ToUri()
        {
            var result = _queryParams.Aggregate("",
                (current, keypair) => current + (keypair.Key + "=" + keypair.Value + "&"));
            _builder.Query = result.Length == 0 ? result : result.Remove(result.Length - 1);
            return _builder.Uri;
        }
    }
}