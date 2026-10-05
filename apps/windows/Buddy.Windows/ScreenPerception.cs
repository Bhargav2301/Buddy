using Buddy.Server;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Automation;

namespace Buddy.Windows;

internal sealed record ScreenSnapshot(IntPtr Window, ScreenContext Context, Dictionary<string, AutomationElement> Nodes, List<Rect> PrivateRects, bool Complete, Dictionary<string,DocumentWordTarget>? WordTargets = null, WindowSelection? Selection = null, ScreenObservationMetrics? Observation = null)
{
    internal string PromptText => Security.Redact(System.Text.Json.JsonSerializer.Serialize(Context, StateStore.Json))[..Math.Min(14000, Security.Redact(System.Text.Json.JsonSerializer.Serialize(Context, StateStore.Json)).Length)];
}

internal sealed class ScreenPerception(Func<DesktopPreferences> preferences, Action<ScreenObservationMetrics>? observation = null, ScreenObservationOptions? options = null)
{
    private readonly ScreenObservationOptions settings = (options ?? new()).Validate();
    private readonly SemaphoreSlim gate = new(1, 1);
    internal static IntPtr PracticeHandle;
    internal async Task<ScreenSnapshot> Capture(IntPtr window, CancellationToken ct, string? requestedText = null, WindowSelection? expected = null)
    {
        var metrics = new ScreenObservationSession(settings);
        void Report(ScreenObservationMetrics value) { try { observation?.Invoke(value); } catch { /* Diagnostics must not change capture success or safety. */ } }
        // One outstanding UIA worker: a hung provider cannot create unlimited workers.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(2));
        var token = deadline.Token;
        try {
            await gate.WaitAsync(token);
            var worker = Task.Run(() => { try { return Read(window, token, requestedText, expected, metrics); } finally { gate.Release(); } }, CancellationToken.None);
            var snapshot = await worker.WaitAsync(token);
            var result = metrics.Snapshot(snapshot.Complete ? "complete" : "partial", snapshot.Complete); Report(result);
            return snapshot with { Observation = result };
        } catch (OperationCanceledException ex) when (!ct.IsCancellationRequested) {
            Report(metrics.Snapshot("timeout", false, ex)); throw new TimeoutException("The app's accessibility provider did not respond. Capture was stopped.");
        } catch (OperationCanceledException ex) { Report(metrics.Snapshot("cancelled", false, ex)); throw; }
        catch (Exception ex) { Report(metrics.Snapshot("failed", false, ex)); throw; }
    }
    internal void Check(IntPtr window, bool agent = false)
    {
        Native.CheckWindow(window); InputNative.CheckDesktopAndElevation(window);
        if (Native.IsOwnWindow(window) && window != PracticeHandle) throw new InvalidOperationException("Focus the app you want Buddy to help with first.");
        var process = InputNative.ProcessName(window);
        if (preferences().BlockedApps.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(x => process.Contains(x, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("This application is in your privacy blocklist.");
        if (agent && new[] { "cmd", "powershell", "pwsh", "windowsterminal", "conhost", "mintty", "wsl", "regedit", "taskmgr" }.Contains(process.ToLowerInvariant()))
            throw new InvalidOperationException("Computer control is unavailable in terminals and system administration tools.");
    }
    private ScreenSnapshot Read(IntPtr window, CancellationToken ct, string? requestedText, WindowSelection? expected, ScreenObservationSession metrics)
    {
        metrics.Stage("access");
        var selected = expected ?? WindowSelection.Capture(window);
        metrics.Selected(selected);
        if (selected.Window != window) throw new WindowSelectionException("window-replaced", "The selected window changed. " + WindowSelection.Refocus, window);
        selected.Validate(); Check(window); metrics.Stage("root");
        var properties = new ScreenObservationProperties(settings.Mode, metrics); var root = properties.Root(window);
        var words = new Dictionary<string,DocumentWordTarget>(); var nodes = new Dictionary<string, AutomationElement>(); var elements = new List<ScreenElement>(); var redactions = new List<Rect>();
        var queue = new Queue<(AutomationElement Node, int Depth)>(); queue.Enqueue((root, 0)); metrics.Enqueue(queue.Count);
        var watch = Stopwatch.StartNew(); metrics.BeginTraversal(); int count = 0; bool complete = true;
        while (queue.Count > 0 && count < 400 && watch.ElapsedMilliseconds < 600) {
            ct.ThrowIfCancellationRequested(); var (node, depth) = queue.Dequeue(); count++; metrics.Visit(depth, queue.Count);
            try {
                var data = properties.Read(node); if (data.Offscreen) { metrics.Offscreen(); continue; }
                if (data.Password) { metrics.Password(); if (!data.Bounds.IsEmpty) redactions.Add(data.Bounds); metrics.Counts(elements.Count, redactions.Count); continue; }
                var rect = data.Bounds;
                if (ScreenObservationProperties.ValidBounds(rect)) {
                    var id = "e" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(window + ":" + string.Join('.', data.RuntimeId!))))[..12];
                    var name = Security.Redact(data.Name ?? "");
                    if (name != (data.UsesLiveComparison ? data.ComparisonName : data.Name)) redactions.Add(rect);
                    name = name[..Math.Min(120, name.Length)];
                    elements.Add(new(id, name, data.Type!.ProgrammaticName.Replace("ControlType.", ""), rect.X, rect.Y, rect.Width, rect.Height, data.Enabled)); nodes[id] = node;
                    if(requestedText is not null) { metrics.Stage("requested-text"); DocumentWordTarget.Collect(node,elements[^1],requestedText,elements,nodes,words,ct); }
                    metrics.Counts(elements.Count, redactions.Count);
                }
                var child = properties.FirstChild(node);
                if (depth >= settings.DepthLimit) { if(child is not null) { complete = false; metrics.Omit("depth-limit"); } continue; }
                while (child is not null && queue.Count + count < 400 && watch.ElapsedMilliseconds < 600) { queue.Enqueue((child, depth + 1)); metrics.Enqueue(queue.Count); child = properties.NextSibling(child); }
                if (child is not null) { complete = false; if (queue.Count + count >= 400) metrics.Omit("node-limit"); if (watch.ElapsedMilliseconds >= 600) metrics.Omit("traversal-time-limit"); }
            } catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException) { complete = false; metrics.ProviderFailure(ex); }
        }
        if (queue.Count > 0) { if (count >= 400) metrics.Omit("node-limit"); if (watch.ElapsedMilliseconds >= 600) metrics.Omit("traversal-time-limit"); }
        metrics.EndTraversal(); metrics.Counts(elements.Count, redactions.Count); metrics.Stage("final-validation");
        ct.ThrowIfCancellationRequested(); selected.Validate(); Check(window);
        string title = Security.Redact(Native.Label(window)); selected.Validate();
        return new(window, new(selected.App, title, elements), nodes, redactions, complete && queue.Count == 0,words,selected);
    }
    internal async Task<CapturedWindow?> Frame(ScreenSnapshot snapshot, CancellationToken ct)
    {
        // If accessibility could not enumerate all controls, do not risk capturing unknown password fields.
        if (!snapshot.Complete || Native.GetForegroundWindow() != snapshot.Window) return null;
        snapshot.Selection?.Validate();
        Check(snapshot.Window);
        var frame = await WindowCapture.Capture(snapshot.Window, snapshot.PrivateRects, ct);
        try {
            var ocr = await LocalOcr.Read(frame, ct);
            var masked = frame.Mask(ocr); frame.Dispose(); frame = masked;
            var current = await Capture(snapshot.Window, ct, expected: snapshot.Selection);
            if (!current.Complete || Native.GetForegroundWindow() != snapshot.Window || !snapshot.PrivateRects.SequenceEqual(current.PrivateRects) || current.Context.Title != snapshot.Context.Title || WindowCapture.Bounds(snapshot.Window) != frame.Bounds) {
                frame.Dispose(); return null;
            }
            return frame;
        } catch { frame.Dispose(); throw; }
    }
    internal async Task<byte[]?> Image(ScreenSnapshot snapshot, CancellationToken ct)
    {
        using var frame = await Frame(snapshot, ct);
        return frame?.ForVision();
    }
}
