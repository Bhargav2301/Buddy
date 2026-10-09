using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Buddy.Server;

public record ConnectorCapability(string Provider,string Name,string Scope,string Setup);
public static class ConnectorCatalog
{
    // Deliberate release boundary: not a saved setting, model tool or remotely writable flag.
    public static bool AccountGrantsEnabled => false;
    public static readonly ConnectorCapability[] Capabilities=[
        new("gmail","Recent message headers only","https://www.googleapis.com/auth/gmail.metadata","Requires a Google Cloud project with Gmail API enabled, a Desktop OAuth client, consent-screen configuration and explicit approval of metadata access. No bodies, attachments, drafts or sending."),
        new("notion","Search titles of shared pages","read_content","Requires a registered public Notion connection with read-content capability, a registered redirect and an approved confidential OAuth broker for its client secret. Only pages explicitly shared with the connection are in scope; no edits.")];
    public static void RequireGrantApproval()=>throw new BuddyException("CONSENT_DISABLED","Account grants are disabled in this build. Obtain the owner's specific provider/scope approval and complete registration before enabling them.");
}
public enum ConnectorState { SetupRequired, Disconnected, ConsentPending, Denied, Connected, ReconnectRequired, RevocationPending, Revoked }
public record ProviderToken(string Provider,string AccessToken,string RefreshToken,string[] Capabilities,DateTimeOffset Expires,bool Enabled=true)
{
    public override string ToString()=>$"ProviderToken {{ Provider = {Provider}, credentials = [redacted], Enabled = {Enabled} }}";
}
public record ConnectorItem(string Id,string Title,string Detail);

// Protocol preparation is side-effect free: no browser, listener, account grant or external request.
public sealed class OAuthAttempt
{
    public string State { get; }=Base64(RandomNumberGenerator.GetBytes(32));
    public string Verifier { get; }=Base64(RandomNumberGenerator.GetBytes(32));
    public DateTimeOffset Expires { get; }=DateTimeOffset.UtcNow.AddMinutes(5);
    private bool consumed;
    public string Accept(string state,string? code,string? error,DateTimeOffset now)
    {
        if(consumed||now>Expires||!Security.Equal(state,State))throw new BuddyException("INVALID_CONSENT","The consent reply expired or did not match this request.");
        consumed=true;
        if(error is not null)throw new BuddyException("CONSENT_DENIED","Provider consent was denied or unavailable.");
        if(string.IsNullOrWhiteSpace(code)||code.Length>4096)throw new BuddyException("INVALID_CONSENT","No valid authorization code was returned.");
        return code;
    }
    public Uri GoogleUrl(string clientId,Uri redirect)
    {
        if(string.IsNullOrWhiteSpace(clientId)||clientId.Length>300||redirect.Scheme!="http"||redirect.Host!="127.0.0.1"||redirect.Port<1024||redirect.AbsolutePath!="/"||redirect.Query.Length>0||redirect.Fragment.Length>0||redirect.UserInfo.Length>0)
            throw new BuddyException("INVALID_CONFIG","Google Desktop OAuth requires a registered client and a dedicated random loopback port.");
        var fields=new Dictionary<string,string>{{"client_id",clientId},{"redirect_uri",redirect.AbsoluteUri},{"response_type","code"},{"scope",ConnectorCatalog.Capabilities[0].Scope},{"state",State},{"code_challenge",Base64(SHA256.HashData(Encoding.ASCII.GetBytes(Verifier)))},{"code_challenge_method","S256"}};
        return new("https://accounts.google.com/o/oauth2/v2/auth?"+string.Join("&",fields.Select(p=>Uri.EscapeDataString(p.Key)+"="+Uri.EscapeDataString(p.Value))));
    }
    private static string Base64(byte[] bytes)=>Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
}

// Encrypted local storage is available for a future explicitly approved connection flow.
// No instance is constructed or token accepted by the shipped UI while grants are disabled.
public sealed class ConnectorTokenVault
{
    private readonly string folder;private readonly IDataProtector protector;
    public ConnectorTokenVault(string folder,IDataProtectionProvider provider){this.folder=folder;protector=provider.CreateProtector("Buddy.ConnectorTokens.v1");}
    private string PathFor(string provider)=>System.IO.Path.Combine(folder,provider is "gmail" or "notion"?provider+".encrypted":throw new ArgumentException("Unknown provider"));
    public async Task Save(ProviderToken token,CancellationToken ct)
    {
        var path=PathFor(token.Provider);Directory.CreateDirectory(folder);var plain=JsonSerializer.SerializeToUtf8Bytes(token);
        byte[] bytes;try{bytes=protector.Protect(plain);}finally{CryptographicOperations.ZeroMemory(plain);}
        try{await File.WriteAllBytesAsync(path+".new",bytes,ct);ct.ThrowIfCancellationRequested();File.Move(path+".new",path,true);}
        finally{if(File.Exists(path+".new"))File.Delete(path+".new");}
    }
    public async Task<ProviderToken?> Read(string provider,CancellationToken ct)
    {
        var path=PathFor(provider);if(!File.Exists(path))return null;var plain=protector.Unprotect(await File.ReadAllBytesAsync(path,ct));
        try{return JsonSerializer.Deserialize<ProviderToken>(plain);}finally{CryptographicOperations.ZeroMemory(plain);}
    }
    public void Forget(string provider){File.Delete(PathFor(provider));File.Delete(PathFor(provider)+".new");}
}

// Narrow read-only adapters; no arbitrary URL, send, draft, delete or page-write surface.
public sealed class ConnectorReadClient(HttpClient http)
{
    public static HttpClient CreateTransport()=>new(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){Timeout=TimeSpan.FromSeconds(20)};
    private static void Require(ProviderToken token,string provider,string capability)
    {
        if(!token.Enabled||token.Provider!=provider||token.Expires<=DateTimeOffset.UtcNow||string.IsNullOrWhiteSpace(token.AccessToken))throw new BuddyException("RECONNECT_REQUIRED","Reconnect this provider before reading.");
        if(!token.Capabilities.Contains(capability))throw new BuddyException("CAPABILITY_DENIED","This connection has not granted the requested capability.");
    }
    public async Task<IReadOnlyList<ConnectorItem>> GmailHeaders(ProviderToken token,CancellationToken ct)
    {
        Require(token,"gmail",ConnectorCatalog.Capabilities[0].Scope);
        using var list=await Read(HttpMethod.Get,"https://gmail.googleapis.com/gmail/v1/users/me/messages?maxResults=5&includeSpamTrash=false",token,null,ct);
        var result=new List<ConnectorItem>();
        if(!list.RootElement.TryGetProperty("messages",out var messages))return result;
        foreach(var item in messages.EnumerateArray().Take(5)) {
            var id=item.GetProperty("id").GetString()!;if(id.Length>128||!id.All(char.IsAsciiLetterOrDigit))throw new BuddyException("PROVIDER_DATA","Unexpected message identifier.");
            using var data=await Read(HttpMethod.Get,"https://gmail.googleapis.com/gmail/v1/users/me/messages/"+id+"?format=metadata&metadataHeaders=Subject&metadataHeaders=From&metadataHeaders=Date",token,null,ct);
            var headers=data.RootElement.GetProperty("payload").GetProperty("headers").EnumerateArray().Take(30).ToArray();
            string Header(string name)=>Clean(headers.FirstOrDefault(h=>h.GetProperty("name").GetString()?.Equals(name,StringComparison.OrdinalIgnoreCase)==true).ValueKind==JsonValueKind.Undefined?"":headers.First(h=>h.GetProperty("name").GetString()?.Equals(name,StringComparison.OrdinalIgnoreCase)==true).GetProperty("value").GetString()??"");
            result.Add(new(id,Header("Subject"),Header("From")+" | "+Header("Date")));
        }
        return result;
    }
    public async Task<IReadOnlyList<ConnectorItem>> NotionTitles(ProviderToken token,string query,CancellationToken ct)
    {
        Require(token,"notion","read_content");Security.Text(query,200,"Page-title query");
        using var data=await Read(HttpMethod.Post,"https://api.notion.com/v1/search",token,new{query,page_size=5,filter=new{property="object",value="page"}},ct);
        return data.RootElement.GetProperty("results").EnumerateArray().Take(5).Select(page=>{
            string title="";
            if(page.TryGetProperty("properties",out var props))foreach(var property in props.EnumerateObject())if(property.Value.TryGetProperty("title",out var words))title=string.Concat(words.EnumerateArray().Take(20).Select(w=>w.TryGetProperty("plain_text",out var text)?text.GetString():""));
            return new ConnectorItem(page.GetProperty("id").GetString()??"",Clean(title),"Shared page title only; page contents were not fetched.");
        }).ToArray();
    }
    private async Task<JsonDocument> Read(HttpMethod method,string uri,ProviderToken token,object? body,CancellationToken ct)
    {
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var request=new HttpRequestMessage(method,uri);request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token.AccessToken);
        if(token.Provider=="notion")request.Headers.Add("Notion-Version","2026-03-11");if(body is not null)request.Content=JsonContent.Create(body);
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,deadline.Token);
        if(response.StatusCode==HttpStatusCode.Unauthorized)throw new BuddyException("RECONNECT_REQUIRED","The provider rejected this connection. Reconnect before reading.");
        if(response.StatusCode==HttpStatusCode.Forbidden)throw new BuddyException("CAPABILITY_DENIED","The provider denied access to this resource.");
        if(!response.IsSuccessStatusCode)throw new BuddyException("PROVIDER_UNAVAILABLE","The provider did not complete this read. No result is claimed.");
        if(response.Content.Headers.ContentLength>256_000)throw new BuddyException("PROVIDER_DATA","The provider response exceeded the local limit.");
        await using var stream=await response.Content.ReadAsStreamAsync(deadline.Token);using var output=new MemoryStream();var buffer=new byte[8192];int count;
        while((count=await stream.ReadAsync(buffer,deadline.Token))>0){if(output.Length+count>256_000)throw new BuddyException("PROVIDER_DATA","The provider response exceeded the local limit.");output.Write(buffer,0,count);}
        return JsonDocument.Parse(output.ToArray());
    }
    private static string Clean(string text)=>Security.Redact(text[..Math.Min(text.Length,1000)]);
}
