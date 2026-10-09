using Buddy.Server;
using System.Diagnostics;

namespace Buddy.Windows;

internal enum RefinementRequestState { Created, Running, Completed, Cancelled, TimedOut, Failed }
internal sealed record RefinementRequestOutcome(RefinementRequestState State, RefinementResult? Result, string Message, string? ErrorCode = null);

// Pure request lifetime shared by both review surfaces. It owns no field writes.
// UI owners additionally check their request reference/generation before rendering
// or enabling Apply. Cancellation cannot promote a streamed preview to a result.
internal sealed class RefinementRequest : IDisposable
{
    private readonly CancellationTokenSource deadline;
    private readonly CancellationTokenSource cancel;
    private readonly CancellationToken callerToken;
    private readonly TimeSpan heartbeat;
    private bool disposed, runStarted, explicitlyCancelled;
    internal RefinementRequestState State { get; private set; }
    internal bool IsRunning => State == RefinementRequestState.Running;
    internal RefinementResult? Result { get; private set; }
    internal RefinementRequest(TimeSpan? timeout = null, CancellationToken token = default, TimeSpan? heartbeat = null)
    {
        var duration = timeout ?? RefinementRequestLimits.Default.Total + TimeSpan.FromSeconds(5);
        this.heartbeat = heartbeat ?? TimeSpan.FromSeconds(1);
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromMinutes(9) || this.heartbeat <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        callerToken = token; deadline = new(duration); cancel = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
    }
    internal void Cancel() { explicitlyCancelled = true; if (!disposed) try { cancel.Cancel(); } catch (ObjectDisposedException) { } }
    internal async Task<RefinementRequestOutcome> Run(Func<CancellationToken, IAsyncEnumerable<RefinementEvent>> source,
        Action<RefinementEvent>? onEvent = null, Action<string>? onProgress = null)
    {
        if (State != RefinementRequestState.Created || disposed) throw new InvalidOperationException("A refinement request can run only once.");
        runStarted = true; State = RefinementRequestState.Running; var elapsed = Stopwatch.StartNew();
        string stage = "Preparing local refinement";
        IAsyncEnumerator<RefinementEvent>? iterator = null; Task<bool>? pending = null;
        using var pulses = new CancellationTokenSource();
        Task pulse = Task.Delay(heartbeat, pulses.Token);
        try {
            cancel.Token.ThrowIfCancellationRequested(); iterator = source(cancel.Token).GetAsyncEnumerator(cancel.Token);
            while (true) {
                pending = iterator.MoveNextAsync().AsTask();
                while (!pending.IsCompleted) {
                    var completed = await Task.WhenAny(pending, pulse).WaitAsync(cancel.Token);
                    cancel.Token.ThrowIfCancellationRequested();
                    if (ReferenceEquals(completed, pulse)) { onProgress?.Invoke(Progress()); pulse = Task.Delay(heartbeat, pulses.Token); }
                }
                bool more = await pending; cancel.Token.ThrowIfCancellationRequested();
                if (!more) return Finish(RefinementRequestState.Failed, "Refinement ended without a verified result. Original unchanged. Try again.", "INCOMPLETE_REFINEMENT");
                var item = iterator.Current;
                if (item.Type is "status" or "stage") { stage = item.Text ?? "Refining locally"; onProgress?.Invoke(Progress()); }
                onEvent?.Invoke(item); cancel.Token.ThrowIfCancellationRequested();
                if (item.Result is { } result) {
                    Result = result; State = RefinementRequestState.Completed;
                    return new(State, result, result.Message);
                }
            }
        } catch (OperationCanceledException) {
            if (!cancel.IsCancellationRequested)
                return Finish(RefinementRequestState.Failed, "Local AI stopped before finishing. Original unchanged. Check PC setup and try again.", "REFINE_INTERRUPTED");
            return deadline.IsCancellationRequested && !callerToken.IsCancellationRequested && !explicitlyCancelled
                ? Finish(RefinementRequestState.TimedOut, "Refinement timed out. Original unchanged. Check local AI in PC setup, or try a smaller draft.", "REFINE_TIMED_OUT")
                : Finish(RefinementRequestState.Cancelled, "Stopped. Original unchanged. Try again when ready.", "REFINE_CANCELLED");
        } catch (BuddyException ex) {
            return Finish(ex.Code == "REFINE_TIMED_OUT" ? RefinementRequestState.TimedOut : RefinementRequestState.Failed, ex.Message, ex.Code);
        } catch (Exception ex) {
            return Finish(RefinementRequestState.Failed, "Refinement failed: " + ex.Message + " Original unchanged.", "REFINE_FAILED");
        } finally {
            pulses.Cancel(); cancel.Cancel();
            _ = Cleanup(pending, iterator);
        }
        string Progress() => stage.TrimEnd('.', '\u2026') + $" - {elapsed.Elapsed.TotalSeconds:0}s elapsed. Original unchanged.";
    }
    private RefinementRequestOutcome Finish(RefinementRequestState state, string message, string code)
    { State = state; Result = null; return new(state, null, message, code); }
    private async Task Cleanup(Task<bool>? pending, IAsyncEnumerator<RefinementEvent>? iterator)
    {
        try { if (pending is not null) await pending.ConfigureAwait(false); } catch { /* The terminal outcome already reports failure/cancel. */ }
        try { if (iterator is not null) await iterator.DisposeAsync().ConfigureAwait(false); } catch { /* Observe late disposal failure. */ }
        finally { cancel.Dispose(); deadline.Dispose(); disposed = true; }
    }
    public void Dispose()
    {
        if (disposed) return; Cancel();
        // Once Run starts, only its cleanup continuation disposes token sources.
        // This also covers the terminal-state/finally gap when Close races completion.
        if (!runStarted) { cancel.Dispose(); deadline.Dispose(); disposed = true; }
    }
}
