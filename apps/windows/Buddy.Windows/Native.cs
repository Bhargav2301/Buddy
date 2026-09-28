using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace Buddy.Windows;

internal static class Native
{
    [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr handle, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr handle, int id);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr handle, System.Text.StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool SetWindowDisplayAffinity(IntPtr handle, uint affinity);
    private struct Rect { public int Left, Top, Right, Bottom; }
    internal static string Label(IntPtr window)
    { var b = new System.Text.StringBuilder(1024); GetWindowText(window, b, b.Capacity); return b.ToString(); }
    internal static bool IsOwnWindow(IntPtr window) { GetWindowThreadProcessId(window, out var pid); return pid == Environment.ProcessId; }
    internal static void CheckWindow(IntPtr window)
    {
        if (window == IntPtr.Zero) throw new InvalidOperationException("Focus the app you want help with, then press Ctrl+Space.");
        GetWindowThreadProcessId(window, out uint pid);
        var name = Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant();
        var title = Label(window).ToLowerInvariant();
        string[] blocked = ["keepass", "1password", "bitwarden", "credential", "lastpass", "authenticator", "logonui", "consent"];
        string[] sensitive = ["bank", "password", "paytm", "phonepe", "payment", "incognito", "inprivate", "sign in", "log in"];
        if (blocked.Any(name.Contains) || sensitive.Any(title.Contains)) throw new InvalidOperationException("Buddy blocks this window because it may contain private credentials or financial data.");
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
    internal static byte[] Capture(IntPtr window, IReadOnlyList<System.Windows.Rect>? privateRects = null)
    {
        CheckWindow(window);
        if (!GetWindowRect(window, out var rect)) throw new InvalidOperationException("That window is no longer available.");
        var bounds = System.Drawing.Rectangle.Intersect(new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top), System.Windows.Forms.SystemInformation.VirtualScreen);
        if (bounds.Width < 1 || bounds.Height < 1) throw new InvalidOperationException("Restore the target window before capturing it.");
        using var bitmap = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap)) {
            graphics.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);
            if (privateRects is not null) foreach (var r in privateRects) graphics.FillRectangle(System.Drawing.Brushes.Black, (float)r.X - bounds.X, (float)r.Y - bounds.Y, (float)r.Width, (float)r.Height);
        }
        var scale = Math.Min(1, 1280.0 / Math.Max(bounds.Width, bounds.Height));
        using var resized = new System.Drawing.Bitmap(bitmap, (int)(bounds.Width * scale), (int)(bounds.Height * scale));
        using var buffer = new MemoryStream(); resized.Save(buffer, System.Drawing.Imaging.ImageFormat.Jpeg); return buffer.ToArray();
    }
}
