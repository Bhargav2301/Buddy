using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Buddy.Server;

public record OAuthRegistration(string Provider,string ClientId,string ClientSecret,Uri Redirect,bool ConfidentialBroker=false)
{
    public override string ToString()=>$"OAuthRegistration {{ Provider = {Provider}, credentials = [redacted] }}";
    public void Validate()
    {
        if(Provider is not ("gmail" or "notion")||string.IsNullOrWhiteSpace(ClientId)||ClientId.Length>300||ClientId.Contains(':')||ClientSecret.Length>4096||!Redirect.IsAbsoluteUri||Redirect.UserInfo.Length>0||Redirect.Query.Length>0||Redirect.Fragment.Length>0)
            throw new BuddyException("INVALID_CONFIG","A valid provider registration and exact callback address are required.");
        if(Provider=="gmail")_ = new OAuthAttempt().GoogleUrl(ClientId,Redirect);
        if(Provider=="notion"&&(!ConfidentialBroker||string.IsNullOrWhiteSpace(ClientSecret)||Redirect.Scheme!="https"||Redirect.IsLoopback))
            throw new BuddyException("INVALID_CONFIG","Public Notion OAuth requires an approved confidential broker and registered HTTPS callback. Do not embed its shared secret in a desktop package.");
    }
}

public static class OAuthCallbacks
{
    public static IReadOnlyDictionary<string,string> Parse(Uri actual,Uri registered)
    {
        if(actual.Scheme!=registered.Scheme||actual.Host!=registered.Host||actual.Port!=registered.Port||actual.AbsolutePath!=registered.AbsolutePath||actual.UserInfo.Length>0||actual.Fragment.Length>0||actual.Query.Length>8192)
            throw new BuddyException("INVALID_CONSENT","The callback address does not match this attempt.");
        var result=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var field in actual.Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries)){
            var pair=field.Split('=',2);var key=Uri.UnescapeDataString(pair[0]);var value=pair.Length==2?Uri.UnescapeDataString(pair[1].Replace('+',' ')):"";
            if(!result.TryAdd(key,value)||result.Count>20)throw new BuddyException("INVALID_CONSENT","Duplicate or oversized callback fields were rejected.");
        }
        return result;
    }
    public static Uri Authorization(OAuthRegistration registration,OAuthAttempt attempt)
    {
        registration.Validate();
        if(registration.Provider=="gmail")return attempt.GoogleUrl(registration.ClientId,registration.Redirect);
        return new("https://api.notion.com/v1/oauth/authorize?owner=user&response_type=code&client_id="+Uri.EscapeDataString(registration.ClientId)+"&redirect_uri="+Uri.EscapeDataString(registration.Redirect.AbsoluteUri)+"&state="+Uri.EscapeDataString(attempt.State));
    }
}

// Actual provider protocol, injected transport for testing. Shipped UI never calls it while grants are disabled.
public sealed class OAuthProtocol(HttpClient http)
{
    public async Task<ProviderToken> Exchange(OAuthRegistration config,string code,OAuthAttempt attempt,CancellationToken ct)
    {
        config.Validate();
        var fields=new Dictionary<string,string>{{"grant_type","authorization_code"},{"code",code},{"redirect_uri",config.Redirect.AbsoluteUri}};
        if(config.Provider=="gmail"){fields["code_verifier"]=attempt.Verifier;fields["client_id"]=config.ClientId;if(config.ClientSecret.Length>0)fields["client_secret"]=config.ClientSecret;}
        using var response=await TokenRequest(config,fields,ct);return Token(config,response.RootElement,null);
    }
    public async Task<ProviderToken> Refresh(OAuthRegistration config,ProviderToken previous,CancellationToken ct)
    {
        config.Validate();if(!previous.Enabled||previous.Provider!=config.Provider||previous.RefreshToken.Length==0)throw new BuddyException("RECONNECT_REQUIRED","This connection cannot refresh; reconnect explicitly.");
        var fields=new Dictionary<string,string>{{"grant_type","refresh_token"},{"refresh_token",previous.RefreshToken}};
        if(config.Provider=="gmail"){fields["client_id"]=config.ClientId;if(config.ClientSecret.Length>0)fields["client_secret"]=config.ClientSecret;}
        using var response=await TokenRequest(config,fields,ct);return Token(config,response.RootElement,previous);
    }
    public async Task Revoke(OAuthRegistration config,ProviderToken token,CancellationToken ct)
    {
        config.Validate();if(token.Provider!=config.Provider)throw new BuddyException("INVALID_CONFIG","The token belongs to another provider.");
        using var request=new HttpRequestMessage(HttpMethod.Post,config.Provider=="gmail"?"https://oauth2.googleapis.com/revoke":"https://api.notion.com/v1/oauth/revoke");
        if(config.Provider=="gmail")request.Content=new FormUrlEncodedContent(new Dictionary<string,string>{{"token",token.RefreshToken.Length>0?token.RefreshToken:token.AccessToken}});
        else{Basic(request,config);request.Content=JsonContent.Create(new{token=token.AccessToken});}
        using var ignored=await Send(request,ct,emptyAllowed:true);
    }
    private async Task<JsonDocument> TokenRequest(OAuthRegistration config,Dictionary<string,string> fields,CancellationToken ct)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,config.Provider=="gmail"?"https://oauth2.googleapis.com/token":"https://api.notion.com/v1/oauth/token");
        if(config.Provider=="gmail")request.Content=new FormUrlEncodedContent(fields);else{Basic(request,config);request.Content=JsonContent.Create(fields);}
        return await Send(request,ct);
    }
    private static void Basic(HttpRequestMessage request,OAuthRegistration config)
    {
        request.Headers.Authorization=new AuthenticationHeaderValue("Basic",Convert.ToBase64String(Encoding.UTF8.GetBytes(config.ClientId+":"+config.ClientSecret)));
        request.Headers.Add("Notion-Version","2026-03-11");
    }
    private async Task<JsonDocument> Send(HttpRequestMessage request,CancellationToken ct,bool emptyAllowed=false)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try{
            using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
            if(!response.IsSuccessStatusCode)throw new BuddyException(response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden?"RECONNECT_REQUIRED":"PROVIDER_UNAVAILABLE","The provider did not confirm this authorization operation. No success is claimed.");
            if(response.Content.Headers.ContentLength>64_000)throw new BuddyException("PROVIDER_DATA","Authorization response exceeded the limit.");
            await using var stream=await response.Content.ReadAsStreamAsync(timeout.Token);using var bytes=new MemoryStream();var buffer=new byte[4096];int size;
            while((size=await stream.ReadAsync(buffer,timeout.Token))>0){if(bytes.Length+size>64_000)throw new BuddyException("PROVIDER_DATA","Authorization response exceeded the limit.");bytes.Write(buffer,0,size);}
            return JsonDocument.Parse(emptyAllowed&&bytes.Length==0?"{}":Encoding.UTF8.GetString(bytes.ToArray()));
        }catch(HttpRequestException){throw new BuddyException("PROVIDER_UNAVAILABLE","Authorization service is unavailable. No result is claimed.");}
        catch(JsonException){throw new BuddyException("PROVIDER_DATA","The authorization response was invalid.");}
        catch(OperationCanceledException)when(!ct.IsCancellationRequested){throw new BuddyException("PROVIDER_UNAVAILABLE","Authorization request timed out; inspect connection status before retrying.");}
    }
    private static ProviderToken Token(OAuthRegistration config,JsonElement json,ProviderToken? previous)
    {
        try{
            string Required(string name){var value=json.GetProperty(name).GetString();if(string.IsNullOrWhiteSpace(value)||value.Length>16000||value.Any(char.IsControl))throw new JsonException();return value;}
            var access=Required("access_token");if(!Required("token_type").Equals("bearer",StringComparison.OrdinalIgnoreCase))throw new JsonException();
            var refresh=json.TryGetProperty("refresh_token",out var refreshed)&&refreshed.ValueKind==JsonValueKind.String?refreshed.GetString()!:previous?.RefreshToken??"";
            if(refresh.Length>16000||refresh.Any(char.IsControl))throw new JsonException();
            string[] capabilities;DateTimeOffset expires;
            if(config.Provider=="gmail"){
                capabilities=Required("scope").Split(' ',StringSplitOptions.RemoveEmptyEntries);
                if(capabilities.Length!=1||capabilities[0]!=ConnectorCatalog.Capabilities[0].Scope)throw new BuddyException("CAPABILITY_DENIED","The granted Google scope differs from the reviewed metadata-only scope.");
                int seconds=json.GetProperty("expires_in").GetInt32();if(seconds is <1 or >2_592_000)throw new JsonException();expires=DateTimeOffset.UtcNow.AddSeconds(seconds);
            }else{_ = Required("bot_id");capabilities=["read_content"];expires=DateTimeOffset.MaxValue;}
            return new(config.Provider,access,refresh,capabilities,expires);
        }catch(Exception e)when(e is JsonException or KeyNotFoundException or InvalidOperationException or FormatException){throw new BuddyException("PROVIDER_DATA","The provider returned incomplete authorization data.");}
    }
}

// A cancellable lifecycle used by protocol tests and a future explicitly approved provider entry point.
// Notion registration and secret stay at the approved confidential broker, not in distributed desktop settings.
public sealed class ConnectorLifecycle(OAuthRegistration config,ConnectorTokenVault vault,OAuthProtocol protocol,ConnectorReadClient reader)
{
    private readonly SemaphoreSlim gate=new(1,1);private CancellationTokenSource? active;
    public ConnectorState State { get; private set; }=ConnectorState.Disconnected;
    public string Status { get; private set; }="Not connected";
    private void Set(ConnectorState state,string status){State=state;Status=status;}
    public void Cancel()=>active?.Cancel();
    public async Task Restore(CancellationToken ct)
    {
        await gate.WaitAsync(ct);try{config.Validate();var token=await vault.Read(config.Provider,ct);Set(token is null?ConnectorState.Disconnected:!token.Enabled?ConnectorState.RevocationPending:token.Expires<=DateTimeOffset.UtcNow?ConnectorState.ReconnectRequired:ConnectorState.Connected,token is null?"Not connected":!token.Enabled?"Reads disabled; provider revocation remains unconfirmed":"Saved authorization loaded; provider may require reconnection");}finally{gate.Release();}
    }
    public async Task Connect(Func<Uri,CancellationToken,Task<Uri>> authorize,CancellationToken ct)
    {
        await gate.WaitAsync(ct);using var operation=CancellationTokenSource.CreateLinkedTokenSource(ct);operation.CancelAfter(TimeSpan.FromMinutes(5));active=operation;
        bool exchanged=false;
        try{
            config.Validate();if(await vault.Read(config.Provider,operation.Token) is not null)throw new BuddyException("ALREADY_CONFIGURED","Disconnect the saved grant before replacing this connection.");
            var attempt=new OAuthAttempt();Set(ConnectorState.ConsentPending,"Waiting for explicit provider consent");
            var callback=await authorize(OAuthCallbacks.Authorization(config,attempt),operation.Token);var values=OAuthCallbacks.Parse(callback,config.Redirect);
            var code=attempt.Accept(values.GetValueOrDefault("state",""),values.GetValueOrDefault("code"),values.GetValueOrDefault("error"),DateTimeOffset.UtcNow);
            operation.Token.ThrowIfCancellationRequested();var token=await protocol.Exchange(config,code,attempt,operation.Token);exchanged=true;
            // Persist disabled before capability verification; a failed probe cannot leave a usable saved grant.
            await vault.Save(token with{Enabled=false},operation.Token);
            await Probe(token,operation.Token);operation.Token.ThrowIfCancellationRequested();await vault.Save(token,operation.Token);
            Set(ConnectorState.Connected,"Provider accepted authorization and the bounded read capability was verified");
        }catch(OperationCanceledException){Set(exchanged?ConnectorState.RevocationPending:ConnectorState.Disconnected,exchanged?"Stopped after provider exchange; reads disabled. Disconnect to revoke the grant.":"Consent cancelled; no connection completed");throw;}
        catch(BuddyException e){Set(exchanged?ConnectorState.RevocationPending:e.Code=="CONSENT_DENIED"?ConnectorState.Denied:e.Code=="INVALID_CONFIG"?ConnectorState.SetupRequired:ConnectorState.ReconnectRequired,exchanged?"Provider grant not activated; disconnect to revoke it.":e.Message);throw;}
        catch{Set(ConnectorState.ReconnectRequired,"Connection was not completed. Review provider authorization before retrying.");throw new BuddyException("CONNECTION_FAILED","Connection was not completed; no credentials are included in this error.");}
        finally{active=null;gate.Release();}
    }
    private Task Probe(ProviderToken token,CancellationToken ct)=>config.Provider=="gmail"?reader.GmailHeaders(token,ct):reader.NotionTitles(token,"Buddy",ct);
    public async Task<IReadOnlyList<ConnectorItem>> Read(string query,CancellationToken ct)
    {
        await gate.WaitAsync(ct);using var operation=CancellationTokenSource.CreateLinkedTokenSource(ct);operation.CancelAfter(TimeSpan.FromSeconds(90));active=operation;
        try{
            var token=await vault.Read(config.Provider,operation.Token);
            if(token is null||!token.Enabled||State is ConnectorState.RevocationPending or ConnectorState.Revoked or ConnectorState.Denied)throw new BuddyException("RECONNECT_REQUIRED","This connection is unavailable; no account data was read.");
            if(token.Expires<=DateTimeOffset.UtcNow.AddSeconds(30)){token=await protocol.Refresh(config,token,operation.Token);await vault.Save(token,operation.Token);}
            var result=config.Provider=="gmail"?await reader.GmailHeaders(token,operation.Token):await reader.NotionTitles(token,query,operation.Token);
            Set(ConnectorState.Connected,"Bounded read completed");return result;
        }catch(BuddyException e){if(State!=ConnectorState.RevocationPending)Set(e.Code=="CAPABILITY_DENIED"?ConnectorState.Denied:ConnectorState.ReconnectRequired,e.Message);throw;}
        finally{active=null;gate.Release();}
    }
    public async Task Disconnect(CancellationToken ct)
    {
        Cancel();await gate.WaitAsync(ct);try{
            var token=await vault.Read(config.Provider,ct);if(token is null){Set(ConnectorState.Disconnected,"No local token remains; no new provider revocation is claimed");return;}
            Set(ConnectorState.RevocationPending,"Reads disabled; asking provider to revoke access");await vault.Save(token with{Enabled=false},ct);
            await protocol.Revoke(config,token,ct);vault.Forget(config.Provider);Set(ConnectorState.Revoked,"Provider confirmed revocation; local tokens removed");
        }catch{Set(ConnectorState.RevocationPending,"Revocation unconfirmed. Reads stay disabled; retry or remove access in the provider's account settings.");throw new BuddyException("REVOCATION_UNCONFIRMED",Status);}
        finally{gate.Release();}
    }
}

public static class ConnectorAccountEntry
{
    public static Task Connect(ConnectorLifecycle lifecycle,Func<Uri,CancellationToken,Task<Uri>> authorize,CancellationToken ct)
    {
        ConnectorCatalog.RequireGrantApproval(); // Executes before any browser, callback listener or HTTP request.
        return lifecycle.Connect(authorize,ct);
    }
}
