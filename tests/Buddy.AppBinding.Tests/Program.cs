using Buddy.Server;
using Buddy.Windows;
using Microsoft.AspNetCore.DataProtection;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Text;
using System.Text.Json;

// Explicit diagnostic mode only. Resolve metadata through the production
// route; never observe the desktop, call StartAsync or dispatch an application.
if (args.SequenceEqual(new[] { "--readonly-calculator-resolver" })) {
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var method = typeof(WindowsRoutineAppBackend).GetMethod("ResolveAsync", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
    var pending = (Task<VerifiedAppLaunch>)method.Invoke(null, new object[] { "calculator", stop.Token })!;
    var launch = await pending.WaitAsync(stop.Token);
    if (!launch.IsPackaged || launch.Start is not null || launch.Registration is null || launch.PackageRegistration is null ||
        launch.AppUserModelId != "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" || Path.GetFileName(launch.MainExecutable) != "CalculatorApp.exe")
        throw new Exception("Calculator did not resolve the exact signed registration.");
    Console.WriteLine(JsonSerializer.Serialize(new { kind = "readonlyCalculatorResolver", launch.Alias, launch.IsPackaged, launch.PackageFullName,
        launch.AppUserModelId, executable = Path.GetFileName(launch.Executable), mainExecutable = Path.GetFileName(launch.MainExecutable),
        packageSignature = launch.PackageRegistration.SignatureKind.ToString(), packageHealthy = launch.PackageRegistration.Status.VerifyIsOK(),
        launchCalls = 0, foregroundCalls = 0, modelCalls = 0, profileCalls = 0 }));
    return;
}
// Opt-in only, pinned to the already recorded APP-57 window. No top-level
// enumeration, activation, focus, pixels, UIA text, profile or input calls.
if (args.SequenceEqual(new[] { "--readonly-calculator-frame-57" })) {
    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(15));
    var backend = new WindowsRoutineAppBackend(() => true, () => "");
    var frame = await backend.ReadOnlyCalculatorFrameAsync(new IntPtr(19466806), stop.Token);
    if (frame is not null && (frame.Host.ProcessId != 27628 || frame.Child.ProcessId != 24952 || frame.Child.Window != new IntPtr(68200)))
        throw new InvalidOperationException("The previously recorded frame identities changed.");
    Console.WriteLine(JsonSerializer.Serialize(new { kind = "readonlyCalculatorFrame", bound = frame is not null,
        host = frame?.Host.Window.ToInt64(), hostPid = frame?.Host.ProcessId, hostThread = frame?.Host.ThreadId, hostStarted = frame?.Host.ProcessStarted,
        child = frame?.Child.Window.ToInt64(), childPid = frame?.Child.ProcessId, childThread = frame?.Child.ThreadId, childStarted = frame?.Child.ProcessStarted,
        package = frame?.PackageFullName, launchCalls = 0, focusCalls = 0, titleOutput = 0, contentReads = 0 }));
    return;
}
if (args.Length != 0) throw new ArgumentException("Unknown test mode.");

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

foreach (var alias in new[] { "calculator", "camera", "spotify" }) {
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
Check(InstalledAppResolver.RegisteredIdentity("calculator") == ("Microsoft.WindowsCalculator_8wekyb3d8bbwe", "8wekyb3d8bbwe", "App"), "Calculator has one fixed Microsoft family/publisher/App identity");
Reject(() => InstalledAppResolver.RegisteredIdentity("CalculatorApp.exe"), "Executable names cannot choose another registered identity");
Check(InstalledAppResolver.RegisteredExecutable("calculator", packageRoot, "CalculatorApp.exe") == packageRoot + @"\CalculatorApp.exe", "Calculator manifest resolves only its fixed main executable");
foreach (var path in new[] { "calc.exe", "Calculator.exe", "WindowsCamera.exe", @"Other\CalculatorApp.exe", @"..\CalculatorApp.exe", @"C:\CalculatorApp.exe", "CalculatorApp.exe --flag", "CalculatorApp.exe:stream" })
    Reject(() => InstalledAppResolver.RegisteredExecutable("calculator", packageRoot, path), "Calculator rejects launcher/foreign/traversal/argument substitutions: " + path);
var calculator = new VerifiedAppLaunch("calculator", packageRoot + @"\CalculatorApp.exe", PackageFullName: "Microsoft.WindowsCalculator_11.2607.0.0_x64__8wekyb3d8bbwe",
    AppUserModelId: "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", PackageRoot: packageRoot, MainExecutable: packageRoot + @"\CalculatorApp.exe");
Check(InstalledAppResolver.MatchesRegisteredProcess(calculator, calculator.MainExecutable, calculator.PackageFullName), "Calculator postcondition binds exact OS package full name and main executable");
foreach (var package in new[] { "", "Microsoft.WindowsCalculator_11.2608.0.0_x64__8wekyb3d8bbwe", "Microsoft.WindowsCalculator_11.2607.0.0_x64__foreign", "Microsoft.WindowsCamera_11.2607.0.0_x64__8wekyb3d8bbwe" })
    Check(!InstalledAppResolver.MatchesRegisteredProcess(calculator, calculator.MainExecutable, package), "Calculator rejects missing/updated/foreign process package identity");
foreach (var path in new[] { packageRoot + @"\calc.exe", packageRoot + @"\WindowsCamera.exe", @"C:\Other\CalculatorApp.exe", packageRoot + @"\Other\CalculatorApp.exe" })
    Check(!InstalledAppResolver.MatchesRegisteredProcess(calculator, path, calculator.PackageFullName), "Calculator rejects same-name foreign path and helper process");
Check(!calculator.SameIdentity(calculator with { AppUserModelId = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!Other" }) &&
    !calculator.SameIdentity(calculator with { PackageFullName = "Microsoft.WindowsCalculator_11.2608.0.0_x64__8wekyb3d8bbwe" }), "Calculator AppId/version changes invalidate the approved checkpoint");
await RejectAsync(() => calculator.StartAsync(default), "Calculator metadata without OS registration cannot activate or fall back to calc.exe");
using (var stoppedCalculator = new CancellationTokenSource()) {
    stoppedCalculator.Cancel(); await RejectAsync(() => calculator.StartAsync(stoppedCalculator.Token), "Stop wins before Calculator activation metadata checks");
}
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

// Pure frame fixtures: no process, window, signature or package APIs are called.
var instant = DateTimeOffset.UtcNow;
string hostPath = @"C:\Windows\System32\ApplicationFrameHost.exe";
var hostNode = new AppFrameNode(new(new(100), 101, 102, 103, "ApplicationFrameHost"), IntPtr.Zero, new(100),
    "ApplicationFrameWindow", true, false, true, hostPath, "");
var childNode = new AppFrameNode(new(new(200), 201, 202, 203, "CalculatorApp"), new(100), new(100),
    "Windows.UI.Core.CoreWindow", true, false, true, calculator.MainExecutable, calculator.PackageFullName);
var sample = new AppFrameSample(instant, new(100), hostNode, new[] { childNode }, true, true);
ComputerFrameBinding? Bind(AppFrameSample value) => AppFrameBinding.Bind(calculator, value, hostPath, instant);
var frameReceipt = Bind(sample) ?? throw new InvalidOperationException("Expected the valid pure frame fixture to bind.");
Check(frameReceipt.Child == childNode.Identity && frameReceipt.Host == hostNode.Identity,
    "Framed Calculator retains actual child HWND/PID separately from Windows host");
Check(AppFrameBinding.Confirm(calculator, sample, sample, hostPath, instant, default) == frameReceipt,
    "Two unchanged trusted same-root direct-child samples confirm exact binding");
Check(AppFrameBinding.Recheck(sample, (window, packaged) => packaged ? childNode : hostNode, default),
    "Within-sample reread preserves unchanged full host and child metadata");
foreach (var changed in new[] { hostNode with { Parent = new(999) }, hostNode with { Root = new(999) },
    hostNode with { ClassName = "Other" }, hostNode with { Visible = false }, hostNode with { Cloaked = true } })
    Check(!AppFrameBinding.Recheck(sample, (window, packaged) => packaged ? childNode : changed, default),
        "Live host topology/visibility change after enumeration invalidates the whole sample");
{
    bool childInspected = false;
    Check(!AppFrameBinding.Recheck(sample, (window, packaged) => {
        if (packaged) { childInspected = true; return childNode; }
        return childInspected ? hostNode with { Visible = false } : hostNode;
    }, default), "Host mutation during final child reread is rejected before sample completion");
}
foreach (var changed in new[] { childNode with { Parent = new(999) }, childNode with { Root = new(999) },
    childNode with { Visible = false }, childNode with { Identity = childNode.Identity with { ProcessStarted = 999 } } })
    Check(!AppFrameBinding.Recheck(sample, (window, packaged) => packaged ? changed : hostNode, default),
        "Child mutation after initial node read cannot survive final topology/identity reread");
using (var stopDuringReread = new CancellationTokenSource()) {
    await RejectAsync(() => Task.FromResult(AppFrameBinding.Recheck(sample, (window, packaged) => {
        if (packaged) { stopDuringReread.Cancel(); return childNode; }
        return hostNode;
    }, stopDuringReread.Token)), "Stop during metadata reread prevents a completed frame sample");
}
Check(!BoundedComputerUse.MatchesAppName("calculator", "ApplicationFrameHost"), "Generic host name never becomes Calculator alias");
foreach (var (altered, name) in new (AppFrameSample, string)[] {
    (sample with { Foreground = new(900) }, "other foreground root"),
    (sample with { Complete = false }, "incomplete sibling enumeration"),
    (sample with { TrustedHost = false }, "unverified host catalog"),
    (sample with { At = instant.AddSeconds(-6) }, "stale metadata"),
    (sample with { At = instant.AddTicks(1) }, "future metadata"),
    (sample with { Children = Array.Empty<AppFrameNode>() }, "missing child"),
    (sample with { Children = Enumerable.Repeat(childNode, 33).ToArray() }, "unbounded child set"),
    (sample with { Children = new[] { childNode, childNode with { Identity = childNode.Identity with { Window = new(201), ProcessId = 301 } } } }, "two visible CoreWindows"),
    (sample with { Children = new[] { childNode, childNode with { Identity = childNode.Identity with { Window = new(201), App = "Other" }, PackageFullName = "other" } } }, "ambiguous foreign visible CoreWindow")
}) Check(Bind(altered) is null, "Frame rejects " + name);
foreach (var (altered, name) in new (AppFrameNode, string)[] {
    (hostNode with { Parent = new(999) }, "nested host"), (hostNode with { Root = new(999) }, "wrong host root"),
    (hostNode with { Visible = false }, "hidden/minimized/zero-sized host"), (hostNode with { Cloaked = true }, "cloaked host"),
    (hostNode with { AccessAllowed = false }, "unknown host elevation/access"), (hostNode with { ClassName = "Other" }, "foreign host class"),
    (hostNode with { Executable = @"C:\Other\ApplicationFrameHost.exe" }, "same basename outside System32"),
    (hostNode with { Executable = hostPath + ":stream" }, "host path stream"),
    (hostNode with { Identity = hostNode.Identity with { App = "Other" } }, "unrelated host process"),
    (hostNode with { Identity = hostNode.Identity with { ProcessId = 0 } }, "missing host PID"),
    (hostNode with { Identity = hostNode.Identity with { ThreadId = 0 } }, "missing host thread"),
    (hostNode with { Identity = hostNode.Identity with { ProcessStarted = 0 } }, "missing host creation time")
}) Check(Bind(sample with { Host = altered }) is null, "Frame rejects " + name);
foreach (var (altered, name) in new (AppFrameNode, string)[] {
    (childNode with { Parent = new(999) }, "non-direct descendant"), (childNode with { Root = new(999) }, "cross-window child"),
    (childNode with { Visible = false }, "hidden/zero-sized child"), (childNode with { Cloaked = true }, "cloaked child"),
    (childNode with { AccessAllowed = false }, "unknown child elevation/access"), (childNode with { ClassName = "Other" }, "foreign child class"),
    (childNode with { Identity = childNode.Identity with { Window = hostNode.Identity.Window } }, "host masquerading as child"),
    (childNode with { Identity = childNode.Identity with { ProcessId = hostNode.Identity.ProcessId } }, "host PID masquerading as child"),
    (childNode with { Identity = childNode.Identity with { ProcessId = 0 } }, "missing child PID"),
    (childNode with { Identity = childNode.Identity with { ThreadId = 0 } }, "missing child thread"),
    (childNode with { Identity = childNode.Identity with { ProcessStarted = 0 } }, "missing child creation time"),
    (childNode with { Identity = childNode.Identity with { App = "Other" } }, "foreign child process"),
    (childNode with { Executable = @"C:\Other\CalculatorApp.exe" }, "same basename outside package root"),
    (childNode with { Executable = packageRoot + @"\calc.exe" }, "launcher instead of main executable"),
    (childNode with { PackageFullName = calculator.PackageFullName.Replace("11.2607", "11.2608") }, "updated package version"),
    (childNode with { PackageFullName = "Other_11.2607.0.0_x64__8wekyb3d8bbwe" }, "different package family"),
    (childNode with { PackageFullName = "" }, "missing package identity")
}) Check(Bind(sample with { Children = new[] { altered } }) is null, "Frame rejects " + name);
foreach (var (second, name) in new (AppFrameSample, string)[] {
    (sample with { Host = hostNode with { Identity = hostNode.Identity with { ProcessId = 401 } } }, "host PID reuse"),
    (sample with { Host = hostNode with { Identity = hostNode.Identity with { ThreadId = 402 } } }, "host thread substitution"),
    (sample with { Host = hostNode with { Identity = hostNode.Identity with { ProcessStarted = 403 } } }, "host process replacement"),
    (sample with { Children = new[] { childNode with { Identity = childNode.Identity with { ProcessId = 501 } } } }, "child PID substitution"),
    (sample with { Children = new[] { childNode with { Identity = childNode.Identity with { ThreadId = 502 } } } }, "child thread substitution"),
    (sample with { Children = new[] { childNode with { Identity = childNode.Identity with { ProcessStarted = 503 } } } }, "same PID with later process creation"),
    (sample with { Children = new[] { childNode with { Identity = childNode.Identity with { Window = new(504) } } } }, "replacement child HWND"),
    (sample with { Children = new[] { childNode with { Parent = new(999) } } }, "child reparenting during verification"),
    (sample with { Children = new[] { childNode with { Root = new(999) } } }, "child moved to another root"),
    (sample with { At = instant.AddTicks(-1) }, "reversed sample order")
}) Check(AppFrameBinding.Confirm(calculator, sample, second, hostPath, instant, default) is null, "Repeated binding rejects " + name);
foreach (var other in new[] { camera, spotify, calculator with { Alias = "notepad" }, calculator with { AppUserModelId = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!Other" } })
    Check(AppFrameBinding.Bind(other, sample, hostPath, instant) is null, "Frame exception cannot broaden to another app or AppId");
foreach (var changed in new[] { calculator with { PackageFullName = calculator.PackageFullName.Replace("11.2607", "11.2608") },
    calculator with { PackageRoot = @"C:\Other" }, calculator with { MainExecutable = @"C:\Other\CalculatorApp.exe" },
    calculator with { AppUserModelId = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!Other" } })
    Check(!AppFrameBinding.MatchesPackage(frameReceipt, changed), "Consumer cannot adopt updated package/root/main path/AppId");
using (var stopped = new CancellationTokenSource()) {
    stopped.Cancel(); await RejectAsync(() => Task.FromResult(AppFrameBinding.Confirm(calculator, sample, sample, hostPath, instant, stopped.Token)), "Stop rejects otherwise-valid final frame confirmation");
    await RejectAsync(() => Task.FromResult(FixedFrameHostTrust.Acquire(FixedFrameHostTrust.ExpectedPath, stopped.Token)), "Stopped host trust performs no native file/trust checks");
}
Reject(() => FixedFrameHostTrust.Acquire(@"C:\Other\ApplicationFrameHost.exe", default), "Host trust rejects foreign path before native calls");
Check(FixedFrameHostTrust.ExpectedPublisher("Microsoft Windows") && !FixedFrameHostTrust.ExpectedPublisher("Other") &&
    !FixedFrameHostTrust.ExpectedPublisher("Microsoft Corporation"), "Catalog publisher policy remains the exact Windows signer");
{
    var fake = new Backend { TransformAfter = value => value with { Window = frameReceipt.Child, Frame = frameReceipt } };
    var result = await RoutineAppOpen.RunAsync("Open Calculator", () => true, () => "", default, fake);
    Check(result.Verified && result.After!.Window == frameReceipt.Child && result.After.Frame == frameReceipt && fake.Dispatches == 1,
        "Bounded controller preserves honest child identity on one framed launch");
}
foreach (var malformed in new[] { frameReceipt with { Host = frameReceipt.Child }, frameReceipt with { Child = frameReceipt.Host },
    frameReceipt with { Host = frameReceipt.Host with { ProcessStarted = 0 } }, frameReceipt with { AppUserModelId = "other!App" } }) {
    var fake = new Backend { TransformAfter = value => value with { Window = frameReceipt.Child, Frame = malformed } };
    var result = await RoutineAppOpen.RunAsync("Open Calculator", () => true, () => "", default, fake);
    Check(!result.Verified && fake.Dispatches == 1 && result.After is null, "Malformed frame receipt never authorizes replay or selection");
}
{
    var fake = new Backend { TransformAfter = value => value with { Window = value.Window with { App = "WindowsCamera" }, Frame = frameReceipt } };
    var result = await RoutineAppOpen.RunAsync("Open Camera", () => true, () => "", default, fake);
    Check(!result.Verified && fake.Dispatches == 1, "Calculator frame cannot satisfy another app's dispatch");
}
using (var stopped = new CancellationTokenSource()) {
    var fake = new Backend { StopAtVerify = stopped, TransformAfter = value => value with { Window = frameReceipt.Child, Frame = frameReceipt } };
    await RejectAsync(() => RoutineAppOpen.RunAsync("Open Calculator", () => true, () => "", stopped.Token, fake), "Stop after framed verification discards completion");
    Check(fake.Dispatches == 1, "Late Stop never repeats an already dispatched launch");
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
    internal CancellationTokenSource? StopAtVerify;
    internal Func<ComputerObservation, ComputerObservation>? TransformAfter;
    public Task<ComputerObservation> ObserveAsync(Guid request, CancellationToken ct) { Trace.Add("observe"); return Task.FromResult(new ComputerObservation(request, Guid.NewGuid(), DateTimeOffset.UtcNow, new(new(1), 2, 3, 4, "owned"))); }
    public Task<ComputerObservation> CheckpointAsync(ComputerObservation before, string alias, CancellationToken ct) { Trace.Add("checkpoint"); StopAtCheckpoint?.Cancel(); return Task.FromResult(before); }
    public Task<ComputerDispatch> DispatchAsync(ComputerObservation before, string alias, CancellationToken ct) { Trace.Add("dispatch"); Dispatches++; Alias = alias; return Task.FromResult(new ComputerDispatch(before.RequestId, before.Id, alias)); }
    public Task<ComputerVerification> VerifyAsync(ComputerObservation before, ComputerDispatch dispatched, CancellationToken ct) {
        Trace.Add("verify"); var after = new ComputerObservation(before.RequestId, Guid.NewGuid(), DateTimeOffset.UtcNow, new(new(5), 6, 7, 8, dispatched.Alias == "camera" ? "WindowsCamera" : dispatched.Alias));
        after = TransformAfter?.Invoke(after) ?? after; StopAtVerify?.Cancel();
        return Task.FromResult(new ComputerVerification(true, after, "Owned fake verification"));
    }
}
