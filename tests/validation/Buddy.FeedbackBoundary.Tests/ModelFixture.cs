using Buddy.Server;
using Microsoft.AspNetCore.DataProtection;
using System.Net;
using System.Text;
using System.Text.Json;

internal sealed class ModelFixture : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "Buddy-feedback-boundary-" + Guid.NewGuid());
    private readonly HttpClient http;
    internal readonly FakeModel Model = new();
    internal readonly StateStore Store;
    internal readonly BuddyService Service;
    internal ModelFixture()
    {
        Store = new(folder, new EphemeralDataProtectionProvider());
        http = new(Model) { BaseAddress = new("http://127.0.0.1:11434/") };
        Service = new(Store, new(http)) { AgentEnabled = true, WebEnabled = false };
    }
    internal Task<int> Saved() => Store.Read(s => s.Conversations.Count + s.Guides.Count + s.Jobs.Count + s.Audit.Count);
    public void Dispose() { http.Dispose(); SourceReceipt.DeleteOwned(folder,"Buddy-feedback-boundary-"); }
}

internal sealed class FakeModel : HttpMessageHandler
{
    internal int Calls;
    internal Func<int, object> Reply = _ => new AssistantPlan("Which app?", []);
    internal Func<CancellationToken, Task>? BeforeReply;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri!.AbsolutePath != "/api/chat") throw new InvalidOperationException("Unexpected mock endpoint; no network handler exists.");
        int call = Interlocked.Increment(ref Calls);
        if (BeforeReply is not null) await BeforeReply(ct);
        string text = JsonSerializer.Serialize(Reply(call), StateStore.Json);
        return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { message = new {content=text}, done=true, done_reason="stop" }), Encoding.UTF8,"application/json") };
    }
}
