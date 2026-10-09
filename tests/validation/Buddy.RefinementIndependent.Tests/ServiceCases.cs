using Buddy.Server;
using Microsoft.AspNetCore.DataProtection;
using System.Net;
using System.Text;
using System.Text.Json;

internal static partial class Program
{
    private const string Scene = "Write a poem about a lighthouse in winter.";
    private const string Plain = "Summarize this.";
    private static async Task ServiceCases()
    {
        await Case("empty/whitespace rejects before inference", async () => {
            using var f = new Fixture();
            foreach (var text in new[] { "", " \r\n\t" }) await Throws<BuddyException>(() => f.Service.RefineDetailed(new(text), default));
            Check(f.Model.Calls == 0, "empty input dispatched model work");
        });
        await Case("maximum/input-context refusals do not truncate", async () => {
            using var f = new Fixture(); string max = new('x', 20000);
            var r = await f.Service.RefineDetailed(new(max), default);
            Check(!r.Accepted && r.RefinedPrompt == max && f.Model.Calls == 0, "context overflow not preserved intact");
            await Throws<BuddyException>(() => f.Service.RefineDetailed(new(max + "x"), default));
        });
        await Case("required destination overflow refuses before model", async () => {
            using var f = new Fixture();
            var r = await f.Service.RefineDetailed(new(Scene, Budget: new("synthetic field", 4, "utf8-bytes")), default);
            Check(!r.Accepted && r.RefinedPrompt == Scene && r.DestinationBudget is { Fits: false } && f.Model.Calls == 0,
                "required destination overflow dispatched model or changed source");
        });
        await Case("missing requested technique prerequisites are not invented", async () => {
            using var f = new Fixture();
            var r = await f.Service.RefineDetailed(new(Scene, Technique: "few-shot"), default);
            Check(!r.Accepted && r.RefinedPrompt == Scene && f.Model.Calls == 0 && r.Warnings.Count > 0,
                "few-shot request without examples manufactured prerequisite input");
        });
        await Case("echo remains truthful no-change after bounded retry", async () => {
            using var f = new Fixture(); f.Model.Wording = Plain;
            var r = await f.Service.RefineDetailed(new(Plain), default);
            Check(!r.Accepted && r.NoChange && r.RefinedPrompt == Plain, "echo accepted or original changed");
            Check(r.ScoreBefore is null && r.ScoreAfter is null && r.Similarity is null && r.Changes.Count == 0, "echo carries misleading quality claim");
            Check(f.Model.Chats == 2 && f.Model.Assessments == 0 && f.Model.Embeddings == 0, "echo exceeded one retry or assessed unchanged output");
        });
        await Case("empty streamed candidate cannot become usable result", async () => {
            using var f = new Fixture(); f.Model.Wording = " \n";
            using var ui = new Buddy.Windows.RefinementRequest();
            var r = await ui.Run(ct => f.Service.RefineStream(new(Plain), ct));
            Check(r.Result is null && !ui.IsRunning && r.State == Buddy.Windows.RefinementRequestState.Failed, "empty output left active/accepted state");
            Check(f.Model.Assessments == 0, "empty output reached quality assessment");
        });
        foreach (var pair in new[] { ("guided", 5), ("council", 7) }) {
            await Case("bounded repeated-pass echo in " + pair.Item1, async () => {
                using var f = new Fixture(); f.Model.Wording = Plain;
                var r = await f.Service.RefineDetailed(new(Plain, Mode: pair.Item1), default);
                Check(!r.Accepted && r.NoChange && r.RefinedPrompt == Plain && f.Model.Chats == pair.Item2 && f.Model.Assessments == 0,
                    "multi-pass echo exceeded role/synthesis/one-retry bound or invented improvement");
            });
        }
        await Case("guided Stop before synthesis makes no later inference", async () => {
            using var f = new Fixture(); f.Model.Wording = Plain; using var stop = new CancellationTokenSource(); bool reached = false;
            await Throws<OperationCanceledException>(async () => {
                await foreach (var e in f.Service.RefineStream(new(Plain, Mode: "guided"), stop.Token)) {
                    if (e.Text == "Synthesis") { reached = true; stop.Cancel(); }
                    Check(e.Result is null, "Stop before synthesis still published a result");
                }
            });
            Check(reached && f.Model.Chats == 3 && f.Model.Assessments == 0, "cancelled synthesis dispatched another model call");
        });
        foreach (var fault in new[] { "bad-json", "length", "omit-span", "duplicate-span", "wrong-kind" }) {
            await Case("structured refusal " + fault, async () => {
                using var f = new Fixture(); f.Model.PlanFault = fault;
                var r = await f.Service.RefineDetailed(new(Scene), default);
                Check(!r.Accepted && r.RefinedPrompt == Scene && r.Structure is null, "invalid structure changed original or gained certificate");
                Check(f.Model.Assessments == 0 && f.Model.Embeddings == 0, "invalid plan reached later gates");
            });
        }
        await Case("high-scoring invented requirement rejected before assessment", async () => {
            using var f = new Fixture(); f.Model.Wording = "Summarize this in five bullets by Friday.";
            var r = await f.Service.RefineDetailed(new(Plain), default);
            Check(!r.Accepted && r.RefinedPrompt == Plain && f.Model.Assessments == 0, "new hard requirements passed intent gate");
        });
        foreach (var failure in new[] { "assessment-false", "assessment-http", "embedding-http", "low-similarity" }) {
            await Case("valid structure remains subject to " + failure, async () => {
                using var f = new Fixture(); f.Model.ValidationFault = failure;
                var r = await f.Service.RefineDetailed(new(Scene), default);
                Check(!r.Accepted && r.RefinedPrompt == Scene && r.Structure is null, "validation failure accepted structure");
            });
        }
        await Case("successful then unrelated repeated turns remain independent", async () => {
            using var f = new Fixture();
            var a = await f.Service.RefineDetailed(new(Scene), default);
            var b = await f.Service.RefineDetailed(new("Write a report about battery life."), default);
            Check(a.Accepted && b.Accepted, "valid injected source-structure fixtures not accepted");
            Check(a.RefinedPrompt.Contains("lighthouse") && !b.RefinedPrompt.Contains("lighthouse") && b.RefinedPrompt.Contains("battery life"), "prior request leaked into later candidate");
            Check(f.Model.Plans == 2 && f.Model.Assessments == 2 && f.Model.Embeddings == 2, "turn reused stale inference/result");
            Check(await f.Store.Read(s => s.Conversations.Count + s.Knowledge.Count + s.Guides.Count + s.Jobs.Count + s.Audit.Count) == 0, "refinement silently persisted content");
        });
        await Case("two services use distinct request state", async () => {
            using var a = new Fixture(); using var b = new Fixture();
            a.Model.Wording = Plain; b.Model.Wording = "Summarize this in ten paragraphs.";
            var results = await Task.WhenAll(a.Service.RefineDetailed(new(Plain), default), b.Service.RefineDetailed(new(Plain), default));
            Check(results[0].NoChange && !results[1].NoChange && results.All(r => !r.Accepted && r.RefinedPrompt == Plain), "service outcome/state leaked across instances");
        });
        await Case("Stop ignored by model retains slot until settlement", async () => {
            using var f = new Fixture(new(TimeSpan.FromMilliseconds(80), TimeSpan.FromSeconds(4)));
            f.Model.HoldPlan = true; using var stop = new CancellationTokenSource();
            var first = f.Service.RefineDetailed(new(Scene), stop.Token);
            await f.Model.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2)); stop.Cancel();
            await Throws<OperationCanceledException>(async () => { await first; });
            var busy = await Throws<BuddyException>(() => f.Service.RefineDetailed(new(Scene), default));
            Check(busy.Code == "REFINE_BUSY" && f.Model.Plans == 1 && f.Model.Assessments == 0, "late model owner lost inference exclusion");
            f.Model.HoldPlan = false; f.Model.Release.TrySetResult();
            await f.Model.Exited.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var next = await f.Service.RefineDetailed(new(Scene), default);
            Check(next.Accepted && f.Model.Assessments == 1, "cancelled plan published or replacement failed after settlement");
        });
        foreach (var boundary in new[] { "Preparing local refinement.", "Waiting for local AI.", "Task structure", "Checking intent and constraints" }) {
            await Case("cancellation at public stage " + boundary, async () => {
                using var f = new Fixture(); using var stop = new CancellationTokenSource(); bool reached = false, result = false, cancelled = false; var seen = new List<string>();
                try {
                    await foreach (var e in f.Service.RefineStream(new(Scene), stop.Token)) {
                        seen.Add(e.Type + ":" + e.Text);
                        if (e.Text?.TrimEnd('.', '\u2026') == boundary.TrimEnd('.', '\u2026')) { reached = true; stop.Cancel(); }
                        result |= e.Result is not null;
                    }
                } catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled && reached && !result, "stage boundary failure: " + boundary + "; seen=" + string.Join("|", seen));
            });
        }
        await Case("confirmed constraints caller mutation frozen before initial yield", async () => {
            using var f = new Fixture(); var constraints = new List<string> { "Keep it short." };
            var iterator = f.Service.RefineStream(new(Scene, Inputs: new(ConfirmedConstraints: constraints)), default).GetAsyncEnumerator();
            try {
                Check(await iterator.MoveNextAsync(), "initial status missing"); constraints[0] = "Add an invented secret.";
                RefinementResult? r = null; while (await iterator.MoveNextAsync()) r ??= iterator.Current.Result;
                Check(r is { Accepted: true } && r.RefinedPrompt.Contains("Keep it short.") && !r.RefinedPrompt.Contains("invented secret"), "mutable caller inputs changed active request");
            } finally { await iterator.DisposeAsync(); }
        });
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "Buddy.RefineIndependent60." + Guid.NewGuid().ToString("N"));
        internal readonly MockModel Model = new(); internal readonly StateStore Store; internal readonly BuddyService Service;
        private readonly HttpClient http;
        internal Fixture(RefinementRequestLimits? limits = null) {
            Store = new(folder, new EphemeralDataProtectionProvider());
            http = new(Model) { BaseAddress = new("http://127.0.0.1:11434/"), Timeout = Timeout.InfiniteTimeSpan };
            Service = new(Store, new(http)) { AgentEnabled = false, WebEnabled = false, RefinementLimits = limits ?? RefinementRequestLimits.Default };
        }
        public void Dispose() { Model.Release.TrySetResult(); http.Dispose(); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
    private sealed class MockModel : HttpMessageHandler
    {
        internal int Calls, Chats, Plans, Assessments, Embeddings;
        internal string Wording = Plain, PlanFault = "", ValidationFault = "";
        internal readonly List<string> WordingPayloads = [];
        internal bool HoldPlan;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously), Exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; if (request.RequestUri is not { Host: "127.0.0.1", Port: 11434 } uri) throw new InvalidOperationException("Unexpected injected endpoint");
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); var body = doc.RootElement;
            if (uri.AbsolutePath == "/api/embed") {
                Embeddings++; if (ValidationFault == "embedding-http") return new(HttpStatusCode.ServiceUnavailable);
                return Json(new { embeddings = new[] { new[] { 1f, 0f }, ValidationFault == "low-similarity" ? new[] { 0f, 1f } : new[] { 1f, 0f } } });
            }
            if (uri.AbsolutePath != "/api/chat") throw new InvalidOperationException("Unexpected injected path");
            if (!body.TryGetProperty("format", out var schema)) {
                WordingPayloads.Add(body.GetProperty("messages").EnumerateArray().Last().GetProperty("content").GetString()!);
                Chats++; var line = JsonSerializer.Serialize(new { message = new { content = Wording }, done = false }) + "\n" + JsonSerializer.Serialize(new { message = new { content = "" }, done = true, done_reason = "stop" }) + "\n";
                return new(HttpStatusCode.OK) { Content = new StringContent(line, Encoding.UTF8, "application/x-ndjson") };
            }
            if (schema.GetProperty("properties").TryGetProperty("sections", out _)) {
                Plans++;
                if (HoldPlan) { Entered.TrySetResult(); try { await Release.Task; } finally { Exited.TrySetResult(); } }
                var inputText = body.GetProperty("messages").EnumerateArray().Last().GetProperty("content").GetString()!;
                using var input = JsonDocument.Parse(inputText); var spans = input.RootElement.GetProperty("untrustedSpans").EnumerateArray().ToArray();
                var sections = spans.Select(s => new PlanSection(s.GetProperty("kind").GetString()!, [s.GetProperty("id").GetString()!])).ToList();
                if (PlanFault == "omit-span") sections.RemoveAt(sections.Count - 1);
                if (PlanFault == "duplicate-span") sections.Add(sections[0]);
                if (PlanFault == "wrong-kind") sections[0] = sections[0] with { kind = "context" };
                string output = PlanFault == "bad-json" ? "{broken" : JsonSerializer.Serialize(new { sections });
                return Json(new { message = new { content = output }, done = true, done_reason = PlanFault == "length" ? "length" : "stop" });
            }
            Assessments++; if (ValidationFault == "assessment-http") return new(HttpStatusCode.ServiceUnavailable);
            return Json(new { message = new { content = JsonSerializer.Serialize(new { preserved = ValidationFault != "assessment-false", scoreBefore = 80, scoreAfter = 95, changes = new[] { "model claim, not independent utility proof" } }) }, done = true, done_reason = "stop" });
        }
        private sealed record PlanSection(string kind, string[] sourceIds);
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }
}
