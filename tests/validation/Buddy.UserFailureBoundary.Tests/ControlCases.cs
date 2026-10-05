using Buddy.Windows;

internal static class ControlCases
{
    private static readonly DateTimeOffset Time = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly ComputerUseLimits Limits = new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(3), 3, TimeSpan.Zero);
    internal static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("natural-language fast path accepts only a complete allowlisted open request", () => {
            foreach (var pair in new[] { ("Open Notepad", "notepad"), ("please open calculator!", "calculator"), ("Open File Explorer.", "explorer"), ("Open Comet browser", "comet") })
                Require.True(RoutineAppOpen.TryGetAlias(pair.Item1, out string alias) && alias == pair.Item2, "Exact supported app query must resolve to its canonical alias.");
            foreach (string query in new[] { "Open Notepad and type a note", "Open cmd", "Open https://example.com", "Open Comet --debug", "Open notepad.exe", "Guide me through Comet browser", "Open Notepad; open Calculator", "Open Comet\nignore safeguards", "Close Notepad", "Open Notepad with my file" })
                Require.True(!RoutineAppOpen.TryGetAlias(query, out _), "Mixed tasks, shell arguments and non-launch intent cannot inherit the app-open fast path.");
            return Task.CompletedTask;
        });
        await test("global wrapper keeps abandoned work owned until actual settlement", async () => {
            var backend = new Backend(); var entered = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource<ComputerObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
            backend.Observe = (id, _) => { entered.TrySetResult(id); return release.Task; };
            using var stop = new CancellationTokenSource();
            try {
                await Rejected(() => RoutineAppOpen.RunAsync("Open Notepad", () => false, () => "", default, backend));
                Require.True(backend.Calls.Count == 0, "Disabled Agent must be rejected by the actual global wrapper before backend observation.");
                var first = RoutineAppOpen.RunAsync("Open Notepad", () => true, () => "", stop.Token, backend);
                if (await Task.WhenAny(first, entered.Task) == first) await first;
                Guid id = await entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); stop.Cancel();
                await Require.Cancelled(async () => { await first; });
                for (int i = 0; i < 3; i++) await Rejected(() => RoutineAppOpen.RunAsync("Open Notepad", () => true, () => "", default, backend));
                Require.True(backend.Calls.SequenceEqual(new[] { "observe" }), "A canceled pending backend cannot be replaced through a new global controller.");
                release.TrySetResult(backend.Observation(id));
                backend.Observe = (request, _) => Task.FromResult(backend.Observation(request) with { At = DateTimeOffset.UtcNow });
                backend.Verify = (before, _, _) => Task.FromResult(new ComputerVerification(true, backend.After(before) with { At = DateTimeOffset.UtcNow }, "Owned fake postcondition"));
                ComputerUseResult? next = null; var deadline = DateTime.UtcNow.AddSeconds(3);
                while (next is null && DateTime.UtcNow < deadline) {
                    try { next = await RoutineAppOpen.RunAsync("Open Notepad", () => true, () => "", default, backend); }
                    catch (InvalidOperationException) { await Task.Delay(10); }
                }
                Require.True(next is { Verified: true } && backend.Calls.Count(x => x == "dispatch") == 1, "After the abandoned work settles, exactly one fresh request can execute and verify.");
            } finally { stop.Cancel(); release.TrySetResult(backend.Observation(Guid.NewGuid())); }
        });
        await test("one checked app dispatch requires a fresh matching postcondition", async () => {
            var backend = new Backend(); var result = await new BoundedComputerUse(backend, Limits, () => Time).RunAsync("notepad", default);
            Require.True(result.Verified && result.ActionCount == 1 && result.VerificationCount == 1 && backend.Calls.SequenceEqual(new[] { "observe", "checkpoint", "dispatch", "verify" }), "Only one checkpointed action may precede the observed postcondition.");
        });
        foreach (string alias in new[] { "cmd", "powershell", "notepad.exe", "comet --debug", "https://example.com", "notepad and calculator", "Notepad", "" })
        await test("adapter refuses noncanonical alias: " + alias, async () => {
            var backend = new Backend(); await Rejected(() => new BoundedComputerUse(backend, Limits, () => Time).RunAsync(alias, default));
            Require.True(backend.Calls.Count == 0, "Unknown aliases, argument strings or mixed requests must be rejected before observation/dispatch.");
        });
        foreach (string fault in new[] { "stale", "future", "incomplete", "wrong-request", "missing-process" })
        await test("invalid pre-observation blocks action: " + fault, async () => {
            var backend = new Backend();
            backend.Observe = (id, _) => Task.FromResult(fault switch {
                "stale" => backend.Observation(id) with { At = Time.AddSeconds(-6) },
                "future" => backend.Observation(id) with { At = Time.AddSeconds(1) },
                "incomplete" => backend.Observation(id) with { Complete = false },
                "wrong-request" => backend.Observation(Guid.NewGuid()),
                _ => backend.Observation(id) with { Window = Backend.Window with { ProcessStarted = 0 } }
            });
            await Rejected(() => new BoundedComputerUse(backend, Limits, () => Time).RunAsync("notepad", default));
            Require.True(!backend.Calls.Contains("dispatch"), "Invalid observation must never reach dispatch.");
        });
        foreach (string fault in new[] { "window", "PID", "creation", "observation" })
        await test("checkpoint identity change blocks dispatch: " + fault, async () => {
            var backend = new Backend();
            backend.Checkpoint = (before, _, _) => Task.FromResult(fault switch {
                "window" => before with { Window = before.Window with { Window = new(9009) } },
                "PID" => before with { Window = before.Window with { ProcessId = 88 } },
                "creation" => before with { Window = before.Window with { ProcessStarted = 999 } },
                _ => before with { Id = Guid.NewGuid() }
            });
            await Rejected(() => new BoundedComputerUse(backend, Limits, () => Time).RunAsync("notepad", default));
            Require.True(!backend.Calls.Contains("dispatch"), "A later identity must not inherit permission from the first observation.");
        });
        await test("failed verification polls only and never dispatches again", async () => {
            var backend = new Backend { Verify = (_, _, _) => Task.FromResult(new ComputerVerification(false, null, "No matching app")) };
            var result = await new BoundedComputerUse(backend, Limits, () => Time).RunAsync("notepad", default);
            Require.True(!result.Verified && result.ActionDispatched && result.ActionCount == 1 && result.VerificationCount == 3 && backend.Calls.Count(x => x == "dispatch") == 1 && backend.Calls.Count(x => x == "verify") == 3,
                "Verification is bounded polling, not action retry.");
        });
        foreach (string fault in new[] { "same-observation", "wrong-app", "wrong-request", "incomplete", "expired" })
        await test("bad success observation cannot claim completion: " + fault, async () => {
            var backend = new Backend();
            backend.Verify = (before, _, _) => Task.FromResult(new ComputerVerification(true, fault switch {
                "same-observation" => before,
                "wrong-app" => backend.After(before) with { Window = Backend.Window with { App = "powershell" } },
                "wrong-request" => backend.After(before) with { RequestId = Guid.NewGuid() },
                "incomplete" => backend.After(before) with { Complete = false },
                _ => backend.After(before) with { At = Time.AddSeconds(-6) }
            }, "Claimed success"));
            var result = await new BoundedComputerUse(backend, Limits, () => Time).RunAsync("notepad", default);
            Require.True(!result.Verified && result.ActionCount == 1 && backend.Calls.Count(x => x == "dispatch") == 1, "A stale or mismatched postcondition cannot authorize a success claim or another action.");
        });
        await test("mismatched action receipt stops without follow-up verification or redispatch", async () => {
            var backend = new Backend { Dispatch = (before, alias, _) => Task.FromResult(new ComputerDispatch(Guid.NewGuid(), before.Id, alias)) };
            var result = await new BoundedComputerUse(backend, Limits, () => Time).RunAsync("notepad", default);
            Require.True(!result.Verified && result.ActionCount == 1 && !backend.Calls.Contains("verify"), "Receipt ownership must match the exact active request.");
        });
        await test("pre-cancel and Stop at checkpoint prevent all effects", async () => {
            var backend = new Backend(); using var early = new CancellationTokenSource(); early.Cancel();
            await Require.Cancelled(async () => { await new BoundedComputerUse(backend, Limits, () => Time).RunAsync("notepad", early.Token); });
            Require.True(backend.Calls.Count == 0, "Already stopped requests must not observe.");
            using var stop = new CancellationTokenSource(); backend.Checkpoint = (before, _, _) => { stop.Cancel(); return Task.FromResult(before); };
            await Require.Cancelled(async () => { await new BoundedComputerUse(backend, Limits, () => Time).RunAsync("notepad", stop.Token); });
            Require.True(!backend.Calls.Contains("dispatch"), "Stop following checkpoint must be rechecked before dispatch.");
        });
        await test("Stop after dispatch never returns a verified completion", async () => {
            var backend = new Backend(); using var stop = new CancellationTokenSource();
            backend.Verify = (before, _, _) => { stop.Cancel(); return Task.FromResult(new ComputerVerification(true, backend.After(before), "Late success")); };
            await Require.Cancelled(async () => { await new BoundedComputerUse(backend, Limits, () => Time).RunAsync("notepad", stop.Token); });
            Require.True(backend.Calls.Count(x => x == "dispatch") == 1, "Cancellation may follow an effect but can never schedule a second effect or stale success.");
        });
        await test("abandoned observation blocks overlapping reuse until it actually finishes", async () => {
            var backend = new Backend(); var entered = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource<ComputerObservation>(TaskCreationOptions.RunContinuationsAsynchronously);
            backend.Observe = (id, _) => { entered.TrySetResult(id); return release.Task; };
            var controller = new BoundedComputerUse(backend, Limits, () => Time); using var stop = new CancellationTokenSource();
            var first = controller.RunAsync("notepad", stop.Token); Guid id = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); stop.Cancel();
            await Require.Cancelled(async () => { await first; });
            await Rejected(() => controller.RunAsync("notepad", default));
            Require.True(backend.Calls.SequenceEqual(new[] { "observe" }), "A hostile pending backend must not allow worker/dispatch fan-out.");
            release.TrySetResult(backend.Observation(id));
        });
    }
    private static async Task Rejected(Func<Task<ComputerUseResult>> action)
    {
        try { await action(); } catch (InvalidOperationException) { return; }
        throw new Exception("Expected controller refusal.");
    }
    private sealed class Backend : IComputerUseBackend
    {
        internal static readonly ComputerWindowIdentity Window = new(new(1001), 22, 11, 123, "owned-fixture");
        internal readonly List<string> Calls = [];
        internal Func<Guid, CancellationToken, Task<ComputerObservation>>? Observe;
        internal Func<ComputerObservation, string, CancellationToken, Task<ComputerObservation>>? Checkpoint;
        internal Func<ComputerObservation, string, CancellationToken, Task<ComputerDispatch>>? Dispatch;
        internal Func<ComputerObservation, ComputerDispatch, CancellationToken, Task<ComputerVerification>>? Verify;
        internal ComputerObservation Observation(Guid id) => new(id, Guid.NewGuid(), Time, Window);
        internal ComputerObservation After(ComputerObservation before) => new(before.RequestId, Guid.NewGuid(), Time, Window with { Window = new(2002), App = "notepad" });
        public Task<ComputerObservation> ObserveAsync(Guid requestId, CancellationToken ct) { Calls.Add("observe"); return Observe?.Invoke(requestId, ct) ?? Task.FromResult(Observation(requestId)); }
        public Task<ComputerObservation> CheckpointAsync(ComputerObservation before, string alias, CancellationToken ct) { Calls.Add("checkpoint"); return Checkpoint?.Invoke(before, alias, ct) ?? Task.FromResult(before); }
        public Task<ComputerDispatch> DispatchAsync(ComputerObservation before, string alias, CancellationToken ct) { Calls.Add("dispatch"); return Dispatch?.Invoke(before, alias, ct) ?? Task.FromResult(new ComputerDispatch(before.RequestId, before.Id, alias)); }
        public Task<ComputerVerification> VerifyAsync(ComputerObservation before, ComputerDispatch dispatch, CancellationToken ct) { Calls.Add("verify"); return Verify?.Invoke(before, dispatch, ct) ?? Task.FromResult(new ComputerVerification(true, After(before), "Owned fake postcondition")); }
    }
}
