using System;
using System.Threading;
using System.Threading.Tasks;

namespace Buddy.Windows;

// This first adapter observes window/process metadata only. It neither reads UIA
// or pixels nor claims that any in-app task is complete.
internal sealed record ComputerWindowIdentity(IntPtr Window, uint ProcessId, uint ThreadId, long ProcessStarted, string App);
internal sealed record ComputerFrameBinding(ComputerWindowIdentity Host, ComputerWindowIdentity Child,
    string PackageFullName, string PackageRoot, string MainExecutable, string AppUserModelId);
internal sealed record ComputerObservation(Guid RequestId, Guid Id, DateTimeOffset At, ComputerWindowIdentity Window, bool Complete = true, ComputerFrameBinding? Frame = null);
internal sealed record ComputerDispatch(Guid RequestId, Guid ObservationId, string Alias);
internal sealed record ComputerVerification(bool Satisfied, ComputerObservation? Observation, string Message);
internal sealed record ComputerUseResult(bool Verified, bool ActionDispatched, string Message, Guid RequestId,
    Guid ObservationId, ComputerObservation? After, int ActionCount, int VerificationCount);

internal interface IComputerUseBackend
{
    Task<ComputerObservation> ObserveAsync(Guid requestId, CancellationToken ct);
    Task<ComputerObservation> CheckpointAsync(ComputerObservation observation, string alias, CancellationToken ct);
    Task<ComputerDispatch> DispatchAsync(ComputerObservation checkpoint, string alias, CancellationToken ct);
    Task<ComputerVerification> VerifyAsync(ComputerObservation before, ComputerDispatch dispatch, CancellationToken ct);
}

internal sealed record ComputerUseLimits(TimeSpan ObservationAge, TimeSpan Timeout, int VerificationAttempts, TimeSpan VerificationDelay)
{
    internal static ComputerUseLimits Default => new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15), 30, TimeSpan.FromMilliseconds(200));
}

internal sealed class BoundedComputerUse(IComputerUseBackend backend, ComputerUseLimits? limits = null, Func<DateTimeOffset>? clock = null)
{
    private int running;
    private Task settlement = Task.CompletedTask;
    internal Task Settlement => Volatile.Read(ref settlement);
    private readonly ComputerUseLimits policy = Validate(limits ?? ComputerUseLimits.Default);
    private readonly Func<DateTimeOffset> now = clock ?? (() => DateTimeOffset.UtcNow);
    internal static bool AllowedAlias(string alias) => alias is "notepad" or "calculator" or "explorer" or "comet" or "camera" or "spotify";
    internal static bool MatchesAppName(string alias, string app) => alias switch {
        "calculator" => app.Equals("calculator", StringComparison.OrdinalIgnoreCase) || app.Equals("calculatorapp", StringComparison.OrdinalIgnoreCase),
        "camera" => app.Equals("WindowsCamera", StringComparison.OrdinalIgnoreCase),
        _ => AllowedAlias(alias) && app.Equals(alias, StringComparison.OrdinalIgnoreCase)
    };

    internal async Task<ComputerUseResult> RunAsync(string alias, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!AllowedAlias(alias)) throw new InvalidOperationException("Only an exact supported app launch is available through this adapter.");
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0) throw new InvalidOperationException("An app launch is already in progress; stop or wait for it first.");
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Volatile.Write(ref settlement, settled.Task);
        using var owner = CancellationTokenSource.CreateLinkedTokenSource(ct);
        owner.CancelAfter(policy.Timeout);
        var token = owner.Token; var requestId = Guid.NewGuid();
        Task? pending = null;
        try {
            var observing = backend.ObserveAsync(requestId, token); pending = observing;
            var observation = await observing.WaitAsync(token); token.ThrowIfCancellationRequested();
            RequireObservation(observation, requestId);
            var checking = backend.CheckpointAsync(observation, alias, token); pending = checking;
            var checkpoint = await checking.WaitAsync(token); token.ThrowIfCancellationRequested();
            RequireObservation(observation, requestId); RequireObservation(checkpoint, requestId);
            if (checkpoint.Id != observation.Id || checkpoint.Window != observation.Window)
                throw new InvalidOperationException("The observed window or process changed before launch; request it again from the intended window.");
            // The backend must perform its own final identity/Stop check immediately
            // before the effect. There is exactly one dispatch call, including errors.
            ComputerDispatch dispatch;
            try {
                token.ThrowIfCancellationRequested();
                var acting = backend.DispatchAsync(checkpoint, alias, token); pending = acting;
                dispatch = await acting.WaitAsync(token); token.ThrowIfCancellationRequested();
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                return Unverified(ActivationFailure(ex.Message) ?? "The launch was attempted but its result is uncertain; inspect the app before trying again.", observation, 0);
            }
            if (dispatch is null || dispatch.RequestId != requestId || dispatch.ObservationId != observation.Id || dispatch.Alias != alias)
                return Unverified("The launch receipt did not match this request; no follow-up action ran.", observation, 0);
            string? lastVerificationFailure = null;
            for (int attempt = 0; attempt < policy.VerificationAttempts; attempt++) {
                token.ThrowIfCancellationRequested();
                ComputerVerification verified;
                try {
                    var verifying = backend.VerifyAsync(observation, dispatch, token); pending = verifying;
                    verified = await verifying.WaitAsync(token); token.ThrowIfCancellationRequested();
                } catch (Exception ex) when (ex is not OperationCanceledException) {
                    return Unverified("The launch was attempted, but the requested app window could not be verified; no launch was repeated.", observation, attempt + 1);
                }
                if (verified is { Satisfied: true, Observation: { } after }) {
                    try {
                        RequireObservation(after, requestId);
                        if (after.Id == observation.Id || after.At < checkpoint.At || !MatchesAppName(alias, after.Window.App) ||
                            after.Frame is not null && alias != "calculator")
                            throw new InvalidOperationException("Postcondition was not freshly observed for the requested app.");
                    } catch (InvalidOperationException) {
                        return Unverified("The returned app window was stale or incomplete; no launch was repeated.", observation, attempt + 1);
                    }
                    token.ThrowIfCancellationRequested();
                    return new(true, true, "Verified " + alias + " in a visible foreground window; no in-app action ran.", requestId, observation.Id, after, 1, attempt + 1);
                }
                // Retain only a fixed host explanation from the final unsuccessful
                // observation. Never expose arbitrary backend text or let an earlier
                // reason describe a different, later result.
                lastVerificationFailure = verified is { Satisfied: false } ? VerificationFailure(verified.Message) : null;
                if (attempt + 1 < policy.VerificationAttempts) await Task.Delay(policy.VerificationDelay, token);
            }
            return Unverified(lastVerificationFailure ?? "The launch was attempted but no requested foreground app window was verified; focus the app and inspect it before retrying.", observation, policy.VerificationAttempts);
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            throw new TimeoutException("App launch verification timed out; the app may have opened. No launch will be repeated automatically.");
        } finally {
            // A backend that ignores cancellation cannot create overlapping workers
            // through this controller while its abandoned operation is still pending.
            if (pending is { IsCompleted: false }) _ = ReleaseAfter(pending, settled);
            else { Volatile.Write(ref running, 0); settled.TrySetResult(); }
        }
    }
    private async Task ReleaseAfter(Task pending, TaskCompletionSource settled)
    {
        try { await pending.ConfigureAwait(false); } catch { }
        finally { Volatile.Write(ref running, 0); settled.TrySetResult(); }
    }
    private void RequireObservation(ComputerObservation observation, Guid requestId)
    {
        if (observation is null || observation.RequestId != requestId || observation.Id == Guid.Empty || !observation.Complete ||
            observation.Window is null || observation.Window.Window == IntPtr.Zero || observation.Window.ProcessId == 0 ||
            observation.Window.ThreadId == 0 || observation.Window.ProcessStarted <= 0 || string.IsNullOrWhiteSpace(observation.Window.App))
            throw new InvalidOperationException("No complete window identity was observed; focus the intended window and try again.");
        var age = now() - observation.At;
        if (age < TimeSpan.Zero || age > policy.ObservationAge)
            throw new InvalidOperationException("The window observation expired; make a fresh request.");
        if (observation.Frame is { } frame && (frame.Child != observation.Window || frame.Host is null ||
            frame.Host.Window == IntPtr.Zero || frame.Host.Window == frame.Child.Window || frame.Host.ProcessId == 0 ||
            frame.Host.ProcessId == frame.Child.ProcessId || frame.Host.ThreadId == 0 || frame.Host.ProcessStarted <= 0 ||
            !string.Equals(frame.Host.App, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase) ||
            !frame.Child.App.Equals("CalculatorApp", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(frame.PackageFullName) || string.IsNullOrEmpty(frame.PackageRoot) || string.IsNullOrEmpty(frame.MainExecutable) ||
            frame.AppUserModelId != "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"))
            throw new InvalidOperationException("The framed app observation did not preserve its separate host and application identities.");
    }
    private static ComputerUseResult Unverified(string message, ComputerObservation before, int checks) =>
        new(false, true, message, before.RequestId, before.Id, null, 1, checks);
    private static string? ActivationFailure(string? message) => message switch {
        "Windows denied the Calculator foreground request; no activation was repeated." => message,
        "Calculator activation could not be safely completed." => "Calculator activation could not be safely completed; no launch was repeated.",
        "The Calculator activation target changed or disappeared; no replacement was selected." => message,
        "The Calculator window changed before foreground confirmation; no activation was repeated." => message,
        "The activated Calculator window was not found within the observation limit." or
        "Calculator did not become visible after the bounded restore attempt." or
        "The activated Calculator window did not become visible within the observation limit." or
        "Calculator did not become the visible foreground window within the observation limit."
            => "Calculator did not reach a verified visible foreground state within the activation limit; no launch was repeated.",
        _ => null
    };
    private static string? VerificationFailure(string? message) => message switch {
        "Waiting for the requested app window." => "The launch was attempted, but the final check found no visible foreground window. No launch was repeated.",
        "The foreground app does not match the request." => "The launch was attempted, but the final foreground app did not match the request. No launch was repeated.",
        "The Calculator frame and signed app process could not be bound to this foreground window." => "The launch was attempted, but the final Calculator frame could not be bound to its signed app process. No launch was repeated.",
        "The app executable identity could not be verified." => "The launch was attempted, but the final app executable identity could not be verified. No launch was repeated.",
        "The installed app identity changed after launch." => "The launch was attempted, but the installed app identity changed afterward. No launch was repeated.",
        "The foreground process is not the same signed package's main application." => "The launch was attempted, but the final foreground process did not match the signed package's main app. No launch was repeated.",
        "The requested app window changed during verification." => "The launch was attempted, but the requested window changed during the final check. No launch was repeated.",
        _ => null
    };
    private static ComputerUseLimits Validate(ComputerUseLimits value)
    {
        if (value.ObservationAge <= TimeSpan.Zero || value.ObservationAge > TimeSpan.FromSeconds(10) ||
            value.Timeout <= TimeSpan.Zero || value.Timeout > TimeSpan.FromSeconds(30) || value.VerificationAttempts is < 1 or > 30 ||
            value.VerificationDelay < TimeSpan.Zero || value.VerificationDelay > TimeSpan.FromSeconds(1))
            throw new ArgumentOutOfRangeException(nameof(value));
        return value;
    }
}
