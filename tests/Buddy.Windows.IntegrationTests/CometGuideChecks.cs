using Buddy.Server;
using Buddy.Windows;
using Microsoft.AspNetCore.DataProtection;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;

internal static class CometGuideChecks
{
    private delegate bool EnumWindow(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, IntPtr parameter);
    private static readonly SemaphoreSlim chromeGate = new(1, 1);
    // Fixed public chrome labels only. Never include a page title, tab title, URL,
    // address value, document text, inferred label or fuzzy match.
    private static readonly HashSet<string> Buttons = new(StringComparer.Ordinal) {
        "Reload", "Reload this page", "Back", "Forward", "New tab", "New Tab", "Open a new tab", "Customize and control Comet", "Comet menu"
    };
    private static readonly HashSet<string> Editors = new(StringComparer.Ordinal) { "Address and search bar" };

    // Root dispatches metadata and local-model modes separately. This method never
    // creates a visible window, changes focus, draws ink, inputs text, launches an
    // app, saves a guide or captures an image.
    internal static int Run(bool localModel = false, ScreenObservationOptions? observationOptions = null)
    {
        string? blockedApps = Environment.GetEnvironmentVariable("BUDDY_COMET_BLOCKED_APPS");
        string? model = Environment.GetEnvironmentVariable("BUDDY_COMET_GUIDE_MODEL");
        if (blockedApps is null || localModel && (Environment.GetEnvironmentVariable("BUDDY_COMET_GUIDE_ALLOW_LOCAL_MODEL") != "1" || string.IsNullOrWhiteSpace(model))) {
            Console.WriteLine("NOT RUN: explicit privacy policy is required; local Guide also requires separate model opt-in and an existing selected model."); return 2;
        }
        if (model is not null && (model.Length > 200 || model.Any(char.IsControl))) return 2;
        int exit = 2; var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Dispatcher.BeginInvoke(new Action(async () => {
            try { exit = await Observe(blockedApps, localModel ? model : null, observationOptions); }
            catch (Exception ex) { Console.WriteLine(JsonSerializer.Serialize(new { kind = "comet_guide_error", error = ex is WindowSelectionException e ? e.Code : ex is BuddyException b ? b.Code : ex.GetType().Name })); exit = 1; }
            finally { app.Shutdown(); }
        }));
        app.Run(); return exit;
    }

    private static async Task<int> Observe(string blockedApps, string? model, ScreenObservationOptions? observationOptions)
    {
        var windows = new List<IntPtr>();
        EnumWindows((window, _) => {
            if (!Native.IsSelectableWindow(window)) return true;
            Native.GetWindowThreadProcessId(window, out uint pid);
            try { using var process = Process.GetProcessById(checked((int)pid)); if (process.ProcessName.Equals("comet", StringComparison.OrdinalIgnoreCase)) windows.Add(window); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
            return true;
        }, IntPtr.Zero);
        int captured = 0;
        var perception = new ScreenPerception(() => new DesktopPreferences { BlockedApps = blockedApps },
            metrics => Console.WriteLine(JsonSerializer.Serialize(new { kind = "comet_capture_metrics", metrics })), observationOptions);
        foreach (IntPtr window in windows.Distinct().Take(4)) {
            using var captureStop = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var identity = WindowSelection.Capture(window);
            if (!identity.App.Equals("comet", StringComparison.OrdinalIgnoreCase)) continue;
            var snapshot = await perception.Capture(window, captureStop.Token, expected: identity);
            identity.Validate();
            var independent = await FilterChrome(snapshot, identity, captureStop.Token);
            var qualified = await BrowserChromeObservation.Qualify(snapshot, captureStop.Token);
            var chrome = independent.Where(e => qualified.Elements.Any(q => q.Ref == e.Ref && q.Name == e.Name && q.Role == e.Role)).ToList();
            captured++;
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "comet_chrome_observation", identity.ProcessId, identity.ThreadId,
                elementCount = snapshot.Context.Elements.Count, snapshot.Complete, eligibleChromeCount = chrome.Count,
                roles = snapshot.Context.Elements.GroupBy(e => e.Role).ToDictionary(g => g.Key, g => g.Count()),
                pageContentLogged = false, screenshot = false, focusChanged = false, inputSent = false }));
            if (model is null) continue;
            var target = chrome.FirstOrDefault(e => e.Name is "Reload" or "Reload this page")
                ?? chrome.FirstOrDefault(e => e.Name is "New tab" or "New Tab" or "Open a new tab")
                ?? chrome.FirstOrDefault(e => e.Name == "Address and search bar");
            if (target is null) continue;
            // Only fixed public labels, references and geometry reach the local
            // model. The full snapshot, title, Nodes and private rectangles do not.
            string goal = target.Name is "Reload" or "Reload this page" ? "Explain how I could reload the page in Comet; do not perform any action."
                : target.Role == "Edit" ? "Explain how I could use the address and search bar in Comet; do not perform any action."
                : "Explain how I could open a new tab in Comet; do not perform any action.";
            var context = new ScreenContext("comet", "", chrome);
            return await Guide(perception, identity, context, target, goal, model, snapshot.Complete);
        }
        Console.WriteLine(JsonSerializer.Serialize(new { kind = "comet_guide_summary", captured, modelRequested = model is not null,
            guide = model is null ? "NOT RUN: metadata-only phase" : "NOT RUN: no verified allowlisted chrome target", fullBrowserTaskAccepted = false }));
        return model is null && captured > 0 ? 0 : 2;
    }

    private static async Task<List<ScreenElement>> FilterChrome(ScreenSnapshot snapshot, WindowSelection identity, CancellationToken ct)
    {
        await chromeGate.WaitAsync(ct);
        var worker = Task.Run(() => {
            try {
                var approved = new List<ScreenElement>();
                foreach (var element in snapshot.Context.Elements.Where(e => e.Enabled &&
                    (e.Role == "Button" && Buttons.Contains(e.Name) || e.Role == "Edit" && Editors.Contains(e.Name))).Take(24)) {
                    ct.ThrowIfCancellationRequested();
                    if (!snapshot.Nodes.TryGetValue(element.Ref, out var node) || !IsChromeDescendant(node, identity, ct)) continue;
                    if (!double.IsFinite(element.X + element.Y + element.Width + element.Height) || element.Width <= 0 || element.Height <= 0) continue;
                    approved.Add(element);
                }
                identity.Validate(); ct.ThrowIfCancellationRequested();
                // Ambiguous names remain unavailable; no fuzzy or role-only fallback.
                return approved.Where(e => approved.Count(other => other.Name == e.Name && other.Role == e.Role) == 1).Take(12).ToList();
            } finally { chromeGate.Release(); }
        }, CancellationToken.None);
        return await worker.WaitAsync(ct);
    }
    private static bool IsChromeDescendant(AutomationElement node, WindowSelection identity, CancellationToken ct)
    {
        bool chromeContainer = false;
        try {
            var rootId = AutomationElement.FromHandle(identity.Window).GetRuntimeId();
            for (int depth = 0; node is not null && depth < 20; depth++) {
                ct.ThrowIfCancellationRequested(); var current = node.Current;
                if (current.ControlType == ControlType.Document || current.IsPassword) return false;
                if (current.ControlType == ControlType.ToolBar || current.ControlType == ControlType.Tab) chromeContainer = true;
                if (node.GetRuntimeId().SequenceEqual(rootId)) return chromeContainer && current.ControlType == ControlType.Window && current.NativeWindowHandle == identity.Window.ToInt64() && current.ProcessId == identity.ProcessId;
                node = TreeWalker.ControlViewWalker.GetParent(node);
            }
        } catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException) { return false; }
        return false;
    }

    private static async Task<int> Guide(ScreenPerception perception, WindowSelection identity, ScreenContext context, ScreenElement target, string goal, string model, bool initialComplete)
    {
        string folder = Path.Combine(Path.GetTempPath(), "Buddy-comet-guide-" + Guid.NewGuid());
        try {
            using var handler = new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false };
            using var http = new HttpClient(handler) { BaseAddress = new("http://127.0.0.1:11434/"), Timeout = TimeSpan.FromMinutes(3) };
            var store = new StateStore(folder, new EphemeralDataProtectionProvider());
            await store.Update(s => { s.Model = model; return true; });
            var service = new BuddyService(store, new(http)) { AgentEnabled = false, WebEnabled = false };
            var readiness = await service.Engine.Status(model, model);
            if (!readiness.Ready) { Console.WriteLine("NOT RUN: selected local model is not ready; nothing was installed."); return 2; }
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(120)); var watch = Stopwatch.StartNew();
            var plan = await service.PlanGuide(new(goal, context, false), stop.Token, browserChromeVerified:true);
            identity.Validate();
            var fresh = await perception.Capture(identity.Window, stop.Token, expected: identity);
            var current = await FilterChrome(fresh, identity, stop.Token);
            bool Exact(GuideStep step) => current.Count(e => e.Ref == step.Ref && e.Name.Equals(step.Target, StringComparison.OrdinalIgnoreCase) && e.Role.Equals(step.Role, StringComparison.OrdinalIgnoreCase) && e.Enabled) == 1
                && context.Elements.Any(old => old.Ref == step.Ref && current.Any(now => now.Ref == old.Ref && now.X == old.X && now.Y == old.Y && now.Width == old.Width && now.Height == old.Height));
            var steps = plan.Steps ?? [];
            bool independentGrounding = steps.Count is > 0 and <= 3 && steps.All(Exact) && steps.Any(s => s.Ref == target.Ref);
            string finalProse = string.Join("\n", new[] { plan.Summary }.Concat(steps.Select(s => s.Instruction)).Concat(plan.Lessons?.Select(l => l.Instruction) ?? []));
            bool performedClaim = Regex.IsMatch(finalProse, @"\b(?:I|Buddy|we)\s+(?:(?:have|has|already)\s+)*(?:clicked|pressed|typed|activated|opened|reloaded|navigated|performed|completed)\b|\b(?:page|tab|control|browser)\s+(?:has been|was|is now)\s+(?:reloaded|opened|activated|navigated)\b|\bsuccessfully\s+(?:reloaded|opened|clicked|completed)\b|^\s*(?:Done|Completed)[.!]", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            string requestedConcept = target.Role == "Edit" ? @"address|search bar" : target.Name.Contains("tab", StringComparison.OrdinalIgnoreCase) ? @"new tab" : @"reload|refresh";
            bool usefulManualExplanation = independentGrounding && !performedClaim && steps.All(s => s.Expect?.Kind is null or "manual")
                && steps.Any(s => s.Ref == target.Ref && Regex.IsMatch(s.Instruction, requestedConcept, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)));
            bool usefulOrientation = plan.Lessons is { Count: >= 2 } && plan.Lessons.Select(l => l.Instruction).Distinct(StringComparer.OrdinalIgnoreCase).Count() == plan.Lessons.Count
                && Regex.IsMatch(finalProse, @"same tab|separate place|website address|search query", RegexOptions.IgnoreCase)
                && Regex.IsMatch(finalProse, @"check|confirm", RegexOptions.IgnoreCase)
                && !plan.Lessons.All(l => l.Instruction.Trim().Equals(steps.FirstOrDefault()?.Instruction.Trim(), StringComparison.OrdinalIgnoreCase));
            usefulManualExplanation &= usefulOrientation;
            bool noSavedContent = await store.Read(s => s.Conversations.Count + s.Guides.Count + s.Jobs.Count + s.Knowledge.Count + s.Audit.Count) == 0;
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "comet_local_guide", model, authoredLocalGuide = BrowserGuideLessons.IsSupported(new(goal, context)), modelInferenceRequired = !BrowserGuideLessons.IsSupported(new(goal, context)), elapsedMs = watch.ElapsedMilliseconds,
                stepCount = steps.Count, lessonCount = plan.Lessons?.Count ?? 0, summaryLength = plan.Summary?.Length ?? 0,
                independentGrounding, usefulManualExplanation, usefulOrientation, performedClaim, noSavedContent, initialComplete,
                currentComplete = fresh.Complete, transmittedElements = context.Elements.Count, transmittedTitleLength = context.Title.Length,
                transmittedContent = "fixed public browser chrome labels + real references/geometry only",
                finalGuideProse = finalProse[..Math.Min(finalProse.Length, 7000)], finalGuideProseTruncated = finalProse.Length > 7000,
                finalGuideProvenance = "generated only from fixed public chrome labels and geometry, not page/title/address values; raw HTTP omitted",
                status = usefulManualExplanation && noSavedContent ? "PASS" : "FAIL", fullBrowserTaskAccepted = false,
                screenshots = false, focusChanged = false, inputSent = false, guideExecuted = false }));
            return usefulManualExplanation && noSavedContent ? 0 : 1;
        } finally {
            string full = Path.GetFullPath(folder), temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("Buddy-comet-guide-", StringComparison.Ordinal) && Directory.Exists(full)) Directory.Delete(full, true);
        }
    }
}
