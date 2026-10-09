using Buddy.Windows;

int checks = 0;
void Check(bool value, string label) { if (!value) throw new Exception("FAIL: " + label); checks++; Console.WriteLine("PASS: " + label); }
async Task<bool> Refused(Func<Task> action) { try { await action(); return false; } catch (InvalidOperationException) { return true; } }
async Task<bool> Cancelled(Task action) { try { await action.WaitAsync(TimeSpan.FromSeconds(2)); return false; } catch (OperationCanceledException) { return true; } }
var limits = new ComputerUseLimits(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2), 3, TimeSpan.Zero);
BoundedComputerUse Controller(Fake backend) => new(backend, limits, () => backend.Now);

foreach (var (query, expected) in new[] { ("Open Notepad", "notepad"), ("please open calculator!", "calculator"), (" OPEN FILE EXPLORER. ", "explorer"), ("open explorer", "explorer"), ("Open Comet Browser", "comet"), ("open comet", "comet") })
    Check(RoutineAppOpen.TryGetAlias(query, out var alias) && alias == expected, "Exact explicit request parses: " + query);
foreach (string query in new[] { "notepad", "", "Open Notepad and type hello", "guide me through Comet browser", "open https://example.com", "open C:\\Windows\\notepad.exe", "open notepad.exe", "open notepad --flag", "open calculator & notepad", "open 'notepad'", "open Comet Browser then click", "please open notepad please", "open cmd", "open powershell", "open calc", "can you open notepad?", "do not open notepad", "open\nnotepad", "open notepad\nthen save" })
    Check(!RoutineAppOpen.TryGetAlias(query, out _), "Mixed, unsupported or nonexact query stays outside routine launch: " + query.Replace('\n', ' '));
using (var cancelled = new CancellationTokenSource()) {
    cancelled.Cancel();
    Check(await Cancelled(RoutineAppOpen.RunAsync("Open Notepad", () => true, () => "", cancelled.Token)), "Production entry refuses pre-cancelled call before any Windows observation");
}
Check(await Refused(() => RoutineAppOpen.RunAsync("Open Notepad", () => false, () => "", default)), "Disabled Agent cannot reach the production backend");
Check(await Refused(() => RoutineAppOpen.RunAsync("Open Notepad and type hello", () => true, () => "", default)), "Production entry rejects mixed request before any Windows observation");

foreach (string alias in new[] { "notepad", "calculator", "explorer", "comet" }) {
    var fake = new Fake(); var result = await Controller(fake).RunAsync(alias, default);
    Check(result.Verified && result.ActionDispatched && result.ActionCount == 1 && result.VerificationCount == 1 && fake.Dispatches == 1, "Observe/checkpoint/one dispatch/fresh verify succeeds for " + alias);
    Check(fake.Trace.SequenceEqual(new[] { "observe", "checkpoint", "dispatch", "verify" }), "Single-action ordering is enforced for " + alias);
}
foreach (string alias in new[] { "", "https://example.com", "notepad.exe", "comet --flag", "shell", "NOTEPAD" }) {
    var fake = new Fake(); Check(await Refused(() => Controller(fake).RunAsync(alias, default)) && fake.Trace.Count == 0, "Canonical backend scope rejects unsupported action target: " + alias);
}
foreach (var mutation in new Func<ComputerObservation, ComputerObservation>[] {
    o => o with { RequestId = Guid.NewGuid() }, o => o with { Id = Guid.Empty }, o => o with { Complete = false },
    o => o with { At = o.At.AddSeconds(-6) }, o => o with { At = o.At.AddSeconds(1) },
    o => o with { Window = o.Window with { Window = IntPtr.Zero } }, o => o with { Window = o.Window with { ProcessId = 0 } },
    o => o with { Window = o.Window with { ThreadId = 0 } }, o => o with { Window = o.Window with { ProcessStarted = 0 } },
    o => o with { Window = o.Window with { App = "" } }
}) {
    var fake = new Fake { MutateObservation = mutation };
    Check(await Refused(() => Controller(fake).RunAsync("notepad", default)) && fake.Dispatches == 0, "Invalid, blank, stale or foreign observation stops before action");
}
foreach (var mutation in new Func<ComputerObservation, ComputerObservation>[] {
    o => o with { Id = Guid.NewGuid() }, o => o with { Window = o.Window with { Window = new IntPtr(999) } },
    o => o with { Window = o.Window with { ProcessId = 77 } }, o => o with { Window = o.Window with { ThreadId = 88 } },
    o => o with { Window = o.Window with { ProcessStarted = o.Window.ProcessStarted + 1 } }, o => o with { Window = o.Window with { App = "other" } }
}) {
    var fake = new Fake { MutateCheckpoint = mutation };
    Check(await Refused(() => Controller(fake).RunAsync("notepad", default)) && fake.Dispatches == 0, "Changed observation/window/process identity at checkpoint never dispatches");
}
{
    var fake = new Fake(); fake.MutateCheckpoint = o => { fake.Now = fake.Now.AddSeconds(6); return o with { At = fake.Now }; };
    Check(await Refused(() => Controller(fake).RunAsync("notepad", default)) && fake.Dispatches == 0, "Refreshing checkpoint cannot revive an expired original observation");
}
{
    var fake = new Fake { PendingVerifications = 2 }; var result = await Controller(fake).RunAsync("comet", default);
    Check(result.Verified && fake.Dispatches == 1 && fake.Verifications == 3, "Window handoff permits bounded read-only recovery without repeating launch");
}
{
    var fake = new Fake { PendingVerifications = 20 }; var result = await Controller(fake).RunAsync("notepad", default);
    Check(!result.Verified && result.ActionDispatched && fake.Dispatches == 1 && fake.Verifications == 3, "Unverified app stays explicitly uncertain after bounded checks, with no blind replay");
}
foreach (var (reason, detail) in new[] {
    ("Waiting for the requested app window.", "no visible foreground window"),
    ("The foreground app does not match the request.", "final foreground app did not match"),
    ("The Calculator frame and signed app process could not be bound to this foreground window.", "frame could not be bound"),
    ("The app executable identity could not be verified.", "executable identity could not be verified"),
    ("The installed app identity changed after launch.", "installed app identity changed"),
    ("The foreground process is not the same signed package's main application.", "did not match the signed package"),
    ("The requested app window changed during verification.", "window changed during the final check")
}) {
    var fake = new Fake { PendingVerifications = 30, PendingReason = _ => reason };
    var result = await new BoundedComputerUse(fake, limits with { VerificationAttempts = 30 }, () => fake.Now).RunAsync("calculator", default);
    Check(!result.Verified && result.After is null && result.Message.Contains(detail) && result.Message.EndsWith("No launch was repeated."), "The final host verification reason survives without claiming completion: " + detail);
    Check(result.ActionDispatched && result.ActionCount == 1 && result.VerificationCount == 30 && fake.Dispatches == 1 && fake.Verifications == 30, "Thirty failed checks still cause exactly one dispatch");
}
foreach (string? unknown in new string?[] { "private document title", "C:\\private\\secret.txt", "https://private.invalid/?token=x", "The foreground app does not match the request. private suffix", "verified", null, new string('x', 20000) }) {
    var fake = new Fake { PendingVerifications = 3, PendingReason = n => n < 3 ? "The foreground app does not match the request." : unknown };
    var result = await Controller(fake).RunAsync("calculator", default);
    Check(!result.Verified && result.Message == "The launch was attempted but no requested foreground app window was verified; focus the app and inspect it before retrying.", "Unknown final reason is not exposed or replaced by an earlier known reason");
}
foreach (bool reverse in new[] { false, true }) {
    var fake = new Fake { PendingVerifications = 3, PendingReason = n => (n == 3) != reverse ? "Waiting for the requested app window." : "The foreground app does not match the request." };
    var result = await Controller(fake).RunAsync("calculator", default);
    Check(!result.Verified && result.Message.Contains(reverse ? "final foreground app did not match" : "no visible foreground window") && fake.Dispatches == 1, "The latest known observation replaces an earlier different reason");
}
{
    var fake = new Fake { VerificationOverride = n => new ComputerVerification(n == 3, null, "The foreground app does not match the request.") };
    var result = await Controller(fake).RunAsync("calculator", default);
    Check(!result.Verified && result.After is null && result.Message.StartsWith("The launch was attempted but no requested foreground"), "Malformed success cannot retain an earlier failure or create a successful result");
}
{
    var fake = new Fake { PendingVerifications = 2, PendingReason = _ => "The foreground app does not match the request." };
    var result = await Controller(fake).RunAsync("calculator", default);
    Check(result.Verified && result.After is not null && !result.Message.Contains("did not match") && fake.Dispatches == 1, "A fresh verified result supersedes earlier failed observations without another launch");
}
{
    var fake = new Fake { PendingVerifications = 3, PendingReason = _ => "The foreground app does not match the request.", VerifyErrorAt = 3 };
    var result = await Controller(fake).RunAsync("calculator", default);
    Check(!result.Verified && result.VerificationCount == 3 && result.Message.Contains("requested app window could not be verified") && !result.Message.Contains("did not match"), "A final observation exception is distinct from an earlier foreground mismatch");
}
{
    var fake = new Fake { DispatchError = true }; var result = await Controller(fake).RunAsync("notepad", default);
    Check(!result.Verified && result.ActionDispatched && fake.Dispatches == 1 && fake.Verifications == 0, "Dispatch exception is uncertain and never retried");
}
{
    var fake = new Fake { VerifyError = true }; var result = await Controller(fake).RunAsync("notepad", default);
    Check(!result.Verified && fake.Dispatches == 1 && fake.Verifications == 1, "Verification error stops without an action retry");
}
foreach (var mutation in new Func<ComputerDispatch, ComputerDispatch>[] { d => d with { RequestId = Guid.NewGuid() }, d => d with { ObservationId = Guid.NewGuid() }, d => d with { Alias = "comet" } }) {
    var fake = new Fake { MutateDispatch = mutation }; var result = await Controller(fake).RunAsync("notepad", default);
    Check(!result.Verified && fake.Dispatches == 1 && fake.Verifications == 0, "Foreign dispatch receipt cannot authorize postcondition success");
}
foreach (var message in new[] {
    "Windows denied the Calculator foreground request; no activation was repeated.",
    "Calculator activation could not be safely completed.",
    "The Calculator activation target changed or disappeared; no replacement was selected.",
    "The Calculator window changed before foreground confirmation; no activation was repeated.",
    "The activated Calculator window was not found within the observation limit.",
    "Calculator did not become visible after the bounded restore attempt.",
    "The activated Calculator window did not become visible within the observation limit.",
    "Calculator did not become the visible foreground window within the observation limit."
}) {
    var fake = new Fake { DispatchError = true, DispatchErrorMessage = message };
    var result = await Controller(fake).RunAsync("calculator", default);
    Check(!result.Verified && result.After is null && result.ActionCount == 1 && fake.Dispatches == 1 && fake.Verifications == 0 &&
        result.Message.Contains("Calculator") && !result.Message.Contains("its result is uncertain"), "Fixed Calculator activation failure remains truthful and never retries");
    var privateFake = new Fake { DispatchError = true, DispatchErrorMessage = message + " private suffix" };
    var privateResult = await Controller(privateFake).RunAsync("calculator", default);
    Check(privateResult.Message == "The launch was attempted but its result is uncertain; inspect the app before trying again.", "Calculator activation failures use exact literals without exposing arbitrary exception payloads");
}
foreach (var mutation in new Func<ComputerObservation, ComputerObservation>[] {
    o => o with { RequestId = Guid.NewGuid() }, o => o with { Complete = false }, o => o with { At = o.At.AddSeconds(-10) },
    o => o with { Window = o.Window with { Window = IntPtr.Zero } }, o => o with { Window = o.Window with { App = "wrongapp" } }
}) {
    var fake = new Fake { MutateAfter = mutation }; var result = await Controller(fake).RunAsync("notepad", default);
    Check(!result.Verified && fake.Dispatches == 1, "Invalid postcondition cannot become successful completion");
}
{
    var fake = new Fake { ReuseObservation = true }; var result = await Controller(fake).RunAsync("notepad", default);
    Check(!result.Verified && fake.Dispatches == 1, "Original observation cannot be replayed as fresh completion");
}
{
    using var stop = new CancellationTokenSource(); var fake = new Fake(); fake.MutateCheckpoint = o => { stop.Cancel(); return o; };
    Check(await Cancelled(Controller(fake).RunAsync("notepad", stop.Token)) && fake.Dispatches == 0, "Stop at checkpoint prevents action");
}
{
    using var stop = new CancellationTokenSource(); var fake = new Fake(); fake.MutateAfter = o => { stop.Cancel(); return o; };
    Check(await Cancelled(Controller(fake).RunAsync("notepad", stop.Token)) && fake.Dispatches == 1, "Stop on late verification rejects completion after one attempted action");
}
{
    using var stop = new CancellationTokenSource(); var fake = new Fake();
    var release = new TaskCompletionSource<ComputerObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
    fake.ObserveOverride = (_, _) => release.Task;
    var controller = Controller(fake); var running = controller.RunAsync("notepad", stop.Token);
    await fake.Entered.Task; stop.Cancel();
    Check(await Cancelled(running), "Stop promptly cancels a hostile observer that ignores its token");
    Check(await Refused(() => controller.RunAsync("notepad", default)), "Pending hostile observer cannot spawn overlapping workers through the same controller");
    release.SetResult(fake.Before!); await Task.Delay(20);
    Check(fake.Dispatches == 0, "Late observed metadata cannot revive a cancelled action");
}
{
    using var stop = new CancellationTokenSource(); var fake = new Fake();
    var release = new TaskCompletionSource<ComputerDispatch>(TaskCreationOptions.RunContinuationsAsynchronously);
    fake.DispatchOverride = (_, _) => release.Task;
    var running = Controller(fake).RunAsync("comet", stop.Token); await fake.ActionEntered.Task; stop.Cancel();
    Check(await Cancelled(running), "Stop promptly returns during a hostile pending dispatcher");
    release.SetResult(new(fake.Before!.RequestId, fake.Before.Id, "comet")); await Task.Delay(20);
    Check(fake.Dispatches == 1 && fake.Verifications == 0, "Late dispatch receipt cannot start verification or replay");
}
{
    var fake = new Fake(); var release = new TaskCompletionSource<ComputerObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
    fake.ObserveOverride = (_, _) => release.Task;
    var controller = new BoundedComputerUse(fake, limits with { Timeout = TimeSpan.FromMilliseconds(40) }, () => fake.Now);
    bool timeout = false; try { await controller.RunAsync("notepad", default); } catch (TimeoutException) { timeout = true; }
    release.SetResult(fake.Before!); await Task.Delay(20);
    Check(timeout && fake.Dispatches == 0, "Deadline bounds a hung observer without later action");
}
{
    using var stop = new CancellationTokenSource(); var fake = new Fake();
    var release = new TaskCompletionSource<ComputerObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
    fake.ObserveOverride = (_, _) => release.Task;
    var running = RoutineAppOpen.RunAsync("Open Notepad", () => true, () => "", stop.Token, fake);
    await fake.Entered.Task; stop.Cancel();
    Check(await Cancelled(running), "Routine wrapper promptly cancels a pending observer");
    var other = new Fake();
    Check(await Refused(() => RoutineAppOpen.RunAsync("Open Calculator", () => true, () => "", default, other)) && other.Trace.Count == 0,
        "Global routine slot remains occupied until cancelled backend work settles, including new controller attempts");
    release.SetResult(fake.Before!); await Task.Delay(30);
    Check((await RoutineAppOpen.RunAsync("Open Calculator", () => true, () => "", default, other)).Verified && fake.Dispatches == 0,
        "A new routine request can run after the abandoned observer settles without reviving the cancelled request");
}
// Exercise the executable identity lease only on inert owned files. No process
// starts and no real installed executable is inspected by these focused tests.
{
    string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Buddy-control45-" + Guid.NewGuid().ToString("N")));
    string folder = Path.Combine(root, "original"); Directory.CreateDirectory(folder);
    string file = Path.Combine(folder, "owned.exe"); await File.WriteAllTextAsync(file, "owned inert file, never executed");
    string? link = null, directoryLink = null;
    try {
        using (WindowsRoutineAppBackend.ExecutableLease.Acquire(file, default)) {
            bool writeDenied = false; try { using var writer = File.Open(file, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); } catch (IOException) { writeDenied = true; }
            Check(writeDenied, "Executable lease denies in-place modification of the exact owned file");
            bool fileRenameDenied = false; try { File.Move(file, Path.Combine(folder, "moved.exe")); } catch (IOException) { fileRenameDenied = true; }
            Check(fileRenameDenied, "Executable lease prevents final-path replacement during publisher check/launch interval");
            bool folderRenameDenied = false; try { Directory.Move(folder, Path.Combine(root, "moved-folder")); } catch (IOException) { folderRenameDenied = true; }
            Check(folderRenameDenied, "Executable lease pins ancestor directory against replacement");
        }
        string renamed = Path.Combine(folder, "renamed.exe"); File.Move(file, renamed); file = renamed;
        Check(File.Exists(file), "Executable lease releases handles after use");
        link = Path.Combine(root, "linked.exe"); File.CreateSymbolicLink(link, file);
        Check(await Refused(() => { using var lease = WindowsRoutineAppBackend.ExecutableLease.Acquire(link, default); return Task.CompletedTask; }), "Executable lease refuses a linked leaf rather than following it");
        directoryLink = Path.Combine(root, "linked-folder"); Directory.CreateSymbolicLink(directoryLink, folder);
        Check(await Refused(() => { using var lease = WindowsRoutineAppBackend.ExecutableLease.Acquire(Path.Combine(directoryLink, "renamed.exe"), default); return Task.CompletedTask; }), "Executable lease refuses a linked ancestor rather than following it");
        using var stop = new CancellationTokenSource(); stop.Cancel();
        bool cancelled = false; try { using var lease = WindowsRoutineAppBackend.ExecutableLease.Acquire(file, stop.Token); } catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "Executable lease honors cancellation before opening file metadata");
    } finally {
        if (link is not null && File.Exists(link)) File.Delete(link);
        if (directoryLink is not null && Directory.Exists(directoryLink)) Directory.Delete(directoryLink);
        string temporary = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!root.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(root).StartsWith("Buddy-control45-", StringComparison.Ordinal) ||
            (File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new Exception("Owned file fixture cleanup escaped its verified temporary root.");
        Directory.Delete(root, recursive: true);
    }
}
Console.WriteLine($"COMPUTER USE MOCK/OWNED-FILE CHECKS PASSED: {checks}");

internal sealed class Fake : IComputerUseBackend
{
    internal DateTimeOffset Now = DateTimeOffset.UtcNow;
    internal readonly List<string> Trace = [];
    internal int Dispatches, Verifications, PendingVerifications, VerifyErrorAt;
    internal Func<int, string?>? PendingReason;
    internal Func<int, ComputerVerification>? VerificationOverride;
    internal bool DispatchError, VerifyError, ReuseObservation;
    internal string DispatchErrorMessage = "owned fake dispatch error";
    internal ComputerObservation? Before;
    internal Func<ComputerObservation, ComputerObservation>? MutateObservation, MutateCheckpoint, MutateAfter;
    internal Func<ComputerDispatch, ComputerDispatch>? MutateDispatch;
    internal Func<Guid, CancellationToken, Task<ComputerObservation>>? ObserveOverride;
    internal Func<ComputerObservation, CancellationToken, Task<ComputerDispatch>>? DispatchOverride;
    internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), ActionEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<ComputerObservation> ObserveAsync(Guid requestId, CancellationToken ct) {
        Trace.Add("observe"); Before = new(requestId, Guid.NewGuid(), Now, new(new IntPtr(10), 20, 30, 40, "Buddy")); Entered.TrySetResult();
        return ObserveOverride?.Invoke(requestId, ct) ?? Task.FromResult(MutateObservation?.Invoke(Before) ?? Before);
    }
    public Task<ComputerObservation> CheckpointAsync(ComputerObservation observation, string alias, CancellationToken ct) { Trace.Add("checkpoint"); return Task.FromResult(MutateCheckpoint?.Invoke(observation) ?? observation); }
    public Task<ComputerDispatch> DispatchAsync(ComputerObservation checkpoint, string alias, CancellationToken ct) {
        Trace.Add("dispatch"); Dispatches++; ActionEntered.TrySetResult();
        if (DispatchError) throw new InvalidOperationException(DispatchErrorMessage);
        var result = new ComputerDispatch(checkpoint.RequestId, checkpoint.Id, alias);
        return DispatchOverride?.Invoke(checkpoint, ct) ?? Task.FromResult(MutateDispatch?.Invoke(result) ?? result);
    }
    public Task<ComputerVerification> VerifyAsync(ComputerObservation before, ComputerDispatch dispatch, CancellationToken ct) {
        Trace.Add("verify"); Verifications++;
        if (VerifyError || Verifications == VerifyErrorAt) throw new InvalidOperationException("owned fake verify error");
        if (VerificationOverride is not null) return Task.FromResult(VerificationOverride(Verifications));
        if (Verifications <= PendingVerifications) return Task.FromResult(new ComputerVerification(false, null, PendingReason is null ? "waiting" : PendingReason(Verifications)!));
        var after = new ComputerObservation(before.RequestId, ReuseObservation ? before.Id : Guid.NewGuid(), Now, new(new IntPtr(50), 60, 70, 80, dispatch.Alias));
        return Task.FromResult(new ComputerVerification(true, MutateAfter?.Invoke(after) ?? after, "fake verified"));
    }
}
