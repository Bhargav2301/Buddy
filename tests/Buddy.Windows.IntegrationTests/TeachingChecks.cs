using Buddy.Server;
using Buddy.Windows;
using Microsoft.AspNetCore.DataProtection;
using System.Net;
using System.Net.Http;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class TeachingChecks
{
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);
    private static object Field(object value, string name) => value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(value)!;
    private static Task Call(object value, string name, params object[] args) => (Task)value.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(value, args)!;
    internal static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; int count = 0, exit = 1;
        void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); count++; Console.WriteLine("PASS: " + name); }
        var folder = Path.Combine(Path.GetTempPath(), "Buddy-teaching-native-" + Guid.NewGuid()); Directory.CreateDirectory(folder);
        var fixture = new Window { Title = "Buddy teaching acceptance fixture", Width = 560, Height = 350 };
        var button = new Button { Content = "Export example", Height = 50 }; AutomationProperties.SetName(button, "Export example");
        var editor = new TextBox { Text = "Unchanged", Height = 45 }; AutomationProperties.SetName(editor, "Fixture document");
        int clicks = 0; button.Click += (_, _) => clicks++;
        var stack = new StackPanel { Margin = new(24) }; stack.Children.Add(editor); stack.Children.Add(button); fixture.Content = stack;
        fixture.Loaded += async (_, _) => {
            VoiceOverlayWindow? voice = null;
            try {
                var handle = new WindowInteropHelper(fixture).Handle; ScreenPerception.PracticeHandle = handle;
                fixture.Activate(); InputNative.SetForegroundWindow(handle); await Task.Delay(250);
                Check(Native.GetForegroundWindow() == handle, "Owned teaching fixture is the selected foreground window");
                var store = new StateStore(folder, DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(folder, "keys")))); await store.EnsureSaved();
                using var model = new TeachingFixtureModel(); using var client = new HttpClient(model) { BaseAddress = new("http://127.0.0.1:11434") };
                var service = new BuddyService(store, new(client));
                var prefs = new DesktopPreferences { ReadVoiceAnswers = false, CaptureOnVoice = true }; var perception = new ScreenPerception(() => prefs);
                int workflows = 0; voice = new(() => service, () => Task.FromResult<string?>(null), () => prefs, _ => {}, () => handle, perception, (_, _) => workflows++, () => {});
                voice.Left = fixture.Left + fixture.Width + 15; voice.Top = fixture.Top; voice.Show();
                typeof(VoiceOverlayWindow).GetField("sourceWindow", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(voice, handle);
                await Call(voice, "Send", "Show me how to export");
                var teaching = (VoiceTeaching)Field(voice, "teaching"); var next = (Button)Field(voice, "nextTeaching");
                Console.WriteLine("Teaching state: " + ((TextBlock)Field(voice, "state")).Text + "; answer=" + ((TextBlock)Field(voice, "answer")).Text + "; pointing=" + teaching.IsPointing + "; next=" + next.Visibility + "/" + next.IsEnabled);
                Check(model.Calls == 1 && workflows == 0, "Ordinary voice guidance uses the teaching core without opening Guide or Agent");
                Check(teaching.IsPointing && next.IsVisible && next.IsEnabled, "One verified target and an explicit Next step control are shown");
                Check(((TextBlock)Field(voice, "answer")).Text == "Check the destination. Choose Export example.", "The complete qualified teaching explanation is displayed");
                var ink = app.Windows.Cast<Window>().Single(w => w.Title == "Buddy guidance ink"); var inkHandle = new WindowInteropHelper(ink).Handle;
                long styles = GetWindowLongPtr(inkHandle, -20).ToInt64();
                Check((styles & 0x20) != 0 && (styles & 0x08000000) != 0 && Native.GetForegroundWindow() == handle, "Core annotations are click-through, nonactivating and preserve target focus");
                CaptureProtection.Set(false); Check(GetWindowDisplayAffinity(inkHandle, out var affinity) && affinity == 0, "Teaching annotation honors screenshot protection off");
                CaptureProtection.Set(true); Check(GetWindowDisplayAffinity(inkHandle, out affinity) && affinity == 0x11, "Teaching annotation honors screenshot protection on");
                await Task.Delay(850); Check(model.Calls == 1, "Waiting for the user's step does not trigger model calls or automatic advancement");
                button.Content = "Save result"; AutomationProperties.SetName(button, "Save result"); model.TargetName = "Save result"; await Task.Delay(150);
                await Call(voice, "ContinueTeaching");
                Check(model.Calls == 2 && ((TextBlock)Field(voice, "answer")).Text.Contains("Choose Save result."), "Next step captures changed native controls and plans from fresh evidence");
                Check(model.LastInput.Contains("untrustedPreviousSuggestions") && model.LastInput.Contains("Choose Export example."), "Bounded task history continues without claiming the prior step was completed");
                model.DuringPlan = () => { button.Content = "Changed during inference"; AutomationProperties.SetName(button, "Changed during inference"); };
                await Call(voice, "ContinueTeaching"); model.DuringPlan = null;
                Check(!teaching.IsPointing && ((TextBlock)Field(voice, "answer")).Text == TeachingPolicy.Unverified, "Control changes during inference suppress stale annotations and instructions");
                Check(clicks == 0 && editor.Text == "Unchanged", "Core teaching invokes no button and changes no document text");
                model.TargetName = "Changed during inference"; model.Hold = true;
                var pending = Call(voice, "Send", "Show me the next control"); await model.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
                voice.Cancel(); await pending.WaitAsync(TimeSpan.FromSeconds(2));
                Check(!voice.IsBusy && !voice.IsListening && !teaching.IsPointing && !next.IsVisible, "Stop cancels active teaching inference, microphone state, annotations and continuation");
                model.Hold = false; await Call(voice, "Send", "Show me that control");
                Check(teaching.IsPointing && !voice.IsListening, "Teaching can reopen after Stop without starting the microphone");
                voice.Cancel();
                model.DuringPlan = () => prefs = prefs with { CaptureOnVoice = false };
                await Call(voice, "Send", "Show me that control"); model.DuringPlan = null;
                Check(!teaching.IsPointing && ((TextBlock)Field(voice, "state")).Text.Contains("Screen context was turned off"), "Disabling screen context during inference prevents fresh capture and annotation output");
                prefs = prefs with { CaptureOnVoice = true }; voice.Cancel();
                var snapshot = await perception.Capture(handle, default); var target = snapshot.Context.Elements.Single(e => e.Role == "Button" && e.Name == "Changed during inference");
                var hashes = new HashSet<string>();
                foreach (var primitive in TeachingPolicy.Primitives) {
                    using var overlay = new GuidanceOverlay(); overlay.Draw(handle, target, primitive, "Example target"); await Task.Delay(400);
                    var window = app.Windows.Cast<Window>().Single(w => w.Title == "Buddy guidance ink"); var visual = (FrameworkElement)window.Content;
                    var bitmap = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
                    var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
                    Check(pixels.Any(p => p != 0), "Native " + primitive + " annotation renders visible pixels"); hashes.Add(Convert.ToHexString(SHA256.HashData(pixels)));
                }
                Check(hashes.Count == 4, "Circle, arrow, underline and label have distinct native rendered geometry");
                using (var overlay = new GuidanceOverlay()) {
                    bool valid = true; int invalidations = 0; overlay.Invalidated += () => invalidations++;
                    overlay.Draw(handle, target, "circle", "Example", () => valid); valid = false; await Task.Delay(400);
                    Check(!overlay.IsVisible && invalidations == 1, "Stale-target validation clears ink and notifies speech cancellation exactly once");
                }
                Check(!Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Any(f => new[]{".png",".jpg",".wav"}.Contains(Path.GetExtension(f))), "Native teaching stores no screen or audio files");
                Console.WriteLine($"ALL {count} NATIVE TEACHING CHECKS PASSED; microphone, physical audio and third-party app acceptance remain separate"); exit = 0;
            } catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { voice?.Dispose(); ScreenPerception.PracticeHandle = IntPtr.Zero; fixture.Close(); Directory.Delete(folder, true); app.Shutdown(); }
        };
        app.Run(fixture); return exit;
    }
    private sealed class TeachingFixtureModel : HttpMessageHandler
    {
        internal int Calls; internal bool Hold; internal Action? DuringPlan; internal string LastInput = ""; internal string TargetName = "Export example";
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++; using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            LastInput = payload.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
            var first = ParseFirst(LastInput); using var input = JsonDocument.Parse(first);
            var control = input.RootElement.GetProperty("untrustedScreen").GetProperty("elements").EnumerateArray().Single(e => e.GetProperty("role").GetString() == "Button" && e.GetProperty("name").GetString() == TargetName);
            var name = control.GetProperty("name").GetString()!; var reference = control.GetProperty("ref").GetString()!;
            DuringPlan?.Invoke();
            if (Hold) { Entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            var plan = new GuidePlan("Check the destination. Choose " + name + ".", [new("Choose " + name + ".", reference, name, "Button", "circle")]);
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { message = new { content = JsonSerializer.Serialize(plan, StateStore.Json) }, done = true }), Encoding.UTF8, "application/json") };
        }
        private static string ParseFirst(string value) { var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(value)); using var first = JsonDocument.ParseValue(ref reader); return first.RootElement.GetRawText(); }
    }
}
