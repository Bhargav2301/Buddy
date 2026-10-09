using Buddy.Windows;

// Pure lifecycle checks. Every observation and effect below is an in-memory
// fake; no window, process, profile, input, package or native API is accessed.
internal static class CalculatorActivationChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        static CalculatorWindowActivation Policy(Fake fake) => new(fake, fake.Delay);
        async Task<T> Fails<T>(Task task, string label) where T : Exception {
            try { await task.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (T error) { check(true, label); return error; }
            throw new Exception("FAIL: " + label);
        }

        {
            var fake = new Fake { MissingInitially = 2 };
            var target = await Policy(fake).RunAsync(default);
            check(target == fake.Target && fake.Observations == 5 && fake.Delays == 2,
                "A newly activated window may appear later, then must be reobserved and finally confirmed");
            check(fake.Restores == 0 && fake.ForegroundCalls == 0,
                "An already-foreground Calculator needs no restore or focus request");
            check(fake.Expected.Take(3).All(value => value is null) && fake.Expected.Skip(3).All(value => value == fake.Target),
                "Discovery stops permanently once the first exact target is pinned");
        }
        {
            var fake = new Fake(framed: false) { IsForeground = false };
            var target = await Policy(fake).RunAsync(default);
            check(target.Root == target.App && target.Frame is null && fake.Restores == 0 && fake.ForegroundCalls == 1,
                "A directly hosted Calculator retains its honest app identity without fabricating a frame");
        }
        {
            var fake = new Fake { MissingInitially = 29 };
            await Policy(fake).RunAsync(default);
            check(fake.Observations == 32 && fake.Delays == 29 && fake.Restores == 0 && fake.ForegroundCalls == 0,
                "The final allowed discovery observation can pin a target without exceeding the discovery bound");
        }
        {
            var fake = new Fake { Minimized = true, Visible = false, IsForeground = false };
            var target = await Policy(fake).RunAsync(default);
            check(target == fake.Target && fake.Restores == 1 && fake.ForegroundCalls == 1,
                "An existing minimized Calculator is restored once and receives one foreground request");
            check(fake.Events.IndexOf("restore") < fake.Events.IndexOf("foreground"),
                "Foreground is requested only after restoration is freshly observed");
            check(fake.Expected.Skip(1).All(value => value == target),
                "Every observation after discovery remains bound to the existing process and window");
        }
        {
            var fake = new Fake { Minimized = true, Visible = false, IsForeground = false, RestoreActivates = true };
            await Policy(fake).RunAsync(default);
            check(fake.Restores == 1 && fake.ForegroundCalls == 0,
                "Restoration that establishes foreground needs no redundant foreground call");
        }
        {
            var fake = new Fake { IsForeground = false };
            await Policy(fake).RunAsync(default);
            check(fake.Restores == 0 && fake.ForegroundCalls == 1,
                "Visible background Calculator preserves its size and only requests foreground once");
        }
        {
            var fake = new Fake { IsForeground = false, ForegroundAccepted = false };
            var error = await Fails<InvalidOperationException>(Policy(fake).RunAsync(default), "Windows foreground denial is a terminal refusal");
            check(error.Message == "Windows denied the Calculator foreground request; no activation was repeated." && fake.ForegroundCalls == 1 && fake.Restores == 0,
                "Foreground denial retains fixed wording and cannot trigger a retry");
        }
        {
            var fake = new Fake { Minimized = true, Visible = false, IsForeground = false, RestoreChangesState = false };
            await Fails<TimeoutException>(Policy(fake).RunAsync(default), "A restore call without a restored state never implies success");
            check(fake.Restores == 1 && fake.ForegroundCalls == 0 && fake.Observations == 32 && fake.Delays == 29,
                "Unconfirmed restoration is capped at 30 observations and never repeated");
        }
        {
            var fake = new Fake { MissingInitially = int.MaxValue };
            await Fails<TimeoutException>(Policy(fake).RunAsync(default), "Discovery times out without an invented window");
            check(fake.Observations == 30 && fake.Delays == 29 && fake.Restores == 0 && fake.ForegroundCalls == 0,
                "Absent windows receive exactly the bounded discovery budget and no effect");
        }
        {
            var fake = new Fake { Visible = false, IsForeground = false };
            await Fails<TimeoutException>(Policy(fake).RunAsync(default), "Hidden non-minimized target is not implicitly shown");
            check(fake.Observations == 32 && fake.Restores == 0 && fake.ForegroundCalls == 0,
                "A hidden target waits within the bound without an unauthorized show operation");
        }
        {
            var fake = new Fake { IsForeground = false, ForegroundChangesState = false };
            await Fails<TimeoutException>(Policy(fake).RunAsync(default), "Accepted foreground request still requires observed foreground");
            check(fake.Observations == 32 && fake.Delays == 29 && fake.ForegroundCalls == 1 && fake.Restores == 0,
                "Foreground observation is bounded with no second request");
        }
        {
            var fake = new Fake { IsForeground = false };
            fake.Read = (index, view) => index == 4 ? view! with { Foreground = false } : view;
            await Fails<InvalidOperationException>(Policy(fake).RunAsync(default), "Lost foreground during final fresh confirmation refuses completion");
            check(fake.ForegroundCalls == 1 && fake.Observations == 4, "Final-state regression cannot rearm foreground activation");
        }
        {
            var fake = new Fake { Minimized = true, IsForeground = false };
            fake.Read = (_, _) => throw new InvalidOperationException("Calculator target is ambiguous.");
            await Fails<InvalidOperationException>(Policy(fake).RunAsync(default), "Ambiguous native discovery is refused immediately");
            check(fake.Observations == 1 && fake.Restores == 0 && fake.ForegroundCalls == 0,
                "Native ambiguity cannot authorize selecting or acting on another target");
        }
        foreach (var change in new Func<CalculatorWindowTarget, CalculatorWindowTarget>[] {
            target => target with { Root = target.Root with { Window = new(999) } },
            target => target with { Root = target.Root with { ThreadId = 999 } },
            target => target with { App = target.App with { ProcessId = 999 } },
            target => target with { App = target.App with { ProcessStarted = 999 } },
            target => target with { Frame = target.Frame! with { PackageFullName = "changed-package" } }
        }) {
            var fake = new Fake { Minimized = true, IsForeground = false };
            fake.Read = (index, view) => index >= 2 ? view! with { Target = change(view!.Target) } : view;
            await Fails<InvalidOperationException>(Policy(fake).RunAsync(default), "Changed HWND/thread/PID/start/package cannot retarget a pinned attempt");
            check(fake.Restores == 0 && fake.ForegroundCalls == 0, "Changed pinned identity prevents every activation effect");
        }
        foreach (int disappearAt in new[] { 2, 3, 4, 5 }) {
            var fake = new Fake { Minimized = true, Visible = false, IsForeground = false };
            fake.Read = (index, view) => index == disappearAt ? null : view;
            await Fails<InvalidOperationException>(Policy(fake).RunAsync(default), "Missing pinned target is refused at every stage");
            check(fake.Observations == disappearAt && fake.Restores <= 1 && fake.ForegroundCalls <= 1,
                "A disappeared target cannot restart discovery or repeat an effect");
        }
        foreach (int replaceAt in new[] { 3, 4, 5 }) {
            var fake = new Fake { Minimized = true, Visible = false, IsForeground = false };
            fake.Read = (index, view) => index == replaceAt ? view! with { Target = view!.Target with { App = view.Target.App with { ThreadId = 999 } } } : view;
            await Fails<InvalidOperationException>(Policy(fake).RunAsync(default), "Target replacement after restoration or foreground request cannot complete");
            check(fake.Observations == replaceAt && fake.Restores == 1 && fake.ForegroundCalls <= 1,
                "Late target substitution never causes retargeting or repeated effects");
        }
        {
            var fake = new Fake();
            fake.Read = (_, view) => view! with { Target = null! };
            await Fails<InvalidOperationException>(Policy(fake).RunAsync(default), "Malformed observation cannot become target identity");
            check(fake.Restores == 0 && fake.ForegroundCalls == 0, "Missing target identity authorizes no effect");
        }

        // Exercise cancellation and ownership/input guard failure at every native
        // boundary in a complete restore + foreground path, not just entry checks.
        var completed = new Fake { Minimized = true, Visible = false, IsForeground = false };
        await Policy(completed).RunAsync(default);
        for (int stopAt = 1; stopAt <= completed.Events.Count; stopAt++) {
            using var stop = new CancellationTokenSource();
            var fake = new Fake { Minimized = true, Visible = false, IsForeground = false };
            int boundary = stopAt;
            fake.OnEvent = index => { if (index == boundary) stop.Cancel(); };
            await Fails<OperationCanceledException>(Policy(fake).RunAsync(stop.Token), "Cancellation is honored at lifecycle boundary " + boundary);
            check(fake.Events.Count == boundary && fake.Restores <= 1 && fake.ForegroundCalls <= 1,
                "Cancellation cannot start another backend boundary or repeat an effect");
        }
        int guards = completed.Events.Count(value => value == "guard");
        for (int failAt = 1; failAt <= guards; failAt++) {
            var fake = new Fake { Minimized = true, Visible = false, IsForeground = false };
            int boundary = failAt;
            fake.OnGuard = index => { if (index == boundary) throw new InvalidOperationException("Input or ownership changed."); };
            await Fails<InvalidOperationException>(Policy(fake).RunAsync(default), "Input/ownership guard failure stops boundary " + boundary);
            check(fake.Events.Last() == "guard" && fake.Guards == boundary && fake.Restores <= 1 && fake.ForegroundCalls <= 1,
                "No backend operation follows a failed guard");
        }

        {
            using var stop = new CancellationTokenSource();
            var release = new TaskCompletionSource<CalculatorWindowView?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var fake = new Fake { ObserveOverride = (_, _) => release.Task };
            var policy = Policy(fake);
            var pending = policy.RunAsync(stop.Token);
            stop.Cancel();
            check(!pending.IsCompleted, "Cancellation does not abandon an observation that ignores its token");
            await Fails<InvalidOperationException>(policy.RunAsync(default), "An unsettled observation cannot acquire the same attempt again");
            release.SetResult(fake.View);
            await Fails<OperationCanceledException>(pending, "Late observation settles before cancellation propagates");
            check(fake.Restores == 0 && fake.ForegroundCalls == 0, "Late observed target cannot revive cancelled activation");
        }
        {
            using var stop = new CancellationTokenSource();
            using var release = new ManualResetEventSlim();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var fake = new Fake { Minimized = true, Visible = false, IsForeground = false };
            fake.OnRestore = () => { entered.SetResult(); release.Wait(); };
            var policy = Policy(fake);
            var pending = Task.Run(() => policy.RunAsync(stop.Token));
            try {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); stop.Cancel();
                check(!pending.IsCompleted, "A synchronous restore remains owned until it actually returns after Stop");
                await Fails<InvalidOperationException>(policy.RunAsync(default), "Blocked restoration cannot rearm the same attempt");
            } finally { release.Set(); }
            await Fails<OperationCanceledException>(pending, "Stop propagates after the held restore settles");
            check(fake.Restores == 1 && fake.ForegroundCalls == 0,
                "Cancellation during restoration never sends the subsequent foreground request");
        }
        {
            using var stop = new CancellationTokenSource();
            var fake = new Fake { MissingInitially = int.MaxValue };
            var pending = new CalculatorWindowActivation(fake).RunAsync(stop.Token);
            check(!pending.IsCompleted, "Default discovery delay is asynchronous");
            stop.Cancel();
            await Fails<OperationCanceledException>(pending, "Default 100ms discovery delay observes cancellation");
            check(fake.Restores == 0 && fake.ForegroundCalls == 0, "Cancelled default delay has no activation effect");
        }
        {
            using var stop = new CancellationTokenSource();
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var fake = new Fake { MissingInitially = int.MaxValue };
            var policy = new CalculatorWindowActivation(fake, _ => release.Task);
            var pending = policy.RunAsync(stop.Token); stop.Cancel();
            check(!pending.IsCompleted, "An injected pending delay remains owned until settlement");
            release.SetResult();
            await Fails<OperationCanceledException>(pending, "Post-delay cancellation check rejects a late ignored-token completion");
            check(fake.Observations == 1, "Late delay cannot start another discovery observation");
        }
        foreach (bool failed in new[] { false, true }) {
            var fake = new Fake { IsForeground = !failed, ForegroundAccepted = !failed };
            var policy = Policy(fake);
            if (failed) await Fails<InvalidOperationException>(policy.RunAsync(default), "Failed attempt completes with refusal");
            else check(await policy.RunAsync(default) == fake.Target, "Lifecycle returns only its immutable target for separate strict verification");
            int before = fake.Events.Count;
            await Fails<InvalidOperationException>(policy.RunAsync(default), "Completed or failed policy instance cannot run again");
            check(fake.Events.Count == before, "Repeated run is refused before touching the backend");
        }
        {
            using var stop = new CancellationTokenSource(); stop.Cancel();
            var fake = new Fake(); var policy = Policy(fake);
            await Fails<OperationCanceledException>(policy.RunAsync(stop.Token), "Pre-cancelled invocation reaches no backend");
            await Fails<InvalidOperationException>(policy.RunAsync(default), "A cancelled invocation cannot be repurposed into a fresh attempt");
            check(fake.Events.Count == 0, "Pre-cancel and reuse perform no native boundary");
        }
    }

    private sealed class Fake : ICalculatorWindowActivation
    {
        internal readonly List<string> Events = [];
        internal readonly List<CalculatorWindowTarget?> Expected = [];
        internal readonly CalculatorWindowTarget Target;
        internal int Observations, Restores, ForegroundCalls, Guards, Delays, MissingInitially;
        internal bool Minimized, Visible = true, IsForeground = true, RestoreChangesState = true,
            RestoreActivates, ForegroundAccepted = true, ForegroundChangesState = true;
        internal Action<int>? OnEvent, OnGuard;
        internal Action? OnRestore;
        internal Func<int, CalculatorWindowView?, CalculatorWindowView?>? Read;
        internal Func<CalculatorWindowTarget?, CancellationToken, Task<CalculatorWindowView?>>? ObserveOverride;
        internal CalculatorWindowView View => new(Target, Minimized, Visible, IsForeground);
        internal Fake(bool framed = true) {
            var root = new ComputerWindowIdentity(new(10), 20, 30, 40, "ApplicationFrameHost");
            var app = new ComputerWindowIdentity(new(50), 60, 70, 80, "CalculatorApp");
            Target = framed ? new(root, app, new(root, app, "fixed-package", "fixed-root", "CalculatorApp.exe", "fixed-aumid")) : new(app, app, null);
        }
        private void Event(string value) { Events.Add(value); OnEvent?.Invoke(Events.Count); }
        public Task<CalculatorWindowView?> ObserveAsync(CalculatorWindowTarget? expected, CancellationToken ct) {
            Observations++; Expected.Add(expected); Event("observe");
            if (ObserveOverride is not null) return ObserveOverride(expected, ct);
            CalculatorWindowView? result = Observations <= MissingInitially ? null : View;
            if (Read is not null) result = Read(Observations, result);
            return Task.FromResult(result);
        }
        public void Guard(CalculatorWindowTarget? target, CancellationToken ct) {
            Guards++; Event("guard"); OnGuard?.Invoke(Guards);
        }
        public void Restore(CalculatorWindowTarget target, CancellationToken ct) {
            Restores++; Event("restore"); OnRestore?.Invoke();
            if (RestoreChangesState) { Minimized = false; Visible = true; }
            if (RestoreActivates) IsForeground = true;
        }
        public bool Foreground(CalculatorWindowTarget target, CancellationToken ct) {
            ForegroundCalls++; Event("foreground");
            if (ForegroundAccepted && ForegroundChangesState) IsForeground = true;
            return ForegroundAccepted;
        }
        internal Task Delay(CancellationToken ct) { Delays++; Event("delay"); return Task.CompletedTask; }
    }
}
