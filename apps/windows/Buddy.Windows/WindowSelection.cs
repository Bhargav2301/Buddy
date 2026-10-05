using System.Runtime.InteropServices;

namespace Buddy.Windows;

// Metadata only: no text, UI Automation, focus changes, or input dispatch.
internal sealed record WindowProbe(bool Exists, uint ThreadId, uint ProcessId, bool ProcessOpened,
    int NativeError, long ProcessStarted, string App, bool ElevationKnown, bool Elevated);

internal sealed class WindowSelectionException(string code, string message, IntPtr window = default,
    uint processId = 0, uint threadId = 0, int nativeError = 0) : InvalidOperationException(message)
{
    internal string Code { get; } = code;
    internal IntPtr Window { get; } = window;
    internal uint ProcessId { get; } = processId;
    internal uint ThreadId { get; } = threadId;
    internal int NativeError { get; } = nativeError;
}

internal sealed record WindowSelection(IntPtr Window, uint ThreadId, uint ProcessId, long ProcessStarted, string App)
{
    internal const string Refocus = "Focus the intended app, then return to Buddy and choose Make plan.";
    internal static WindowSelection Capture(IntPtr window, Func<IntPtr, WindowProbe>? inspect = null)
    {
        if (window == IntPtr.Zero) throw new WindowSelectionException("no-selection", "No app is selected. " + Refocus);
        var probe = (inspect ?? InputNative.InspectWindow)(window);
        WindowSelectionException Failure(string code, string message) => new(code, message + " " + Refocus,
            window, probe.ProcessId, probe.ThreadId, probe.NativeError);
        if (!probe.Exists || probe.ThreadId == 0 || probe.ProcessId == 0)
            throw Failure("window-gone", "The selected window is no longer available.");
        if (!probe.ProcessOpened)
            throw Failure("process-query", $"Windows could not inspect the selected app (error {probe.NativeError}). Access restrictions remain in place.");
        if (probe.ProcessStarted <= 0 || string.IsNullOrWhiteSpace(probe.App))
            throw Failure("process-identity", $"The selected app's identity could not be verified (error {probe.NativeError}).");
        if (!probe.ElevationKnown)
            throw Failure("elevation-unknown", $"The selected app's access level could not be checked (error {probe.NativeError}).");
        if (probe.Elevated)
            throw Failure("elevated", "Buddy cannot control or capture elevated applications.");
        return new(window, probe.ThreadId, probe.ProcessId, probe.ProcessStarted, probe.App);
    }
    internal void Validate(Func<IntPtr, WindowProbe>? inspect = null)
    {
        var current = Capture(Window, inspect);
        if (current.ThreadId != ThreadId || current.ProcessId != ProcessId || current.ProcessStarted != ProcessStarted ||
            !string.Equals(current.App, App, StringComparison.OrdinalIgnoreCase))
            throw new WindowSelectionException("window-replaced", "The selected app or window was replaced. " + Refocus,
                Window, current.ProcessId, current.ThreadId);
    }
}

// The cache only remembers observed external foreground windows. Revalidation never retargets it.
internal sealed class WindowSelectionTracker
{
    private readonly Func<IntPtr, WindowProbe> inspect;
    private readonly Func<IntPtr, bool> isOwnWindow;
    private readonly Func<long> clock;
    private readonly Func<IntPtr, bool> isSelectableWindow;
    private readonly Action<IntPtr> checkWindow;
    private readonly long lifetime;
    private WindowSelection? selected;
    private long observedAt;
    private WindowSelectionException? failure;
    internal WindowSelectionTracker(Func<IntPtr, WindowProbe>? inspect = null, Func<long>? clock = null,
        Func<IntPtr, bool>? isOwnWindow = null, TimeSpan? lifetime = null,
        Func<IntPtr, bool>? isSelectableWindow = null, Action<IntPtr>? checkWindow = null)
    {
        this.inspect = inspect ?? InputNative.InspectWindow;
        this.clock = clock ?? (() => Environment.TickCount64);
        this.isOwnWindow = isOwnWindow ?? Native.IsOwnWindow;
        this.isSelectableWindow = isSelectableWindow ?? Native.IsSelectableWindow;
        this.checkWindow = checkWindow ?? Native.CheckWindow;
        this.lifetime = (long)(lifetime ?? TimeSpan.FromMinutes(2)).TotalMilliseconds;
        if (this.lifetime <= 0) throw new ArgumentOutOfRangeException(nameof(lifetime));
    }
    internal void Observe(IntPtr foreground)
    {
        if (foreground == IntPtr.Zero || isOwnWindow(foreground) || !isSelectableWindow(foreground)) return;
        try { var candidate = WindowSelection.Capture(foreground, inspect); checkWindow(foreground); candidate.Validate(inspect); selected = candidate; observedAt = clock(); failure = null; }
        catch (WindowSelectionException ex) { selected = null; failure = ex; }
        catch (InvalidOperationException ex) { selected = null; failure = new("window-unavailable", ex.Message, foreground); }
    }
    internal WindowSelection RequireCurrent()
    {
        if (failure is not null) throw failure;
        if (selected is null) throw new WindowSelectionException("no-selection", "No app is selected. " + WindowSelection.Refocus);
        long age = clock() - observedAt;
        if (age < 0 || age > lifetime) {
            selected = null;
            throw new WindowSelectionException("selection-expired", "The previous app selection expired. " + WindowSelection.Refocus);
        }
        try { selected.Validate(inspect); checkWindow(selected.Window); selected.Validate(inspect); return selected; }
        catch (WindowSelectionException ex) { selected = null; failure = ex; throw; }
        catch (InvalidOperationException ex) { var hwnd = selected.Window; selected = null; failure = new("window-unavailable", ex.Message, hwnd); throw failure; }
    }
    internal void Clear() { selected = null; failure = null; observedAt = 0; }
}

// Register/dispose on Buddy's UI thread. Out-of-context WinEvents return to that thread's
// message loop; no code is injected into another process and no input is observed.
internal sealed class WindowSelectionMonitor : IDisposable
{
    private delegate void WinEvent(IntPtr hook, uint kind, IntPtr window, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWinEventHook(uint first, uint last, IntPtr module, WinEvent callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
    private readonly WinEvent callback;
    private readonly Action<IntPtr> observe;
    private IntPtr hook;
    private bool disposed;
    internal Exception? LastError { get; private set; }
    internal WindowSelectionMonitor(Action<IntPtr> observe, Action? invalidate = null)
    {
        this.observe = observe;
        callback = (_, kind, window, objectId, childId, _, time) => {
            if (disposed || kind != 3 || window == IntPtr.Zero || objectId != 0 || childId != 0) return;
            if (unchecked((uint)Environment.TickCount - time) > 5000) {
                try { invalidate?.Invoke(); }
                catch (Exception ex) { LastError = ex; }
                return;
            }
            Observe(window);
        };
        hook = SetWinEventHook(3, 3, IntPtr.Zero, callback, 0, 0, 0); // EVENT_SYSTEM_FOREGROUND, WINEVENT_OUTOFCONTEXT
        if (hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Foreground app tracking could not start; periodic selection checks remain available.");
        Observe(Native.GetForegroundWindow());
    }
    private void Observe(IntPtr window)
    {
        try { observe(window); LastError = null; }
        catch (Exception ex) { LastError = ex; } // Never unwind through a native event callback.
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        if (hook != IntPtr.Zero) { UnhookWinEvent(hook); hook = IntPtr.Zero; }
        GC.KeepAlive(callback);
    }
}
