using System;
using System.Threading;
using System.Threading.Tasks;

namespace Buddy.Windows;

// Identity-only restoration evidence. This is never a verified foreground
// observation or authority for subsequent input, capture, or in-app actions.
internal sealed record CalculatorWindowTarget(ComputerWindowIdentity Root, ComputerWindowIdentity App, ComputerFrameBinding? Frame);
internal sealed record CalculatorWindowView(CalculatorWindowTarget Target, bool Minimized, bool Visible, bool Foreground);

internal interface ICalculatorWindowActivation
{
    // The native adapter owns signed package/process/frame validation and unique
    // target selection. Null means no candidate yet, and is allowed only before
    // the policy pins a target. An expected target must never be replaced.
    Task<CalculatorWindowView?> ObserveAsync(CalculatorWindowTarget? expected, CancellationToken ct);
    // Recheck the original deadline, Agent/privacy, input ownership, desktop,
    // process identity and source-or-target foreground immediately at boundaries.
    void Guard(CalculatorWindowTarget? target, CancellationToken ct);
    void Restore(CalculatorWindowTarget target, CancellationToken ct);
    bool Foreground(CalculatorWindowTarget target, CancellationToken ct);
}

internal sealed class CalculatorWindowActivation(ICalculatorWindowActivation backend, Func<CancellationToken, Task>? delay = null)
{
    private const int MaximumObservations = 30;
    private readonly Func<CancellationToken, Task> pause = delay ?? (ct => Task.Delay(100, ct));
    private int claimed;

    internal async Task<CalculatorWindowTarget> RunAsync(CancellationToken ct)
    {
        // One instance owns one attempt, including failure or cancellation. It
        // cannot be reused to repeat an effect or overlap an unsettled backend.
        if (Interlocked.Exchange(ref claimed, 1) != 0)
            throw new InvalidOperationException("This Calculator activation attempt was already used; no activation was repeated.");

        CalculatorWindowTarget? target = null;
        for (int attempt = 0; attempt < MaximumObservations; attempt++) {
            var discovered = await ObserveAsync(null, ct).ConfigureAwait(false);
            if (discovered is not null) { target = discovered.Target; break; }
            if (attempt + 1 < MaximumObservations) await PauseAsync(null, ct).ConfigureAwait(false);
        }
        if (target is null) throw new TimeoutException("The activated Calculator window was not found within the observation limit.");

        // Even the first candidate must be freshly reobserved with an explicit
        // expected identity before any restore or foreground request.
        var view = (await ObserveAsync(target, ct).ConfigureAwait(false))!;
        if (view.Minimized) {
            Check(target, ct);
            // ShowWindow's native return value describes prior visibility, not
            // success. Restore is deliberately void; only fresh state can prove it.
            backend.Restore(target, ct);
            Check(target, ct);
            view = await WaitForAsync(target, Restored,
                "Calculator did not become visible after the bounded restore attempt.", ct).ConfigureAwait(false);
        } else if (!view.Visible) {
            view = await WaitForAsync(target, Restored,
                "The activated Calculator window did not become visible within the observation limit.", ct).ConfigureAwait(false);
        }

        if (!view.Foreground) {
            Check(target, ct);
            bool accepted = backend.Foreground(target, ct);
            Check(target, ct);
            if (!accepted) throw new InvalidOperationException("Windows denied the Calculator foreground request; no activation was repeated.");
            await WaitForAsync(target, Ready,
                "Calculator did not become the visible foreground window within the observation limit.", ct).ConfigureAwait(false);
        }

        var final = (await ObserveAsync(target, ct).ConfigureAwait(false))!;
        if (!Ready(final)) throw new InvalidOperationException("The Calculator window changed before foreground confirmation; no activation was repeated.");
        Check(target, ct);
        // The caller must still perform the independent strict foreground/frame
        // postcondition. Returning a target does not construct a completion receipt.
        return target;
    }

    private async Task<CalculatorWindowView> WaitForAsync(CalculatorWindowTarget target,
        Func<CalculatorWindowView, bool> ready, string failure, CancellationToken ct)
    {
        for (int attempt = 0; attempt < MaximumObservations; attempt++) {
            var view = (await ObserveAsync(target, ct).ConfigureAwait(false))!;
            if (ready(view)) return view;
            if (attempt + 1 < MaximumObservations) await PauseAsync(target, ct).ConfigureAwait(false);
        }
        throw new TimeoutException(failure);
    }

    private async Task<CalculatorWindowView?> ObserveAsync(CalculatorWindowTarget? target, CancellationToken ct)
    {
        Check(target, ct);
        // Await the actual operation: cancellation never abandons native work
        // and must not release its owner while that operation is still pending.
        var view = await backend.ObserveAsync(target, ct).ConfigureAwait(false);
        Check(target, ct);
        if (view is not null && view.Target is null || target is not null && (view is null || view.Target != target))
            throw new InvalidOperationException("The Calculator activation target changed or disappeared; no replacement was selected.");
        return view;
    }

    private async Task PauseAsync(CalculatorWindowTarget? target, CancellationToken ct)
    {
        Check(target, ct);
        await pause(ct).ConfigureAwait(false);
        Check(target, ct);
    }

    private void Check(CalculatorWindowTarget? target, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        backend.Guard(target, ct);
        ct.ThrowIfCancellationRequested();
    }
    private static bool Restored(CalculatorWindowView view) => !view.Minimized && view.Visible;
    private static bool Ready(CalculatorWindowView view) => Restored(view) && view.Foreground;
}
