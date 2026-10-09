using Buddy.Server;
using Microsoft.AspNetCore.DataProtection;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

internal static class OAuthChecks
{
    internal static async Task Run(Action<bool,string> check)
    {
        var root=Path.Combine(Path.GetTempPath(),"Buddy-oauth-fixture-"+Guid.NewGuid());Directory.CreateDirectory(root);
        var provider=DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(root,"keys")));
        using var handler=new MockOAuth();using var http=new HttpClient(handler);var protocol=new OAuthProtocol(http);var reader=new ConnectorReadClient(http);
        var config=new OAuthRegistration("gmail","fixture-client","fixture-client-secret",new("http://127.0.0.1:51345/"));
        var vault=new ConnectorTokenVault(Path.Combine(root,"google"),provider);var life=new ConnectorLifecycle(config,vault,protocol,reader);
        async Task<string> Error(Func<Task> action){try{await action();return "NO_ERROR";}catch(BuddyException e){return e.Code;}catch(OperationCanceledException){return "CANCELLED";}}
        Task<Uri> Callback(Uri url,CancellationToken ct){ct.ThrowIfCancellationRequested();var state=Get(url,"state");return Task.FromResult(new Uri(config.Redirect,"?state="+Uri.EscapeDataString(state)+"&code=fixture-code"));}
        try{
            int callbacks=0;var blocked=await Error(()=>ConnectorAccountEntry.Connect(life,(url,ct)=>{callbacks++;return Callback(url,ct);},default));
            check(blocked=="CONSENT_DISABLED"&&callbacks==0&&handler.Requests.Count==0,"Production entry blocks before listener/browser/HTTP even when a protocol object exists");
            await life.Connect(Callback,default);check(life.State==ConnectorState.Connected&&(await vault.Read("gmail",default))!.Enabled,"Mock authorization exchanges and verifies capability before enabling a saved connection");
            var exchange=handler.Requests.Single(r=>r.Uri=="https://oauth2.googleapis.com/token");
            check(exchange.Body.Contains("code_verifier=")&&exchange.Body.Contains("redirect_uri=")&&!exchange.Uri.Contains("fixture-code"),"Google exchange binds PKCE and redirect without credentials in request URL");
            var restarted=new ConnectorLifecycle(config,vault,protocol,reader);await restarted.Restore(default);check(restarted.State==ConnectorState.Connected,"Only a saved verified token restores connected state after restart");
            var token=(await vault.Read("gmail",default))!;await vault.Save(token with{Expires=DateTimeOffset.UtcNow.AddMinutes(-1)},default);
            await restarted.Read("",default);check(handler.Requests.Last(r=>r.Uri.EndsWith("/token")).Body.Contains("grant_type=refresh_token")&&(await vault.Read("gmail",default))!.RefreshToken=="fixture-refresh","Expired Google token refreshes while preserving its prior refresh token when omitted");
            handler.RevokeFailure=true;check(await Error(()=>restarted.Disconnect(default))=="REVOCATION_UNCONFIRMED"&&restarted.State==ConnectorState.RevocationPending,"Failed provider revocation remains visibly unconfirmed");
            check(!(await vault.Read("gmail",default))!.Enabled,"A failed revoke leaves stored tokens disabled for every later read");
            var pendingRestart=new ConnectorLifecycle(config,vault,protocol,reader);await pendingRestart.Restore(default);int before=handler.Requests.Count;
            check(pendingRestart.State==ConnectorState.RevocationPending&&await Error(()=>pendingRestart.Read("",default))=="RECONNECT_REQUIRED"&&handler.Requests.Count==before,"Revocation-pending survives restart and blocks account reads before HTTP");
            handler.RevokeFailure=false;await pendingRestart.Disconnect(default);check(pendingRestart.State==ConnectorState.Revoked&&await vault.Read("gmail",default) is null,"Confirmed provider revocation removes local tokens");
            check(handler.Requests.Last().Body.Contains("token=fixture-refresh")&&!handler.Requests.Last().Uri.Contains("fixture-refresh"),"Google revoke sends its token in the POST body only");
            var denied=new ConnectorLifecycle(config,new(Path.Combine(root,"denied"),provider),protocol,reader);before=handler.Requests.Count;
            var denial=await Error(()=>denied.Connect((url,ct)=>Task.FromResult(new Uri(config.Redirect,"?state="+Get(url,"state")+"&error=access_denied")),default));
            check(denial=="CONSENT_DENIED"&&denied.State==ConnectorState.Denied&&handler.Requests.Count==before,"Denied browser consent causes no exchange, read or connection");
            var malformed=new ConnectorLifecycle(config,new(Path.Combine(root,"malformed"),provider),protocol,reader);
            check(await Error(()=>malformed.Connect((url,ct)=>Task.FromResult(new Uri(config.Redirect,"?state=wrong&code=fixture-code")),default))=="INVALID_CONSENT"&&handler.Requests.Count==before,"Bad callback state never reaches the token endpoint");
            check(await Error(()=>{OAuthCallbacks.Parse(new(config.Redirect,"?state=a&state=b&code=c"),config.Redirect);return Task.CompletedTask;})=="INVALID_CONSENT","Duplicate callback state is refused");
            check(await Error(()=>{OAuthCallbacks.Parse(new("https://unregistered.invalid/?state=a&code=c"),config.Redirect);return Task.CompletedTask;})=="INVALID_CONSENT","A substituted callback origin is refused");
            var waiting=new TaskCompletionSource();var cancelled=new ConnectorLifecycle(config,new(Path.Combine(root,"cancelled"),provider),protocol,reader);
            var pending=cancelled.Connect(async(_,ct)=>{waiting.SetResult();await Task.Delay(Timeout.Infinite,ct);return config.Redirect;},default);await waiting.Task;cancelled.Cancel();
            check(await Error(()=>pending)=="CANCELLED"&&cancelled.State==ConnectorState.Disconnected&&handler.Requests.Count==before,"Cancellation releases a live consent wait without HTTP or saved authorization");
            var freshRestart=new ConnectorLifecycle(config,new(Path.Combine(root,"cancelled"),provider),protocol,reader);await freshRestart.Restore(default);check(freshRestart.State==ConnectorState.Disconnected,"Restart never resumes an unfinished consent attempt");
            handler.BadScope=true;check(await Error(()=>new ConnectorLifecycle(config,new(Path.Combine(root,"scope"),provider),protocol,reader).Connect(Callback,default))=="CAPABILITY_DENIED","An unexpectedly broader Google scope is rejected");handler.BadScope=false;
            handler.ProbeDenied=true;var failedVault=new ConnectorTokenVault(Path.Combine(root,"probe"),provider);var failed=new ConnectorLifecycle(config,failedVault,protocol,reader);
            check(await Error(()=>failed.Connect(Callback,default))=="CAPABILITY_DENIED"&&failed.State==ConnectorState.RevocationPending&&!(await failedVault.Read("gmail",default))!.Enabled,"Failure of the actual capability probe cannot leave an enabled connection");handler.ProbeDenied=false;
            await failed.Disconnect(default);
            var notion=config with{Provider="notion",Redirect=new("https://approved-broker.example/oauth/notion/callback"),ConfidentialBroker=true};
            var notionVault=new ConnectorTokenVault(Path.Combine(root,"notion"),provider);var notionLife=new ConnectorLifecycle(notion,notionVault,protocol,reader);
            await notionLife.Connect((url,ct)=>Task.FromResult(new Uri(notion.Redirect,"?state="+Get(url,"state")+"&code=notion-fixture")),default);
            check(notionLife.State==ConnectorState.Connected&&handler.Requests.Any(r=>r.Uri=="https://api.notion.com/v1/oauth/token"&&r.Auth=="Basic"&&r.Body.Contains("redirect_uri")),"Confidential Notion protocol uses Basic client auth and the registered redirect");
            var rotated=await protocol.Refresh(notion,(await notionVault.Read("notion",default))!,default);check(rotated.RefreshToken=="notion-rotated-refresh","Notion refresh rotation retains the new refresh token");await notionVault.Save(rotated,default);
            await notionLife.Disconnect(default);check(notionLife.State==ConnectorState.Revoked&&await notionVault.Read("notion",default) is null,"Notion revoke endpoint confirmation clears the local token");
            check(await Error(()=>{(notion with{ConfidentialBroker=false}).Validate();return Task.CompletedTask;})=="INVALID_CONFIG","Shared Notion client secrets are not accepted as a desktop registration");
            check(!token.ToString().Contains("fixture-access")&&!token.ToString().Contains("fixture-refresh")&&!config.ToString().Contains("fixture-client-secret"),"Token and registration diagnostic strings redact all credential values");
            handler.ThrowSecret=true;string details="";try{await protocol.Exchange(config,"code",new OAuthAttempt(),default);}catch(Exception e){details=e.ToString();}
            check(!details.Contains("fixture-access")&&details.Contains("PROVIDER")==false&&details.Contains("unavailable"),"Transport errors do not expose provider credential text or inner exceptions");handler.ThrowSecret=false;
            var output=Console.Out;using var capture=new StringWriter();try{Console.SetOut(capture);await protocol.Exchange(config,"code",new OAuthAttempt(),default);}finally{Console.SetOut(output);}
            check(capture.ToString().Length==0,"Token exchange emits no console credential or response logs");
            using(var receiver=new OAuthLoopbackReceiver()){
                check(receiver.Redirect.Host=="127.0.0.1"&&receiver.Redirect.Port>1023,"Actual callback listener binds a random loopback port only");
                using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));var receive=receiver.Receive(timeout.Token);
                using var socket=new TcpClient();await socket.ConnectAsync(IPAddress.Loopback,receiver.Redirect.Port,timeout.Token);using var stream=socket.GetStream();
                var request=Encoding.ASCII.GetBytes("GET /?state=fixture-state&code=fixture-code HTTP/1.1\r\nHost: "+receiver.Redirect.Authority+"\r\n\r\n");await stream.WriteAsync(request,timeout.Token);
                var callback=await receive;var buffer=new byte[4096];int n=await stream.ReadAsync(buffer,timeout.Token);var text=Encoding.ASCII.GetString(buffer,0,n);
                check(callback.Query.Contains("fixture-code")&&text.Contains("no-store")&&!text.Contains("fixture-code"),"Actual loopback receiver delivers a bounded callback without echoing its code into the browser page");
            }
            using(var receiver=new OAuthLoopbackReceiver())using(var cancel=new CancellationTokenSource()){var receive=receiver.Receive(cancel.Token);cancel.Cancel();check(await Error(()=>receive)=="CANCELLED","Actual loopback callback wait cancels without a browser or grant");}
        }finally{Directory.Delete(root,true);}
    }
    private static string Get(Uri uri,string name)=>Uri.UnescapeDataString(uri.Query.TrimStart('?').Split('&').Single(p=>p.StartsWith(name+"=",StringComparison.Ordinal))[(name.Length+1)..]);
    private sealed class MockOAuth:HttpMessageHandler
    {
        internal readonly List<(string Uri,string Body,string? Auth)> Requests=[];
        internal bool RevokeFailure,BadScope,ProbeDenied,ThrowSecret;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();if(ThrowSecret)throw new HttpRequestException("fixture-access must not appear in diagnostics");
            string body=request.Content is null?"":await request.Content.ReadAsStringAsync(ct);string uri=request.RequestUri!.AbsoluteUri;Requests.Add((uri,body,request.Headers.Authorization?.Scheme));
            bool notion=request.RequestUri.Host=="api.notion.com";string response;var status=HttpStatusCode.OK;
            if(uri.EndsWith("/revoke")){response="{}";if(RevokeFailure)status=HttpStatusCode.ServiceUnavailable;}
            else if(uri.EndsWith("/token")){
                if(notion)response=JsonSerializer.Serialize(new{access_token="notion-access",token_type="bearer",refresh_token=body.Contains("refresh_token")?"notion-rotated-refresh":"notion-refresh",bot_id="fixture-bot"});
                else{var data=new Dictionary<string,object>{{"access_token","fixture-access"},{"token_type","Bearer"},{"expires_in",3600},{"scope",BadScope?"https://www.googleapis.com/auth/gmail.modify":ConnectorCatalog.Capabilities[0].Scope}};if(!body.Contains("grant_type=refresh_token"))data["refresh_token"]="fixture-refresh";response=JsonSerializer.Serialize(data);}
            }else{response=notion?"{\"results\":[]}":"{\"messages\":[]}";if(ProbeDenied)status=HttpStatusCode.Forbidden;}
            return new(status){Content=new StringContent(response)};
        }
    }
}
