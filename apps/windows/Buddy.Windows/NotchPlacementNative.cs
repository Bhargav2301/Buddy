using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Buddy.Windows;

internal static class NotchPlacementNative
{
    [StructLayout(LayoutKind.Sequential)] private struct Rect { internal int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    internal static IReadOnlyList<NotchPlacementMonitor> Monitors()
        => System.Windows.Forms.Screen.AllScreens.Select(screen => new NotchPlacementMonitor(screen.DeviceName,
            new(screen.WorkingArea.Left, screen.WorkingArea.Top, screen.WorkingArea.Width, screen.WorkingArea.Height))).ToArray();
    internal static PixelBounds Bounds(Window window)
    {
        if (!GetWindowRect(new WindowInteropHelper(window).Handle, out var bounds)) throw new InvalidOperationException("The Buddy bar position is unavailable.");
        return new(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top);
    }
}
