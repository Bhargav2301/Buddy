using Buddy.Server;
using Buddy.Windows;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

internal static class ParityVisualChecks
{
    private static object Field(object value, string name) => value.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(value)!;
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    private static T Named<T>(DependencyObject root, string name) where T : DependencyObject
        => Descendants(root).OfType<T>().Single(c => AutomationProperties.GetName(c) == name);
    private static void Click(DependencyObject root, string label)
        => Descendants(root).OfType<Button>().Single(b => Equals(b.Content, label)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    internal static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        string folder = Path.Combine(Path.GetTempPath(), "Buddy-parity43-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        string preferencesPath = Path.Combine(folder, "desktop.json");
        int exit = 1, count = 0, saves = 0;
        var saved = new DesktopPreferences { ReduceMotion = true, NeuralSpeakerId = 85, NeuralPreset = "f3", HeadphonesOnly = true, HeadphoneDeviceId = "fixture-headphones" };
        // Inject preferences and persistence: the fixture never reads or writes an installed profile.
        var home = new MainWindow(false, saved, value => { value.Save(preferencesPath); saved = value; saves++; });
        void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL: " + message); count++; Console.WriteLine("PASS: " + message); }
        home.Loaded += async (_, _) => {
            CursorCompanionWindow? companion = null;
            try {
                BuddyTheme.Apply("Light", true);
                Check(!new DesktopPreferences().CompactPointerMode && !new DesktopPreferences().StreamVoiceSentences && new DesktopPreferences().InkLifetimeSeconds == 15,
                    "New profiles preserve the official mascot, whole-answer voice and 15-second ink defaults");
                File.WriteAllText(preferencesPath, "{\"SchemaVersion\":2,\"InkLifetimeSeconds\":30,\"FutureSetting\":{\"keep\":true}}");
                var loaded = DesktopPreferences.Load(preferencesPath);
                loaded.Save(preferencesPath);
                using (var data = JsonDocument.Parse(File.ReadAllText(preferencesPath)))
                    Check(loaded.InkLifetimeSeconds == 30 && data.RootElement.GetProperty("FutureSetting").GetProperty("keep").GetBoolean(),
                        "30-second ink persists while extension data survives a preference roundtrip");
                foreach (int seconds in new[] { -1, 0, 6, 14, 31, int.MaxValue })
                    Check(new DesktopPreferences { InkLifetimeSeconds = seconds }.InkLifetimeSeconds == 15, $"Unsupported ink lifetime {seconds} normalizes to 15 seconds");
                File.WriteAllText(preferencesPath, "{\"SchemaVersion\":2,\"InkLifetimeSeconds\":900}");
                Check(DesktopPreferences.Load(preferencesPath).InkLifetimeSeconds == 15, "Invalid persisted ink lifetime normalizes on load");
                foreach (int seconds in new[] { 5, 15, 30 })
                    Check(new DesktopPreferences { InkLifetimeSeconds = seconds }.InkLifetimeSeconds == seconds, $"Supported {seconds}-second ink lifetime remains exact");

                home.OpenSettingsSection("General"); home.UpdateLayout();
                Named<CheckBox>(home, "Use compact pointer and activity").IsChecked = true; Click(home, "Save changes");
                Check(saved.CompactPointerMode && DesktopPreferences.Load(preferencesPath).CompactPointerMode, "Real General settings save optional compact presentation through isolated persistence");
                companion = new CursorCompanionWindow(() => false); companion.Show();
                var face = (CompanionFace)Field(companion, "glyph");
                var body = (FrameworkElement)Field(face, "body");
                Check(body is FacialRig && !face.IsPointer, "Companion starts with the official mascot renderer");
                companion.SetCompactPointerMode(true);
                foreach (var mood in new[] { CompanionMood.Listening, CompanionMood.Thinking, CompanionMood.Speaking }) {
                    companion.SetMood(mood);
                    var activity = (CompanionActivity)Field(companion, "activity");
                    Check(face.IsPointer && body.Visibility == Visibility.Collapsed && activity.Mood == mood && ((StackPanel)Field(companion, "activityPanel")).Visibility == Visibility.Visible,
                        $"Compact pointer visibly presents {mood} activity");
                    Check(!activity.MotionEnabled && Enumerable.Range(0, 5).All(i => CompanionActivity.HeightFor(i, mood, 0, false) == CompanionActivity.HeightFor(i, mood, 99, false)),
                        $"Reduced motion keeps {mood} bars still");
                    Check(AutomationProperties.GetName(activity).Contains("decorative, not microphone level"), $"{mood} activity distinguishes itself from a measured microphone waveform");
                }
                companion.SetCompactPointerMode(false);
                Check(!face.IsPointer && body.Visibility == Visibility.Visible && body.Opacity == 1 && face.Width == 72 && ((StackPanel)Field(companion, "activityPanel")).Visibility == Visibility.Collapsed,
                    "Turning compact mode off immediately restores the same official mascot and hides decorative activity");
                Named<CheckBox>(home, "Use compact pointer and activity").IsChecked = false; Click(home, "Save changes");
                Check(!DesktopPreferences.Load(preferencesPath).CompactPointerMode, "Returning to the mascot persists in General settings");
                companion.Dispose(); companion = null;

                home.OpenSettingsSection("Voice"); home.UpdateLayout();
                Named<CheckBox>(home, "Start speaking complete sentences while Buddy prepares the rest").IsChecked = true; Click(home, "Save changes");
                Check(saved.StreamVoiceSentences && saved.NeuralSpeakerId == 85 && saved.NeuralPreset == "f3" && saved.HeadphonesOnly && saved.HeadphoneDeviceId == "fixture-headphones",
                    "Voice opt-in saves while preserving the selected F3 voice and headphone policy");
                home.OpenSettingsSection("Guide & Agent"); home.UpdateLayout();
                Named<ComboBox>(home, "Guidance ink lifetime in seconds").SelectedItem = 30; Click(home, "Save changes");
                Check(saved.InkLifetimeSeconds == 30 && DesktopPreferences.Load(preferencesPath).InkLifetimeSeconds == 30, "Guide settings save and reload the 30-second option");

                home.OpenSettingsSection("Brains"); home.UpdateLayout();
                var draft = Descendants(home).OfType<ProviderDraftSetup>().Single();
                Check(Descendants(draft).OfType<TextBox>().All(t => !t.IsVisible && !t.IsKeyboardFocused) && !Descendants(draft).OfType<PasswordBox>().Any(), "Disconnected provider setup exposes no free-form secret or credential input; noneditable ComboBox template editors stay hidden");
                var provider = Named<ComboBox>(draft, "Provider draft"); var model = Named<ComboBox>(draft, "Model draft");
                Check(!provider.IsEditable && !model.IsEditable, "Provider and model drafts use bounded non-editable choices");
                int before = saves; provider.SelectedItem = provider.Items.Cast<object>().Single(c => c.ToString() == "OpenAI API");
                model.SelectedItem = "gpt-4.1-mini"; Click(draft, "Save disconnected draft");
                Check(saves == before + 1 && saved.ProviderDraft == "openai" && saved.ProviderModelDraft == "gpt-4.1-mini" && Field(home, "host") is null && !ProviderProtocols.Connected,
                    "Saving a provider/model draft only persists locally; no service or provider route is activated");
                var connect = Named<Button>(draft, "Connect - unavailable in this build");
                connect.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(!connect.IsEnabled && saves == before + 1 && Field(home, "host") is null,
                    "Unavailable Connect control has no activation handler or persistence side effect");
                Check(Descendants(draft).OfType<TextBlock>().Any(t => t.Text.Contains("Still disconnected")) && Descendants(draft).OfType<TextBlock>().Any(t => t.Text.Contains("live activation unavailable")),
                    "Status clearly remains disconnected after saving a draft");

                var hwnd = new WindowInteropHelper(home).Handle;
                home.Activate(); InputNative.SetForegroundWindow(hwnd); await Task.Delay(100);
                Check(Native.GetForegroundWindow() == hwnd, "Only the owned fixture receives focus for native input invalidation");
                var bounds = WindowCapture.Bounds(hwnd);
                var element = new ScreenElement("fixture", "Owned target", "Button", bounds.X + 80, bounds.Y + 80, 120, 40);
                var now = DateTimeOffset.UtcNow; var focused = hwnd;
                using var overlay = new GuidanceOverlay(() => saved.InkLifetimeSeconds, () => now, () => focused, h => h == hwnd, () => false);
                int invalidations = 0; overlay.Invalidated += () => invalidations++;
                overlay.Draw(hwnd, element, "ring", "Owned target", () => true);
                now = now.AddSeconds(29); await overlay.CheckValidity();
                Check(overlay.IsVisible, "30-second ink remains visible before expiry with current evidence");
                now = now.AddSeconds(1); await overlay.CheckValidity();
                Check(!overlay.IsVisible && invalidations == 1, "30-second ink expires at its deadline using an injected clock without sleeping 30 seconds");
                overlay.Draw(hwnd, element, "ring", "Owned target", () => true); focused = new IntPtr(0x12345); await overlay.CheckValidity();
                Check(!overlay.IsVisible, "Simulated unrelated foreground focus invalidates 30-second ink immediately"); focused = hwnd;
                overlay.Draw(hwnd, element, "ring", "Owned target", () => WindowCapture.Bounds(hwnd) == bounds);
                home.Left += 12; home.UpdateLayout(); await overlay.CheckValidity();
                Check(!overlay.IsVisible, "Moving the owned target window clears ink before its 30-second deadline");
                overlay.Draw(hwnd, element, "ring", "Owned target", () => false); await overlay.CheckValidity();
                Check(!overlay.IsVisible, "Disappeared or changed target validation clears ink before expiry");
                overlay.Draw(hwnd, element, "ring", "Owned target", () => true);
                Check(Native.GetForegroundWindow() == hwnd, "Owned fixture remains foreground before synthetic Tab input");
                InputNative.Keys("Tab", default); await Task.Delay(100);
                Check(!overlay.IsVisible, "Actual input hook clears guidance ink on a key press in the owned fixture");

                using var release = new ManualResetEventSlim();
                try {
                    overlay.Draw(hwnd, element, "ring", "Owned target", () => { release.Wait(); return true; });
                    await overlay.CheckValidity();
                    Check(!overlay.IsVisible, "A hung target validator fails closed after the bounded wait");
                    bool rejected = false;
                    try { overlay.Draw(hwnd, element, "ring", "New target", () => false); } catch (InvalidOperationException) { rejected = true; }
                    Check(rejected && !overlay.IsVisible, "Fresh ink is rejected while a previous validator still occupies the bounded worker");
                } finally { release.Set(); }
                var finished = System.Diagnostics.Stopwatch.StartNew();
                while ((bool)Field(overlay, "validating") && finished.Elapsed < TimeSpan.FromSeconds(2)) await Task.Delay(20);
                Check(!(bool)Field(overlay, "validating"), "The old validation worker finishes within the fixture cleanup bound");
                overlay.Draw(hwnd, element, "ring", "Fresh target", () => true); await overlay.CheckValidity();
                Check(overlay.IsVisible, "A fresh verified draw works after the old validator finishes"); overlay.Clear();
                Check(!Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Any(p => new[] { ".wav", ".png", ".jpg" }.Contains(Path.GetExtension(p))),
                    "The fixture persists no microphone audio, screenshots or provider payloads");
                Console.WriteLine($"ALL {count} PARITY43 CHECKS PASSED; owned windows and injected expiry/focus only; no physical mic, headphones, high-contrast OS toggle or live provider acceptance"); exit = 0;
            } catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally {
                companion?.Dispose();
                typeof(MainWindow).GetField("shuttingDown", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(home, true);
                home.Close();
                var resolved = Path.GetFullPath(folder);
                if (resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(resolved).StartsWith("Buddy-parity43-", StringComparison.Ordinal)) Directory.Delete(resolved, true);
                app.Shutdown();
            }
        };
        app.Run(home); return exit;
    }
}
