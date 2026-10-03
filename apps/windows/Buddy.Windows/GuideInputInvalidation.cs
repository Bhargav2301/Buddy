using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Buddy.Windows;

// Active only while guidance ink is visible. Records no keys or pointer data and never suppresses input.
internal sealed class GuideInputInvalidation : IDisposable
{
    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    private readonly HookProc mouseCallback, keyCallback;
    private readonly Action changed;
    private readonly Func<bool>? interested;
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private IntPtr mouse, keyboard;
    private bool pending, disposed;
    internal GuideInputInvalidation(Action changed, Func<bool>? interested = null)
    {
        this.changed = changed; this.interested = interested;
        mouseCallback = (code, message, data) => {
            if (code >= 0 && message.ToInt64() is 0x201 or 0x204 or 0x207 or 0x20A or 0x20E) Notify();
            return CallNextHookEx(mouse, code, message, data);
        };
        keyCallback = (code, message, data) => {
            if (code >= 0 && message.ToInt64() is 0x100 or 0x104) Notify();
            return CallNextHookEx(keyboard, code, message, data);
        };
        mouse = SetWindowsHookEx(14, mouseCallback, GetModuleHandle(null), 0);
        keyboard = SetWindowsHookEx(13, keyCallback, GetModuleHandle(null), 0);
        if (mouse == IntPtr.Zero || keyboard == IntPtr.Zero) { Dispose(); throw new InvalidOperationException("Live guidance could not monitor screen-changing input."); }
    }
    private void Notify()
    {
        if (interested is not null && !interested()) return;
        if (pending || disposed) return; pending = true;
        dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => { if (!disposed) changed(); }));
    }
    public void Dispose() { disposed = true; if (mouse != IntPtr.Zero) UnhookWindowsHookEx(mouse); if (keyboard != IntPtr.Zero) UnhookWindowsHookEx(keyboard); mouse = keyboard = IntPtr.Zero; }
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int kind, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
