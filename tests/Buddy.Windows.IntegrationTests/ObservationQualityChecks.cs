using Buddy.Windows;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;

internal static class ObservationQualityChecks
{
    internal static void Diagnostics(Action<bool, string> check)
    {
        var session = new ScreenObservationSession();
        session.Selected(new(new IntPtr(123), 456, 789, 101112, "Never copy this app label into diagnostics"));
        session.Enqueue(1); session.BeginTraversal(); session.Visit(0, 0);
        session.Offscreen(); session.Password(); session.Omit("depth-limit"); session.ProviderFailure(new InvalidOperationException("Never copy this provider payload into diagnostics"));
        session.Counts(3, 1); session.EndTraversal();
        var metrics = session.Snapshot("partial", false);
        string json = JsonSerializer.Serialize(metrics);
        check(!json.Contains("Never copy") && !json.Contains("provider payload"), "Diagnostic payload excludes app labels and exception content");
        check(metrics.SelectedIdentity is { Window: 123, ThreadId: 456, ProcessId: 789, ProcessStarted: 101112 }, "Safe native identity metadata remains available for capture diagnosis");
        check(metrics.Visited == 1 && metrics.Enqueued == 1 && metrics.Remaining == 0 && metrics.ElementCount == 3 && metrics.PrivateRectangleCount == 1,
            "Visited nodes, produced elements and pending nodes remain separate diagnostic counts");
        check(metrics.OffscreenSkipped == 1 && metrics.PasswordSkipped == 1 && metrics.DepthOmissions == 1 &&
            metrics.LimitReasons.Contains("depth-limit") && metrics.ProviderErrors["invalid-operation"] == 1,
            "Pruned privacy/offscreen nodes and depth/provider incompleteness have explicit separate causes");
        session.Omit("node-limit"); session.ProviderFailure(new InvalidOperationException("Later provider detail"));
        check(!metrics.LimitReasons.Contains("node-limit") && metrics.ProviderErrors["invalid-operation"] == 1,
            "Published diagnostic snapshots do not mutate when a late worker continues");
        var timedOut = session.Snapshot("timeout", false, new OperationCanceledException("Untrusted detail"));
        check(timedOut.FailureCode == "capture-deadline" && timedOut.LimitReasons.Contains("capture-deadline") && !timedOut.Complete,
            "Deadline refusal remains distinct from traversal limits and user cancellation");
        var failed = new ScreenObservationSession().Snapshot("failed", false,
            new WindowSelectionException("process-query", "Never log this message", new IntPtr(456), 789, 123, 5));
        check(failed.FailureCode == "process-query" && failed.NativeError == 5 && failed.SelectedIdentity is { Window: 456, ProcessId: 789, ThreadId: 123 },
            "Failed native access retains available identity and error code without the error message");
        foreach (var refusal in new[] {
            ("Buddy cannot access a secure desktop.", "secure-desktop"),
            ("Buddy blocks this window because it may contain private credentials or financial data.", "private-window"),
            ("This application is in your privacy blocklist.", "blocked-app"),
            ("Untrusted app content must never become a diagnostic field", "invalid-operation") }) {
            var refusalMetrics = new ScreenObservationSession().Snapshot("failed", false, new InvalidOperationException(refusal.Item1));
            check(refusalMetrics.FailureCode == refusal.Item2 && !JsonSerializer.Serialize(refusalMetrics).Contains(refusal.Item1),
                "Known access refusals receive fixed safe codes; raw exception text remains excluded");
        }
        foreach (int depth in new[] { 0, 25 }) {
            try { _ = new ScreenObservationOptions(DepthLimit: depth).Validate(); check(false, "Unbounded diagnostic depth refuses"); }
            catch (ArgumentOutOfRangeException) { check(true, "Out-of-bounds diagnostic depth cannot silently expand traversal"); }
        }
    }
    internal static int Run()
    {
        int count = 0, exit = 1;
        void Check(bool ok, string note) { if (!ok) throw new Exception("FAIL: " + note); count++; Console.WriteLine("PASS: " + note); }
        Diagnostics(Check);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new Window { Width = 620, Height = 700, Title = "Owned observation fixture", Content = SmallContent() };
        window.Loaded += async (_, _) => {
            try {
                BuddyTheme.Apply("Night Mint", true);
                var hwnd = new WindowInteropHelper(window).Handle; ScreenPerception.PracticeHandle = hwnd;
                window.Activate(); InputNative.SetForegroundWindow(hwnd); await Task.Delay(80);
                var reports = new List<ScreenObservationMetrics>();
                var perception = new ScreenPerception(() => new(), reports.Add);
                var selected = WindowSelection.Capture(hwnd);
                var small = await perception.Capture(hwnd, default, expected: selected);
                Check(reports.Count == 1 && ReferenceEquals(small.Observation, reports[0]), "Production Capture attaches and reports one immutable observation result");
                var first = small.Observation!;
                Check(first.NodeLimit == 400 && first.DepthLimit == 16 && first.TraversalLimitMilliseconds == 600 && first.DeadlineMilliseconds == 2000,
                    "Observation diagnostics expose the measured depth-16 traversal with unchanged node/time/deadline limits");
                Check(first.Visited > 0 && first.Enqueued >= first.Visited && first.Remaining == first.Enqueued - first.Visited && first.ElementCount == small.Context.Elements.Count,
                    "Owned native traversal reports consistent node, queue and result counts");
                Check(first.PasswordSkipped >= 1 && small.PrivateRects.Count >= 1,
                    "Owned password controls remain excluded and their rectangles remain redacted");
                Check(!small.Context.Elements.Any(element => element.Name.Contains("owned-secret-value", StringComparison.Ordinal)), "Password values never become observation elements");
                Check(first.AccessStrategy == "cached-properties" && first.CachedPropertyReads > 0 && first.CacheRefreshes > 0 && first.NavigationReads > 0 && first.RuntimeIdReads > 0 && first.TraversalElapsedMilliseconds <= first.ElapsedMilliseconds,
                    "Cached capture exposes property/cache/navigation/runtime work independently of outer checks");
                var live = await new ScreenPerception(() => new(), options: new(ScreenObservationMode.LiveProperties)).Capture(hwnd, default, expected: selected);
                Check(live.Observation!.AccessStrategy == "live-properties" && live.Observation.PropertyReads > 0 && live.Observation.CacheRefreshes == 0,
                    "Baseline live-property mode remains available for bounded diagnostic comparison");
                Check(small.Context.Elements.Select(e => e.Ref).Order().SequenceEqual(live.Context.Elements.Select(e => e.Ref).Order()) &&
                    small.Context.Elements.Select(e => (e.Ref, e.Name, e.Role, e.Enabled)).OrderBy(e => e.Ref).SequenceEqual(live.Context.Elements.Select(e => (e.Ref, e.Name, e.Role, e.Enabled)).OrderBy(e => e.Ref)) &&
                    small.PrivateRects.SequenceEqual(live.PrivateRects), "Owned cached and live observations retain the same control identities, public metadata and privacy rectangles");
                var textNode = small.Context.Elements.First(e => e.Role == "Edit");
                // UIA must call an owned WPF provider off its dispatcher while that dispatcher pumps.
                // Compare the same live identity in both modes; never turn an unsupported pattern into a pass.
                var patterns = await Task.Run(() => {
                    bool cachedAvailable = (bool)small.Nodes[textNode.Ref].GetCurrentPropertyValue(AutomationElement.IsValuePatternAvailableProperty);
                    bool cachedPattern = small.Nodes[textNode.Ref].TryGetCurrentPattern(ValuePattern.Pattern, out _);
                    bool liveAvailable = (bool)live.Nodes[textNode.Ref].GetCurrentPropertyValue(AutomationElement.IsValuePatternAvailableProperty);
                    bool livePattern = live.Nodes[textNode.Ref].TryGetCurrentPattern(ValuePattern.Pattern, out _);
                    return new { cachedAvailable, cachedPattern, liveAvailable, livePattern };
                }).WaitAsync(TimeSpan.FromSeconds(2));
                Console.WriteLine(JsonSerializer.Serialize(new { kind = "owned_pattern_parity", patterns }));
                Check(patterns.cachedAvailable && patterns.cachedPattern && patterns.liveAvailable && patterns.livePattern,
                    "Cached Full-mode and baseline nodes both retain live pattern access off the owned dispatcher");

                var toolbar = new ToolBar(); toolbar.Items.Add(new Button { Content = "Reload" });
                var chromeFixture = new StackPanel(); chromeFixture.Children.Add(toolbar); chromeFixture.Children.Add(new Button { Content = "Reload" });
                window.Content = chromeFixture; window.UpdateLayout(); await Task.Delay(60);
                var chromeSnapshot = await perception.Capture(hwnd, default, expected: selected);
                var qualifiedChrome = await BrowserChromeObservation.Qualify(chromeSnapshot, default);
                Check(chromeSnapshot.Context.Elements.Count(e => e.Name == "Reload" && e.Role == "Button") == 2 && qualifiedChrome.Elements.Count == 1,
                    "Browser chrome gate retains the toolbar target and rejects a same-labelled page-like button");
                Check(qualifiedChrome.Title == "" && qualifiedChrome.Elements[0].Name == "Reload", "Qualified browser context contains no page title or field value");

                object content = new Button { Content = "Owned deep endpoint", MinHeight = 25 };
                for (int depth = 0; depth < 17; depth++) content = new GroupBox { Header = "Owned group", Content = content, Padding = new(1) };
                window.Content = content; window.UpdateLayout(); await Task.Delay(60);
                var deep = await perception.Capture(hwnd, default, expected: selected);
                var diagnostic = deep.Observation!;
                Check(!deep.Complete && diagnostic.LimitReasons.Count > 0, "Bounded deep owned tree reports an explicit cause rather than silently claiming completeness");
                Check(diagnostic.MaximumDepth <= 16 && diagnostic.Visited <= 400 && diagnostic.Enqueued <= 400,
                    "Measured depth-16 and fixed node limits remain bounded during diagnostics");
                if (diagnostic.DepthOmissions > 0) Check(diagnostic.LimitReasons.Contains("depth-limit") && diagnostic.MaximumDepth == 16, "A truncated depth branch is specifically identified");
                Check(await perception.Frame(deep, default) is null, "Incomplete capture refuses images before any owned screenshot acquisition");
                var diagnosticDepth = await new ScreenPerception(() => new(), options: new(DepthLimit: 24)).Capture(hwnd, default, expected: selected);
                Check(diagnosticDepth.Observation!.DepthLimit == 24 && diagnosticDepth.Observation.NodeLimit == 400 &&
                    diagnosticDepth.Observation.TraversalLimitMilliseconds == 600 && diagnosticDepth.Observation.DeadlineMilliseconds == 2000,
                    "Explicit diagnostic depth keeps the same node, traversal-time and outer deadline budgets");

                using (var assistant = new DesktopAssistant(() => null, () => new(), () => hwnd, _ => { })) {
                    await assistant.Open("guide", "");
                    var method = typeof(DesktopAssistant).GetMethod("ShowObservationScope", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                    method.Invoke(assistant, [deep]);
                    var scope = (TextBlock)typeof(DesktopAssistant).GetField("observationScope", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(assistant)!;
                    Check(scope.Text == "Some controls could not be read; guidance uses the controls observed.", "Guide panel keeps a plain-language partial-observation notice beside the selected app");
                    assistant.Cancel(); Check(scope.Text.Length == 0, "Stopping clears the old partial-guide notice");
                }

                using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); int before = reports.Count;
                try { await perception.Capture(hwnd, cancelled.Token, expected: selected); Check(false, "Cancelled capture refuses"); }
                catch (OperationCanceledException) { Check(reports.Count == before + 1 && reports[^1].Outcome == "cancelled", "Cancelled capture publishes one content-free cancellation record"); }
                Console.WriteLine($"ALL {count} OBSERVATION-46 CHECKS PASSED; injected diagnostics and owned UIA only; no Comet or model acceptance"); exit = 0;
            } catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { ScreenPerception.PracticeHandle = IntPtr.Zero; window.Close(); app.Shutdown(); }
        };
        app.Run(window); return exit;
    }
    private static UIElement SmallContent()
    {
        var panel = new StackPanel { Margin = new(20) };
        panel.Children.Add(new TextBox { Text = "Owned visible draft", Height = 60 });
        var password = new PasswordBox { Password = "owned-secret-value", Height = 30 }; AutomationProperties.SetName(password, "Owned protected field"); panel.Children.Add(password);
        panel.Children.Add(new Button { Content = "Owned review control", Height = 30 });
        panel.Children.Add(new TextBox { Text = "Owned collapsed draft", Visibility = Visibility.Collapsed });
        return panel;
    }
}
