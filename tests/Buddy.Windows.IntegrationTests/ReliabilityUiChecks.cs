using Buddy.Server;
using Buddy.Windows;
using Microsoft.AspNetCore.DataProtection;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

internal static class ReliabilityUiChecks
{
    private static object? Field(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(value);
    private static void Set(object value, string name, object? next) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(value, next);
    private static Task Call(object value, string name) => (Task)value.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(value, null)!;
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    private static async Task MakePlan(DesktopAssistant assistant, string task, int timeoutSeconds = 10)
    {
        ((TextBox)Field(assistant, "goal")!).Text = task;
        var panel = (Window)Field(assistant, "panel")!; panel.UpdateLayout();
        Descendants(panel).OfType<Button>().Single(b => Equals(b.Content, "Make plan")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var time = System.Diagnostics.Stopwatch.StartNew();
        while (Field(assistant, "operation") is not null && time.Elapsed < TimeSpan.FromSeconds(timeoutSeconds)) await Task.Delay(20);
        if (Field(assistant, "operation") is not null) throw new TimeoutException("Owned fixture planning did not finish.");
    }
    internal static int Run(bool realModel = false)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var fixture = new PracticeWindow(); int count = 0, exit = 1;
        fixture.Editor.Text = "Owned fixture draft; planning must leave this unchanged.";
        string folder = Path.Combine(Path.GetTempPath(), "Buddy-reliability-ui44-" + Guid.NewGuid()); Directory.CreateDirectory(folder);
        void Check(bool ok, string note) { if (!ok) throw new Exception("FAIL: " + note); count++; Console.WriteLine("PASS: " + note); }
        fixture.Loaded += async (_, _) => {
            DesktopAssistant? assistant = null;
            try {
                BuddyTheme.Apply("Light", true);
                var hwnd = new WindowInteropHelper(fixture).Handle; ScreenPerception.PracticeHandle = hwnd;
                fixture.Activate(); InputNative.SetForegroundWindow(hwnd); await Task.Delay(120);
                var preferences = new DesktopPreferences { AgentEnabled = false, ReduceMotion = true };
                var perception = new ScreenPerception(() => preferences);
                var snapshot = await perception.Capture(hwnd, default);
                var editor = snapshot.Context.Elements.Single(e => e.Name == "Example text" && e.Role == "Edit");
                Check(snapshot.Context.Elements.Count > 0 && snapshot.Context.Elements.Contains(editor), "Planning fixture observes actual owned-window controls through production UIA");
                if (realModel) {
                    using var local = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false }) { BaseAddress = new("http://127.0.0.1:11434"), Timeout = TimeSpan.FromMinutes(3) };
                    var liveStore = new StateStore(folder, DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(folder, "keys"))));
                    await liveStore.EnsureSaved(); await liveStore.Update(s => { s.Model = "gemma3:4b"; return true; });
                    var liveService = new BuddyService(liveStore, new(local)) { AgentEnabled = false };
                    assistant = new(() => liveService, () => preferences, () => hwnd, _ => { });
                    await assistant.Open("agent", "");
                    await MakePlan(assistant, "Guide me through Comet browser", 180);
                    Check((string)Field(assistant, "mode")! == "guide" && ((Window)Field(assistant, "panel")!).Title == "Buddy - Guide" && Field(assistant, "guide") is GuidePlan && Field(assistant, "plan") is null,
                        "Exact Comet guidance uses actual Make plan with Agent disabled and deterministic missing-view handling on an observed owned window");
                    Check(!((GuidanceOverlay)Field(assistant, "overlay")!).IsVisible && !((Button)Field(assistant, "run")!).IsEnabled,
                        "The Comet guidance request cannot target unrelated fixture controls or become executable");
                    await MakePlan(assistant, "Guide me to the Example text field in this fixture", 180);
                    var liveGuide = Field(assistant, "guide") as GuidePlan;
                    Console.WriteLine("REAL OWNED-WINDOW GUIDE: " + JsonSerializer.Serialize(new { plan = liveGuide, state = ((TextBlock)Field(assistant, "state")!).Text, editor, overlayVisible = ((GuidanceOverlay)Field(assistant, "overlay")!).IsVisible }));
                    Check(liveGuide is not null && liveGuide.Steps?.Any(s => s.Ref == editor.Ref && s.Target == editor.Name && s.Role == editor.Role) == true && liveGuide.Lessons is { Count: > 0 } && ((GuidanceOverlay)Field(assistant, "overlay")!).IsVisible && ((TextBlock)Field(assistant, "state")!).Text.Contains("pointer verified"),
                        "Real model nested GuidePlan parses and resolves a verified pointer from observed owned-window UIA");
                    Check(fixture.Editor.Text == "Owned fixture draft; planning must leave this unchanged.", "Real local-model Guide leaves the owned source field unchanged");
                    Console.WriteLine($"ALL {count} RELIABILITY UI44 REAL-MODEL CHECKS PASSED; observed owned window and gemma3:4b, no real Comet launch or physical audio acceptance"); exit = 0; return;
                }
                using var mock = new Model(editor);
                using var http = new HttpClient(mock) { BaseAddress = new("http://127.0.0.1:11434") };
                var store = new StateStore(folder, DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(folder, "keys")))); await store.EnsureSaved();
                var service = new BuddyService(store, new(http)) { AgentEnabled = true };
                var jobs = new JobLedger();
                assistant = new(() => service, () => preferences, () => hwnd, _ => { }, jobs: jobs);
                await assistant.Open("agent", "");
                await MakePlan(assistant, "Guide me through Comet browser");
                var panel = (Window)Field(assistant, "panel")!;
                var run = (Button)Field(assistant, "run")!;
                Check((string)Field(assistant, "mode")! == "guide" && panel.Title == "Buddy - Guide" && ((TextBlock)Field(assistant, "modeLabel")!).Text.StartsWith("Guide -"),
                    "Actual Make plan changes a stale Agent panel into visible Guide for the exact Comet request");
                Check(mock.AgentCalls == 0 && Field(assistant, "guide") is GuidePlan && run.Visibility == Visibility.Collapsed && !run.IsEnabled && jobs.Snapshot.Count == 0,
                    "Guidance-only request uses Guide even with Agent disabled, without creating an action job or executable plan");
                Check(!((GuidanceOverlay)Field(assistant, "overlay")!).IsVisible, "A Comet request does not point at controls in the unrelated owned fixture");

                await MakePlan(assistant, "Guide me through this fixture");
                Check(mock.LastInput.Contains("Example text") && mock.LastInput.Contains(editor.Ref) && Field(assistant, "guide") is GuidePlan,
                    "Owned-window Guide planning passes observed controls and parses a nested GuidePlan through the real panel path");
                Check(((GuidanceOverlay)Field(assistant, "overlay")!).IsVisible && ((TextBlock)Field(assistant, "state")!).Text.Contains("pointer verified"),
                    "Nested owned-window Guide resolves its actual editor and shows verified ink without invoking it");

                var otherTarget = snapshot.Context.Elements.Single(e => e.Name == "Simulate export (no file)" && e.Role == "Button");
                mock.GuideOverride = new("Review the target.", [new("Review the simulation control.", editor.Ref, otherTarget.Name, otherTarget.Role)]);
                int beforeRepair = mock.GuideCalls;
                await MakePlan(assistant, "Guide me to Example text in this fixture");
                var rejectedGuide = (GuidePlan)Field(assistant, "guide")!;
                Check(mock.GuideCalls == beforeRepair + 2 && rejectedGuide.Steps is { Count: 0 } && rejectedGuide.Lessons is { Count: 1 },
                    "Actual Make plan bounds correction of a contradictory editor reference and button name");
                Check(!((GuidanceOverlay)Field(assistant, "overlay")!).IsVisible && rejectedGuide.Lessons![0].Role == "" && !run.IsEnabled,
                    "Rejected pointer stays nonpointable through native lesson display even when its conflicting name matches another real control");
                mock.GuideOverride = null;

                preferences = preferences with { AgentEnabled = true };
                Check(AssistantIntent.PlanningMode("agent", "Teach me how to open Comet") == "guide", "Teaching how to open an app remains manual guidance");
                foreach (string polite in new[] { "Please guide me through Comet browser", "Can you guide me through Comet browser", "Could you please guide me through Comet browser" })
                    Check(AssistantIntent.PlanningMode("agent", polite) == "guide", $"Polite guidance stays manual: {polite}");
                Check(AssistantIntent.Mode("Please search my app notes editor") == "talk" && AssistantIntent.KnowledgeQuery("search my app notes editor") == "editor",
                    "Polite guidance normalization does not rewrite unrelated app-notes command semantics");
                Check(AssistantIntent.PlanningMode("guide", "Teach me this and write a note") == "agent" && AssistantIntent.PlanningMode("agent", "Guide me and then click Save") == "agent",
                    "Mixed teaching and requested actions retain Agent planning and approval boundaries");
                await MakePlan(assistant, "Guide me through this fixture and click its button");
                Check((string)Field(assistant, "mode")! == "agent" && panel.Title == "Buddy - Agent plan" && mock.AgentCalls == 1 && run.IsEnabled,
                    "Actual Make plan preserves a mixed instruction as a reviewable Agent plan");
                Check(jobs.Snapshot.Last().State == JobState.AwaitingApproval && jobs.Snapshot.Last().Receipts.Count == 0 && Field(assistant, "interruption") is null,
                    "Prepared actions await approval and have no execution receipts or input dispatcher");
                ((TextBox)Field(assistant, "goal")!).Text = "Open a different item";
                Check(!run.IsEnabled && Field(assistant, "plan") is null, "Editing a reviewed goal invalidates the previous executable plan immediately");

                mock.Clarify = true;
                await MakePlan(assistant, "Open the item I meant");
                Check(((TextBox)Field(assistant, "planText")!).Text == Model.Question && ((TextBlock)Field(assistant, "state")!).Text.StartsWith("Clarification needed"),
                    "An empty-action planning result visibly asks its clarification question");
                Check(!run.IsEnabled && jobs.Snapshot.Last().State == JobState.ReviewNeeded && jobs.Snapshot.Last().Receipts.Count == 0,
                    "Clarification keeps Run disabled and never enters AwaitingApproval");
                await Call(assistant, "Execute");
                Check(!(bool)Field(assistant, "executing")! && Field(assistant, "interruption") is null && jobs.Snapshot.Last().Receipts.Count == 0,
                    "Even direct invocation of Execute cannot run an empty clarification");
                Set(assistant, "plan", new AssistantPlan("Invalid launch", [new("open", Value: "cmd.exe /c echo unsafe")]));
                Set(assistant, "job", null);
                await Call(assistant, "Execute");
                Check(!(bool)Field(assistant, "executing")! && Field(assistant, "interruption") is null && !run.IsEnabled && ((TextBlock)Field(assistant, "state")!).Text.Contains("unsupported app launch"),
                    "Nonempty Execute still applies strict launch validation before preparing any dispatcher");
                Check(fixture.Editor.Text == "Owned fixture draft; planning must leave this unchanged.", "Planning, teaching and clarification leave the owned editor unchanged");
                assistant.Dispose(); assistant = null;
                await CheckIslandAndSettings(fixture, folder, Check);
                Console.WriteLine($"ALL {count} RELIABILITY UI44 CHECKS PASSED; owned-window UIA plus mocked inference only; no installed profile, real Comet launch, external account or physical audio acceptance"); exit = 0;
            } catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally {
                assistant?.Dispose(); ScreenPerception.PracticeHandle = IntPtr.Zero; fixture.Close();
                string resolved = Path.GetFullPath(folder);
                if (resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(resolved).StartsWith("Buddy-reliability-ui44-", StringComparison.Ordinal)) Directory.Delete(resolved, true);
                app.Shutdown();
            }
        };
        app.Run(fixture); return exit;
    }
    private static async Task CheckIslandAndSettings(Window fixture, string folder, Action<bool, string> check)
    {
        check(new DesktopPreferences().IslandMode == "Hidden" && !new DesktopPreferences().CoreControlsOnly,
            "The optional bar starts hidden and existing advanced controls remain visible by default");
        check(new DesktopPreferences { IslandMode = "invalid" }.IslandMode == "Hidden", "Unknown persisted bar modes normalize to hidden");
        var settings = new DesktopPreferences { ReduceMotion = true, AgentEnabled = true, NeuralSpeakerId = 85, NeuralPreset = "f3", HeadphonesOnly = true,
            AdditionalSettings = new() { ["FutureFixtureSetting"] = JsonSerializer.SerializeToElement("preserve") } };
        string path = Path.Combine(folder, "desktop.json");
        int modeSaves = 0; var routes = new List<string>(); bool suppressed = false;
        var activity = new IslandActivity("Owned fixture guidance", "Review the current step", CompanionMood.Pointing, "Local brain ready · fixture-model");
        using (var bar = new CompanionIsland(() => activity, routes.Add, () => suppressed, mode => { settings = settings with { IslandMode = mode }; settings.Save(path); modeSaves++; })) {
            var hwnd = new WindowInteropHelper(fixture).Handle; fixture.Activate(); InputNative.SetForegroundWindow(hwnd);
            bar.SetMode("Compact"); bar.UpdateLayout();
            check(bar.IsVisible && bar.Mode == "Compact" && Native.GetForegroundWindow() == hwnd,
                "Showing the compact bar preserves the owned source window's foreground focus");
            check(Descendants(bar).OfType<Image>().Any(i => ReferenceEquals(i.Source, AppBranding.Character)), "The bar uses the original Buddy artwork");
            var expand = Descendants(bar).OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Expand Buddy bar");
            expand.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); bar.UpdateLayout();
            check(bar.Mode == "Expanded" && DesktopPreferences.Load(path).IslandMode == "Expanded" && modeSaves == 1,
                "Explicit expansion persists exactly once without activating a task");
            check(routes.Count == 0 && Descendants(bar).OfType<TextBlock>().Any(t => t.Text.Contains("fixture-model") && t.Text.Contains("Provider disconnected")),
                "Bar status reflects the supplied local brain state while external providers remain disconnected");
            foreach (string action in new[] { "Type", "Voice", "Guide", "Refine", "Select area", "Stop" })
                Descendants(bar).OfType<Button>().Single(b => Equals(b.Content, action)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(routes.SequenceEqual(new[] { "Type", "Voice", "Guide", "Refine", "Select area", "Stop" }),
                "Core buttons route once to their existing owner, including shared Stop; the bar creates no voice session");
            activity = activity with { Status = "Listening on this PC", Mood = CompanionMood.Listening, BrainStatus = "Local brain unavailable" }; bar.Refresh();
            check(AutomationProperties.GetName(bar).Contains("Listening on this PC") && Descendants(bar).OfType<TextBlock>().Any(t => t.Text.Contains("Local brain unavailable")),
                "Current task and live status remain accessible and do not claim unavailable service readiness");
            check(!bar.HasAnimatedProperties && !BuddyTheme.Animate, "Reduced-motion bar state changes do not animate");
            suppressed = true; bar.Refresh(); check(!bar.IsVisible && bar.Mode == "Expanded", "Companion ownership suppression hides the bar without changing its saved mode");
            suppressed = false; bar.Refresh(); bar.UpdateLayout();
            Descendants(bar).OfType<Button>().Single(b => Equals(b.Content, "Hide bar")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            check(!bar.IsVisible && bar.Mode == "Hidden" && DesktopPreferences.Load(path).IslandMode == "Hidden" && modeSaves == 2,
                "Hide bar persists Hidden without stopping or repeating any task");
            bar.SetMode("Hidden"); check(modeSaves == 2, "Reapplying saved mode does not invoke persistence recursively");
        }
        var home = new MainWindow(false, settings, next => { next.Save(path); settings = next; });
        try {
            home.Show(); home.OpenSettingsSection("General"); home.UpdateLayout();
            var core = Descendants(home).OfType<CheckBox>().Single(c => AutomationProperties.GetName(c) == "Show core controls first"); core.IsChecked = true;
            Descendants(home).OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "Top-edge Buddy bar").SelectedItem = "Compact";
            Descendants(home).OfType<Button>().Single(b => Equals(b.Content, "Save changes")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); home.UpdateLayout();
            var nav = (Dictionary<string, Button>)Field(home, "navigationButtons")!;
            var sections = (Dictionary<string, Button>)Field(home, "settingsButtons")!;
            check(settings.CoreControlsOnly && settings.IslandMode == "Compact" && nav["Devices"].Visibility == Visibility.Collapsed && !sections.ContainsKey("Connectors"),
                "General settings reversibly hide advanced navigation and preserve the chosen bar mode");
            home.NavigateHome("Conversations"); home.UpdateLayout();
            check(!Descendants(home).OfType<Button>().Any(b => Equals(b.Content, "Agent task")) && Descendants(home).OfType<Button>().Any(b => Equals(b.Content, "Refine source field")),
                "Core Home keeps guidance and refinement while de-emphasizing generic Agent dispatch");
            home.OpenSettingsSection("Add-ons"); home.UpdateLayout();
            check(Descendants(home).OfType<CheckBox>().Any(c => AutomationProperties.GetName(c) == "Enable circle selection") && Descendants(home).OfType<Button>().Any(b => Equals(b.Content, "App notes")) && !Descendants(home).OfType<Button>().Any(b => Equals(b.Content, "Local jobs and app knowledge")),
                "Selected-area guidance and App notes stay discoverable when advanced jobs are de-emphasized");
            home.OpenSettingsSection("General"); home.UpdateLayout();
            Descendants(home).OfType<CheckBox>().Single(c => AutomationProperties.GetName(c) == "Show core controls first").IsChecked = false;
            Descendants(home).OfType<Button>().Single(b => Equals(b.Content, "Save changes")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); home.UpdateLayout();
            sections = (Dictionary<string, Button>)Field(home, "settingsButtons")!;
            var restored = DesktopPreferences.Load(path);
            check(nav["Devices"].Visibility == Visibility.Visible && sections.ContainsKey("Connectors") && restored.AgentEnabled && restored.NeuralSpeakerId == 85 && restored.NeuralPreset == "f3" && restored.HeadphonesOnly && restored.AdditionalSettings!["FutureFixtureSetting"].GetString() == "preserve",
                "Restoring advanced presentation retains permissions, voice, headphones and unknown settings");
            check(Field(home, "host") is null, "Presentation changes in the isolated settings fixture never start a service or account connection");
        } finally { Set(home, "shuttingDown", true); home.Close(); }
        await Task.CompletedTask;
    }
    private sealed class Model(ScreenElement editor) : HttpMessageHandler
    {
        internal const string Question = "Which item should I open? Name the app or provide a public HTTPS address.";
        internal bool Clarify; internal int AgentCalls, GuideCalls; internal string LastInput = ""; internal GuidePlan? GuideOverride;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var data = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            LastInput = data.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
            bool actions = data.RootElement.GetProperty("format").GetProperty("properties").TryGetProperty("actions", out _);
            object result;
            if (actions) { AgentCalls++; result = Clarify ? new AssistantPlan(Question, []) : new AssistantPlan("Open Comet after approval.", [new("open", Value: "comet", Description: "Open the verified installed Comet browser", Risk: "high")]); }
            else { GuideCalls++; result = GuideOverride ?? new GuidePlan("Inspect the owned editor.", [new("Look at the editor", editor.Ref, editor.Name, editor.Role)]); }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { message = new { content = JsonSerializer.Serialize(result, StateStore.Json) }, done = true })) };
        }
    }
}
