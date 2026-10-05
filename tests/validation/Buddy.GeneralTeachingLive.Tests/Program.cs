using Buddy.Server;
using Microsoft.AspNetCore.DataProtection;
using System.Diagnostics;
using System.Net;
using System.Text.Json;

SourceReceipt.Verify("TEACH-50 canned model-generated teaching; authored browser route explicitly disabled. Root-only live mode. No Windows APIs, service listener, installed profile, images, web research or execution.");
string Option(string prefix, string fallback) => args.FirstOrDefault(a => a.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..] ?? fallback;
var selected = Option("--case=", "recorded-reload");
var allCases = Fixtures.All.Concat(Buddy.IndependentTeaching.TeachingFixtures.All.Where(c => c.HeldOut)
    .Select(c => new TeachingCase(c.Id, c.Provenance + " Independent held-out question.", c.Query, c.Context))).ToArray();
var cases = selected == "all" ? allCases : allCases.Where(c => c.Id == selected).ToArray();
if (cases.Length == 0) throw new ArgumentException("Unknown canned case.");
string route = Option("--route=", "guide");
if (route is not ("guide" or "teach")) throw new ArgumentException("Use --route=guide or --route=teach.");
bool live = args.Contains("--live", StringComparer.Ordinal);
if (!live) {
    foreach (var item in cases) Console.WriteLine(JsonSerializer.Serialize(new { kind = "canned_fixture", route, item.Id, item.Provenance, item.Query, item.Context, live = false }, StateStore.Json));
    Console.WriteLine("DRY RUN: no model client created. Root may opt in with --live --model=gemma3:4b; --case=all includes independent held-out cases. This executable logs evidence and makes no usefulness-pass claim.");
    return;
}
string model = Option("--model=", "");
if (model != "gemma3:4b") throw new ArgumentException("Live mode requires explicit existing --model=gemma3:4b. This harness never installs or pulls a model.");
var folder = Path.Combine(Path.GetTempPath(), "Buddy-GeneralTeaching-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
int failed = 0;
try {
    var store = new StateStore(folder, new EphemeralDataProtectionProvider());
    await store.Update(s => { s.Model = model; return true; });
    using var trace = new ModelTrace(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
    using var client = new HttpClient(trace) { BaseAddress = new("http://127.0.0.1:11434/"), Timeout = TimeSpan.FromMinutes(3) };
    var service = new BuddyService(store, new(client)) { AgentEnabled = false, WebEnabled = false };
    foreach (var item in cases) {
        int before = trace.Calls; var elapsed = Stopwatch.StartNew();
        using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        GuidePlan? plan = null; string? error = null;
        try {
            if (route == "guide") plan = await service.PlanGuide(new(item.Query, item.Context), stop.Token, browserChromeVerified: false);
            else { var turn = await service.Teach(new(item.Query, item.Context), stop.Token); plan = new(turn.Speech, turn.Targets.ToList(), Lessons: []); }
        } catch (Exception ex) { error = ex is BuddyException b ? b.Code : ex.GetType().Name; failed++; }
        bool noSavedContent = await store.Read(s => s.Guides.Count + s.Conversations.Count + s.Jobs.Count + s.Knowledge.Count + s.Audit.Count == 0);
        Console.WriteLine(JsonSerializer.Serialize(new { kind = "teaching_result", caseId = item.Id, provenance = item.Provenance, query = item.Query, context = item.Context, route, model, browserChromeVerified = false, plan, error, modelCalls = trace.Calls - before, elapsedMs = elapsed.ElapsedMilliseconds, noSavedContent, liveGrounding = false, performedActions = false, usefulnessRequiresIndependentReview = true }, StateStore.Json));
        if (!noSavedContent) throw new InvalidOperationException("Unexpected stored user content.");
    }
} finally { SourceReceipt.DeleteOwned(folder, "Buddy-GeneralTeaching-"); }
Environment.ExitCode = failed == 0 ? 0 : 1;

internal sealed class ModelTrace(HttpMessageHandler inner) : DelegatingHandler(inner)
{
    internal int Calls;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri is not { Scheme: "http", Host: "127.0.0.1", Port: 11434, AbsolutePath: "/api/chat" } || request.Method != HttpMethod.Post || ++Calls > 30)
            throw new InvalidOperationException("Only bounded requests to the existing local model are permitted.");
        string input = await request.Content!.ReadAsStringAsync(ct);
        if (input.Length > 100000) throw new InvalidOperationException("Unexpected input size.");
        using var inputJson = JsonDocument.Parse(input);
        if (inputJson.RootElement.GetProperty("model").GetString() != "gemma3:4b") throw new InvalidOperationException("Unexpected model.");
        var elapsed = Stopwatch.StartNew();
        var response = await base.SendAsync(request, ct);
        try {
            await response.Content.LoadIntoBufferAsync(1_000_000);
            var raw = await response.Content.ReadAsStringAsync(ct);
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "canned_model_trace", call = Calls, status = (int)response.StatusCode, elapsedMs = elapsed.ElapsedMilliseconds, request = input[..Math.Min(input.Length, 24000)], requestTruncated = input.Length > 24000, response = raw[..Math.Min(raw.Length, 24000)], responseTruncated = raw.Length > 24000, source = "canned-replay-or-synthetic-only" }, StateStore.Json));
            return response;
        } catch { response.Dispose(); throw; }
    }
}
