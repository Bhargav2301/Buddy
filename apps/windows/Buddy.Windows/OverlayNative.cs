using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Buddy.Windows;

internal static class OverlayNative
{
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    internal static PixelBounds WorkArea(Point point)
    {
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (GetMonitorInfoW(MonitorFromPoint(point, 2), ref info))
            return new(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
        var fallback = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(point.X, point.Y)).WorkingArea;
        return new(fallback.Left, fallback.Top, fallback.Width, fallback.Height);
    }
    internal static double Scale(IntPtr window) => Math.Max(96, GetDpiForWindow(window)) / 96.0;
    internal static void Configure(IntPtr window, bool clickThrough)
    {
        long style = GetWindowLongPtr(window, -20).ToInt64() | 0x80; // tool window
        if (clickThrough) style |= 0x08000000 | 0x20 | 0x80000; // no-activate, transparent, layered
        SetWindowLongPtr(window, -20, new IntPtr(style));
        Native.SetWindowDisplayAffinity(window, 0x11);
    }
    internal static void Place(Window window, Point point)
        => Move(window, Position(window, point));
    internal static PixelPosition Position(Window window, Point point)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        double scale = Scale(handle);
        double width = window.Width * scale, height = window.Height * scale;
        if (GetWindowRect(handle, out var rect) && rect.Right > rect.Left && rect.Bottom > rect.Top)
        { width = rect.Right - rect.Left; height = rect.Bottom - rect.Top; }
        return OverlayPlacement.NearPointer(point.X, point.Y, width, height, WorkArea(point), scale);
    }
    internal static void Move(Window window, PixelPosition position)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        SetWindowPos(handle, new IntPtr(-1), (int)Math.Round(position.X), (int)Math.Round(position.Y), 0, 0, 0x0010 | 0x0001);
    }
    internal static bool IsFullscreenForeground()
    {
        var window = Native.GetForegroundWindow();
        if (window == IntPtr.Zero || Native.IsOwnWindow(window) || !GetWindowRect(window, out var rect)) return false;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfoW(MonitorFromPoint(new Point { X = (rect.Left + rect.Right) / 2, Y = (rect.Top + rect.Bottom) / 2 }, 2), ref info)) return false;
        return rect.Left <= info.Monitor.Left && rect.Top <= info.Monitor.Top && rect.Right >= info.Monitor.Right && rect.Bottom >= info.Monitor.Bottom;
    }
    internal static void SetBounds(Window window, int x, int y, int width, int height)
    {
        var handle = new WindowInteropHelper(window).EnsureHandle();
        SetWindowPos(handle, new IntPtr(-1), x, y, width, height, 0x0010);
    }
}
