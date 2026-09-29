namespace Buddy.Windows;

internal readonly record struct InterruptionInput(int X, int Y, bool Escape);

// Never waits on WPF or an accessibility provider. Cancellation is visible at
// every dispatch fence even when presentation is busy.
internal sealed class DispatchInterruption : IDisposable
{
    private readonly Func<InterruptionInput> read;
    private readonly Action<string> stop;
    private readonly ManualResetEvent shutdown = new(false);
    private readonly object gate = new();
    private readonly Thread worker;
    private InterruptionInput origin;
    private bool armed, stopped;
    internal DispatchInterruption(Func<InterruptionInput> read, Action<string> stop)
    {
        this.read = read; this.stop = stop;
        worker = new Thread(Monitor) { IsBackground = true, Name = "Buddy dispatch interruption" }; worker.Start();
    }
    internal void ArmPointer() { lock (gate) { origin = read(); armed = true; } }
    internal void DisarmPointer() { lock (gate) armed = false; }
    private void Monitor()
    {
        while (!shutdown.WaitOne(10)) {
            string? reason = null;
            try {
                lock (gate) {
                    if (stopped) return;
                    var current = read();
                    if (current.Escape) reason = "Stopped by Escape.";
                    else if (armed && (current.X != origin.X || current.Y != origin.Y)) reason = "Stopped because the pointer moved.";
                    if (reason is not null) stopped = true;
                }
            } catch { reason = "Stopped because input monitoring became unavailable."; }
            if (reason is not null) { stop(reason); return; }
        }
    }
    public void Dispose() { shutdown.Set(); worker.Join(); shutdown.Dispose(); }
}
