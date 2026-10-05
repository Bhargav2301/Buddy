using Buddy.Server;
using Buddy.Windows;
using Microsoft.AspNetCore.DataProtection;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class UserFailureChecks
{
    private const string Original = "Write a poem on a boat sailing in a sea on a lonely night";
    private const string Faithful = "Draft a poem on a boat sailing in a sea on a lonely night.";
    private delegate bool EnumWindow(IntPtr window, IntPtr value);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindow callback, IntPtr value);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
    private static object? Field(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(target);
    private static void Propose(InlinePromptWindow window, string text) => typeof(InlinePromptWindow).GetMethod("ShowProposal", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [text]);
    private static Task Call(object target, string name) => (Task)target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(target, null)!;
    private static async Task Render(Window window) { window.UpdateLayout(); await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); }
    private static async Task Wait(Func<bool> ready)
    {
        var timer = Stopwatch.StartNew(); while (!ready() && timer.Elapsed < TimeSpan.FromSeconds(8)) await Task.Delay(10);
        if (!ready()) throw new TimeoutException("Owned fixture state did not settle.");
    }
    private static void Invoke(object value, string name, params object[] args) => value.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(value, args);
    private static void SaveOwned(InlinePromptWindow window, string path)
    {
        var root = (FrameworkElement)window.Content; root.UpdateLayout(); var dpi = VisualTreeHelper.GetDpi(root);
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX)), Math.Max(1, (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY)), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var visual = new DrawingVisual(); using (var context = visual.RenderOpen()) context.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }

    // Explicit root dispatch only. No foreground change, input, launch, screenshots,
    // model calls or installed settings. UIA names exist transiently in Capture but
    // only counts and roles are emitted; no page title/text/value enters the log.
    internal static int RunCometReadOnly()
    {
        string? blockedApps = Environment.GetEnvironmentVariable("BUDDY_COMET_BLOCKED_APPS");
        if (blockedApps is null) { Console.WriteLine("NOT RUN: root must explicitly supply the current BlockedApps policy in BUDDY_COMET_BLOCKED_APPS."); return 2; }
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int exit = 2;
        app.Dispatcher.BeginInvoke(new Action(async () => {
            try {
                var windows = new List<IntPtr>();
                EnumWindows((window, _) => {
                    if (!IsWindowVisible(window) || IsIconic(window) || !Native.IsSelectableWindow(window)) return true;
                    Native.GetWindowThreadProcessId(window, out uint pid);
                    try { using var process = Process.GetProcessById(checked((int)pid)); if (process.ProcessName.Equals("comet", StringComparison.OrdinalIgnoreCase)) windows.Add(window); }
                    catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { }
                    return true;
                }, IntPtr.Zero);
                int observed = 0, errors = 0;
                int depth = int.TryParse(Environment.GetEnvironmentVariable("BUDDY_COMET_DIAGNOSTIC_DEPTH"), out int selectedDepth) ? selectedDepth : 12;
                var strategy = Environment.GetEnvironmentVariable("BUDDY_COMET_DIAGNOSTIC_MODE") == "live" ? ScreenObservationMode.LiveProperties : ScreenObservationMode.CachedProperties;
                var perception = new ScreenPerception(() => new DesktopPreferences { BlockedApps = blockedApps }, metrics =>
                    Console.WriteLine(JsonSerializer.Serialize(new { kind = "comet_observation_metrics", metrics })), new ScreenObservationOptions(strategy, depth));
                foreach (var window in windows.Distinct()) {
                    using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    try {
                        var identity = WindowSelection.Capture(window);
                        if (!identity.App.Equals("comet", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Process changed before observation.");
                        var snapshot = await perception.Capture(window, stop.Token, expected: identity);
                        identity.Validate();
                        if (snapshot.Window != window || !snapshot.Context.App.Equals("comet", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Capture no longer belongs to the selected Comet window.");
                        int count = snapshot.Context.Elements.Count;
                        if (count > 0) observed++;
                        Console.WriteLine(JsonSerializer.Serialize(new { kind = "comet_readonly", status = count > 0 ? "OBSERVED" : "EMPTY", process = "comet", identity.ProcessId, identity.ThreadId,
                            processLifetimeVerified = true, elevationKnown = true, elevated = false, elementCount = count, roles = snapshot.Context.Elements.GroupBy(e => e.Role).ToDictionary(g => g.Key, g => g.Count()),
                            snapshot.Complete, redactionRegionCount = snapshot.PrivateRects.Count, windowCount = windows.Count, screenshots = false, focusChanged = false, inputSent = false }));
                    } catch (Exception ex) {
                        errors++;
                        string safeCode = ex is WindowSelectionException selectionError ? selectionError.Code : ex.Message switch {
                            "Buddy blocks this window because it may contain private credentials or financial data." => "privacy-window-block",
                            "Buddy cannot access a secure desktop." => "secure-desktop",
                            "This application is in your privacy blocklist." => "privacy-app-block",
                            _ => ex.GetType().Name
                        };
                        Console.WriteLine(JsonSerializer.Serialize(new { kind = "comet_readonly", status = "BLOCKED", error = safeCode,
                            nativeError = ex is WindowSelectionException native ? native.NativeError : 0, screenshot = false, focusChanged = false, inputSent = false }));
                    }
                }
                exit = observed > 0 ? 0 : 2;
                Console.WriteLine(JsonSerializer.Serialize(new { kind = "comet_readonly_summary", visibleCandidates = windows.Count, observed, errors, outcome = observed > 0 ? "Current Comet UIA observed; browser task acceptance not established" : "No successful current Comet UIA observation", pageTextLogged = false, installedProfileRead = false }));
            } catch (Exception ex) { exit = 1; Console.WriteLine(JsonSerializer.Serialize(new { kind = "comet_readonly_error", error = ex.GetType().Name })); }
            finally { app.Shutdown(); }
        }));
        app.Run(); return exit;
    }

    // This branch creates only owned windows; inference uses an in-memory handler.
    internal static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; BuddyTheme.Apply("Night Mint", true);
        var field = new TextBox { Text = Original, AcceptsReturn = true, Margin = new(20) };
        var owner = new Window { Title = "Buddy owned user-failure fixture", Width = 640, Height = 360, Content = field };
        string folder = Path.Combine(Path.GetTempPath(), "Buddy-userfailure-ui-" + Guid.NewGuid()); int count = 0, exit = 1;
        string evidence = Path.Combine(Environment.GetEnvironmentVariable("BUDDY_USER_FAILURE_EVIDENCE") ?? Path.GetTempPath(), "Buddy-userfailure-owned-" + Guid.NewGuid()); Directory.CreateDirectory(evidence);
        void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); count++; Console.WriteLine("PASS: " + message); }
        owner.Loaded += async (_, _) => {
            InlinePromptWindow? review = null; MainWindow? home = null;
            try {
                var handle = new WindowInteropHelper(owner).Handle; owner.Activate(); InputNative.SetForegroundWindow(handle); field.Focus(); await Render(owner);
                Check(Native.GetForegroundWindow() == handle && field.IsKeyboardFocused, "Owned source fixture is the focused field before review");
                using var model = new Model(); using var http = new HttpClient(model) { BaseAddress = new("http://127.0.0.1:11434/") };
                var store = new StateStore(folder, new EphemeralDataProtectionProvider()); var service = new BuddyService(store, new(http)) { WebEnabled = false, AgentEnabled = false };
                var adapter = new OwnedField(field); var edit = new GuardedEdit(adapter, Original);
                var at = field.PointToScreen(new Point(0, 0)); var dpi = VisualTreeHelper.GetDpi(field);
                var draft = new FocusedDraft(edit, "owned fixture", "Owned prompt", new(handle, adapter.Identity, new(at.X, at.Y, field.ActualWidth * dpi.DpiScaleX, field.ActualHeight * dpi.DpiScaleY)), VerifyFocus: () => {
                    if (Native.GetForegroundWindow() != handle || !field.Dispatcher.Invoke(() => field.IsKeyboardFocused)) throw new InvalidOperationException("Owned source lost focus.");
                });
                review = new InlinePromptWindow(service, new FocusedFieldEditor(new ScreenPerception(() => new())), draft, () => { }); review.Show(); await Render(review);
                Propose(review, Original); await Call(review, "Apply");
                var accept = (Button)Field(review, "accept")!;
                Check(adapter.Writes == 0 && field.Text == Original && (accept.Visibility != Visibility.Visible || !accept.IsEnabled), "An echoed proposal is neither visibly applicable nor writable through forced Apply");
                Propose(review, "  " + Original.Replace(" ", "  ") + "  "); await Call(review, "Apply");
                Check(adapter.Writes == 0 && Field(review, "proposal") is null, "Whitespace-only inline echo cannot become an applicable proposal");
                Propose(review, Original.ToUpperInvariant() + "."); await Call(review, "Apply");
                Check(adapter.Writes == 0 && Field(review, "proposal") is null, "Cosmetic capitalization and punctuation cannot enable inline Accept or forced Apply");
                await review.Refine();
                Check(adapter.Writes == 0 && Field(review, "proposal") is null && (accept.Visibility != Visibility.Visible || !accept.IsEnabled), "A malformed source-structure plan leaves the inline source unchanged with Accept unavailable");
                Check(((TextBlock)Field(review, "status")!).Text.Length > 0 && model.Chats == 1, "Inline refusal feedback remains visible without an unbounded wording retry");
                await Render(review); SaveOwned(review, Path.Combine(evidence, "inline-no-refinement.png"));
                review.Close();

                var work = OverlayNative.WorkArea(new OverlayNative.Point { X = (int)at.X, Y = (int)at.Y });
                var shortArea = new PixelBounds(work.Left, work.Top, Math.Min(420, work.Width), Math.Min(240, work.Height));
                review = new InlinePromptWindow(service, new FocusedFieldEditor(new ScreenPerception(() => new())), draft, () => { }, workArea: _ => shortArea); review.Show();
                Propose(review, Faithful); await Render(review);
                Check(Native.GetForegroundWindow() == handle && field.IsKeyboardFocused && !review.ShowActivated, "A changed inline review preserves the original field focus without activation");
                Check(double.IsFinite(review.ActualHeight) && review.ActualHeight > 0 && review.ActualHeight * dpi.DpiScaleY <= shortArea.Height,
                    "Inline review fits a synthetic 240-pixel work area without changing monitor settings");
                accept = (Button)Field(review, "accept")!; accept.BringIntoView(); await Render(review);
                var bounds = accept.TransformToAncestor(review).TransformBounds(new Rect(0, 0, accept.ActualWidth, accept.ActualHeight));
                Check(accept.IsVisible && bounds.Top >= 0 && bounds.Bottom <= review.ActualHeight && bounds.Right <= review.ActualWidth,
                    "The actual Accept control is reachable within the bounded review window");
                SaveOwned(review, Path.Combine(evidence, "inline-short-area-footer.png"));
                await Call(review, "Apply");
                Check(adapter.Writes == 1 && field.Text == Faithful, "Accept writes the changed reviewed text only to the original owned field");
                await Call(review, "Undo");
                Check(adapter.Writes == 2 && field.Text == Original, "Undo restores the exact original owned prompt without submitting");
                review.Close();

                var stale = WindowSelection.Capture(handle); owner.Close();
                bool refused = false; try { stale.Validate(); } catch (WindowSelectionException e) { refused = e.Code == "window-gone"; }
                Check(refused, "A destroyed actual owned HWND is rejected by production lifetime validation");
                int runs = 0; CancellationToken capturedStop = default;
                var release = new TaskCompletionSource<ComputerUseResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                home = new MainWindow(false, new DesktopPreferences { AgentEnabled = true, Appearance = "Night Mint", ReduceMotion = true }, _ => { }, (query, ct) => { runs++; capturedStop = ct; return release.Task; }); home.Show(); await Render(home);
                foreach (string query in new[] { "Open Notepad and type a note", "Open cmd", "Open https://example.com", "Open Comet --debug" }) Invoke(home, "StartWorkflow", "agent", query);
                Invoke(home, "StartWorkflow", "guide", "Open Notepad");
                Invoke(home, "StartWorkflow", "knowledge", "Open Notepad");
                Check(runs == 0, "Actual routing keeps mixed, unsupported, Guide and knowledge tasks out of the direct app-open path");
                Invoke(home, "StartWorkflow", "agent", "Open Notepad");
                await Wait(() => runs == 1); Invoke(home, "StartWorkflow", "agent", "Open Notepad");
                Check(runs == 1 && Field(home, "routineRequest") is not null, "Exact Agent request reaches the injected routine runner once and duplicate overlap is suppressed");
                Invoke(home, "Cancel");
                Check(capturedStop.IsCancellationRequested, "Root Stop owns and cancels the active routine request token");
                Check(((TextBlock)Field(home, "status")!).Text.Contains("Stopped", StringComparison.OrdinalIgnoreCase), "Root Stop immediately replaces the opening status with stopped feedback");
                release.TrySetResult(new(true, true, "LATE_FAKE_SUCCESS", Guid.NewGuid(), Guid.NewGuid(), null, 1, 1));
                await Wait(() => Field(home, "routineRequest") is null);
                Check(!((TextBlock)Field(home, "status")!).Text.Contains("LATE_FAKE_SUCCESS"), "A late canceled routine success cannot replace stopped UI state");
                typeof(MainWindow).GetField("shuttingDown", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(home, true); home.Close(); home = null;
                home = new MainWindow(false, new DesktopPreferences { AgentEnabled = false, ReduceMotion = true }, _ => { }, (_, _) => { runs++; throw new Exception("Disabled Agent dispatched"); });
                Invoke(home, "StartWorkflow", "agent", "Open Notepad");
                Check(runs == 1, "An exact app-open request cannot bypass disabled Agent through the production route");
                Check(await store.Read(s => s.Conversations.Count + s.Jobs.Count + s.Guides.Count + s.Audit.Count) == 0, "Owned echo and Apply/Undo checks save no conversations or action completions");
                Console.WriteLine($"ALL {count} USER-FAILURE OWNED NATIVE CHECKS PASSED; in-memory model and owned editable field only, not browser acceptance."); exit = 0;
            } catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally {
                review?.Close(); owner.Close();
                if (home is not null) { typeof(MainWindow).GetField("shuttingDown", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(home, true); home.Close(); }
                Console.WriteLine("OWNED USER-FAILURE PNG EVIDENCE: " + evidence);
                string full = Path.GetFullPath(folder), temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("Buddy-userfailure-ui-", StringComparison.Ordinal) && Directory.Exists(full)) Directory.Delete(full, true);
                app.Shutdown();
            }
        };
        app.Run(owner); return exit;
    }
    private sealed class OwnedField(TextBox field) : IVerifiedTextField
    {
        internal int Writes;
        public string Identity { get; } = Guid.NewGuid().ToString();
        public string Read() => field.Dispatcher.Invoke(() => field.Text);
        public void Write(string expected, string value, CancellationToken ct) => field.Dispatcher.Invoke(() => { ct.ThrowIfCancellationRequested(); if (field.Text != expected) throw new InvalidOperationException("Owned source changed."); Writes++; field.Text = value; });
    }
    private sealed class Model : HttpMessageHandler
    {
        internal int Chats;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var data = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement;
            if (request.RequestUri!.AbsolutePath != "/api/chat" || data.GetProperty("stream").GetBoolean() || !data.GetProperty("format").GetProperty("properties").TryGetProperty("sections", out _))
                throw new InvalidOperationException("Malformed structure fixture must not self-assess, embed or enter wording fallback.");
            Chats++; if (Chats != 1) throw new InvalidOperationException("Unexpected extra structure call.");
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { message = new { content = "{\"sections\":[]}" }, done = true }), Encoding.UTF8, "application/json") };
        }
    }
}
