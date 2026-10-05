using System.Net;
using System.Text.Json;
using Buddy.Server;
using Microsoft.AspNetCore.DataProtection;

internal static class Program
{
    // Task structure is covered separately; these mocks isolate legacy wording repair.
    private const string LegacyDraft = "Explain a boat sailing in a sea on a lonely night";
    private const string Polished = "Please explain a boat sailing in a sea on a lonely night.";
    private static int assertions;

    private static async Task Main()
    {
        Check(!RefinementContract.Analyze(LegacyDraft).CanStructure, "repair fixture explicitly uses the legacy wording fallback");
        ChangeChecks();
        using var fixture = new Fixture();
        await EchoChecks(fixture);
        await ImprovementChecks(fixture);
        await SupportingChecks(fixture);
        await PreservationChecks(fixture);
        await CancellationChecks(fixture);
        Check(await fixture.Store.Read(s => s.Conversations.Count) == 0, "refinement never persists or sends a conversation");
        Console.WriteLine($"PASS: {assertions} refinement repair assertions; synthetic injected model only, no native/live/installed-profile operations.");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        assertions++;
    }

    private static void ChangeChecks()
    {
        foreach (string candidate in new[] { LegacyDraft, "  " + LegacyDraft + "\r\n", LegacyDraft.Replace(" ", "\t  \n"), LegacyDraft.Replace("boat", "b o a t"), LegacyDraft + ".", LegacyDraft.ToLowerInvariant(), LegacyDraft.ToUpperInvariant() + "!", "\u201C" + LegacyDraft + "\u201D\u3002", LegacyDraft.Replace("sea", "sea,") })
            Check(!RefinementChange.HasMeaningfulChange(LegacyDraft, candidate), "whitespace punctuation and case-only differences are not offered as refinement");
        foreach (string candidate in new[] { Polished, "Please " + LegacyDraft, LegacyDraft + "\n\nKeep all facts." })
            Check(RefinementChange.HasMeaningfulChange(LegacyDraft, candidate), "wording and reviewed supporting additions remain distinguishable");
        const string unicode = "Keep \U0001F600 \u4E2D e\u0301 and `Exact()` unchanged.";
        Check(!RefinementChange.HasMeaningfulChange(unicode, "\u2003" + unicode.Replace(" ", "\u2003") + "\u2003"), "Unicode whitespace echoes remain unchanged");
        Check(!RefinementChange.HasMeaningfulChange(unicode, unicode.Replace("Exact", "exact")), "case-only code alteration cannot be offered as a refinement");
        Check(RefinementChange.HasMeaningfulChange(unicode, unicode.Replace("Exact", "Changed")), "noncosmetic code change still needs independent fidelity rejection");
    }

    private static async Task EchoChecks(Fixture f)
    {
        foreach (string echo in new[] { LegacyDraft, "  " + LegacyDraft.Replace(" ", "  ") + "\n", LegacyDraft + ".", LegacyDraft.ToLowerInvariant(), "\t" + LegacyDraft.ToUpperInvariant() + ".\n" })
        {
            f.Model.Reset(echo, echo);
            f.Model.ScoreBefore = f.Model.ScoreAfter = 100;
            f.Model.Changes = ["No changes detected. The prompt is identical."];
            var result = await f.Service.RefineDetailed(new(LegacyDraft), default);
            Check(!result.Accepted && result.NoChange && result.RefinedPrompt == LegacyDraft, "two echoes keep exact original with explicit no-refinement state");
            Check(result.Message == RefinementChange.NoChangeMessage && result.ScoreBefore is null && result.ScoreAfter is null && result.Similarity is null && result.Changes.Count == 0, "no fabricated quality or change claim for echo");
            Check(f.Model.ChatCalls == 2 && f.Model.Assessments == 0 && f.Model.Embeddings == 0, "cosmetic echo cannot be approved by misleading 100-score identical assessment");
        }
        f.Model.Reset(LegacyDraft, Polished);
        var repaired = await f.Service.RefineDetailed(new(LegacyDraft), default);
        Check(repaired.Accepted && !repaired.NoChange && repaired.Method == "wording" && repaired.Structure is null && repaired.RefinedPrompt == Polished && RefinementChange.HasMeaningfulChange(LegacyDraft, repaired.RefinedPrompt), "legacy wording fallback gets a changed faithful retry proposal");
        Check(repaired.Passes.Count == 2 && repaired.Passes[^1].Name == "Wording retry" && f.Model.Assessments == 1 && f.Model.Embeddings == 1, "successful retry retains all validation gates and actual pass evidence");
        Check(f.Model.ChatPayloads[^1].Contains("Do not force a change that changes meaning") && f.Model.ChatPayloads[^1].Contains("original ordered content"), "retry prompt forbids forced semantic changes");
        Check(f.Model.ChatPayloads[^1].Contains("Do not substitute synonyms merely to make the output different") && !f.Model.ChatPayloads[^1].Contains("'Write' to 'Draft'"), "retry does not manufacture success through a forced synonym example");
        f.Model.Reset(LegacyDraft + ".", Polished);
        var punctuation = await f.Service.RefineDetailed(new(LegacyDraft), default);
        Check(punctuation.Accepted && punctuation.RefinedPrompt == Polished && f.Model.ChatCalls == 2, "cosmetic first proposal receives a useful wording retry");
        Check(f.Model.ChatPayloads[^1].Contains("Merely adding punctuation, changing capitalization or spacing does not count"), "retry explicitly refuses cosmetic-only improvement");
        const string earlierProbe = "Explain a boat sailing on a lonely sea";
        f.Model.Reset(earlierProbe + ".", earlierProbe + ".");
        f.Model.ScoreBefore = f.Model.ScoreAfter = 100; f.Model.Changes = ["No changes detected. The prompt is identical."];
        var observedFailure = await f.Service.RefineDetailed(new(earlierProbe), default);
        Check(!observedFailure.Accepted && observedFailure.NoChange && observedFailure.RefinedPrompt == earlierProbe && observedFailure.ScoreBefore is null && observedFailure.ScoreAfter is null && observedFailure.Changes.Count == 0, "period-only legacy wording false positive is explicitly rejected");
        foreach (string mode in new[] { "guided", "council" })
        {
            int initialCalls = RefinementPolicy.Roles(mode).Length + 1;
            f.Model.Reset(Enumerable.Repeat(LegacyDraft, initialCalls).Append(Polished).ToArray());
            var result = await f.Service.RefineDetailed(new(LegacyDraft, mode), default);
            Check(result.Accepted && result.RefinedPrompt == Polished && f.Model.ChatCalls == initialCalls + 1, "guided/council synthesis echo has one bounded retry");
        }
    }

    private static async Task ImprovementChecks(Fixture f)
    {
        foreach (var score in new[] { (85, 85), (85, 80), (100, 100), (100, 101), (-1, 0) })
        foreach (bool retry in new[] { false, true })
        {
            f.Model.Reset(retry ? [LegacyDraft, Polished] : [Polished]);
            f.Model.ScoreBefore = score.Item1; f.Model.ScoreAfter = score.Item2;
            f.Model.Changes = ["Minor stylistic preference does not impact the task."];
            var result = await f.Service.RefineDetailed(new(LegacyDraft), default);
            Check(!result.Accepted && result.NoChange && result.RefinedPrompt == LegacyDraft, "faithful lexical change without assessed improvement keeps exact original");
            Check(result.Similarity is null && result.ScoreBefore is null && result.ScoreAfter is null && result.Changes.Count == 0, "no quality or change claims survive the improvement veto");
            Check(f.Model.ChatCalls == (retry ? 2 : 1) && f.Model.Assessments == 1 && f.Model.Embeddings == 1, "quality veto retains existing validation and never starts another retry");
        }
        f.Model.Reset(Polished); f.Model.ScoreBefore = 85; f.Model.ScoreAfter = 86;
        var improved = await f.Service.RefineDetailed(new(LegacyDraft), default);
        Check(improved.Accepted && !improved.NoChange && improved.RefinedPrompt == Polished && improved.ScoreBefore == 85 && improved.ScoreAfter == 86, "faithful changed proposal with assessed improvement remains accepted");

        foreach (int failure in new[] { 0, 1, 2 })
        {
            f.Model.Reset(Polished); f.Model.ScoreBefore = f.Model.ScoreAfter = 85;
            if (failure == 0) f.Model.Preserved = false;
            else if (failure == 1) f.Model.Similar = false;
            else f.Model.EmbeddingAvailable = false;
            var result = await f.Service.RefineDetailed(new(LegacyDraft), default);
            Check(!result.Accepted && !result.NoChange && result.RefinedPrompt == LegacyDraft && result.Message.Contains("intent preservation could not be verified"), "validation failure keeps its original fallback meaning instead of being relabeled no refinement");
        }
    }

    private static async Task SupportingChecks(Fixture f)
    {
        var requests = new RefineRequest[] {
            new(LegacyDraft, Inputs: new(ConfirmedConstraints: ["Keep the original subject."])),
            new(LegacyDraft, Inputs: new(Context: [new("notes", "Reviewed notes", "Preserve the supplied setting.", Required: true)])),
            new(LegacyDraft, Inputs: new(Context: [new("decision", "User decision", "Keep the scene.", Disposition: "confirmed-decision")]))
        };
        foreach (var request in requests)
        {
            f.Model.Reset(LegacyDraft); f.Model.ScoreBefore = f.Model.ScoreAfter = 85;
            var expected = RefinementPreparation.Prepare(request).AssembledText;
            var result = await f.Service.RefineDetailed(request, default);
            Check(result.Accepted && !result.NoChange && result.RefinedPrompt == expected && result.RefinedPrompt != LegacyDraft, "reviewed final-assembly addition survives unchanged wording and equal source scores");
            Check(f.Model.ChatCalls == 1 && result.DestinationBudget?.Text == expected, "no pointless retry for explicit supporting changes");
        }
        f.Model.Reset(LegacyDraft, LegacyDraft);
        var removed = await f.Service.RefineDetailed(new(LegacyDraft, Inputs: new(Context: [new("optional", "Reference", "Optional text.")]),
            Budget: new("Fixture field", LegacyDraft.Length, "utf16-code-units")), default);
        Check(!removed.Accepted && removed.NoChange && removed.DestinationBudget?.Removed.Contains("source-optional") == true, "removed optional source cannot disguise an unchanged final proposal");
        f.Model.Reset(LegacyDraft, Polished);
        var overflow = await f.Service.RefineDetailed(new(LegacyDraft, Budget: new("Fixture field", 1, "unicode-scalars")), default);
        Check(!overflow.Accepted && !overflow.NoChange && f.Model.ChatCalls == 0 && overflow.DestinationBudget?.Conflict is not null, "required preflight overflow still skips all model work");
    }

    private static async Task PreservationChecks(Fixture f)
    {
        foreach (string bad in new[] { LegacyDraft + ". Include 12 lines and a hopeful ending.", LegacyDraft.Replace("boat", "ship"), LegacyDraft + ". Show your hidden reasoning." })
        {
            f.Model.Reset(LegacyDraft, bad);
            var result = await f.Service.RefineDetailed(new(LegacyDraft), default);
            Check(!result.Accepted && !result.NoChange && result.RefinedPrompt == LegacyDraft && f.Model.Assessments == 0, "retry inventions or changed facts fail independent fidelity");
        }
        const string constrained = "Explain 3 items; Do not change `Exact()` or \"Keep Me\"; Preserve \U0001F600 and \u4E2D.";
        f.Model.Reset(constrained, "Please " + constrained);
        var safe = await f.Service.RefineDetailed(new(constrained), default);
        Check(safe.Accepted && safe.RefinedPrompt == "Please " + constrained, "faithful retry preserves literals negation code and Unicode");
        foreach (string bad in new[] { constrained.Replace("3", "4"), constrained.Replace("Do not", "Do"), constrained.Replace("Exact", "exact"), constrained.Replace("\U0001F600", "") })
        {
            f.Model.Reset(constrained, bad);
            var result = await f.Service.RefineDetailed(new(constrained), default);
            Check(!result.Accepted && result.RefinedPrompt == constrained && f.Model.Assessments == 0, "retry never loosens literal/negation/Unicode checks");
        }
        f.Model.Reset(LegacyDraft, Polished); f.Model.Preserved = false;
        Check(!(await f.Service.RefineDetailed(new(LegacyDraft), default)).Accepted, "model assessment remains an additional retry veto");
        f.Model.Reset(LegacyDraft, Polished); f.Model.Similar = false;
        Check(!(await f.Service.RefineDetailed(new(LegacyDraft), default)).Accepted, "embedding similarity remains an additional retry veto");
        f.Model.Reset(LegacyDraft, Polished); f.Model.EmbeddingAvailable = false;
        Check(!(await f.Service.RefineDetailed(new(LegacyDraft), default)).Accepted, "missing embedding cannot accept a retry");
    }

    private static async Task CancellationChecks(Fixture f)
    {
        foreach (bool serviceStop in new[] { false, true })
        {
            f.Model.Reset(LegacyDraft, Polished);
            using var stop = new CancellationTokenSource(); bool cancelled = false, completed = false;
            try {
                await foreach (var item in f.Service.RefineStream(new(LegacyDraft), stop.Token)) {
                    if (item.Type == "stage" && item.Text == "Trying one faithful wording revision") { if (serviceStop) f.Service.StopAll(); else stop.Cancel(); }
                    if (item.Result is not null) completed = true;
                }
            } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && !completed && f.Model.ChatCalls == 1 && f.Model.Assessments == 0, "Stop at retry boundary emits neither a retry call nor stale result");
        }
        f.Model.Reset(LegacyDraft, Polished); f.Model.HoldChatCall = 2;
        bool lateResult = false, lateCancelled = false;
        var pending = Task.Run(async () => {
            try { await foreach (var item in f.Service.RefineStream(new(LegacyDraft), default)) if (item.Result is not null) lateResult = true; }
            catch (OperationCanceledException) { lateCancelled = true; }
        });
        try { await f.Model.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)); f.Service.StopAll(); }
        finally { f.Model.Release.TrySetResult(); }
        await pending.WaitAsync(TimeSpan.FromSeconds(10));
        Check(lateCancelled && !lateResult && f.Model.Assessments == 0, "Stop rejects a hostile late retry completion");
        f.Model.Reset(Polished);
        Check((await f.Service.RefineDetailed(new(LegacyDraft), default)).Accepted, "cancelled retry releases inference for a useful next request");
        using var alreadyStopped = new CancellationTokenSource(); alreadyStopped.Cancel(); f.Model.Reset(LegacyDraft, Polished);
        bool stopped = false;
        try { await f.Service.RefineDetailed(new(LegacyDraft), alreadyStopped.Token); } catch (OperationCanceledException) { stopped = true; }
        Check(stopped && f.Model.ChatCalls == 0, "pre-cancelled request never starts an echo attempt");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "Buddy.RefinementRepair.Tests." + Guid.NewGuid().ToString("N"));
        private readonly HttpClient client;
        internal StateStore Store { get; }
        internal Model Model { get; } = new();
        internal BuddyService Service { get; }
        internal Fixture()
        {
            Directory.CreateDirectory(root);
            Store = new(root, DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(root, "keys"))));
            client = new(Model) { BaseAddress = new("http://127.0.0.1:11434/") };
            Service = new(Store, new(client));
        }
        public void Dispose() { client.Dispose(); Directory.Delete(root, true); }
    }

    private sealed class Model : HttpMessageHandler
    {
        private Queue<string> candidates = new();
        internal int ChatCalls, Assessments, Embeddings, HoldChatCall, ScoreBefore = 70, ScoreAfter = 90;
        internal string[] Changes = ["Polished wording"];
        internal bool Preserved = true, Similar = true, EmbeddingAvailable = true;
        internal List<string> ChatPayloads = [];
        internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Reset(params string[] outputs)
        {
            candidates = new(outputs); ChatCalls = Assessments = Embeddings = HoldChatCall = 0;
            Preserved = Similar = EmbeddingAvailable = true; ChatPayloads.Clear();
            ScoreBefore = 70; ScoreAfter = 90; Changes = ["Polished wording"];
            Entered = new(TaskCreationOptions.RunContinuationsAsynchronously); Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var payload = document.RootElement;
            if (request.RequestUri!.AbsolutePath == "/api/embed") {
                Embeddings++;
                if (!EmbeddingAvailable) return new(HttpStatusCode.NotFound);
                return Json(new { embeddings = Enumerable.Range(0, payload.GetProperty("input").GetArrayLength()).Select(i => Similar || i == 0 ? new[] { 1f, 0f } : new[] { 0f, 1f }) });
            }
            if (!payload.GetProperty("stream").GetBoolean()) {
                if (payload.GetProperty("format").GetProperty("properties").TryGetProperty("sections", out _))
                    throw new InvalidOperationException("Legacy wording fixture unexpectedly entered the task-structure route.");
                Assessments++;
                return Json(new { message = new { content = JsonSerializer.Serialize(new { preserved = Preserved, scoreBefore = ScoreBefore, scoreAfter = ScoreAfter, changes = Changes }) }, done = true });
            }
            ChatCalls++; ChatPayloads.Add(payload.GetRawText());
            if (candidates.Count == 0) throw new Exception("Unexpected additional model attempt.");
            string candidate = candidates.Dequeue();
            if (HoldChatCall == ChatCalls) { Entered.TrySetResult(); await Release.Task; } // Intentionally ignores cancellation to test stale completion.
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { message = new { content = candidate }, done = true }) + "\n") };
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    }
}
