using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Buddy.Server;

namespace Buddy.Windows;

internal static class InputNative
{
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder value, uint length, out uint needed);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(IntPtr process, out long created, out long exited, out long kernel, out long user);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder path, ref uint size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, int kind, out int value, int size, out int returned);
    [StructLayout(LayoutKind.Explicit, Size = 40)] private struct Input { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public ushort Key; [FieldOffset(10)] public ushort Scan; [FieldOffset(12)] public uint Flags; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    internal static string ProcessName(IntPtr window) => WindowSelection.Capture(window).App;
    internal static WindowProbe InspectWindow(IntPtr window)
    {
        if (window == IntPtr.Zero || !IsWindow(window)) return new(false, 0, 0, false, 1400, 0, "", false, false);
        uint thread = GetWindowThreadProcessId(window, out uint pid);
        if (thread == 0 || pid == 0) return new(false, thread, pid, false, Marshal.GetLastWin32Error(), 0, "", false, false);
        var process = OpenProcess(0x1000, false, pid);
        if (process == IntPtr.Zero) return new(true, thread, pid, false, Marshal.GetLastWin32Error(), 0, "", false, false);
        try {
            if (!GetProcessTimes(process, out long created, out _, out _, out _))
                return new(true, thread, pid, true, Marshal.GetLastWin32Error(), 0, "", false, false);
            var path = new StringBuilder(32768); uint length = (uint)path.Capacity;
            if (!QueryFullProcessImageName(process, 0, path, ref length))
                return new(true, thread, pid, true, Marshal.GetLastWin32Error(), created, "", false, false);
            string app = System.IO.Path.GetFileNameWithoutExtension(path.ToString());
            if (!OpenProcessToken(process, 8, out var token))
                return new(true, thread, pid, true, Marshal.GetLastWin32Error(), created, app, false, false);
            try {
                if (!GetTokenInformation(token, 20, out int elevated, 4, out _))
                    return new(true, thread, pid, true, Marshal.GetLastWin32Error(), created, app, false, false);
                uint currentThread = GetWindowThreadProcessId(window, out uint currentPid);
                bool same = IsWindow(window) && currentThread == thread && currentPid == pid;
                return new(same, thread, pid, true, same ? 0 : 1400, created, app, true, elevated != 0);
            } finally { CloseHandle(token); }
        } finally { CloseHandle(process); }
    }
    internal static void CheckDesktopAndElevation(IntPtr window)
    {
        var desktop = OpenInputDesktop(0, false, 1);
        if (desktop == IntPtr.Zero) throw new InvalidOperationException("Buddy cannot access a secure desktop.");
        try { var name = new StringBuilder(128); if (!GetUserObjectInformation(desktop, 2, name, 256, out _) || name.ToString() != "Default") throw new InvalidOperationException("Buddy cannot access a secure desktop."); }
        finally { CloseDesktop(desktop); }
        _ = WindowSelection.Capture(window);
    }
    internal static void Keys(string chord, CancellationToken ct)
    {
        if (!ActionPolicy.Keys.Contains(chord)) throw new InvalidOperationException("Unsupported key chord.");
        var map = new Dictionary<string, ushort> { ["Ctrl"] = 0x11, ["Shift"] = 0x10, ["Tab"] = 9, ["Enter"] = 13, ["Escape"] = 27, ["A"] = 65, ["C"] = 67, ["Z"] = 90, ["Up"] = 38, ["Down"] = 40, ["Left"] = 37, ["Right"] = 39 };
        var keys = chord.Split('+').Select(s => map[s]).ToArray(); ct.ThrowIfCancellationRequested();
        var pressed = new List<ushort>();
        try { foreach (var key in keys) { ct.ThrowIfCancellationRequested(); Send([new() { Type = 1, Key = key }]); pressed.Add(key); } }
        finally { foreach (var key in pressed.AsEnumerable().Reverse()) Send([new() { Type = 1, Key = key, Flags = 2 }]); }
    }
    private static void Send(Input[] inputs) { if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length) throw new InvalidOperationException("Windows refused input. The run has stopped."); }
}
