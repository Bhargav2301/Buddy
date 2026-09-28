using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Buddy.Windows;

internal sealed class PushToTalkHook : IDisposable
{
    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardData { public uint Key, Scan, Flags, Time; public IntPtr Extra; }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int kind, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    private readonly HookProc callback;
    private readonly Action tap, start, finish;
    private readonly uint modifiers;
    private readonly HoldGesture gesture = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(15) };
    private IntPtr hook;
    private bool down;
    internal PushToTalkHook(uint modifiers, Action tap, Action start, Action finish)
    {
        this.modifiers = modifiers; this.tap = tap; this.start = start; this.finish = finish; callback = OnKey;
        hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) throw new InvalidOperationException("Hold-to-talk is unavailable; the regular shortcut remains active.");
        timer.Tick += (_, _) => { if (gesture.Tick(Environment.TickCount64)) start(); };
    }
    private IntPtr OnKey(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0) {
            var k = Marshal.PtrToStructure<KeyboardData>(data); bool up = message.ToInt64() is 0x101 or 0x105;
            if ((k.Flags & 0x10) == 0) {
                uint current = 0; if ((InputNative.GetAsyncKeyState(0x11) & 0x8000) != 0) current |= 2; if ((InputNative.GetAsyncKeyState(0x10) & 0x8000) != 0) current |= 4; if ((InputNative.GetAsyncKeyState(0x12) & 0x8000) != 0) current |= 1; if ((InputNative.GetAsyncKeyState(0x5b) & 0x8000) != 0 || (InputNative.GetAsyncKeyState(0x5c) & 0x8000) != 0) current |= 8;
                if (!up && k.Key == 0x20 && current == modifiers) { if (!down) { down = true; gesture.Down(Environment.TickCount64); timer.Start(); } return new IntPtr(1); }
                if (up && down && k.Key is 0x20 or 0x10 or 0x11 or 0x12 or 0xa0 or 0xa1 or 0xa2 or 0xa3 or 0xa4 or 0xa5 or 0x5b or 0x5c) {
                    down = false; timer.Stop(); bool held = gesture.Held; bool tapped = gesture.Up(); if (held) finish(); else if (tapped) tap();
                    if (k.Key == 0x20) return new IntPtr(1);
                }
            }
        }
        return CallNextHookEx(hook, code, message, data);
    }
    public void Dispose() { timer.Stop(); if (hook != IntPtr.Zero) UnhookWindowsHookEx(hook); hook = IntPtr.Zero; down = false; gesture.Up(); }
}
