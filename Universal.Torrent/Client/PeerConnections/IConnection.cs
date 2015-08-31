// IConnection.cs created with MonoDevelop
// User: alan at 22:58 22/01/2008
//
// To change standard headers go to Edit->Preferences->Coding->Standard Headers
//

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Universal.Torrent.Client.PeerConnections
{
    public interface IConnection : IDisposable
    {
        byte[] AddressBytes { get; }

        bool Connected { get; }

        bool CanReconnect { get; }

        bool IsIncoming { get; }

        EndPoint EndPoint { get; }

        Uri Uri { get; }

        Task ConnectAsync(CancellationToken token);

        IAsyncResult BeginConnect(AsyncCallback callback, object state);

        void EndConnect(IAsyncResult result);

        Task<uint> ReceiveAsync(byte[] buffer, int offset, int count);

        IAsyncResult BeginReceive(byte[] buffer, int offset, int count, AsyncCallback callback, object state);

        int EndReceive(IAsyncResult result);

        Task<uint> SendAsync(byte[] buffer, int offset, int count);

        IAsyncResult BeginSend(byte[] buffer, int offset, int count, AsyncCallback callback, object state);

        int EndSend(IAsyncResult result);
    }
}