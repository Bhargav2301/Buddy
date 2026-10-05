using Buddy.Server;
using Buddy.Windows;
using Microsoft.AspNetCore.DataProtection;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;

internal static partial class Program
{
    private const string Draft = "Write a poem on a boat sailing in a sea on a lonely night";
    private static int checks;
    private static readonly RefinementResult Accepted = new("Verified changed proposal", "mock", "quick", "zero-shot", true, 1, null, null, [], [], "Ready for review.");
    private static async Task Main()
    {
        await RequestChecks(); await ServiceChecks(); await WatcherChecks();
        Console.WriteLine($"PASS: {checks} refinement lifecycle assertions. Injected streams/models only; no native calls, actual inference or installed profile.");
    }
    private static void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); checks++; }
    private static async Task Code(Task<RefinementResult> task, string code)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (BuddyException ex) { Check(ex.Code == code, "exact terminal code " + code); return; }
        throw new Exception("Expected " + code);
    }
    private static async Task Cancelled(Task<RefinementResult> task)
    { try { await task.WaitAsync(TimeSpan.FromSeconds(5)); } catch (OperationCanceledException) { checks++; return; } throw new Exception("Expected cancellation"); }
    private static async Task RequestChecks()
    {
        using (var request = new RefinementRequest()) {
            var statuses = new List<string>(); var outcome = await request.Run(_ => Events(new RefinementEvent("stage", "Checking intent"), new("done", Result: Accepted)), onProgress: statuses.Add);
            Check(outcome.State == RefinementRequestState.Completed && outcome.Result == Accepted && request.Result == Accepted && !request.IsRunning, "only verified result becomes completed");
            Check(statuses.Single().Contains("Checking intent") && statuses[0].Contains("elapsed"), "stage and elapsed time visible");
        }
        foreach (var stream in new[] { Events(), Events(new RefinementEvent("delta", "Unverified preview")), Events(new RefinementEvent("done")) }) {
            using var request = new RefinementRequest(); var outcome = await request.Run(_ => stream);
            Check(outcome.State == RefinementRequestState.Failed && outcome.ErrorCode == "INCOMPLETE_REFINEMENT" && outcome.Result is null && request.Result is null, "empty/truncated stream cannot leave a busy state or usable preview");
        }
        foreach (var failure in new Exception[] { new BuddyException("REFINE_BUSY", "Local AI busy; original unchanged."), new InvalidOperationException("synthetic transport failure"), new OperationCanceledException("unsolicited transport cancel") }) {
            using var request = new RefinementRequest(); var outcome = await request.Run(_ => Fault(failure));
            Check(outcome.State == RefinementRequestState.Failed && outcome.Result is null && !request.IsRunning, "transport failure is terminal without acceptance");
            Check(failure is not OperationCanceledException || outcome.ErrorCode == "REFINE_INTERRUPTED", "unrequested transport interruption is not mislabeled user Stop");
        }
        var held = new HeldStream(); using (var request = new RefinementRequest(TimeSpan.FromMilliseconds(90), heartbeat: TimeSpan.FromMilliseconds(15))) {
            var messages = new List<string>(); var watch = Stopwatch.StartNew(); var task = request.Run(_ => held, onProgress: messages.Add);
            await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2)); var outcome = await task.WaitAsync(TimeSpan.FromSeconds(2));
            Check(outcome.State == RefinementRequestState.TimedOut && outcome.Result is null && watch.Elapsed < TimeSpan.FromSeconds(2), "ignored cancellation returns bounded terminal timeout");
            Check(messages.Count > 0 && !held.Disposed, "heartbeat stays visible; no concurrent iterator disposal");
            held.Release.TrySetResult(); await held.Finished.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Check(request.Result is null && request.State == RefinementRequestState.TimedOut, "late accepted completion cannot overwrite timeout");
        }
        var late = new HeldStream(); var previous = new RefinementRequest(TimeSpan.FromSeconds(2));
        var previousTask = previous.Run(_ => late); await late.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2)); previous.Cancel();
        using (var current = new RefinementRequest()) {
            var currentOutcome = await current.Run(_ => Events(new RefinementEvent("done", Result: Accepted)));
            var previousOutcome = await previousTask.WaitAsync(TimeSpan.FromSeconds(2));
            Check(currentOutcome.Result == Accepted && previousOutcome.State == RefinementRequestState.Cancelled && previous.Result is null, "replacement keeps independent request identity and rejects old work");
            late.Release.TrySetResult(); await late.Finished.Task.WaitAsync(TimeSpan.FromSeconds(2)); previous.Dispose();
            Check(current.Result == Accepted && previous.Result is null, "late prior result cannot replace current result");
        }
        using (var cts = new CancellationTokenSource()) using (var request = new RefinementRequest(token: cts.Token)) {
            cts.Cancel(); bool called = false; var outcome = await request.Run(_ => { called = true; return Events(new RefinementEvent("done", Result: Accepted)); });
            Check(!called && outcome.State == RefinementRequestState.Cancelled, "pre-cancel never starts stream");
        }
        using (var request = new RefinementRequest()) {
            var outcome = await request.Run(_ => Events(new RefinementEvent("done", Result: Accepted)), _ => request.Cancel());
            Check(outcome.State == RefinementRequestState.Cancelled && outcome.Result is null, "close/Stop at result callback cannot accept stale completion");
        }
    }
    private static async Task ServiceChecks()
    {
        using (var f = new Fixture(new(TimeSpan.FromMilliseconds(80), TimeSpan.FromMilliseconds(220)))) {
            f.Model.Hold = true; var first = f.Service.RefineDetailed(new(Draft), default);
            await f.Model.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2)); await Code(first, "REFINE_TIMED_OUT");
            Check(f.Model.Plans == 1 && f.Model.Assessments == 0, "hung plan times out without weakening validation");
            await Code(f.Service.RefineDetailed(new(Draft), default), "REFINE_BUSY");
            Check(f.Model.Plans == 1, "timed-out but active transport retains inference slot");
            f.Model.Hold = false; f.Model.Release.TrySetResult(); await f.Model.Exited.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var next = await f.Service.RefineDetailed(new(Draft), default).WaitAsync(TimeSpan.FromSeconds(3));
            Check(next.Accepted && next.Method == "source-structure" && next.Structure is not null, "slot released only after old call finishes; next result uses all structure gates");
            Check(next.ScoreBefore is null && next.Changes.Count > 0 && next.RefinedPrompt.Contains("a boat sailing in a sea on a lonely night"), "source structure and score policy remain intact");
            Check(await f.Store.Read(s => s.Conversations.Count + s.Jobs.Count + s.Audit.Count) == 0, "timeout/retry stores no completion or conversation");
        }
        using (var f = new Fixture(new(TimeSpan.FromMilliseconds(70), TimeSpan.FromSeconds(2)))) {
            f.Model.Hold = true; using var cancel = new CancellationTokenSource();
            var first = f.Service.RefineDetailed(new(Draft), cancel.Token); await f.Model.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await Code(f.Service.RefineDetailed(new(Draft), default), "REFINE_BUSY");
            cancel.Cancel(); await Cancelled(first); Check(f.Model.Plans == 1, "queued timeout never overlaps inference");
            f.Model.Release.TrySetResult(); await f.Model.Exited.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        using (var f = new Fixture(new(TimeSpan.FromMilliseconds(70), TimeSpan.FromSeconds(2)))) {
            f.Model.Hold = true; var first = f.Service.RefineDetailed(new(Draft), default); await f.Model.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            f.Service.StopAll(); await Cancelled(first); Check(f.Model.Assessments == 0, "service Stop interrupts ignored transport wait promptly");
            f.Model.Release.TrySetResult(); await f.Model.Exited.Task.WaitAsync(TimeSpan.FromSeconds(2));
        }
        using (var f = new Fixture(new(TimeSpan.FromMilliseconds(80), TimeSpan.FromMilliseconds(180)))) {
            using var release = new ManualResetEventSlim(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var holdStore = Task.Run(() => f.Store.Update(s => { entered.TrySetResult(); release.Wait(); return true; }));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            try { await Code(f.Service.RefineDetailed(new(Draft), default), "REFINE_TIMED_OUT"); Check(f.Model.Plans == 0, "deadline includes previously unbounded state-store wait"); }
            finally { release.Set(); await holdStore.WaitAsync(TimeSpan.FromSeconds(2)); }
        }
        using (var f = new Fixture(RefinementRequestLimits.Default)) {
            using var cancel = new CancellationTokenSource(); cancel.Cancel(); await Cancelled(f.Service.RefineDetailed(new(Draft), cancel.Token));
            Check(f.Model.Plans == 0, "service pre-cancel avoids all inference");
            bool stopped = false, completed = false;
            try { await foreach (var item in f.Service.RefineStream(new(Draft), default)) { if (item.Text == "Preparing local refinement.") f.Service.StopAll(); if (item.Result is not null) completed = true; } }
            catch (OperationCanceledException) { stopped = true; }
            Check(stopped && !completed && f.Model.Plans == 0, "Stop registered before first status and settings wait");
        }
    }
    private static async IAsyncEnumerable<RefinementEvent> Events(params RefinementEvent[] events)
    { await Task.CompletedTask; foreach (var item in events) yield return item; }
    private static async IAsyncEnumerable<RefinementEvent> Fault(Exception failure)
    { await Task.Yield(); if (failure is not null) throw failure; yield break; }
    private sealed class HeldStream : IAsyncEnumerable<RefinementEvent>, IAsyncEnumerator<RefinementEvent>
    {
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously), Finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Disposed; private bool moving;
        public RefinementEvent Current => new("done", Result: Accepted);
        public IAsyncEnumerator<RefinementEvent> GetAsyncEnumerator(CancellationToken cancellationToken = default) => this;
        public async ValueTask<bool> MoveNextAsync() { moving = true; Entered.TrySetResult(); await Release.Task; moving = false; return true; }
        public ValueTask DisposeAsync() { if (moving) throw new Exception("Concurrent iterator disposal"); Disposed = true; Finished.TrySetResult(); return ValueTask.CompletedTask; }
    }
    private sealed class Fixture : IDisposable
    {
        private readonly string path = Path.Combine(Path.GetTempPath(), "Buddy.RefinementLifecycle." + Guid.NewGuid().ToString("N"));
        private readonly HttpClient client;
        internal readonly Model Model = new(); internal readonly StateStore Store; internal readonly BuddyService Service;
        internal Fixture(RefinementRequestLimits limits) { Directory.CreateDirectory(path); Store = new(path, new EphemeralDataProtectionProvider()); client = new(Model) { BaseAddress = new("http://127.0.0.1:11434/"), Timeout = Timeout.InfiniteTimeSpan }; Service = new(Store, new(client)) { RefinementLimits = limits }; }
        public void Dispose() { client.Dispose(); Directory.Delete(path, true); }
    }
    private sealed class Model : HttpMessageHandler
    {
        internal bool Hold; internal int Plans, Assessments;
        internal readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously), Exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); var body = document.RootElement;
            if (request.RequestUri!.AbsolutePath == "/api/embed") return Json(new { embeddings = body.GetProperty("input").EnumerateArray().Select(_ => new[] { 1f, 0f }) });
            if (body.GetProperty("format").GetProperty("properties").TryGetProperty("sections", out _)) {
                Plans++;
                if (Hold) { Entered.TrySetResult(); try { await Release.Task; } finally { Exited.TrySetResult(); } }
                return Json(new { message = new { content = "{\"sections\":[{\"kind\":\"task\",\"sourceIds\":[\"s0\"]},{\"kind\":\"subject\",\"sourceIds\":[\"s1\"]}]}" }, done = true });
            }
            Assessments++; return Json(new { message = new { content = "{\"preserved\":true,\"scoreBefore\":85,\"scoreAfter\":75,\"changes\":[\"Not authoritative\"]}" }, done = true });
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
    }
}
