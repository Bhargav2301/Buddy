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
Console.WriteLine($"{assertions} desktop logic assertions passed. Native Windows interaction is a separate acceptance check.");
