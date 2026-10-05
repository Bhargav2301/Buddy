using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace Buddy.Windows;

internal static class Native
{
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr handle, int id);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr handle, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder text, int size);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr handle, System.Text.StringBuilder text, int count);
    [DllImport("user32.dll")] internal static extern bool SetWindowDisplayAffinity(IntPtr handle, uint affinity);
    internal static string Label(IntPtr window)
    { var b = new System.Text.StringBuilder(1024); GetWindowText(window, b, b.Capacity); return b.ToString(); }
    internal static bool IsOwnWindow(IntPtr window) { GetWindowThreadProcessId(window, out var pid); return pid == Environment.ProcessId; }
    internal static bool IsSelectableWindow(IntPtr window)
    {
        if (window == IntPtr.Zero || !IsWindowVisible(window) || GetAncestor(window, 2) != window) return false;
        long style = GetWindowLongPtr(window, -20).ToInt64();
        if ((style & (0x80L | 0x08000000L)) != 0) return false;
        var name = new System.Text.StringBuilder(128); GetClassName(window, name, name.Capacity);
        return name.ToString() is not ("#32768" or "tooltips_class32" or "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
    }
    internal static void CheckWindow(IntPtr window)
    {
        var selected = WindowSelection.Capture(window);
        var name = selected.App.ToLowerInvariant();
        var title = Label(window).ToLowerInvariant();
        string[] blocked = ["keepass", "1password", "bitwarden", "credential", "lastpass", "authenticator", "logonui", "consent"];
        string[] sensitive = ["bank", "password", "paytm", "phonepe", "payment", "incognito", "inprivate", "sign in", "log in"];
        if (blocked.Any(name.Contains) || sensitive.Any(title.Contains)) throw new InvalidOperationException("Buddy blocks this window because it may contain private credentials or financial data.");
        selected.Validate();
    }
    internal static string ReadWindow(IntPtr window)
    {
        CheckWindow(window);
        var root = AutomationElement.FromHandle(window);
        var queue = new Queue<(AutomationElement Node, int Depth)>(); queue.Enqueue((root, 0));
        var result = new System.Text.StringBuilder("Window: " + Label(window) + "\n");
        var watch = Stopwatch.StartNew(); int visited = 0;
        while (queue.Count > 0 && visited++ < 500 && watch.ElapsedMilliseconds < 300 && result.Length < 18000)
        {
            var (node, depth) = queue.Dequeue();
            try
            {
                var c = node.Current;
                if (c.IsPassword || c.IsOffscreen) continue;
                var name = c.Name;
                if (!string.IsNullOrWhiteSpace(name)) result.AppendLine(c.ControlType.ProgrammaticName.Replace("ControlType.", "") + ": " + name);
                if (depth >= 15) continue;
                var child = TreeWalker.ControlViewWalker.GetFirstChild(node);
                while (child is not null && queue.Count < 500 && watch.ElapsedMilliseconds < 300) { queue.Enqueue((child, depth + 1)); child = TreeWalker.ControlViewWalker.GetNextSibling(child); }
            }
            catch (ElementNotAvailableException) { }
        }
        return Buddy.Server.Security.Redact(result.ToString());
    }
}
