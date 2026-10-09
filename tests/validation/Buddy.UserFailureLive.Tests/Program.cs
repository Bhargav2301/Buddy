using Buddy.Server;
using Microsoft.AspNetCore.DataProtection;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// Root alone schedules this explicit loopback run. It never reads the installed
// profile, observes an application, plays audio, executes an action or edits a field.
string? model = args.FirstOrDefault(x => x.StartsWith("--model="))?[8..];
int rounds = int.TryParse(args.FirstOrDefault(x => x.StartsWith("--rounds="))?[9..], out int count) ? count : 1;
if (!args.Contains("--live") || string.IsNullOrWhiteSpace(model)) { Console.WriteLine("NOT RUN: root's exclusive model slot and explicit --live --model=<existing-selected-model> are required."); return 2; }
if (rounds is < 1 or > 3 || model.Length > 200 || model.Any(char.IsControl)) throw new ArgumentException("Use 1-3 rounds and a valid installed model name.");
SourceReceipt.Verify("canned refinement through production service and actual loopback model; no installed HTTP/UI/source-field acceptance");
string folder = Path.Combine(Path.GetTempPath(), "Buddy-userfailure-live-" + Guid.NewGuid());
using var handler = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false };
using var http = new HttpClient(handler) { BaseAddress = new("http://127.0.0.1:11434/"), Timeout = TimeSpan.FromMinutes(3) };
int passed = 0, failed = 0, acceptedAssessedCandidates = 0, truthfulNoRefinements = 0;
try {
    var store = new StateStore(folder, new EphemeralDataProtectionProvider());
    await store.Update(s => { s.Model = model; s.EmbeddingModel = "all-minilm:22m"; return true; });
    var service = new BuddyService(store, new(http)) { AgentEnabled = false, WebEnabled = false };
    var status = await service.Engine.Status(model, model);
    Console.WriteLine(JsonSerializer.Serialize(new { kind = "readiness", model, status.Reachable, status.Ready, embeddingInstalled = status.Installed.Contains("all-minilm:22m") }));
    if (!status.Ready || !status.Installed.Contains("all-minilm:22m")) return 2;
    for (int round = 1; round <= rounds; round++) {
        Console.WriteLine(JsonSerializer.Serialize(new { kind = "round", round, residency = round == 1 ? "initial residency unknown" : "subsequent round; not forced", forcedUnload = false }));
        foreach (var (name, original, words) in new[] {
            ("reported poem exact", "Write a poem on a boat sailing in a sea on a lonely night", new[] { "poem", "boat", "sailing", "sea", "lonely", "night" }),
            ("email fidelity regression", "Write a polite email requesting Friday off.", new[] { "polite", "email", "Friday", "off" }),
            ("poem grammar", "Write poem about boat sailing at sea on lonely night.", new[] { "poem", "boat", "sailing", "sea", "lonely", "night" }),
            ("explanation grammar and limit", "Explain difference between RAM and storage in 2 sentences. Do not recommend brands.", new[] { "difference", "RAM", "storage", "2", "sentences", "not", "recommend", "brands" }),
            ("ordered grammar and negation", "First summarize report. Then list risks. Do not suggest solutions.", new[] { "First", "summarize", "report", "Then", "list", "risks", "not", "suggest", "solutions" }),
            ("literal and grammar", "Explain purpose of `cache_size=64` in one sentence. Do not change `cache_size=64`.", new[] { "purpose", "cache_size", "64", "one", "sentence", "not", "change" }),
            ("rough email request", "write polite email asking for Friday off.", new[] { "polite", "email", "Friday", "off" }),
            ("substantial rephrase boundary", "please can you help me to write instructions that explain how to rename a file in Windows. use simple words.", new[] { "instructions", "rename", "file", "Windows", "simple", "words" })
        }) {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(120)); var watch = Stopwatch.StartNew();
            try {
                var events = new List<RefinementEvent>();
                await foreach (var item in service.RefineStream(new(original), stop.Token)) events.Add(item);
                var result = events.LastOrDefault(x => x.Result is not null)?.Result ?? throw new InvalidOperationException("No final refinement result.");
                // Independent lexical oracle, intentionally not the production change
                // helper: letters/numbers/marks must differ after Unicode normalization.
                // A period, capitalization or formatting alone cannot pass this test.
                string Lexical(string value) => string.Concat(Regex.Matches(value.Normalize(NormalizationForm.FormKC), @"[\p{L}\p{N}\p{M}]+").Select(m => m.Value)).ToUpperInvariant();
                bool changed = Lexical(result.RefinedPrompt) != Lexical(original);
                bool retained = words.All(word => Regex.IsMatch(result.RefinedPrompt, "\\b" + Regex.Escape(word) + "\\b", RegexOptions.IgnoreCase));
                bool invented = name is "reported poem exact" or "poem grammar"
                    ? Regex.IsMatch(result.RefinedPrompt, @"\b(captain|tragic|rhyme|stanza|sonnet|metaphor|deadline)\b", RegexOptions.IgnoreCase)
                    : Regex.IsMatch(result.RefinedPrompt, @"\b(reason|deadline|commitment|manager|appointment|medical)\b", RegexOptions.IgnoreCase);
                bool assessedImprovement = result.ScoreBefore is { } before && result.ScoreAfter is { } after && after > before;
                bool acceptedCandidate = result.Accepted && !result.NoChange && changed && retained && !invented && assessedImprovement;
                bool truthfulNoRefinement = !result.Accepted && result.NoChange && result.RefinedPrompt == original && result.ScoreBefore is null && result.ScoreAfter is null && result.Similarity is null && result.Changes.Count == 0;
                bool originalKept = !result.Accepted && result.RefinedPrompt == original;
                bool expectedFidelityRefusal = name == "substantial rephrase boundary" && originalKept && !result.NoChange && result.Message.StartsWith("Original kept:", StringComparison.Ordinal) && result.ScoreBefore is null && result.ScoreAfter is null;
                bool ok = acceptedCandidate || truthfulNoRefinement || expectedFidelityRefusal;
                if (acceptedCandidate) acceptedAssessedCandidates++;
                if (truthfulNoRefinement) truthfulNoRefinements++;
                if (ok) passed++; else failed++;
                Console.WriteLine(JsonSerializer.Serialize(new { kind = "live_case", round, name, original, status = ok ? "PASS" : "FAIL", elapsedMs = watch.ElapsedMilliseconds,
                    changed, retained, invented, assessedImprovement, acceptedCandidate, truthfulNoRefinement, originalKept, expectedFidelityRefusal, qualityManuallyAccepted = false,
                    result, stages = events.Where(e => e.Type == "stage").Select(e => e.Text),
                    oracle = "independent normalized lexical change plus retained explicit concepts; manual quality review still required",
                    outcome = acceptedCandidate ? "accepted lexical candidate with higher model assessment; human quality review remains required" : truthfulNoRefinement ? "truthful no-refinement; original preserved, no useful refinement demonstrated" : "invalid or unsupported refinement outcome", fieldWrites = 0 }));
            } catch (Exception e) { failed++; Console.WriteLine(JsonSerializer.Serialize(new { kind = "live_case", round, name, status = "FAIL", elapsedMs = watch.ElapsedMilliseconds, error = e is BuddyException b ? b.Code : e.GetType().Name, message = e.Message })); }
        }
    }
    bool noContentSaved = await store.Read(s => s.Conversations.Count + s.Knowledge.Count + s.Jobs.Count + s.Guides.Count + s.Audit.Count) == 0;
    if (!noContentSaved) failed++;
    Console.WriteLine(JsonSerializer.Serialize(new { kind = "summary", passed, failed, acceptedAssessedCandidates, truthfulNoRefinements,
        outcomeIntegrityOnly = true, assessmentIsModelReported = true, qualityManuallyAccepted = false,
        noContentSaved, realModel = true, nativeUi = "NOT RUN", actualBrowserRefinement = "NOT RUN", installedService = "NOT RUN" }));
    return failed == 0 ? 0 : 1;
} finally { SourceReceipt.DeleteOwned(folder, "Buddy-userfailure-live-"); }
