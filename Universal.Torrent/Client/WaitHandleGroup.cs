using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace Universal.Torrent.Client
{
    internal class WaitHandleGroup : WaitHandle
    {
        private readonly List<WaitHandle> _handles;
        private readonly List<string> _names;

        public WaitHandleGroup()
        {
            _handles = new List<WaitHandle>();
            _names = new List<string>();
        }

        public void AddHandle(WaitHandle handle, string name)
        {
            _handles.Add(handle);
            _names.Add(name);
        }

        public override bool WaitOne()
        {
            return _handles.Count == 0 || WaitAll(_handles.ToArray());
        }

        public override bool WaitOne(int millisecondsTimeout)
        {
            return _handles.Count == 0 || WaitAll(_handles.ToArray(), millisecondsTimeout);
        }

        public override bool WaitOne(TimeSpan timeout)
        {
            return _handles.Count == 0 || WaitAll(_handles.ToArray(), timeout);
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            for (var i = 0; i < _handles.Count; i ++)
            {
                sb.Append("WaitHandle: ");
                sb.Append(_names[i]);
                sb.Append(". State: ");
                sb.Append(_handles[i].WaitOne(0) ? "Signalled" : "Unsignalled");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        protected override void Dispose(bool explicitDisposing)
        {
            foreach (var t in _handles)
                t.Dispose();
            base.Dispose(explicitDisposing);
        }
    }
}