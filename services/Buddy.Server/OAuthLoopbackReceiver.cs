using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Buddy.Server;

// Created only inside an explicitly approved future Desktop OAuth entry point.
// Binds one random IPv4 loopback port, without URL ACL, firewall or certificate changes.
public sealed class OAuthLoopbackReceiver : IDisposable
{
    private readonly TcpListener listener=new(IPAddress.Loopback,0);
    private bool received;
    public Uri Redirect { get; }
    public OAuthLoopbackReceiver(){listener.Start(1);Redirect=new($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/");}
    public async Task<Uri> Receive(CancellationToken ct)
    {
        if(received)throw new BuddyException("INVALID_CONSENT","This callback receiver was already used.");received=true;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromMinutes(5));
        using var client=await listener.AcceptTcpClientAsync(deadline.Token);
        if(client.Client.RemoteEndPoint is not IPEndPoint endpoint||!IPAddress.IsLoopback(endpoint.Address))throw new BuddyException("INVALID_CONSENT","Only a loopback callback is accepted.");
        using var stream=client.GetStream();var bytes=new byte[8192];int used=0;string headers="";
        while(used<bytes.Length){int n=await stream.ReadAsync(bytes.AsMemory(used),deadline.Token);if(n==0)break;used+=n;headers=Encoding.ASCII.GetString(bytes,0,used);if(headers.Contains("\r\n\r\n",StringComparison.Ordinal))break;}
        var first=headers.Split("\r\n")[0].Split(' ');
        if(!headers.Contains("\r\n\r\n",StringComparison.Ordinal)||first.Length!=3||first[0]!="GET"||first[2]!="HTTP/1.1"||!first[1].StartsWith("/?",StringComparison.Ordinal)||first[1].Length>7000)
            throw new BuddyException("INVALID_CONSENT","The OAuth callback request was invalid.");
        var hosts=headers.Split("\r\n").Where(s=>s.StartsWith("Host:",StringComparison.OrdinalIgnoreCase)).ToArray();
        if(hosts.Length!=1||hosts[0][5..].Trim()!=Redirect.Authority)throw new BuddyException("INVALID_CONSENT","The callback host was not this receiver.");
        var callback=new Uri(Redirect,first[1]);_ = OAuthCallbacks.Parse(callback,Redirect);
        const string body="Buddy received the callback. Return to Buddy to check connection status. This page does not confirm a grant.";
        var response=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nCache-Control: no-store\r\nReferrer-Policy: no-referrer\r\nConnection: close\r\nContent-Length: "+Encoding.UTF8.GetByteCount(body)+"\r\n\r\n"+body);
        await stream.WriteAsync(response,deadline.Token);return callback;
    }
    public void Dispose()=>listener.Stop();
}
