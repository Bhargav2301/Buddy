using Buddy.Server;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Automation;

namespace Buddy.Windows;

internal sealed record ScreenSnapshot(IntPtr Window, ScreenContext Context, Dictionary<string, AutomationElement> Nodes, List<Rect> PrivateRects, bool Complete, Dictionary<string,DocumentWordTarget>? WordTargets = null)
{
    internal string PromptText => Security.Redact(System.Text.Json.JsonSerializer.Serialize(Context, StateStore.Json))[..Math.Min(14000, Security.Redact(System.Text.Json.JsonSerializer.Serialize(Context, StateStore.Json)).Length)];
}

internal sealed class ScreenPerception(Func<DesktopPreferences> preferences)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    internal static IntPtr PracticeHandle;
    internal async Task<ScreenSnapshot> Capture(IntPtr window, CancellationToken ct, string? requestedText = null)
    {
        // One outstanding UIA worker: a hung provider cannot create unlimited workers.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(2));
        var token = deadline.Token;
        try {
            await gate.WaitAsync(token);
            var worker = Task.Run(() => { try { return Read(window, token, requestedText); } finally { gate.Release(); } }, CancellationToken.None);
            return await worker.WaitAsync(token);
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("The app's accessibility provider did not respond. Capture was stopped."); }
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
    private ScreenSnapshot Read(IntPtr window, CancellationToken ct, string? requestedText)
    {
        Check(window); var root = AutomationElement.FromHandle(window);
        var words = new Dictionary<string,DocumentWordTarget>(); var nodes = new Dictionary<string, AutomationElement>(); var elements = new List<ScreenElement>(); var redactions = new List<Rect>();
        var queue = new Queue<(AutomationElement Node, int Depth)>(); queue.Enqueue((root, 0));
        var watch = Stopwatch.StartNew(); int count = 0; bool complete = true;
        while (queue.Count > 0 && count++ < 400 && watch.ElapsedMilliseconds < 600) {
            ct.ThrowIfCancellationRequested(); var (node, depth) = queue.Dequeue();
            try {
                var c = node.Current; if (c.IsOffscreen) continue;
                if (c.IsPassword) { if (!c.BoundingRectangle.IsEmpty) redactions.Add(c.BoundingRectangle); continue; }
                var rect = c.BoundingRectangle;
                if (!rect.IsEmpty && rect.Width > 0 && rect.Height > 0 && double.IsFinite(rect.X) && double.IsFinite(rect.Y)) {
                    var id = "e" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(window + ":" + string.Join('.', node.GetRuntimeId()))))[..12];
                    var name = Security.Redact(c.Name ?? "");
                    if (name != c.Name) redactions.Add(rect);
                    name = name[..Math.Min(120, name.Length)];
                    elements.Add(new(id, name, c.ControlType.ProgrammaticName.Replace("ControlType.", ""), rect.X, rect.Y, rect.Width, rect.Height, c.IsEnabled)); nodes[id] = node;
                    if(requestedText is not null)DocumentWordTarget.Collect(node,elements[^1],requestedText,elements,nodes,words,ct);
                }
                var child = TreeWalker.ControlViewWalker.GetFirstChild(node);
                if (depth >= 12) { if(child is not null)complete = false; continue; }
                while (child is not null && queue.Count + count < 400 && watch.ElapsedMilliseconds < 600) { queue.Enqueue((child, depth + 1)); child = TreeWalker.ControlViewWalker.GetNextSibling(child); }
                if (child is not null) complete = false;
            } catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException) { complete = false; }
        }
        return new(window, new(InputNative.ProcessName(window), Security.Redact(Native.Label(window)), elements), nodes, redactions, complete && queue.Count == 0,words);
    }
    internal async Task<CapturedWindow?> Frame(ScreenSnapshot snapshot, CancellationToken ct)
    {
        // If accessibility could not enumerate all controls, do not risk capturing unknown password fields.
        if (!snapshot.Complete || Native.GetForegroundWindow() != snapshot.Window) return null;
        Check(snapshot.Window);
        var frame = await WindowCapture.Capture(snapshot.Window, snapshot.PrivateRects, ct);
        try {
            var ocr = await LocalOcr.Read(frame, ct);
            var masked = frame.Mask(ocr); frame.Dispose(); frame = masked;
            var current = await Capture(snapshot.Window, ct);
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
