using Buddy.Server;
using Microsoft.AspNetCore.DataProtection;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

// Explicit opt-in only. Root serializes this runner with every other Ollama/UI run.
// Production service + real local model; ephemeral profile; canned prompts only.
// This is NOT the installed HTTP service, real UI capture, action execution or audio.
string? model = args.FirstOrDefault(a => a.StartsWith("--model="))?[8..];
int rounds = int.TryParse(args.FirstOrDefault(a => a.StartsWith("--rounds="))?[9..], out int requestedRounds) ? requestedRounds : 1;
if (!args.Contains("--live") || string.IsNullOrWhiteSpace(model))
{
    Console.WriteLine("NOT RUN: explicit --live --model=<already-installed-selected-model> and the orchestrator's exclusive Ollama slot are required.");
    return 2;
}
if (rounds is < 1 or > 3 || model.Length > 200 || model.Any(char.IsControl)) throw new ArgumentException("Use 1-3 rounds and a valid existing local model name.");
FixtureFiles.SourceManifest(true);
var folder = Path.Combine(Path.GetTempPath(), "Buddy-reliability-live-" + Guid.NewGuid());
int passed = 0, failed = 0;
using var transport = new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false };
using var client = new HttpClient(transport) { BaseAddress = new("http://127.0.0.1:11434/"), Timeout = TimeSpan.FromMinutes(3) };
try
{
    var store = new StateStore(folder, new EphemeralDataProtectionProvider());
    await store.Update(s => { s.Model = model; s.VisionModel = model; s.EmbeddingModel = "all-minilm:22m"; return true; });
    var service = new BuddyService(store, new(client)) { AgentEnabled = true, WebEnabled = false };
    var status = await service.Engine.Status(model, model);
    Console.WriteLine(JsonSerializer.Serialize(new { kind = "readiness", model, status.Reachable, status.Ready, embeddingInstalled = status.Installed.Contains("all-minilm:22m"), boundary = "temporary-profile production service; loopback model; no installed HTTP/UI acceptance" }));
    if (!status.Ready || !status.Installed.Contains("all-minilm:22m")) return 2;
    async Task Probe(int round, string name, string prompt, string context, Func<CancellationToken, Task<(bool Ok, object Result)>> run)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var clock = Stopwatch.StartNew();
        try
        {
            var (ok, result) = await run(stop.Token);
            if (ok) passed++; else failed++;
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "live_case", round, name, prompt, context, status = ok ? "PASS" : "FAIL", elapsedMs = clock.ElapsedMilliseconds, result, executedActions = false, usedWeb = false }));
        }
        catch (Exception e)
        {
            failed++;
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "live_case", round, name, prompt, context, status = "FAIL", elapsedMs = clock.ElapsedMilliseconds, error = e is BuddyException b ? b.Code : e.GetType().Name, message = e.Message, executedActions = false }));
        }
    }
    ScreenContext Empty() => new("", "Owned diagnostic: no accessible window or observed controls", []);
    for (int round = 1; round <= rounds; round++)
    {
        // Report residency honestly: this runner never unloads/primes a model or inserts latency.
        Console.WriteLine(JsonSerializer.Serialize(new { kind = "round", round, thermalState = round == 1 ? "initial residency unknown" : "subsequent round; residency not forced", forcedUnload = false }));
        const string exact = "Guide me through Comet browser";
        await Probe(round, "reported Agent request", exact, "explicit empty fallback; no UI capture", async ct => {
            var plan = await service.PlanAgent(new(exact, Empty()), ct);
            bool bounded = plan.Actions is { Count: 0 } && !string.IsNullOrWhiteSpace(plan.Summary) || plan.Actions is { Count: > 0 } && plan.Actions.All(a => a.Kind != "open" || a.Value == "comet");
            return (bounded, new { plan, outcome = plan.Actions?.Count == 0 ? "clarification; task not completed" : "reviewable plan; no action run" });
        });
        await Probe(round, "canonical launch control", "Open Comet Browser", "empty fallback; deterministic path", async ct => {
            var plan = await service.PlanAgent(new("Open Comet Browser", Empty()), ct);
            return (plan.Actions is { Count: 1 } && plan.Actions[0].Value == "comet" && plan.Actions[0].Risk == "high", plan);
        });
        await Probe(round, "reported Guide request", exact, "explicit empty fallback; no UI capture", async ct => {
            var plan = await service.PlanGuide(new(exact, Empty()), ct);
            var content = JsonSerializer.Serialize(plan);
            bool reportsOnlyMissingView = content.Contains("verified", StringComparison.OrdinalIgnoreCase) && !Regex.IsMatch(content, @"not responding|unresponsive|crash|failed to (?:open|launch)", RegexOptions.IgnoreCase);
            return (plan.Steps is { Count: 0 } && plan.Lessons is { Count: > 0 } && reportsOnlyMissingView, new { plan, reportsOnlyMissingView, deterministicMissingView = true });
        });
        var synthetic = new ScreenContext("owned-qa-fixture", "Synthetic QA form, not an observed application", [new("qa-export", "Export example", "Button", 10, 10, 140, 40)]);
        await Probe(round, "nested grounded GuidePlan schema", "Show the Export example button", "synthetic single-control snapshot; NOT observed/native acceptance", async ct => {
            var plan = await service.PlanGuide(new("Show the Export example button", synthetic), ct);
            return (plan.Steps is { Count: > 0 } && plan.Steps.All(s => s.Ref == "qa-export" && s.Target == "Export example" && s.Role == "Button"), plan);
        });
        const string chat = "Explain rainbows in one sentence.";
        await Probe(round, "explicit one-sentence chat", chat, "text-only isolated conversation", async ct => {
            var conversation = await service.CreateConversation("Owned canned QA live check");
            var events = new List<StreamEvent>();
            try
            {
                await foreach (var item in service.Chat(new(conversation.Id, chat, Guid.NewGuid().ToString()), ct)) events.Add(item);
                string text = string.Concat(events.Where(e => e.Type == "delta").Select(e => e.Text));
                int sentenceCount = Regex.Matches(text, @"[.!?]+(?=\s|$)").Count;
                bool relevant = text.Contains("rain", StringComparison.OrdinalIgnoreCase) && text.Contains("light", StringComparison.OrdinalIgnoreCase);
                bool noOffer = !Regex.IsMatch(text, @"\b(?:Guide|Agent)\b", RegexOptions.IgnoreCase);
                return (sentenceCount == 1 && relevant && noOffer && events.Count(e => e.Type == "done") == 1,
                    new { text, sentenceCount, relevant, noOffer, savedMessages = await store.Read(s => s.Conversations.Single(c => c.Id == conversation.Id).Messages.Count) });
            }
            finally { await service.DeleteConversation(conversation.Id); }
        });
        foreach (string original in new[] { "Write a polite email requesting Friday off.", "Write a polite email asking for Friday off." })
        await Probe(round, "refinement fidelity and usefulness", original, "canned text; no source field editing", async ct => {
            var result = await service.RefineDetailed(new(original), ct);
            bool retained = result.RefinedPrompt.Contains("Friday", StringComparison.OrdinalIgnoreCase) && result.RefinedPrompt.Contains("polite", StringComparison.OrdinalIgnoreCase);
            bool invented = Regex.IsMatch(result.RefinedPrompt, @"\b(reason|deadline|commitment|manager|supervisor|appointment|medical)\b", RegexOptions.IgnoreCase);
            // Kept-original can be a safety success while usefulness is explicitly unproven.
            return (retained && !invented && (result.Accepted || result.RefinedPrompt == original), new { result, fidelity = retained && !invented, usefulness = result.Accepted ? "accepted rewrite; inspect text" : "original kept; no successful rewrite demonstrated" });
        });
    }
    Console.WriteLine(JsonSerializer.Serialize(new { kind = "summary", passed, failed, realModel = true, observedWindow = "NOT RUN here: native ReliabilityUiChecks owns this check", makePlanUi = "NOT RUN here", actionExecution = "NOT RUN", physicalAudio = "NOT RUN" }));
    return failed == 0 ? 0 : 1;
}
finally { FixtureFiles.DeleteTemporary(folder, "Buddy-reliability-live-"); }
