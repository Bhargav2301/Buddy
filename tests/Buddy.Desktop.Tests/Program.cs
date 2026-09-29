using Buddy.Windows;
using System.Diagnostics;
using System.IO.Pipes;

if (args.Length == 3 && args[0] == "--activation-client") {
    Environment.Exit(await DesktopActivation.Redirect(Enum.Parse<LaunchDestination>(args[2]), args[1]) ? 0 : 1); return;
}

int assertions = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); assertions++; Console.WriteLine("PASS: " + name); }
var primary = new PixelBounds(0, 0, 1920, 1040);
var normal = OverlayPlacement.NearPointer(100, 100, 480, 354, primary, 1);
Check(normal == new PixelPosition(118, 118), "Chat opens beside the pointer with a gap");
var corner = OverlayPlacement.NearPointer(1918, 1038, 480, 354, primary, 1);
Check(corner.X < 1918 && corner.Y < 1038 && corner.X + 480 <= 1912 && corner.Y + 354 <= 1032,
    "Bottom-right activation flips left/up and stays above the taskbar");
var leftScreen = new PixelBounds(-2560, -300, 2560, 1400);
var left = OverlayPlacement.NearPointer(-2555, -296, 600, 442.5, leftScreen, 1.25);
Check(left.X >= -2550 && left.Y >= -290 && left.X < 0, "Negative monitor origins remain negative and correctly clamped");
var hidpi = OverlayPlacement.NearPointer(3700, 2050, 960, 708, new(1920, 0, 2560, 2080), 2);
Check(hidpi.X >= 1936 && hidpi.X + 960 <= 4464 && hidpi.Y + 708 <= 2064, "200 percent scale uses physical pixels and the target work area");
var tiny = OverlayPlacement.NearPointer(40, 40, 480, 354, new(0, 0, 320, 240), 1);
Check(double.IsFinite(tiny.X) && double.IsFinite(tiny.Y), "A smaller work area never throws from an inverted clamp");

foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0, 3.0 })
{
    foreach (var work in new[] { primary, leftScreen, new PixelBounds(1920, -1080, 3840, 2080) })
    {
        double width = Math.Min(480 * scale, work.Width - 16 * scale), height = Math.Min(354 * scale, work.Height - 16 * scale);
        for (int ix = 0; ix <= 10; ix++) for (int iy = 0; iy <= 10; iy++)
        {
            var point = OverlayPlacement.NearPointer(work.Left + work.Width * ix / 10, work.Top + work.Height * iy / 10, width, height, work, scale);
            if (point.X < work.Left || point.Y < work.Top || point.X + width > work.Left + work.Width + .001 || point.Y + height > work.Top + work.Height + .001)
                throw new Exception("Placement escaped work area");
        }
    }
}
Check(true, "1,815 activation positions across negative origins and 100–300 percent scales stay inside the work area");

var registered = new Dictionary<int, uint>(); bool conflict = false; int nativeCalls = 0;
using var shortcut = new ShortcutRegistration((id, mods) => { nativeCalls++; if (conflict) return false; registered.Add(id, mods); return true; }, id => registered.Remove(id));
Check(shortcut.TrySet(ShortcutChoice.Choices[0]) && registered.Count == 1, "Initial shortcut is registered");
int oldId = shortcut.ActiveId;
conflict = true;
Check(!shortcut.TrySet(ShortcutChoice.Choices[3]) && shortcut.ActiveId == oldId && registered.ContainsKey(oldId), "Reserved Windows shortcut failure preserves the working shortcut");
conflict = false;
Check(shortcut.TrySet(ShortcutChoice.Choices[1]) && shortcut.ActiveId != oldId && !registered.ContainsKey(oldId) && registered.Count == 1,
    "Successful replacement removes only the previous shortcut");
Check((registered[shortcut.ActiveId] & 0x4000) != 0, "Holding the shortcut does not generate repeated activations");
int calls = nativeCalls;
Check(shortcut.TrySet(ShortcutChoice.Choices[1]) && nativeCalls == calls, "Saving an unchanged shortcut does not re-register it");
shortcut.Dispose();
Check(registered.Count == 0 && shortcut.ActiveId == 0, "Quit releases the shortcut");
Check(!new DesktopPreferences().ShortcutStartsVoice, "Default shortcut opens chat without activating the microphone");
var gesture = new HoldGesture();
gesture.Down(1000); Check(!gesture.Tick(1249) && gesture.Up(), "A short press opens typed chat without starting the microphone");
gesture.Down(2000); gesture.Down(2100); Check(gesture.Tick(2250) && !gesture.Tick(2300), "Hold fires once at 250 ms despite key repeat");
Check(!gesture.Up() && !gesture.Up(), "Release after hold never opens typed chat");
gesture.Down(3000); Check(gesture.Up(), "A later tap works after hold completion");
var sentences = new SentenceBuffer();
Check(!sentences.Add("The result is 3.").Any(), "An incomplete stream fragment is not spoken prematurely");
Check(sentences.Add("14. Next").Single() == "The result is 3.14.", "Decimal and split sentence boundaries survive streaming");
sentences.Clear(); Check(!sentences.Flush().Any(), "Stop discards queued partial speech");
Check(AssistantIntent.Mode("Open Notepad") == "agent" && AssistantIntent.Mode("Show me Export") == "guide" && AssistantIntent.Mode("What does this mean?") == "talk", "Explicit commands route to action/guide workflows; questions remain chat");
Check(!new DesktopPreferences().AgentEnabled && !new DesktopPreferences().AllowWebResearch && !new DesktopPreferences().HoldToTalk, "Agent, internet and keyboard hook require opt-in");
Check(DesktopLaunch.Parse([]) == LaunchDestination.Home, "Launching Buddy normally opens Home, even with companion startup enabled");
Check(DesktopLaunch.Parse(["--settings"]) == LaunchDestination.Settings, "The Settings shortcut targets Settings directly");
Check(DesktopLaunch.Parse(["--background"]) == LaunchDestination.Background && DesktopLaunch.Parse(["--background", "--home"]) == LaunchDestination.Home, "Only an explicit background launch can hide Home");
if (OperatingSystem.IsWindows()) {
    var channel = "Buddy.Activation.Test." + Guid.NewGuid(); var received = new List<LaunchDestination>();
    using var activation = new DesktopActivation(target => { lock (received) received.Add(target); return Task.CompletedTask; }, channel);
    async Task RedirectFromOtherProcess(LaunchDestination target) {
        var command = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase)) command.ArgumentList.Add(System.Reflection.Assembly.GetExecutingAssembly().Location);
        command.ArgumentList.Add("--activation-client"); command.ArgumentList.Add(channel); command.ArgumentList.Add(target.ToString());
        using var process = Process.Start(command)!;
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); Check(process.ExitCode == 0, "Second process receives navigation acknowledgement: " + target); }
        finally { if (!process.HasExited) process.Kill(); }
    }
    await RedirectFromOtherProcess(LaunchDestination.Home); await RedirectFromOtherProcess(LaunchDestination.Settings);
    lock (received) Check(received.SequenceEqual([LaunchDestination.Home, LaunchDestination.Settings]), "One running instance receives both Home and Settings requests");
    using (var invalid = new NamedPipeClientStream(".", channel, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly)) {
        using var timeout = new CancellationTokenSource(2000); await invalid.ConnectAsync(timeout.Token); await invalid.WriteAsync(new byte[] { 255 }, timeout.Token);
        Check(await invalid.ReadAsync(new byte[1], timeout.Token) == 0, "The activation channel rejects unknown commands");
    }
    Check(await DesktopActivation.Redirect(LaunchDestination.Settings, channel), "A rejected command does not break later Settings requests");
    activation.Dispose(); await activation.Completion.WaitAsync(TimeSpan.FromSeconds(2));
    Check(!await DesktopActivation.Redirect(LaunchDestination.Home, channel, 100), "An unavailable instance returns a bounded failure");
    var startupChannel = channel + ".Startup";
    var pendingLaunch = DesktopActivation.Redirect(LaunchDestination.Settings, startupChannel, 3000);
    using var startup = new DesktopActivation(_ => Task.CompletedTask, startupChannel);
    Check(await pendingLaunch, "A launch arriving before the listener starts is delivered");
}
var field = new TestField { Value = "  Original\r\nCafe\u0301 \U0001F600\t" };
var exactOriginal = field.Value; var edit = new GuardedEdit(field, exactOriginal); var now = DateTimeOffset.UtcNow;
edit.Apply("Replacement", now, default); Check(field.Value == "Replacement" && field.Writes == 1, "Refine replaces the verified field exactly once");
edit.Undo(now.AddSeconds(29), default); Check(field.Value == exactOriginal, "Undo preserves whitespace, newline and Unicode code units exactly");
void Reject(Action action, string name) { bool refused = false; try { action(); } catch (InvalidOperationException) { refused = true; } Check(refused, name); }
field.Value = "Original"; edit = new GuardedEdit(field, field.Value); field.Value = "User edit"; int writes = field.Writes;
Reject(() => edit.Apply("Replacement", now, default), "A changed original cannot be overwritten"); Check(field.Value == "User edit" && field.Writes == writes, "Changed-field rejection dispatches no write");
field.Value = "Original"; edit = new GuardedEdit(field, field.Value); edit.Apply("Replacement", now, default); field.Value = "Edited replacement";
Reject(() => edit.Undo(now.AddSeconds(5), default), "Undo refuses to overwrite edits made after applying");
field.Value = "Original"; edit = new GuardedEdit(field, field.Value); edit.Apply("Replacement", now, default);
Reject(() => edit.Undo(now.AddSeconds(31), default), "Undo expires after thirty seconds");
field.Value = "Original"; edit = new GuardedEdit(field, field.Value); field.Identity = "another-field";
Reject(() => edit.Apply("Replacement", now, default), "A replaced field identity cannot receive the rewrite");
field.Identity = "field"; edit = new GuardedEdit(field, field.Value); using var cancelled = new CancellationTokenSource(); field.OnRead = cancelled.Cancel; writes = field.Writes;
bool cancelledBeforeWrite = false; try { edit.Apply("Replacement", now, cancelled.Token); } catch (OperationCanceledException) { cancelledBeforeWrite = true; }
Check(cancelledBeforeWrite && field.Writes == writes, "Cancellation during validation prevents the subsequent write"); field.OnRead = null;
field.Transform = true; edit = new GuardedEdit(field, field.Value);
Reject(() => edit.Apply("Replacement", now, default), "A host that transforms text cannot be reported as a successful replacement");
InstallationTests.Run(Check);
LocalDataTests.Run(Check);
var insertion = DictationInsertion.Verify("Hi world", "Hi world", "Hi ", "world");
Check(insertion.Replace("Hi world", "Buddy") == "Hi Buddy", "Dictation replaces only the explicitly captured selection");
Check(new DictationInsertion(2, 0).Replace("ab\r\n😀 ", "c") == "abc\r\n😀 ", "Caret insertion preserves surrounding line endings and Unicode exactly");
Reject(() => DictationInsertion.Verify("Original", "Different", "", ""), "Mismatched UIA document and writable value cannot define an insertion");
Reject(() => DictationInsertion.Verify("Original", "Original", "Orig", "changed"), "An inconsistent selected range is refused");
Reject(() => new DictationInsertion(99, 0).Replace("Original", "words"), "Out-of-range insertion is refused before editing");
int pointerX = 0; var trip = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
using (var interrupt = new DispatchInterruption(() => new(Volatile.Read(ref pointerX), 0, false), reason => trip.TrySetResult(reason))) {
    interrupt.ArmPointer(); var latency = Stopwatch.StartNew(); Volatile.Write(ref pointerX, 1);
    Thread.Sleep(150); // Deliberately block the caller, as a busy UI might.
    Check(trip.Task.IsCompleted && (await trip.Task).Contains("pointer"), "One-pixel movement cancels independently of a blocked presentation thread");
}
int esc = 0; trip = new(TaskCreationOptions.RunContinuationsAsynchronously);
using (var interrupt = new DispatchInterruption(() => new(0, 0, Volatile.Read(ref esc) == 1), reason => trip.TrySetResult(reason))) {
    var latency = Stopwatch.StartNew(); Volatile.Write(ref esc, 1);
    await trip.Task.WaitAsync(TimeSpan.FromSeconds(1));
    Check(latency.ElapsedMilliseconds < 100, "Injected input fixture reaches the dispatch-stop fence within 100 ms (native measurement still required)");
    Console.WriteLine("Fixture interruption latency: " + latency.Elapsed.TotalMilliseconds.ToString("F1") + " ms");
}
var activity = new CompanionState();
activity.Set("guide", CompanionMood.Pointing); activity.Set("voice", CompanionMood.Listening); activity.Set("chat", CompanionMood.Idle);
Check(activity.Current == CompanionMood.Listening, "An idle chat cannot erase the microphone's active companion state");
activity.Set("voice", CompanionMood.Idle);
Check(activity.Current == CompanionMood.Pointing, "Ending voice restores the still-active Guide state");
activity.Set("guide", CompanionMood.Idle);
Check(activity.Current == CompanionMood.Idle, "Companion returns to idle only after all activities finish");
var spring = new CompanionSpring(); spring.Step(new(-500, 100), .033, true);
for (int i = 0; i < 180; i++) spring.Step(new(-200, 500), .033, true);
Check(Math.Abs(spring.Position.X + 200) + Math.Abs(spring.Position.Y - 500) < 1, "Companion spring converges across negative screen coordinates");
Check(spring.Step(new(10, 20), .033, false) == new PixelPosition(10, 20), "Reduced motion places the companion immediately");
Console.WriteLine($"{assertions} desktop logic assertions passed. Native Windows interaction is a separate acceptance check.");

sealed class TestField : IVerifiedTextField
{
    public string Identity { get; set; } = "field";
    public string Value = ""; public int Writes; public bool Transform; public Action? OnRead;
    public string Read() { OnRead?.Invoke(); return Value; }
    public void Write(string expected, string value, CancellationToken ct) { ct.ThrowIfCancellationRequested(); if (Value != expected) throw new InvalidOperationException(); Writes++; Value = Transform ? value + " changed" : value; }
}
