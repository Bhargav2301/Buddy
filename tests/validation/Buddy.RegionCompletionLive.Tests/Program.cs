using Buddy.Server;
using Buddy.RegionCompletion;
using System.Reflection;
using System.Security.Cryptography;

// Root-only opt-in runner. No HWND, capture, microphone, audio, account or installed profile API.
bool modelOptIn = args.Contains("--live-model"), webOptIn = args.Contains("--liveweb"), researchOnly = args.Contains("--research-only");
string? Value(string name) => args.FirstOrDefault(a => a.StartsWith(name + "=", StringComparison.Ordinal))?[(name.Length + 1)..];
string model = Value("--model") ?? "gemma3:4b";
if (!modelOptIn || researchOnly && !webOptIn || args.Any(a => a is not ("--live-model" or "--liveweb" or "--research-only") && !new[] { "--model=", "--owned-png=", "--expectation-json=", "--output=" }.Any(p => a.StartsWith(p, StringComparison.Ordinal)))) {
    Console.WriteLine("NOT RUN: root must reserve the local-model slot and pass --live-model [--model=gemma3:4b] [--liveweb] [--research-only] [--output=<new absolute JSONL file>]. Default image is a deterministic synthetic chart. Optional --owned-png=<absolute owned PNG> requires --expectation-json=<absolute canned fact JSON>."); return 2;
}
if (model.Length is < 1 or > 200 || model.Any(char.IsControl)) { Console.WriteLine("Invalid existing local model name."); return 2; }
using var log = new ProbeLog(Value("--output"));
try {
    SourceReceipt.Verify("REGION-50 production service, noninteractive owned inputs; not installed service/UI/gesture acceptance");
    using var receipt = Assembly.GetExecutingAssembly().GetManifestResourceStream("QaReviewedInputs")!;
    log.Write(new { kind = "build_receipt", sha256 = Convert.ToHexString(SHA256.HashData(receipt)), includesCurrentSourceHashCheck = true });
    var png = Value("--owned-png"); var expectation = Value("--expectation-json");
    if ((png is null) != (expectation is null)) throw new ArgumentException("Supply both owned PNG and its canned independent expectations.");
    var fixture = png is null ? RegionFixture.Synthetic() : RegionFixture.Owned(png, expectation!);
    log.Write(new { kind = "scope", model, fixture.Origin, imageSha256 = fixture.Sha256, imageBytes = fixture.Png.Length, question = fixture.Expectation.Question, expectations = fixture.Expectation, reviewedWebQuery = webOptIn ? RegionFixture.ReviewedQuery : null, noLiveCapture = true, noForeground = true, noAudio = true, installedProfile = false, completeGestureEndToEnd = false });
    using var state = new OwnedState(); await state.Select(model);
    using var modelTransport = new ModelAuditHandler(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, UseCookies = false }, log) { ExpectedImageSha256 = fixture.Sha256 };
    using var client = new HttpClient(modelTransport) { BaseAddress = new("http://127.0.0.1:11434/"), Timeout = Timeout.InfiniteTimeSpan };
    var engine = new OllamaEngine(client);
    using var readiness = new CancellationTokenSource(TimeSpan.FromSeconds(8));
    var status = await engine.Status(model, model, readiness.Token);
    log.Write(new { kind = "readiness", status.Ready, status.Reachable, model, modelAvailable = status.Installed.Contains(model) });
    if (!status.Ready) return 2;
    int failed = 0, passed = 0;
    async Task<bool> Run(string name, Func<CancellationToken, Task<CompletionCase>> action, Action stop) {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try { var result = await action(deadline.Token); if (result.Passed) passed++; else failed++; log.Write(new { kind = "completion_case", name, status = result.Passed ? "PASS" : "FAIL", result }); return true; }
        catch (Exception e) { failed++; log.Write(new { kind = "completion_case", name, status = "FAIL", error = e is BuddyException b ? b.Code : e.GetType().Name, message = e.Message }); return false; }
        finally { stop(); }
    }
    bool regionSettled = true;
    if (!researchOnly) {
        modelTransport.Stage = "region";
        var service = new BuddyService(state.Store, engine, new NoWeb()) { WebEnabled = false };
        regionSettled = await Run("selected-area explanation", ct => ProbeRunner.Region(service, fixture, ct), service.StopAll);
    }
    if (webOptIn && regionSettled) {
        modelTransport.Stage = "research";
        using var audit = new WebAuditHandler(WebAuditHandler.LiveTransport(log), log);
        using var web = new WebResearch(audit); var observed = new ObservedResearch(web, log);
        var service = new BuddyService(state.Store, engine, observed) { WebEnabled = true };
        await Run("exact reviewed-query research", ct => ProbeRunner.Research(service, observed, ct), service.StopAll);
        log.Write(new { kind = "web_boundary", searchCalls = observed.Queries.Count, fetchAttempts = observed.FetchAttempts.Count, publicRequests = audit.Destinations.Count, allRequestsPublicHttps = audit.Destinations.All(u => u.Scheme == "https"), imageRequests = 0, audioRequests = 0 });
    }
    else if (webOptIn) log.Write(new { kind = "research_skipped", reason = "Region call failed or timed out; do not overlap a potentially unsettled local model call. Run --research-only separately after settlement." });
    log.Write(new { kind = "summary", passed, failed, liveModel = true, liveWeb = webOptIn, modelTransport.ImageRequests, modelTransport.TextRequests, fullGestureEndToEnd = "NOT TESTED", installedApplication = "NOT TESTED", physicalAudio = "NOT TESTED", semanticOracle = "fixed owned facts and textual usefulness checks; inspect recorded responses" });
    return failed == 0 ? 0 : 1;
} catch (Exception e) { log.Write(new { kind = "blocked", error = e.GetType().Name, message = e.Message }); return 2; }
