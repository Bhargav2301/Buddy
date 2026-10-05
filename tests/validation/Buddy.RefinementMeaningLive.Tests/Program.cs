using Buddy.Server;
using Microsoft.AspNetCore.DataProtection;
using System.Diagnostics;
using System.Text.Json;

string? model = args.FirstOrDefault(a => a.StartsWith("--model="))?[8..];
string? only = args.FirstOrDefault(a => a.StartsWith("--case="))?[7..];
int rounds = int.TryParse(args.FirstOrDefault(a => a.StartsWith("--rounds="))?[9..], out int n) ? n : 1;
if (!args.Contains("--live") || string.IsNullOrWhiteSpace(model)) { Console.WriteLine("NOT RUN: root's exclusive local-model slot and --live --model=<existing model> are required."); return 2; }
if (rounds is < 1 or > 3 || model.Length > 200 || model.Any(char.IsControl)) throw new ArgumentException("Invalid bounded model-run arguments.");
var goldens = MeaningGoldens.All.Where(g => only is null || g.Id == only).ToArray();
if (goldens.Length == 0) throw new ArgumentException("Unknown canned golden identifier.");
SourceReceipt.Verify("independent canned meaning/usefulness checks through local service; not installed/browser acceptance");
string folder = Path.Combine(Path.GetTempPath(), "Buddy-meaning-live-" + Guid.NewGuid());
using var handler = new CannedResponseRecorder();
using var http = new HttpClient(handler) { BaseAddress = new("http://127.0.0.1:11434/"), Timeout = TimeSpan.FromMinutes(3) };
int useful = 0, unmetUtility = 0, unsafeOrInvalid = 0, safeRefusals = 0;
var usefulIds = new HashSet<string>();
try {
    var store = new StateStore(folder, new EphemeralDataProtectionProvider());
    await store.Update(s => { s.Model = model; s.EmbeddingModel = "all-minilm:22m"; return true; });
    var service = new BuddyService(store, new(http)) { AgentEnabled = false, WebEnabled = false };
    var status = await service.Engine.Status(model, model);
    Console.WriteLine(JsonSerializer.Serialize(new { kind = "readiness", model, status.Reachable, status.Ready, newModelsInstalled = false }));
    if (!status.Ready) return 2;
    for (int round = 1; round <= rounds; round++) foreach (var golden in goldens) {
        handler.CaseId = golden.Id; handler.Round = round;
        var watch = Stopwatch.StartNew(); using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        try {
            var result = await service.RefineDetailed(new(golden.Original), stop.Token);
            var verdict = MeaningOracle.Judge(golden, result.RefinedPrompt);
            bool usefulCandidate = golden.CanStructure && result.Accepted && !result.NoChange && verdict.MeetsGolden;
            bool safeOriginal = !result.Accepted && result.RefinedPrompt == golden.Original;
            if (usefulCandidate) { useful++; usefulIds.Add(golden.Id); }
            else if (safeOriginal) { safeRefusals++; if (golden.CanStructure) unmetUtility++; }
            else unsafeOrInvalid++;
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "meaning_golden", round, golden.Id, priorEightComparison = MeaningGoldens.PriorEightIds.Contains(golden.Id), original = golden.Original,
                golden.Utility, candidate = result.RefinedPrompt, result.Accepted, result.NoChange, result.Message,
                result.Method, result.Similarity, structuralOperations = result.Structure?.Operations,
                verdict.MeaningCovered, verdict.UsefulStructure, verdict.Failures, usefulCandidate, safeOriginal,
                status = usefulCandidate || !golden.CanStructure && safeOriginal ? "PASS" : "FAIL", elapsedMs = watch.ElapsedMilliseconds,
                modelScoresIgnoredByOracle = true, humanQualityReviewRequired = true,
                outcome = usefulCandidate ? "fixture-specific useful structure with checked relationships; root must inspect wording" : safeOriginal ? "original safely retained; useful refinement not demonstrated" : "candidate failed independent meaning or utility criteria" }));
        } catch (Exception ex) {
            unsafeOrInvalid++;
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "meaning_golden", round, golden.Id, status = "FAIL", elapsedMs = watch.ElapsedMilliseconds, error = ex is BuddyException b ? b.Code : ex.GetType().Name }));
        }
    }
    bool noSavedContent = await store.Read(s => s.Conversations.Count + s.Knowledge.Count + s.Guides.Count + s.Jobs.Count + s.Audit.Count) == 0;
    Console.WriteLine(JsonSerializer.Serialize(new { kind = "summary", useful, unmetUtility, unsafeOrInvalid, safeRefusals, noSavedContent,
        rounds, uniqueScenarios = goldens.Length, usefulUniqueScenarios = usefulIds.Count,
        priorEightUniqueScenarios = goldens.Count(g => MeaningGoldens.PriorEightIds.Contains(g.Id)), priorEightUsefulUniqueScenarios = usefulIds.Count(MeaningGoldens.PriorEightIds.Contains),
        manualQualityAccepted = false, actualBrowser = "NOT RUN", fieldWrites = 0, installedProfileRead = false }));
    return unmetUtility == 0 && unsafeOrInvalid == 0 && noSavedContent ? 0 : 1;
} finally { SourceReceipt.DeleteOwned(folder, "Buddy-meaning-live-"); }
