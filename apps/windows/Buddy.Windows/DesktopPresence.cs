using System.Windows.Threading;

namespace Buddy.Windows;

// Preview data remains isolated, but the visible companion has one desktop owner.
// Older installed builds already honor this mutex and the normal activation pipe.
internal sealed class DesktopPresence : IDisposable
{
    private readonly Mutex gate;
    private readonly Func<IDisposable> activate;
    private readonly Action<bool> changed;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private IDisposable? activation;
    private bool owned, disposed;
    internal bool OwnsPresence => owned;
    internal DesktopPresence(Func<IDisposable> activate, Action<bool> changed, string name = "Local\\Buddy.Desktop.v1", bool startTimer = true)
    {
        this.activate = activate; this.changed = changed; gate = new Mutex(false, name);
        changed(false); Poll(); timer.Tick += (_, _) => Poll(); if (startTimer) timer.Start();
    }
    internal void Poll()
    {
        if (disposed || owned) return;
        bool acquired;
        try { acquired = gate.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) return;
        try { activation = activate(); owned = true; changed(true); }
        catch (IOException) { gate.ReleaseMutex(); }
        catch { gate.ReleaseMutex(); throw; }
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true; timer.Stop(); activation?.Dispose();
        if (owned) { owned = false; gate.ReleaseMutex(); }
        gate.Dispose();
    }
}
