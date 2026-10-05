using Buddy.Server;
using Buddy.Windows;
using Microsoft.AspNetCore.DataProtection;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

internal static class WindowSelectionChecks
{
    private static object? Field(object owner, string name) => owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner);
    private static object? Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(owner, args);
    internal static void Policy(Action<bool, string> check)
    {
        var handle = new IntPtr(123); long now = 1000;
        var good = new WindowProbe(true, 10, 20, true, 0, 123456, "fixture", true, false);
        WindowProbe probe = good;
        void Refuses(Action action, string code, string note, int? error = null) {
            try { action(); check(false, note); }
            catch (WindowSelectionException ex) { check(ex.Code == code && (error is null || ex.NativeError == error), note); }
        }
        var selected = WindowSelection.Capture(handle, _ => probe);
        check(selected.ProcessId == 20 && selected.ThreadId == 10 && selected.ProcessStarted == 123456, "Selection retains HWND, thread, PID and process creation identity");
        Refuses(() => WindowSelection.Capture(IntPtr.Zero, _ => throw new Exception("Must not inspect zero")), "no-selection", "Zero HWND refuses before native inspection");
        foreach (var item in new[] {
            (good with { Exists = false }, "window-gone", "Destroyed HWND refuses"),
            (good with { ThreadId = 0 }, "window-gone", "Zero thread ID refuses"),
            (good with { ProcessId = 0 }, "window-gone", "Zero PID refuses"),
            (good with { ProcessOpened = false, NativeError = 5 }, "process-query", "Denied process query refuses"),
            (good with { ProcessStarted = 0, NativeError = 6 }, "process-identity", "Unknown creation identity refuses"),
            (good with { ElevationKnown = false, NativeError = 5 }, "elevation-unknown", "Unknown elevation refuses"),
            (good with { Elevated = true }, "elevated", "Elevated process refuses") }) {
            probe = item.Item1; Refuses(() => WindowSelection.Capture(handle, _ => probe), item.Item2, item.Item3, probe.NativeError);
        }
        foreach (var changed in new[] { good with { ProcessId = 21 }, good with { ThreadId = 11 }, good with { ProcessStarted = 123457 }, good with { App = "other" } }) {
            probe = changed; Refuses(() => selected.Validate(_ => probe), "window-replaced", "Reused HWND/PID/thread or changed app cannot inherit selection");
        }
        probe = good;
        bool appWindow = true, blocked = false;
        var tracker = new WindowSelectionTracker(_ => probe, () => now, window => window == new IntPtr(999), TimeSpan.FromSeconds(2),
            _ => appWindow, _ => { if (blocked) throw new InvalidOperationException("Private app blocked"); });
        tracker.Observe(handle); check(tracker.RequireCurrent() == selected, "Explicit observed foreground selects its verified identity");
        tracker.Observe(new IntPtr(999)); tracker.Observe(IntPtr.Zero); appWindow = false; tracker.Observe(new IntPtr(456)); appWindow = true;
        check(tracker.RequireCurrent() == selected, "Buddy, zero and transient windows cannot replace selected app");
        now += 2001; Refuses(() => tracker.RequireCurrent(), "selection-expired", "Cached selection expires without a new external observation");
        tracker.Observe(handle); probe = good with { ProcessId = 21 };
        Refuses(() => tracker.RequireCurrent(), "window-replaced", "Revalidation does not silently adopt a reused handle");
        tracker.Observe(handle); check(tracker.RequireCurrent().ProcessId == 21, "New explicit foreground observation may select the new identity");
        blocked = true; tracker.Observe(handle); Refuses(() => tracker.RequireCurrent(), "window-unavailable", "Unsafe app observation clears the old safe selection");
        blocked = false; probe = good; tracker.Observe(handle); probe = good with { ProcessOpened = false, NativeError = 5 }; tracker.Observe(handle);
        Refuses(() => tracker.RequireCurrent(), "process-query", "Failed foreground access preserves native error and clears selection", 5);
        tracker.Clear(); Refuses(() => tracker.RequireCurrent(), "no-selection", "Explicit clear leaves no fallback target");
    }
    internal static int Run()
    {
        int count = 0, exit = 1;
        void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL: " + message); count++; Console.WriteLine("PASS: " + message); }
        Policy(Check);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var fixture = new PracticeWindow(); fixture.Editor.Text = "Write a short poem.";
        string folder = Path.Combine(Path.GetTempPath(), "Buddy-window45-" + Guid.NewGuid().ToString("N"));
        fixture.Loaded += async (_, _) => {
            InlinePromptWindow? card = null; DesktopAssistant? assistant = null; Window? transient = null;
            try {
                BuddyTheme.Apply("Night Mint", true);
                var hwnd = new WindowInteropHelper(fixture).Handle; ScreenPerception.PracticeHandle = hwnd;
                fixture.Activate(); InputNative.SetForegroundWindow(hwnd); fixture.Editor.Focus(); await Task.Delay(120);
                var selected = WindowSelection.Capture(hwnd); selected.Validate();
                Check(selected.Window == hwnd && selected.ProcessId == Environment.ProcessId && selected.ProcessStarted > 0, "Real owned window has a live process identity");
                var perception = new ScreenPerception(() => new());
                var snapshot = await perception.Capture(hwnd, default, expected: selected);
                Check(snapshot.Selection == selected && snapshot.Context.Elements.Any(x => x.Name == "Example text" && x.Role == "Edit"), "UIA snapshot retains the verified owned window identity");
                try { await perception.Capture(hwnd, default, expected: selected with { ProcessStarted = selected.ProcessStarted + 1 }); Check(false, "Wrong identity refuses capture"); }
                catch (WindowSelectionException ex) { Check(ex.Code == "window-replaced", "Wrong identity refuses capture before returning UIA content"); }
                var foregroundEvents = new List<IntPtr>();
                using var monitor = new WindowSelectionMonitor(window => foregroundEvents.Add(window));
                Check(foregroundEvents.Contains(hwnd), "Foreground monitor samples the initial owned foreground without moving focus");
                transient = new Window { Width = 200, Height = 100, Title = "Owned window lifetime fixture" }; transient.Show(); transient.Activate();
                var transientHandle = new WindowInteropHelper(transient).Handle; InputNative.SetForegroundWindow(transientHandle);
                var eventWait = System.Diagnostics.Stopwatch.StartNew();
                while (!foregroundEvents.Contains(transientHandle) && eventWait.Elapsed < TimeSpan.FromSeconds(2)) await Task.Delay(20);
                Check(foregroundEvents.Contains(transientHandle) && monitor.LastError is null, "Native foreground events observe a rapid owned-window selection without waiting for the polling timer");
                monitor.Dispose();
                var destroyed = WindowSelection.Capture(new WindowInteropHelper(transient).Handle); transient.Close(); transient = null;
                try { destroyed.Validate(); Check(false, "Destroyed owned window refuses"); }
                catch (WindowSelectionException ex) { Check(ex.Code == "window-gone", "Actual destroyed owned HWND cannot be reused as a selection"); }

                using var model = new NoTransport(); using var http = new HttpClient(model) { BaseAddress = new("http://127.0.0.1:11434") };
                var store = new StateStore(folder, DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(folder, "keys"))));
                var service = new BuddyService(store, new(http));
                bool unavailable = true;
                assistant = new(() => service, () => new(), () => hwnd, _ => { }, selectedWindow: () => unavailable
                    ? throw new WindowSelectionException("process-query", "Windows could not inspect the selected app (error 5).", hwnd, selected.ProcessId, selected.ThreadId, 5)
                    : selected);
                await assistant.Open("guide", "Guide me through the selected app");
                Check(model.Calls == 0 && Field(assistant, "guide") is null && !((Button)Field(assistant, "run")!).IsEnabled,
                    "Unavailable tracked target cannot fall back to raw HWND or request a generic model guide");
                Check(assistant.CurrentStatus.Contains("error 5") && ((TextBlock)Field(assistant, "stepText")!).Text.Contains("Make plan"), "Unavailable Guide shows the error and an explicit refocus/retry action");
                unavailable = false; await assistant.Open("guide", "");
                Check(((TextBlock)Field(assistant, "selectedApp")!).Text == "Selected app: " + selected.App, "A fresh explicit selection visibly identifies its app without exposing a title");
                assistant.Dispose(); assistant = null;

                fixture.Activate(); InputNative.SetForegroundWindow(hwnd); fixture.Editor.Focus(); await Task.Delay(120);
                var editor = new FocusedFieldEditor(perception); var draft = await editor.Capture(hwnd, default, strictFocus: true, explicitInvocation: true);
                double scale = OverlayNative.Scale(hwnd); var realWork = OverlayNative.WorkArea(new() { X = (int)draft.Anchor.Bounds.Right, Y = (int)draft.Anchor.Bounds.Top });
                var shortWork = new PixelBounds(realWork.Left, realWork.Top, 480 * scale, 260 * scale);
                card = new(service, editor, draft, () => { }, workArea: _ => shortWork); card.Show(); card.UpdateLayout();
                Call(card, "ShowProposal", draft.Edit.Original); await (Task)Call(card, "Apply")!;
                Check(!((Button)Field(card, "accept")!).IsEnabled && Field(card, "proposal") is null && fixture.Editor.Text == draft.Edit.Original, "Exact no-op disables Accept and forced Apply leaves actual owned field unchanged");
                Call(card, "ShowProposal", "  Write   a short poem. \n"); await (Task)Call(card, "Apply")!;
                Check(((TextBlock)Field(card, "status")!).Text == RefinementChange.NoChangeMessage && fixture.Editor.Text == draft.Edit.Original, "Whitespace-only proposal is visibly a no-refinement result");
                foreach (string cosmetic in new[] { "Write a short poem!", "WRITE A SHORT POEM." }) {
                    Call(card, "ShowProposal", cosmetic); await (Task)Call(card, "Apply")!;
                    Check(!((Button)Field(card, "accept")!).IsEnabled && Field(card, "proposal") is null && fixture.Editor.Text == draft.Edit.Original,
                        "Punctuation-only or capitalization-only proposal cannot enable Accept or forced Apply");
                }
                Check(!card.ShowActivated && Native.GetForegroundWindow() == hwnd && await card.IsCurrent(default), "Owned inline review remains nonactivating and preserves source focus");
                Call(card, "ShowProposal", "Please write a short poem."); card.UpdateLayout();
                var footer = (ScrollViewer)Field(card, "footerScroll")!; var footerBounds = footer.TransformToAncestor(card).TransformBounds(new Rect(0, 0, footer.ActualWidth, footer.ActualHeight));
                Check(card.ActualHeight <= 244.5 && footerBounds.Bottom <= card.ActualHeight + .5 && footer.ViewportHeight > 0, "Short synthetic work area bounds actual native height and keeps the actions viewport reachable");
                await (Task)Call(card, "Apply")!; Check(fixture.Editor.Text == "Please write a short poem.", "Changed proposal applies through the existing guarded source adapter");
                await (Task)Call(card, "Undo")!; Check(fixture.Editor.Text == draft.Edit.Original, "Undo restores the exact owned original after bounded review");
                Console.WriteLine($"ALL {count} WINDOW-45 CHECKS PASSED; injected policy and owned WPF/UIA only, no third-party app acceptance"); exit = 0;
            } catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { card?.Close(); assistant?.Dispose(); transient?.Close(); fixture.Close(); app.Shutdown(); }
        };
        app.Run(fixture); return exit;
    }
    private sealed class NoTransport : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Calls++; throw new InvalidOperationException("Owned refusal fixture must not call a model."); }
    }
}
