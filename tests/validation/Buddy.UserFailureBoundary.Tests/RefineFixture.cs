using Buddy.Server;
using Microsoft.AspNetCore.DataProtection;
using System.Net;
using System.Text;
using System.Text.Json;

internal sealed class RefineFixture : IDisposable
{
    // This fixture isolates the conservative wording fallback; task/subject
    // restructuring of the reported poem is covered by the meaning suites.
    internal const string Original = "Explain a boat sailing in a sea on a lonely night";
    internal const string Faithful = "Please explain a boat sailing in a sea on a lonely night.";
    internal readonly RefineModel Model = new();
    internal readonly StateStore Store;
    internal readonly BuddyService Service;
    private readonly string folder = Path.Combine(Path.GetTempPath(), "Buddy-userfailure-boundary-" + Guid.NewGuid());
    private readonly HttpClient http;
    internal RefineFixture()
    {
        Require.True(!RefinementContract.Analyze(Original).CanStructure, "Legacy wording fixture must not use task structure.");
        Store = new(folder, new EphemeralDataProtectionProvider());
        http = new(Model) { BaseAddress = new("http://127.0.0.1:11434/") };
        Service = new(Store, new(http)) { AgentEnabled = false, WebEnabled = false };
    }
    public void Dispose() { http.Dispose(); SourceReceipt.DeleteOwned(folder, "Buddy-userfailure-boundary-"); }
}
internal sealed class RefineModel : HttpMessageHandler
{
    internal readonly Queue<string> Candidates = [];
    internal readonly List<JsonElement> Payloads = [];
    internal Func<int, CancellationToken, Task>? BeforeChat;
    internal int Chats, Assessments, Embeddings;
    internal int ScoreBefore = 70, ScoreAfter = 80;
    internal bool AssessmentAllows = true, Similar = true;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var data = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone(); Payloads.Add(data);
        if (request.RequestUri!.AbsolutePath == "/api/embed") { Embeddings++; return Json(new { embeddings = Enumerable.Range(0, data.GetProperty("input").GetArrayLength()).Select(i => Similar || i == 0 ? new[] { 1f, 0f } : new[] { 0f, 1f }) }); }
        if (request.RequestUri.AbsolutePath != "/api/chat") throw new InvalidOperationException("Unexpected mock route.");
        if (!data.GetProperty("stream").GetBoolean()) {
            if (data.GetProperty("format").GetProperty("properties").TryGetProperty("sections", out _))
                throw new InvalidOperationException("Legacy wording fixture unexpectedly entered the task-structure route.");
            Assessments++; return Json(new { message = new { content = JsonSerializer.Serialize(new { preserved = AssessmentAllows, scoreBefore = ScoreBefore, scoreAfter = ScoreAfter, changes = new[] { "Polished wording" } }) }, done = true });
        }
        int call = ++Chats;
        if (BeforeChat is not null) await BeforeChat(call, ct);
        if (!Candidates.TryDequeue(out string? candidate)) throw new InvalidOperationException("Exceeded the explicitly supplied mock candidate allowance.");
        return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { message = new { content = candidate }, done = true }) + "\n", Encoding.UTF8, "application/x-ndjson") };
    }
    private static HttpResponseMessage Json(object data) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json") };
}
