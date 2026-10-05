using Buddy.Server;
using Buddy.Windows;
using Microsoft.AspNetCore.DataProtection;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Text.Json;

int checks = 0;
void Check(bool value, string label) { if (!value) throw new Exception("FAIL: " + label); checks++; Console.WriteLine("PASS: " + label); }
void Reject(Action run, string label) { try { run(); } catch (Exception ex) when (ex is BuddyException or InvalidOperationException) { Check(true, label); return; } throw new Exception("FAIL: " + label); }
async Task RejectAsync(Func<Task> run, string label) { try { await run(); } catch (Exception ex) when (ex is BuddyException or InvalidOperationException or OperationCanceledException) { Check(true, label); return; } throw new Exception("FAIL: " + label); }
AssistantPlan Plan(string value) => new("Open Calculator via Comet", [new("open", Value: value, Description: "Open a different app", Risk: "low")]);
var empty = new ScreenContext("owned", "Owned test view", []);

foreach (var (query, alias) in new[] { ("Open Camera app.", "camera"), ("Open spotify", "spotify"), ("Please open Windows Camera!", "camera"), ("Open Spotify app", "spotify"), ("Open Notepad", "notepad"), ("Open Comet Browser", "comet"), ("Open File Explorer", "explorer") }) {
    Check(RoutineAppOpen.TryGetAlias(query, out var parsed) && parsed == alias, "Exact user launch binds to " + alias);
    var reviewed = ActionPolicy.ValidateForReview(Plan(alias), query);
    var reviewedAction = reviewed.Actions!.Single();
    Check(reviewedAction.Value == alias && reviewedAction.Risk == "high", "General-plan review retains high-risk approval: " + alias);
    Check(reviewed.Summary.Contains(AppLaunchIntent.DisplayName(alias)) && reviewedAction.Description == "Open " + AppLaunchIntent.DisplayName(alias) + ".", "Host replaces misleading model card text: " + alias);
}
foreach (var (query, wrong) in new[] {
    ("Open Camera app.", "calculator"), ("Open spotify", "comet"), ("Open Notepad", "calculator"), ("Open Explorer", "notepad"),
    ("Open Spotify", "https://open.spotify.com/"), ("Open Camera", "https://example.com/camera"), ("Open unknown app", "notepad"),
    ("Open https://example.com/manual", "https://example.org/manual"), ("Open https://example.com/manual", "https://example.com/other"),
    ("Open https://example.com/manual?x=1", "https://example.com/manual?x=2"), ("Open https://example.com/manual#part", "https://example.com/manual"),
    ("Do not open https://example.com/manual", "https://example.com/manual"), ("Do not open Notepad", "notepad"),
    ("Summarize the selected text", "calculator")
}) {
    Reject(() => ActionPolicy.ValidateForReview(Plan(wrong), query), "Reject substituted review target: " + query + " -> " + wrong);
    Reject(() => ActionPolicy.Validate(Plan(wrong), query), "Reject substituted execution target: " + query + " -> " + wrong);
}
var url = "https://example.com/manual?x=1#part";
Check(ActionPolicy.Validate(Plan(url), "Open " + url).Actions!.Single().Value == url, "Exact public URL path/query/fragment survives binding");
Reject(() => ActionPolicy.ValidateForReview(new("Extra", [new("open", Value: "spotify"), new("open", Value: "comet")]), "Open spotify"), "Exact launch cannot add a second app");
Reject(() => ActionPolicy.ValidateForReview(new("Extra", [new("open", Value: "notepad"), new("type", Role: "Edit", Value: "hello")]), "Open Notepad"), "Exact launch cannot add an in-app change");
Check(ActionPolicy.ValidateForReview(new("Which app?", []), "Open unknown app").Actions!.Count == 0, "Unsupported request permits clarification");
foreach (string query in new[] { "open notepad.exe", "open C:\\Windows\\notepad.exe", "open calculator & notepad", "open notepad --flag", "open notepad and type hello", "open\nnotepad", "open https://example.com", "do not open camera", "open Camera then record", "open Spotify using Comet Browser" })
    Check(!RoutineAppOpen.TryGetAlias(query, out _), "No direct launch for nonexact task: " + query.Replace('\n', ' '));

foreach (var (query, wrong) in new[] { ("Open Camera app.", "calculator"), ("Open spotify", "comet"), ("Open Notepad", "calculator"), ("Open https://example.com/manual", "https://example.org/other") }) {
    await using var f = new Fixture(); f.Model.Add(Plan(wrong)); f.Model.Add(Plan(wrong));
    var result = await f.Service.PlanAgent(new(query, empty), default);
    Check(result.Actions is { Count: 0 } && f.Model.Calls == 2, "Two wrong model plans end without action: " + query);
}
await using (var f = new Fixture()) {
    f.Model.Add(Plan("calculator")); f.Model.Add(Plan("camera"));
    var result = await f.Service.PlanAgent(new("Open Camera app.", empty), default);
    Check(result.Actions!.Single().Value == "camera" && f.Model.Calls == 2, "One repair can recover the originally requested Camera");
}
await using (var f = new Fixture()) {
    var result = await f.Service.PlanAgent(new("Open unknown app", empty), default);
    Check(result.Actions is { Count: 0 } && f.Model.Calls == 0, "Unknown exact app clarifies without a model substitution");
}
await using (var f = new Fixture()) {
    f.Model.Add(new AgentDecision("continue", "Open Calculator", [new("open", Value: "calculator")]));
    await RejectAsync(() => f.Service.ContinueAgent(new("Open Camera app.", empty, [new(1, new("read", Role: "Text"), true, "Owned read")], 24), default), "Continuation rejects Camera-to-Calculator substitution");
}
await using (var f = new Fixture()) {
    await RejectAsync(() => f.Service.ContinueAgent(new("Open Camera app.", empty, [new(1, new("open", Value: "calculator"), true, "Wrong-app receipt")], 24), default), "Wrong-app prior receipt cannot support model completion");
    Check(f.Model.Calls == 0, "Wrong-app receipt is rejected before inference");
}
using (var stop = new CancellationTokenSource()) {
    stop.Cancel(); await using var f = new Fixture();
    await RejectAsync(() => f.Service.PlanAgent(new("Open Comet Browser", empty), stop.Token), "Stop wins before deterministic Comet plan");
    Check(f.Model.Calls == 0, "Pre-cancel has no model call");
}

foreach (var alias in new[] { "camera", "spotify" }) {
    var fake = new Backend();
    var result = await RoutineAppOpen.RunAsync("Open " + alias, () => true, () => "", default, fake);
    Check(result.Verified && fake.Dispatches == 1 && fake.Alias == alias, "Mock direct route dispatches exactly requested " + alias + " once");
    Check(fake.Trace.SequenceEqual(new[] { "observe", "checkpoint", "dispatch", "verify" }), "Observation/checkpoint/dispatch/verify retained: " + alias);
}
{
    var fake = new Backend();
    await RejectAsync(() => RoutineAppOpen.RunApprovedAsync("Open Camera", new("open", Value: "calculator"), () => true, () => "", default, fake), "Effect boundary rejects stale wrong-app approved action");
    Check(fake.Trace.Count == 0, "Wrong-app approval never reaches backend observation");
}
{
    var fake = new Backend();
    await RejectAsync(() => RoutineAppOpen.RunAsync("Open Camera", () => false, () => "", default, fake), "Agent-disabled blocks direct Camera route");
    Check(fake.Dispatches == 0, "Disabled Agent never dispatches");
}
{
    using var stop = new CancellationTokenSource(); var fake = new Backend { StopAtCheckpoint = stop };
    await RejectAsync(() => RoutineAppOpen.RunAsync("Open Spotify", () => true, () => "", stop.Token, fake), "Stop after checkpoint prevents Spotify launch");
    Check(fake.Dispatches == 0, "Stopped checkpoint dispatch count remains zero");
}
Check(BoundedComputerUse.MatchesAppName("camera", "WindowsCamera") && !BoundedComputerUse.MatchesAppName("camera", "calculator"), "Camera postcondition recognizes only its actual process name");
Check(BoundedComputerUse.MatchesAppName("spotify", "Spotify") && !BoundedComputerUse.MatchesAppName("spotify", "comet"), "Spotify postcondition cannot be satisfied by Comet");
string packageRoot = @"C:\Program Files\WindowsApps\OwnedPackage";
Check(InstalledAppResolver.RegisteredExecutable("camera", packageRoot, "WindowsCamera.exe") == packageRoot + @"\WindowsCamera.exe", "Camera manifest resolves its fixed executable");
Check(InstalledAppResolver.RegisteredExecutable("spotify", packageRoot, @"Spotify\Spotify.exe") == packageRoot + @"\Spotify\Spotify.exe", "Spotify manifest supports a contained installed subdirectory");
Check(InstalledAppResolver.RegisteredExecutable("spotify", packageRoot, "SpotifyMigrator.exe") == packageRoot + @"\SpotifyMigrator.exe", "Audited Spotify App Id may activate its fixed Store migration entry");
foreach (var path in new[] { @"..\Spotify.exe", @"C:\Spotify.exe", @"\\server\Spotify.exe", @"Spotify.exe:stream", @"folder/Spotify.exe", @"folder\\Spotify.exe", @"folder\.\Spotify.exe", "Calculator.exe", "Spotify.exe --flag" })
    Reject(() => InstalledAppResolver.RegisteredExecutable("spotify", packageRoot, path), "Reject unsafe/foreign manifest executable: " + path);
foreach (var path in new[] { "SpotifyLauncher.exe", "spotify_cli.exe", "SpotifyXboxGamebarWebView.exe", "SpotifyStartupTask.exe", @"Other\Spotify.exe", @"Other\SpotifyMigrator.exe", "WindowsCamera.exe" })
    Reject(() => InstalledAppResolver.RegisteredExecutable("spotify", packageRoot, path), "Alternative entry/helper cannot become Spotify activation: " + path);
Reject(() => InstalledAppResolver.RegisteredExecutable("camera", packageRoot, "SpotifyMigrator.exe"), "Spotify migration exception cannot cross into Camera");
var spotify = new VerifiedAppLaunch("spotify", packageRoot + @"\SpotifyMigrator.exe", PackageFullName: "SpotifyAB.SpotifyMusic_1.303.264.0_x64__zpdnekdrzrea0",
    AppUserModelId: "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", PackageRoot: packageRoot, MainExecutable: packageRoot + @"\Spotify.exe");
Check(spotify.IsPackaged && spotify.SameIdentity(spotify with { }), "Registered package identity is stable across an unchanged re-observation");
Check(InstalledAppResolver.MatchesRegisteredProcess(spotify, packageRoot + @"\Spotify.exe", spotify.PackageFullName), "Same signed package main Spotify process satisfies the metadata postcondition");
foreach (var path in new[] { packageRoot + @"\SpotifyMigrator.exe", packageRoot + @"\SpotifyLauncher.exe", packageRoot + @"\spotify_cli.exe", @"C:\Other\Spotify.exe", packageRoot + @"\Other\Spotify.exe" })
    Check(!InstalledAppResolver.MatchesRegisteredProcess(spotify, path, spotify.PackageFullName), "Helper or same-name foreign process cannot count as the main app: " + Path.GetFileName(path));
foreach (var package in new[] { "", "SpotifyAB.SpotifyMusic_1.303.265.0_x64__zpdnekdrzrea0", "SpotifyAB.SpotifyMusic_1.303.264.0_x64__wrongpublisher", "Other_1.0.0.0_x64__zpdnekdrzrea0" })
    Check(!InstalledAppResolver.MatchesRegisteredProcess(spotify, spotify.MainExecutable, package), "Missing/updated/foreign OS package identity cannot satisfy a reviewed launch");
foreach (var altered in new[] {
    spotify with { PackageFullName = "SpotifyAB.SpotifyMusic_1.303.265.0_x64__zpdnekdrzrea0" },
    spotify with { AppUserModelId = "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!SpotifyLauncher" },
    spotify with { Executable = packageRoot + @"\SpotifyLauncher.exe" },
    spotify with { MainExecutable = packageRoot + @"\SpotifyMigrator.exe" },
    spotify with { PackageRoot = @"C:\Other" }, spotify with { Alias = "camera" }
}) Check(!spotify.SameIdentity(altered), "Registration/full-version/helper/main-path changes invalidate the checkpoint");
var camera = new VerifiedAppLaunch("camera", packageRoot + @"\WindowsCamera.exe", PackageFullName: "Microsoft.WindowsCamera_2026.2607.1.0_x64__8wekyb3d8bbwe",
    AppUserModelId: "Microsoft.WindowsCamera_8wekyb3d8bbwe!App", PackageRoot: packageRoot, MainExecutable: packageRoot + @"\WindowsCamera.exe");
Check(InstalledAppResolver.MatchesRegisteredProcess(camera, camera.MainExecutable, camera.PackageFullName), "Camera main process requires its exact package version and main executable");
Check(!InstalledAppResolver.MatchesRegisteredProcess(camera, spotify.MainExecutable, camera.PackageFullName), "Same package text cannot substitute Spotify for Camera executable");
await RejectAsync(() => spotify.StartAsync(default), "Package metadata without an OS registration refuses before activation or classic fallback");
foreach (var partial in new[] {
    new VerifiedAppLaunch("spotify", "inert-fixture.exe", PackageFullName: spotify.PackageFullName),
    new VerifiedAppLaunch("spotify", "inert-fixture.exe", AppUserModelId: spotify.AppUserModelId),
    new VerifiedAppLaunch("spotify", "inert-fixture.exe", PackageRoot: packageRoot),
    new VerifiedAppLaunch("spotify", "inert-fixture.exe", MainExecutable: spotify.MainExecutable)
}) {
    Check(partial.IsPackaged, "Partial package authority cannot become a classic executable request");
    await RejectAsync(() => partial.StartAsync(default), "Incomplete package authority has no launch fallback");
}
using (var stopped = new CancellationTokenSource()) {
    stopped.Cancel(); await RejectAsync(() => camera.StartAsync(stopped.Token), "Stop precedes every package activation check");
}
Console.WriteLine($"APP BINDING MOCK/PURE CHECKS PASSED: {checks}; no native discovery, application launches, profiles, models or network");

sealed class Fixture : IAsyncDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "Buddy-app47-" + Guid.NewGuid());
    private readonly HttpClient client;
    internal readonly Model Model = new();
    internal BuddyService Service { get; }
    internal Fixture() { client = new(Model) { BaseAddress = new("http://127.0.0.1:11434") }; Service = new(new StateStore(folder, new EphemeralDataProtectionProvider()), new(client)) { AgentEnabled = true }; }
    public ValueTask DisposeAsync() {
        client.Dispose(); string path = Path.GetFullPath(folder), temporary = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(path).StartsWith("Buddy-app47-", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe fixture cleanup path.");
        if (Directory.Exists(path)) Directory.Delete(path, true); return ValueTask.CompletedTask;
    }
}
sealed class Model : HttpMessageHandler
{
    private readonly Queue<string> replies = new();
    internal int Calls;
    internal void Add(object value) => replies.Enqueue(JsonSerializer.Serialize(value, StateStore.Json));
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
        ct.ThrowIfCancellationRequested(); Calls++;
        if (replies.Count == 0) throw new Exception("Unexpected fake model call");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { message = new { content = replies.Dequeue() }, done = true }), Encoding.UTF8, "application/json") });
    }
}
sealed class Backend : IComputerUseBackend
{
    internal readonly List<string> Trace = [];
    internal int Dispatches;
    internal string Alias = "";
    internal CancellationTokenSource? StopAtCheckpoint;
    public Task<ComputerObservation> ObserveAsync(Guid request, CancellationToken ct) { Trace.Add("observe"); return Task.FromResult(new ComputerObservation(request, Guid.NewGuid(), DateTimeOffset.UtcNow, new(new(1), 2, 3, 4, "owned"))); }
    public Task<ComputerObservation> CheckpointAsync(ComputerObservation before, string alias, CancellationToken ct) { Trace.Add("checkpoint"); StopAtCheckpoint?.Cancel(); return Task.FromResult(before); }
    public Task<ComputerDispatch> DispatchAsync(ComputerObservation before, string alias, CancellationToken ct) { Trace.Add("dispatch"); Dispatches++; Alias = alias; return Task.FromResult(new ComputerDispatch(before.RequestId, before.Id, alias)); }
    public Task<ComputerVerification> VerifyAsync(ComputerObservation before, ComputerDispatch dispatched, CancellationToken ct) { Trace.Add("verify"); return Task.FromResult(new ComputerVerification(true, new(before.RequestId, Guid.NewGuid(), DateTimeOffset.UtcNow, new(new(5), 6, 7, 8, dispatched.Alias == "camera" ? "WindowsCamera" : dispatched.Alias)), "Owned fake verification")); }
}
