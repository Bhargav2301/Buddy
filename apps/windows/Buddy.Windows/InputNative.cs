using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Buddy.Server;

namespace Buddy.Windows;

internal static class InputNative
{
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern IntPtr OpenInputDesktop(uint flags, bool inherit, uint access);
    [DllImport("user32.dll")] private static extern bool CloseDesktop(IntPtr desktop);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder value, uint length, out uint needed);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll")] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll")] private static extern bool GetTokenInformation(IntPtr token, int kind, out int value, int size, out int returned);
    [StructLayout(LayoutKind.Explicit, Size = 40)] private struct Input { [FieldOffset(0)] public uint Type; [FieldOffset(8)] public ushort Key; [FieldOffset(10)] public ushort Scan; [FieldOffset(12)] public uint Flags; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    internal static string ProcessName(IntPtr window) { GetWindowThreadProcessId(window, out var pid); using var p = Process.GetProcessById((int)pid); return p.ProcessName; }
    internal static void CheckDesktopAndElevation(IntPtr window)
    {
        var desktop = OpenInputDesktop(0, false, 1);
        if (desktop == IntPtr.Zero) throw new InvalidOperationException("Buddy cannot access a secure desktop.");
        try { var name = new StringBuilder(128); if (!GetUserObjectInformation(desktop, 2, name, 256, out _) || name.ToString() != "Default") throw new InvalidOperationException("Buddy cannot access a secure desktop."); }
        finally { CloseDesktop(desktop); }
        GetWindowThreadProcessId(window, out var pid); var process = OpenProcess(0x1000, false, pid);
        if (process == IntPtr.Zero) throw new InvalidOperationException("This app cannot be inspected.");
        try {
            if (!OpenProcessToken(process, 8, out var token)) throw new InvalidOperationException("This app's access level could not be checked.");
            try { if (!GetTokenInformation(token, 20, out var elevated, 4, out _) || elevated != 0) throw new InvalidOperationException("Buddy cannot control or capture elevated applications."); }
            finally { CloseHandle(token); }
        } finally { CloseHandle(process); }
    }
    internal static void Type(string text, CancellationToken ct)
    {
        foreach (var c in text) { ct.ThrowIfCancellationRequested(); Send([new() { Type = 1, Scan = c, Flags = 4 }, new() { Type = 1, Scan = c, Flags = 6 }]); }
    }
    internal static void Keys(string chord, CancellationToken ct)
    {
        if (!ActionPolicy.Keys.Contains(chord)) throw new InvalidOperationException("Unsupported key chord.");
        var map = new Dictionary<string, ushort> { ["Ctrl"] = 0x11, ["Shift"] = 0x10, ["Tab"] = 9, ["Enter"] = 13, ["Escape"] = 27, ["A"] = 65, ["C"] = 67, ["Z"] = 90, ["Up"] = 38, ["Down"] = 40, ["Left"] = 37, ["Right"] = 39 };
        var keys = chord.Split('+').Select(s => map[s]).ToArray(); ct.ThrowIfCancellationRequested();
        try { foreach (var key in keys) Send([new() { Type = 1, Key = key }]); }
        finally { foreach (var key in keys.Reverse()) Send([new() { Type = 1, Key = key, Flags = 2 }]); }
    }
    private static void Send(Input[] inputs) { if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length) throw new InvalidOperationException("Windows refused input. The run has stopped."); }
}
