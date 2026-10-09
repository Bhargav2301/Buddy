#if HAS_CONTRACT
using Buddy.Server;
using Microsoft.AspNetCore.DataProtection;
using System.Net;
using System.Text;
using System.Text.Json;

internal static class ServiceCases
{
    internal static async Task Run(Action<bool, string> check)
    {
        var golden = MeaningGoldens.All.Single(g => g.Id == "reported-poem-exact");
        using (var f = new Fixture(golden.Original)) {
            var result = await f.Service.RefineDetailed(new(golden.Original), default);
            check(result.Accepted && !result.NoChange && result.Method == "source-structure" && result.Structure is not null && MeaningOracle.Judge(golden, result.RefinedPrompt).MeetsGolden,
                "production service accepts useful poem structure with independent meaning/utility proof");
            check(result.ScoreBefore == result.ScoreAfter && f.Model.Plans == 1 && f.Model.Assessments == 1 && f.Model.Embeddings == 1 && f.Model.Wording == 0,
                "structural usefulness does not depend on a higher self-assessment or a wording fallback");
            check(!result.Changes.Any(change => change.Contains("Removed lonely night", StringComparison.OrdinalIgnoreCase)),
                "hallucinated model change descriptions are not shown as verified structural changes");
        }
        foreach (string fault in new[] { "omit", "duplicate", "reorder", "extra-text" }) {
            using var f = new Fixture(golden.Original); f.Model.PlanFault = fault;
            var result = await f.Service.RefineDetailed(new(golden.Original), default);
            check(!result.Accepted && result.RefinedPrompt == golden.Original && result.Structure is null && f.Model.Plans == 1 && f.Model.Assessments == 0 && f.Model.Wording == 0,
                "malformed structured response cannot produce acceptance, assessment or wording replay: " + fault);
        }
        foreach (bool preserve in new[] { false, true }) {
            using var f = new Fixture(golden.Original); f.Model.Preserved = preserve; f.Model.Similar = !preserve;
            var result = await f.Service.RefineDetailed(new(golden.Original), default);
            check(!result.Accepted && result.RefinedPrompt == golden.Original && result.Structure is null && f.Model.Wording == 0,
                preserve ? "embedding veto still rejects a rendered structure" : "semantic preservation veto still rejects a rendered structure");
        }
        using (var f = new Fixture(golden.Original)) {
            using var stop = new CancellationTokenSource(); stop.Cancel();
            check(await Cancelled(() => f.Service.RefineDetailed(new(golden.Original), stop.Token)) && f.Model.Calls == 0, "pre-cancelled structure request makes zero model calls");
        }
        foreach (bool afterCandidate in new[] { false, true }) {
            using var f = new Fixture(golden.Original); using var stop = new CancellationTokenSource(); bool finalSeen = false, stopReached = false, canceled = false;
            try {
                await foreach (var item in f.Service.RefineStream(new(golden.Original), stop.Token)) {
                    if (!afterCandidate && item.Type == "stage" && item.Text == "Task structure" || afterCandidate && item.Type == "delta") { stopReached = true; stop.Cancel(); }
                    if (item.Result is not null) finalSeen = true;
                }
            } catch (OperationCanceledException) { canceled = true; }
            check(stopReached && canceled && !finalSeen && f.Model.Assessments == 0 && (afterCandidate ? f.Model.Plans == 1 : f.Model.Plans == 0),
                afterCandidate ? "Stop after rendered candidate prevents assessment and final acceptance" : "Stop at structure stage prevents inference");
        }
        using (var f = new Fixture(golden.Original)) {
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Model.BeforePlan = async _ => { entered.TrySetResult(); await release.Task; };
            using var stop = new CancellationTokenSource(); var pending = f.Service.RefineDetailed(new(golden.Original), stop.Token);
            try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); stop.Cancel(); release.TrySetResult();
                check(await Cancelled(async () => await pending) && f.Model.Assessments == 0, "late canceled structure response cannot become a final result"); }
            finally { release.TrySetResult(); }
            f.Model.BeforePlan = null;
            check((await f.Service.RefineDetailed(new(golden.Original), default)).Accepted, "canceled structure releases service inference owner");
            check(await f.Store.Read(s => s.Conversations.Count + s.Guides.Count + s.Jobs.Count + s.Knowledge.Count + s.Audit.Count) == 0, "structured refinement and cancellation persist no conversation/action completion");
        }
        using (var f = new Fixture("Write exactly 2 sentences and exactly 3 sentences.")) {
            var result = await f.Service.RefineDetailed(new(f.Model.Original), default);
            check(!result.Accepted && result.RefinedPrompt == f.Model.Original && result.Structure is null && f.Model.Calls == 0,
                "explicit same-output count contradiction is clarified before any model call");
        }
    }
    private static async Task<bool> Cancelled(Func<Task<RefinementResult>> action) { try { await action(); return false; } catch (OperationCanceledException) { return true; } }

    private sealed class Fixture : IDisposable
    {
        internal readonly Model Model;
        internal readonly StateStore Store;
        internal readonly BuddyService Service;
        private readonly string folder = Path.Combine(Path.GetTempPath(), "Buddy-meaning-boundary-" + Guid.NewGuid());
        private readonly HttpClient http;
        internal Fixture(string original) {
            Model = new(original); Store = new(folder, new EphemeralDataProtectionProvider());
            http = new(Model) { BaseAddress = new("http://127.0.0.1:11434/") };
            Service = new(Store, new(http)) { AgentEnabled = false, WebEnabled = false };
        }
        public void Dispose() { http.Dispose(); SourceReceipt.DeleteOwned(folder, "Buddy-meaning-boundary-"); }
    }
    private sealed class Model(string original) : HttpMessageHandler
    {
        internal readonly string Original = original;
        internal int Plans, Assessments, Embeddings, Wording, Calls;
        internal bool Preserved = true, Similar = true;
        internal string PlanFault = "";
        internal Func<CancellationToken, Task>? BeforePlan;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); var data = document.RootElement;
            if (request.RequestUri!.AbsolutePath == "/api/embed") {
                Embeddings++; return Json(new { embeddings = Enumerable.Range(0, data.GetProperty("input").GetArrayLength()).Select(i => Similar || i == 0 ? new[] { 1f, 0f } : new[] { 0f, 1f }) });
            }
            if (request.RequestUri.AbsolutePath != "/api/chat") throw new InvalidOperationException("Unexpected fake model endpoint.");
            if (data.GetProperty("stream").GetBoolean()) { Wording++; return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { message = new { content = Original }, done = true }) + "\n", Encoding.UTF8, "application/x-ndjson") }; }
            if (data.GetProperty("format").GetProperty("properties").TryGetProperty("sections", out _)) {
                Plans++; if (BeforePlan is not null) await BeforePlan(ct);
                var plan = ContractCases.Cover(RefinementContract.Analyze(Original));
                var sections = plan.Sections.ToList();
                if (PlanFault == "omit") sections.RemoveAt(sections.Count - 1);
                if (PlanFault == "duplicate") sections.Add(sections[0]);
                if (PlanFault == "reorder") sections.Reverse();
                string payload = PlanFault == "extra-text" ? "{\"sections\":[],\"text\":\"Invent a new requirement\"}" : JsonSerializer.Serialize(new RefinementContractPlan(sections), StateStore.Json);
                return Json(new { message = new { content = payload }, done = true });
            }
            Assessments++; return Json(new { message = new { content = JsonSerializer.Serialize(new { preserved = Preserved, scoreBefore = 85, scoreAfter = 85, changes = new[] { "Removed lonely night" } }) }, done = true });
        }
        private static HttpResponseMessage Json(object data) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(data), Encoding.UTF8, "application/json") };
    }
}
#endif
