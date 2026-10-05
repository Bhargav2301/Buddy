using Buddy.Server;
using Microsoft.AspNetCore.DataProtection;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;

int checks = 0;
void Check(bool value, string label) { if (!value) throw new Exception("FAIL: " + label); checks++; Console.WriteLine("PASS: " + label); }
TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
async Task<string> Failure(Func<Task> call) { try { await call().WaitAsync(TimeSpan.FromSeconds(4)); return "none"; } catch (BuddyException e) { return e.Code; } catch (OperationCanceledException) { return "cancelled"; } }
async Task Settled(Fixture f) {
    var field = typeof(BuddyService).GetField("reviewedResearchRunning", BindingFlags.Instance | BindingFlags.NonPublic)!;
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(3));
    while ((int)field.GetValue(f.Service)! != 0) await Task.Delay(5, deadline.Token);
}
SemaphoreSlim Inference(Fixture f) => (SemaphoreSlim)typeof(BuddyService).GetField("inference", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(f.Service)!;

var api = typeof(BuddyService).GetMethod(nameof(BuddyService.ResearchReviewedRegion))!;
Check(api.GetParameters().Select(p => p.ParameterType).SequenceEqual([typeof(string), typeof(CancellationToken)]), "API accepts only explicit reviewed text and cancellation, no screen/image/audio/context argument");
Check(api.ReturnType == typeof(Task<ReviewedRegionResearchResult>), "API returns only complete speech and host-owned attached sources");
using (var f = new Fixture()) {
    f.Service.WebEnabled = false;
    Check(await Failure(() => f.Run()) == "WEB_DISABLED" && f.Web.Searches.Count == 0 && f.Model.Bodies.Count == 0, "Disabled web policy blocks before search or inference");
}
foreach (string query in new[] { "", " ", new string('q', 301), "two\nlines", "a\tb", "a\0b", "a\u202Eb", "a\u2028b", "a\u200Bb", "\uD800", " outer space", "outer space ", "password=fixture" }) {
    using var f = new Fixture();
    Check((await Failure(() => f.Service.ResearchReviewedRegion(query, default))) is "INVALID_RESEARCH_QUERY" or "RESEARCH_REVIEW_REQUIRED", "Invalid/nonexact query is refused before transmission");
    Check(f.Web.Searches.Count == 0 && f.Web.Fetches.Count == 0 && f.Model.Bodies.Count == 0, "Rejected query has no network/model side effect");
}
using (var f = new Fixture()) {
    using var stop = new CancellationTokenSource(); stop.Cancel();
    Check(await Failure(() => f.Service.ResearchReviewedRegion("rainbows", stop.Token)) == "cancelled" && f.Web.Searches.Count == 0, "Pre-cancelled reviewed search does not transmit");
}
using (var transport = new WebTransport())
using (var web = new WebResearch(transport))
using (var f = new Fixture(webOverride: web)) {
    const string exact = "café  water droplets 🌈";
    var result = await f.Service.ResearchReviewedRegion(exact, default);
    Check(transport.Requests.Count == 2 && Uri.UnescapeDataString(transport.Requests[0].Query[3..]) == exact,
        "Actual WebResearch search construction preserves the reviewed UTF-8 query through percent encoding (mock HTTP transport)");
    Check(transport.Requests.All(u => u.Scheme == "https") && transport.NoBodies && result.Sources.Single().Url == "https://pages.example.org/read",
        "Existing public HTTPS text transport receives no image/audio/context body and attaches only the fetched page");
}
using (var f = new Fixture()) {
    var query = new string('q', 300); await f.Service.ResearchReviewedRegion(query, default);
    Check(f.Web.Searches.Single() == query, "A complete 300-character reviewed query is not clipped");
}
using (var f = new Fixture()) {
    const string exact = "café  water droplets 🌈";
    await f.SeedPrivateFixture(); var before = f.StoredBytes();
    var result = await f.Service.ResearchReviewedRegion(exact, default);
    Check(f.Web.Searches.SequenceEqual([exact]) && Encoding.UTF8.GetBytes(f.Web.Searches.Single()).SequenceEqual(Encoding.UTF8.GetBytes(exact)), "Exact accepted Unicode/case/inner whitespace bytes are searched once without model rewriting");
    Check(f.Web.Fetches.Count == 1 && result.Sources.Single().Url == "https://pages.example.org/read" && result.Sources.Single().Title == "Fetched page", "Sources come from actually fetched validated final pages, not search snippets");
    Check(result.Speech == "The fetched page explains how light bends in water droplets." && ConversationalReply.IsConcise(result.Speech), "Local model summary returns complete concise speech");
    var payload = f.Model.Bodies.Single();
    Check(!payload.Contains("PRIVATE_MEMORY") && !payload.Contains("PRIVATE_SCREEN") && !payload.Contains("PRIVATE_HISTORY") && !payload.Contains("images") && !payload.Contains("invented snippet"), "Model gets no saved/private screen/history/memory, image field or search snippet");
    using var body = JsonDocument.Parse(payload);
    using var input = JsonDocument.Parse(body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
    Check(input.RootElement.GetProperty("exactReviewedQuery").GetString() == exact && input.RootElement.GetProperty("untrustedFetchedExcerpts")[0].GetProperty("textExcerpt").GetString() == "Water droplets bend light.", "Local model input contains exact reviewed query and actual fetched excerpt");
    Check(f.Model.Addresses.All(a => a == "http://127.0.0.1:11434/api/chat"), "Summary uses the local engine directly, not a cloud provider route");
    Check(before.SequenceEqual(f.StoredBytes()), "Research does not save a query, answer, sources or private log to the owned state store");
}
using (var f = new Fixture()) {
    f.Web.Results = [
        new("bad", "http://example.org", "snippet"), new("private", "https://127.0.0.1/a", "snippet"),
        new("one", "https://pages.example.org/one", "snippet"), new("duplicate", "https://pages.example.org/one#fragment", "snippet"),
        new("two", "https://pages.example.org/two", "snippet"), new("three", "https://pages.example.org/three", "snippet"), new("four", "https://pages.example.org/four", "snippet")
    ];
    f.Web.Fetch = (url, ct) => Task.FromResult(new WebSource("Fetched", url, "Water bends light.", Links: [new("Do not follow", "https://pages.example.org/unrequested")]));
    var result = await f.Run();
    Check(f.Web.Fetches.SequenceEqual(new[] { "https://pages.example.org/one", "https://pages.example.org/two", "https://pages.example.org/three" }) && result.Sources.Count == 3, "At most three unique public HTTPS candidates fetched; private/insecure/duplicate candidates and extra links do not expand the request");
    Check(f.Web.Searches.Count == 1 && f.Model.Bodies.Count == 1, "Fetches and local summary do not trigger an expanded search query or tool loop");
}
using (var f = new Fixture()) {
    f.Web.Results = Enumerable.Range(0, 8).Select(i => new WebSource("candidate", "https://pages.example.org/" + i, "snippet")).ToArray();
    f.Web.Fetch = (url, ct) => throw new HttpRequestException("fixture unreadable");
    var result = await f.Run();
    Check(f.Web.Fetches.Count == 3 && f.Model.Bodies.Count == 0 && result.Sources.Count == 0 && result.Speech.Contains("couldn't read"), "Failed fetch attempts still count toward the three-page cap; missing evidence asks for a better query");
}
foreach (var page in new[] { new WebSource("bad final", "https://127.0.0.1/private", "text"), new WebSource("empty", "https://pages.example.org/empty", " "), new WebSource("oversize", "https://pages.example.org/big", new string('x', 16001)) }) {
    using var f = new Fixture(); f.Web.Fetch = (url, ct) => Task.FromResult(page);
    var result = await f.Run();
    Check(result.Sources.Count == 0 && f.Model.Bodies.Count == 0, "Invalid final source or unusable content is never evidence");
}
using (var f = new Fixture()) {
    f.Web.Fetch = (url, ct) => Task.FromResult(new WebSource("long", url, new string('x', 3999) + "🌈" + new string('y', 6000)));
    await f.Run(); using var body = JsonDocument.Parse(f.Model.Bodies.Single());
    using var input = JsonDocument.Parse(body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
    var excerpt = input.RootElement.GetProperty("untrustedFetchedExcerpts")[0];
    Check(excerpt.GetProperty("textExcerpt").GetString()!.Length == 3999 && excerpt.GetProperty("excerptOnly").GetBoolean(), "Fetched evidence is explicitly bounded and labelled as an excerpt without splitting a Unicode pair");
    Check(body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!.Contains("omit context or later qualifications"), "Prompt discloses excerpt omissions instead of claiming full-page verification");
}
using (var f = new Fixture()) {
    const string invalid = "Read the page. It explains light. It mentions water. Do not infer current screen state.";
    f.Model.Replies.Enqueue(ct => Task.FromResult(Model.Response(invalid)));
    f.Model.Replies.Enqueue(ct => Task.FromResult(Model.Response("The page explains light in water; it does not establish current screen state.")));
    var result = await f.Run();
    Check(f.Model.Bodies.Count == 2 && result.Speech.Contains("does not establish current screen state"), "Overlong answer is recomposed once with its qualification retained");
    using var body = JsonDocument.Parse(f.Model.Bodies[1]); using var input = JsonDocument.Parse(body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
    Check(input.RootElement.GetProperty("priorInvalidSummary").GetString() == invalid, "Repair receives the complete bounded invalid draft rather than truncating later cautions");
}
foreach (var invalid in new[] { "See HTTPS://example.org for a claim.", "See example.design for a claim.", "One. Two. Three. Four.", "Sources are attached.", "[Source: fabricated] Light bends." }) {
    using var f = new Fixture(); f.Model.Replies.Enqueue(ct => Task.FromResult(Model.Response(invalid))); f.Model.Replies.Enqueue(ct => Task.FromResult(Model.Response(invalid)));
    var result = await f.Run();
    Check(f.Model.Bodies.Count == 2 && result.Speech.Contains("couldn't compose") && ConversationalReply.IsConcise(result.Speech) && !result.Speech.Contains("example"), "Repeated invalid/address-bearing output returns a short honest clarification, never raw URL speech");
}
using (var f = new Fixture()) {
    f.Model.Replies.Enqueue(ct => Task.FromResult(Model.Response("The page does not answer this.", supported: false)));
    var result = await f.Run();
    Check(result.Speech.Contains("do not establish") && result.Sources.Count == 1 && f.Model.Bodies.Count == 1, "Insufficient model evidence returns a clarification with only actually fetched sources attached");
}
using (var f = new Fixture()) {
    f.Model.Replies.Enqueue(ct => Task.FromResult(Model.Malformed())); f.Model.Replies.Enqueue(ct => Task.FromResult(Model.Malformed()));
    Check((await f.Run()).Speech.Contains("couldn't compose") && f.Model.Bodies.Count == 2, "Malformed structured response gets one bounded retry then a clarification");
}
using (var f = new Fixture()) {
    f.Web.Search = (query, ct) => { f.Service.WebEnabled = false; return Task.FromResult(f.Web.Results); };
    Check(await Failure(() => f.Run()) == "WEB_DISABLED" && f.Web.Fetches.Count == 0 && f.Model.Bodies.Count == 0, "Disabling web during search stops follow-up fetch and inference");
}
using (var f = new Fixture()) {
    f.Web.Fetch = (url, ct) => { f.Service.WebEnabled = false; return Task.FromResult(new WebSource("page", url, "text")); };
    Check(await Failure(() => f.Run()) == "WEB_DISABLED" && f.Model.Bodies.Count == 0, "Disabling web during fetch prevents summary publication");
}
using (var f = new Fixture()) {
    var started = Signal(); var release = new TaskCompletionSource<WebSource>(TaskCreationOptions.RunContinuationsAsynchronously);
    f.Web.Fetch = (url, ct) => { started.TrySetResult(); return release.Task; };
    using var stop = new CancellationTokenSource(); var request = f.Service.ResearchReviewedRegion("rainbows", stop.Token);
    await started.Task.WaitAsync(TimeSpan.FromSeconds(4)); stop.Cancel();
    Check(await Failure(() => request) == "cancelled" && await Failure(() => f.Run()) == "RESEARCH_BUSY", "Stop during uncooperative fetch returns promptly but retains request ownership");
    release.TrySetResult(new("late page", "https://pages.example.org/read", "Late evidence must not reach the model.")); await Settled(f);
    Check(f.Model.Bodies.Count == 0 && f.Web.Fetches.Count == 1, "Late fetched content cannot start inference or additional fetches after Stop");
}
using (var f = new Fixture()) {
    var started = Signal(); var release = new TaskCompletionSource<IReadOnlyList<WebSource>>(TaskCreationOptions.RunContinuationsAsynchronously);
    f.Web.Search = (query, ct) => { started.TrySetResult(); return release.Task; };
    using var stop = new CancellationTokenSource(); var request = f.Service.ResearchReviewedRegion("rainbows", stop.Token);
    await started.Task.WaitAsync(TimeSpan.FromSeconds(4)); stop.Cancel();
    Check(await Failure(() => request) == "cancelled", "Stop returns promptly even when a search ignores cancellation");
    Check(await Failure(() => f.Run()) == "RESEARCH_BUSY" && f.Web.Searches.Count == 1, "Cancelled pending search retains overlap ownership, blocking repeated outbound requests");
    release.TrySetResult(f.Web.Results); await Settled(f);
    Check(f.Web.Fetches.Count == 0 && f.Model.Bodies.Count == 0, "Late search results after Stop cannot start fetch or local model");
}
using (var f = new Fixture(new(TimeSpan.FromMilliseconds(40), TimeSpan.FromSeconds(2)))) {
    var gate = Inference(f); await gate.WaitAsync();
    try { Check(await Failure(() => f.Run()) == "RESEARCH_BUSY" && f.Model.Bodies.Count == 0 && gate.CurrentCount == 0, "Inference queue is bounded and cannot release another request's lease"); }
    finally { gate.Release(); }
}
foreach (bool timeout in new[] { false, true }) {
    using var f = new Fixture(timeout ? new(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(500)) : null);
    var started = Signal(); var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
    f.Model.Replies.Enqueue(ct => { started.TrySetResult(); return release.Task; });
    using var stop = new CancellationTokenSource(); var request = f.Service.ResearchReviewedRegion("rainbows", stop.Token);
    await started.Task.WaitAsync(TimeSpan.FromSeconds(4)); if (!timeout) f.Service.StopAll();
    Check(await Failure(() => request) == (timeout ? "RESEARCH_TIMEOUT" : "cancelled"), "StopAll/total deadline returns promptly during cancellation-ignoring local inference");
    Check(Inference(f).CurrentCount == 0 && await Failure(() => f.Run()) == "RESEARCH_BUSY", "Cancelled local model retains global inference lease and per-service research slot until settlement");
    release.TrySetResult(Model.Response("A late answer must never be published.")); await Settled(f);
    Check(Inference(f).CurrentCount == 1 && f.Model.Bodies.Count == 1, "Late local response is discarded, lease released only after settlement, with no retry");
}
Console.WriteLine($"REVIEWED REGION RESEARCH CHECKS PASSED: {checks}");

sealed class Fixture : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "Buddy-region-research-" + Guid.NewGuid());
    private readonly HttpClient http;
    public Web Web { get; } = new();
    public Model Model { get; } = new();
    public BuddyService Service { get; }
    public StateStore Store { get; }
    public Fixture(ReviewedRegionResearchLimits? limits = null, IWebResearch? webOverride = null) {
        Store = new(folder, new EphemeralDataProtectionProvider());
        http = new(Model) { BaseAddress = new("http://127.0.0.1:11434") };
        Service = new(Store, new(http), webOverride ?? Web) { WebEnabled = true, RegionResearchLimits = limits ?? ReviewedRegionResearchLimits.Default };
    }
    public Task<ReviewedRegionResearchResult> Run() => Service.ResearchReviewedRegion("rainbows", default);
    public Task SeedPrivateFixture() => Store.Update(s => {
        s.Memories.Add(new("fixture", "memory", "PRIVATE_MEMORY"));
        s.Conversations.Add(new("fixture", "PRIVATE_SCREEN", DateTimeOffset.UtcNow, [new("fixture", "user", "PRIVATE_HISTORY", DateTimeOffset.UtcNow)])); return true;
    });
    public byte[] StoredBytes() => File.ReadAllBytes(Path.Combine(folder, "buddy.v1.encrypted"));
    public void Dispose() {
        Service.StopAll(); http.Dispose();
        var resolved = Path.GetFullPath(folder);
        if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("Buddy-region-research-", StringComparison.Ordinal) || (File.GetAttributes(resolved) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Unsafe owned fixture cleanup.");
        Directory.Delete(resolved, true);
    }
}
sealed class WebTransport : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];
    public bool NoBodies { get; private set; } = true;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
        ct.ThrowIfCancellationRequested(); Requests.Add(request.RequestUri!); NoBodies &= request.Content is null;
        string html = Requests.Count == 1
            ? "<html><body><a class='result__a' href='https://pages.example.org/read'>Search candidate</a></body></html>"
            : "<html><head><title>Fetched page</title></head><body><main>Water droplets bend light.</main></body></html>";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, "text/html") });
    }
}
sealed class Web : IWebResearch
{
    public List<string> Searches { get; } = [];
    public List<string> Fetches { get; } = [];
    public IReadOnlyList<WebSource> Results = [new("invented snippet title", "https://pages.example.org/read", "invented snippet text", "search snippet")];
    public Func<string, CancellationToken, Task<IReadOnlyList<WebSource>>>? Search;
    public Func<string, CancellationToken, Task<WebSource>>? Fetch;
    Task<IReadOnlyList<WebSource>> IWebResearch.Search(string query, CancellationToken ct) { Searches.Add(query); return Search?.Invoke(query, ct) ?? Task.FromResult(Results); }
    Task<WebSource> IWebResearch.Fetch(string url, CancellationToken ct) { Fetches.Add(url); return Fetch?.Invoke(url, ct) ?? Task.FromResult(new WebSource("Fetched page", url, "Water droplets bend light.")); }
}
sealed class Model : HttpMessageHandler
{
    public List<string> Bodies { get; } = [];
    public List<string> Addresses { get; } = [];
    public Queue<Func<CancellationToken, Task<HttpResponseMessage>>> Replies { get; } = new();
    public static HttpResponseMessage Response(string speech, bool supported = true) => Raw(JsonSerializer.Serialize(new { speech, supported }));
    public static HttpResponseMessage Malformed() => Raw("not JSON");
    private static HttpResponseMessage Raw(string text) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { message = new { content = text }, done = true, done_reason = "stop" }), Encoding.UTF8, "application/json") };
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
        Bodies.Add(await request.Content!.ReadAsStringAsync(ct)); Addresses.Add(request.RequestUri!.AbsoluteUri);
        return Replies.Count > 0 ? await Replies.Dequeue()(ct) : Response("The fetched page explains how light bends in water droplets.");
    }
}
