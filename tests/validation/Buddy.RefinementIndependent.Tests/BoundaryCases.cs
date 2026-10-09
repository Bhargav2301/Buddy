using Buddy.Server;
using Buddy.Windows;
using System.Runtime.CompilerServices;
using System.Text;

internal static partial class Program
{
    private static readonly RefinementResult Ready = new("Please summarize this.", "injected", "quick", "zero-shot", true, 1, 80, 90, [], [], "Ready for review.");
    private static async Task RequestCases()
    {
        await Case("UI wrapper terminal contract and single use", async () => {
            using var request = new RefinementRequest();
            var result = await request.Run(_ => Events(new("delta", "unverified"), new("done", Result: Ready)));
            Check(result.State == RefinementRequestState.Completed && result.Result == Ready && !request.IsRunning, "verified terminal result missing");
            await Throws<InvalidOperationException>(() => request.Run(_ => Events(new RefinementEvent("done", Result: Ready))));
        });
        await Case("empty/delta-only/done-only streams never enable review", async () => {
            foreach (var events in new[] { Array.Empty<RefinementEvent>(), new[] { new RefinementEvent("delta", "preview") }, new[] { new RefinementEvent("done") } }) {
                using var request = new RefinementRequest(); var result = await request.Run(_ => Events(events));
                Check(result.Result is null && request.Result is null && !request.IsRunning && result.ErrorCode == "INCOMPLETE_REFINEMENT", "partial stream accepted");
            }
        });
        await Case("Stop at result callback cannot promote result", async () => {
            using var request = new RefinementRequest();
            var result = await request.Run(_ => Events(new RefinementEvent("done", Result: Ready)), _ => request.Cancel());
            Check(result.State == RefinementRequestState.Cancelled && result.Result is null && request.Result is null, "late result won over Stop");
        });
        await Case("throwing presentation callback cannot leave busy request", async () => {
            using var request = new RefinementRequest();
            var result = await request.Run(_ => Events(new RefinementEvent("done", Result: Ready)), _ => throw new InvalidOperationException("owned callback failure"));
            Check(result.State == RefinementRequestState.Failed && !request.IsRunning && request.Result is null, "throwing callback leaked owner/result");
        });
        await Case("ignored-cancel old UI stream cannot replace newer session", async () => {
            var held = new HeldEvents(); using var old = new RefinementRequest();
            try {
            var pending = old.Run(_ => held); await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2)); old.Cancel();
            var cancelled = await pending.WaitAsync(TimeSpan.FromSeconds(2));
            using var current = new RefinementRequest(); var result = await current.Run(_ => Events(new RefinementEvent("done", Result: Ready)));
            Check(cancelled.State == RefinementRequestState.Cancelled && !held.Disposed && result.Result == Ready, "old request incorrectly disposed concurrently or replacement failed");
            held.Release.TrySetResult(); await held.Settled.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Check(old.Result is null && current.Result == Ready && held.Disposed && !held.ConcurrentDispose, "late old output changed current session");
            } finally { held.Release.TrySetResult(); }
        });
        await Case("absolute UI timeout settles without late acceptance", async () => {
            var held = new HeldEvents(); using var request = new RefinementRequest(TimeSpan.FromMilliseconds(80));
            try {
            var pending = request.Run(_ => held); await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var result = await pending.WaitAsync(TimeSpan.FromSeconds(2));
            Check(result.State == RefinementRequestState.TimedOut && request.Result is null && !held.Disposed, "timeout missing or iterator disposed while pending");
            held.Release.TrySetResult(); await held.Settled.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Check(request.Result is null && request.State == RefinementRequestState.TimedOut, "late output rewrote timed-out state");
            } finally { held.Release.TrySetResult(); }
        });
        await Case("pre-cancel and unsolicited cancel distinguished", async () => {
            using var stop = new CancellationTokenSource(); stop.Cancel(); using var request = new RefinementRequest(token: stop.Token); bool called = false;
            var result = await request.Run(_ => { called = true; return Events(); });
            Check(!called && result.State == RefinementRequestState.Cancelled, "pre-cancel dispatched source");
            using var interrupted = new RefinementRequest(); var failure = await interrupted.Run(_ => Interrupted());
            Check(failure.State == RefinementRequestState.Failed && failure.ErrorCode == "REFINE_INTERRUPTED", "unsolicited engine interruption mislabeled user Stop");
        });
    }
    private static async IAsyncEnumerable<RefinementEvent> Events(params RefinementEvent[] events)
    { await Task.CompletedTask; foreach (var e in events) yield return e; }
    private static async IAsyncEnumerable<RefinementEvent> Interrupted()
    { await Task.Yield(); throw new OperationCanceledException("owned unrelated interruption");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }
    private sealed class HeldEvents : IAsyncEnumerable<RefinementEvent>, IAsyncEnumerator<RefinementEvent>
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously), Settled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Disposed, ConcurrentDispose; private bool moving;
        public RefinementEvent Current => new("done", Result: Ready);
        public IAsyncEnumerator<RefinementEvent> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;
        public async ValueTask<bool> MoveNextAsync() { moving = true; Entered.TrySetResult(); await Release.Task; moving = false; return true; }
        public ValueTask DisposeAsync() { ConcurrentDispose |= moving; Disposed = true; Settled.TrySetResult(); return ValueTask.CompletedTask; }
    }
    private static void FieldCases()
    {
        var now = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
        SyncCase("synthetic exact Apply and Undo", () => {
            var field = new MemoryField("old"); var edit = new GuardedEdit(field, "old"); edit.Apply("new", now, default);
            Check(field.Text == "new" && field.Writes == 1 && edit.CanUndo(now), "apply did not target exact original");
            edit.Undo(now.AddSeconds(1), default); Check(field.Text == "old" && field.Writes == 2 && !edit.CanUndo(now), "undo did not restore exact original");
        });
        foreach (var mutation in new[] { "identity", "text", "readonly", "destroyed", "pre-cancel" }) {
            SyncCase("synthetic stale Apply refuses " + mutation, () => {
                var field = new MemoryField("old"); var edit = new GuardedEdit(field, "old"); using var stop = new CancellationTokenSource();
                if (mutation == "identity") field.Id = "different";
                if (mutation == "text") field.Text = "user's new text";
                if (mutation == "readonly") field.Writable = false;
                if (mutation == "destroyed") field.Live = false;
                if (mutation == "pre-cancel") stop.Cancel();
                try { edit.Apply("new", now, stop.Token); throw new Exception("Apply should refuse"); } catch (InvalidOperationException) { } catch (OperationCanceledException) { }
                Check(field.Writes == 0 && !edit.CanUndo(now), "rejected Apply wrote or manufactured undo authority");
            });
        }
        foreach (var mutation in new[] { "identity", "text", "expired", "clock-backward", "readonly" }) {
            SyncCase("synthetic Undo refuses " + mutation, () => {
                var field = new MemoryField("old"); var edit = new GuardedEdit(field, "old"); edit.Apply("new", now, default);
                var at = now.AddSeconds(2); if (mutation == "identity") field.Id = "different";
                if (mutation == "text") field.Text = "user edited after Apply";
                if (mutation == "expired") at = now.AddSeconds(31);
                if (mutation == "clock-backward") at = now.AddTicks(-1);
                if (mutation == "readonly") field.Writable = false;
                try { edit.Undo(at, default); throw new Exception("Undo should refuse"); } catch (InvalidOperationException) { }
                Check(field.Writes == 1, "failed Undo overwrote changed destination");
            });
        }
        SyncCase("destination transformation cannot be reported applied", () => {
            var field = new MemoryField("old") { Transform = s => s.ToUpperInvariant() }; var edit = new GuardedEdit(field, "old");
            try { edit.Apply("new", now, default); throw new Exception("Transformed value should fail verification"); } catch (InvalidOperationException) { }
            Check(field.Text == "NEW" && field.Writes == 1 && !edit.CanUndo(now), "post-write mismatch falsely accepted; real write remains explicitly visible");
        });
        SyncCase("destination changes between read and write refused by adapter", () => {
            var field = new MemoryField("old") { BeforeWrite = f => f.Id = "replacement-field" }; var edit = new GuardedEdit(field, "old");
            try { edit.Apply("new", now, default); throw new Exception("Adapter identity recheck should refuse"); } catch (InvalidOperationException) { }
            Check(field.Writes == 0, "destination adapter did not preserve exact identity at write");
        });
    }
    private sealed class MemoryField(string original) : IVerifiedTextField
    {
        internal string Id = "original-field", Text = original; internal bool Live = true, Writable = true; internal int Writes;
        internal Func<string, string>? Transform; internal Action<MemoryField>? BeforeWrite;
        public string Identity => Live ? Id : throw new InvalidOperationException("destroyed owned adapter");
        public string Read() => Live ? Text : throw new InvalidOperationException("destroyed owned adapter");
        public void Write(string expected, string text, CancellationToken ct) {
            BeforeWrite?.Invoke(this); ct.ThrowIfCancellationRequested();
            if (!Live || !Writable || Id != "original-field" || Text != expected) throw new InvalidOperationException("owned adapter target/value changed");
            Writes++; Text = Transform?.Invoke(text) ?? text;
        }
    }
    private static void BudgetCases()
    {
        SyncCase("Unicode destination units count actual text", () => {
            const string text = "A🌙李e\u0301\n`x != 0`";
            Check(RefinementCore.Count(text, "utf16-code-units") == text.Length, "UTF16 count mismatch");
            Check(RefinementCore.Count(text, "unicode-scalars") == text.EnumerateRunes().Count(), "scalar count mismatch");
            Check(RefinementCore.Count(text, "utf8-bytes") == Encoding.UTF8.GetByteCount(text), "UTF8 count mismatch");
        });
        SyncCase("required overflow preserves intent and reports conflict", () => {
            var blocks = new[] { new RefinementBlock("intent", "Keep 🌙 and `x <= 2` exactly.", true), new RefinementBlock("optional", "Extra text") };
            var result = RefinementCore.ApplyBudget(blocks, new("owned fixture", 4, "utf8-bytes"));
            Check(!result.Fits && result.Text == "" && result.Conflict is not null, "required content clipped or accepted over budget");
            Check(blocks[0].Text == "Keep 🌙 and `x <= 2` exactly.", "caller data mutated");
        });
        SyncCase("optional omissions and exact final count agree", () => {
            const string text = "Keep 🌙."; var blocks = new[] { new RefinementBlock("intent", text, true), new RefinementBlock("reference", new string('x', 100)) };
            int limit = Encoding.UTF8.GetByteCount(text); var result = RefinementCore.ApplyBudget(blocks, new("owned fixture", limit, "utf8-bytes"));
            Check(result.Fits && result.Text == text && result.Count == limit && result.Removed.SequenceEqual(new[] { "reference" }), "optional omission metadata/count not exact");
        });
        SyncCase("preflight full assembled JSON counted rather than raw snippets", () => {
            var prepared = RefinementPreparation.Prepare(new(Scene, Inputs: new(ConfirmedConstraints: ["Use 🌙."])));
            Check(prepared.Ready && prepared.AssembledText.Contains("Use") && prepared.Budget.Count == prepared.AssembledText.Length, "preparation did not count its full assembled payload");
            var bounded = RefinementPreparation.Prepare(prepared.Request with { Budget = new("owned", prepared.AssembledText.Length - 1, "utf16-code-units") });
            Check(!bounded.Ready && !bounded.Budget.Fits, "required JSON/scaffolding overflow silently omitted");
        });
    }
}
