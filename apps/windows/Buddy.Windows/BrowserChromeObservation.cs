using Buddy.Server;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace Buddy.Windows;

// Proves browser-toolbar ancestry before assigning authored browser functions to
// labels. A web page's own "Reload" button is not browser chrome.
internal static class BrowserChromeObservation
{
    private static readonly SemaphoreSlim gate = new(1, 1);
    private static readonly HashSet<string> buttons = new(StringComparer.OrdinalIgnoreCase) { "Reload", "Reload this page", "Refresh", "New tab", "Open a new tab" };
    private static readonly HashSet<string> editors = new(StringComparer.OrdinalIgnoreCase) { "Address and search bar", "Search or enter address", "Search with Google or enter address" };
    internal static async Task<ScreenContext> Qualify(ScreenSnapshot snapshot, CancellationToken ct)
    {
        var selected = snapshot.Selection ?? throw new InvalidOperationException("A pinned browser observation is required.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(2)); var token = deadline.Token;
        await gate.WaitAsync(token);
        var worker = Task.Run(() => {
            try {
                selected.Validate(); InputNative.CheckDesktopAndElevation(snapshot.Window);
                var rootId = AutomationElement.FromHandle(snapshot.Window).GetRuntimeId();
                var kept = new List<ScreenElement>();
                foreach (var element in snapshot.Context.Elements.Where(e => e.Enabled &&
                    (e.Role == "Button" && buttons.Contains(e.Name) || e.Role == "Edit" && editors.Contains(e.Name))).Take(24)) {
                    token.ThrowIfCancellationRequested();
                    if (!snapshot.Nodes.TryGetValue(element.Ref, out var node)) continue;
                    try {
                        var data = node.Current;
                        if (data.IsPassword || data.IsOffscreen || !data.IsEnabled || data.Name != element.Name ||
                            data.ControlType.ProgrammaticName != "ControlType." + element.Role ||
                            data.BoundingRectangle != new System.Windows.Rect(element.X, element.Y, element.Width, element.Height)) continue;
                        bool toolbar = false;
                        for (int depth = 0; node is not null && depth < 20; depth++) {
                            token.ThrowIfCancellationRequested(); var ancestor = node.Current;
                            if (ancestor.IsPassword || ancestor.ControlType == ControlType.Document) break;
                            if (ancestor.ControlType == ControlType.ToolBar || ancestor.ControlType == ControlType.Tab) toolbar = true;
                            if (node.GetRuntimeId().SequenceEqual(rootId)) {
                                if (toolbar && ancestor.ControlType == ControlType.Window && ancestor.NativeWindowHandle == snapshot.Window.ToInt64() && ancestor.ProcessId == selected.ProcessId) kept.Add(element);
                                break;
                            }
                            node = TreeWalker.ControlViewWalker.GetParent(node);
                        }
                    } catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException) { }
                }
                selected.Validate(); token.ThrowIfCancellationRequested();
                return new ScreenContext(snapshot.Context.App, "", kept.Where(e => kept.Count(x => x.Name.Equals(e.Name, StringComparison.OrdinalIgnoreCase) && x.Role == e.Role) == 1).ToList());
            } finally { gate.Release(); }
        }, CancellationToken.None);
        return await worker.WaitAsync(token);
    }
}
