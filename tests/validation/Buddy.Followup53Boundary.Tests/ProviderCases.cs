#if HAS_PROVIDER53
using Buddy.Server;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

internal static class ProviderCases
{
    private const string Key = "canned-qa-fixture-key";
    private static readonly ProviderConsent Consent = new("openai", "fixture-model", true, true);
    private static HttpResponseMessage Reply() => new(HttpStatusCode.OK) {
        Content = new StringContent("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"A complete canned answer.\"}}]}", Encoding.UTF8, "application/json")
    };
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> act) : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { Calls++; return act(request, token); }
    }
    internal static async Task Run(Checks c)
    {
        var now = DateTimeOffset.Parse("2030-01-01T00:00:00Z"); long ticks = 1000;
        int sends = 0; string? posted = null; string? endpoint = null;
        Handler Good() => new(async (r, ct) => { sends++; endpoint = r.RequestUri?.AbsoluteUri; posted = await r.Content!.ReadAsStringAsync(ct); return Reply(); });
        c.Rejects<InvalidOperationException>(() => new ProviderConsent("openai", "fixture-model").Validate(), "PROVIDER-consent", "Absent text-sharing or charges consent cannot configure routing.");
        using var session = ProviderRoutingSession.CreateForTesting(Consent, Key, Good(), () => now, () => ticks);
        c.Check(sends == 0 && session.Status.Configured && session.Status.CompletedResponses == 0, "PROVIDER-configure-only", "Configuration performs no dispatch and is not successful live acceptance.");
        await c.RejectsAsync<InvalidOperationException>(() => session.CompleteAsync("openai", "fixture-model", "Unreviewed"), "PROVIDER-unreviewed", "Generic completion cannot bypass explicit text review.");
        c.Rejects<InvalidOperationException>(() => session.PrepareQuestion("Question\ud800"), "PROVIDER-invalid-unicode", "Malformed Unicode cannot be silently replaced after review.");
        const string exact = "Explain `x <= 2` and 🌈, keeping the newline.\nNo actions.";
        var old = session.PrepareQuestion("First question."); var review = session.PrepareQuestion(exact);
        await c.RejectsAsync<InvalidOperationException>(() => session.CompleteReviewedAsync(old), "PROVIDER-replaced-review", "A newer review invalidates its predecessor.");
        await c.RejectsAsync<InvalidOperationException>(() => session.CompleteReviewedAsync(review with { Model = "different-model" }), "PROVIDER-model-bound", "The review cannot be rebound to a different selected model.");
        c.Check(sends == 0, "PROVIDER-refusals-no-transport", "Unreviewed, malformed and replaced inputs make zero transport calls.");
        string answer = await session.CompleteReviewedAsync(review);
        using (var body = JsonDocument.Parse(posted!))
            c.Check(body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString() == exact && endpoint == "https://api.openai.com/v1/chat/completions", "PROVIDER-exact-payload", "Only the exact reviewed Unicode/code text reaches the fixed endpoint.");
        c.Check(answer == "A complete canned answer." && session.Status.CompletedResponses == 1, "PROVIDER-complete", "A complete mocked response changes session status exactly once.");
        await c.RejectsAsync<InvalidOperationException>(() => session.CompleteReviewedAsync(review), "PROVIDER-once", "Successful approval is consumed once.");
        var cancelReview = session.PrepareQuestion("Cancelled review."); session.CancelReview();
        await c.RejectsAsync<InvalidOperationException>(() => session.CompleteReviewedAsync(cancelReview), "PROVIDER-cancel-review", "Cancel invalidates a pending text approval.");
        var expired = session.PrepareQuestion("Expired review."); ticks += 121L * Stopwatch.Frequency;
        await c.RejectsAsync<InvalidOperationException>(() => session.CompleteReviewedAsync(expired), "PROVIDER-stationary-clock", "Monotonic elapsed time expires authority while UTC stays stationary.");
        var backward = session.PrepareQuestion("Backward clock."); now -= TimeSpan.FromSeconds(1);
        await c.RejectsAsync<InvalidOperationException>(() => session.CompleteReviewedAsync(backward), "PROVIDER-clock-rollback", "Backward UTC invalidates review rather than extending approval.");
        now += TimeSpan.FromSeconds(1);
        c.Check(!review.ToString().Contains(exact, StringComparison.Ordinal) && !session.Status.ToString().Contains(Key, StringComparison.Ordinal), "PROVIDER-redacted-metadata", "Review/status display does not expose payload or key.");
        session.Disconnect();
        c.Rejects<InvalidOperationException>(() => session.PrepareQuestion("After disconnect."), "PROVIDER-disconnect", "A disconnected session cannot prepare another request.");

        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var delayed = ProviderRoutingSession.CreateForTesting(Consent, Key, new Handler((_, _) => { entered.SetResult(); return pending.Task; }));
        var run = delayed.CompleteReviewedAsync(delayed.PrepareQuestion("Delayed reply.")); await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        delayed.Disconnect();
        await c.RejectsAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(2)), "PROVIDER-stop-late", "Disconnect returns cancellation even if the injected handler ignores it.");
        pending.SetResult(Reply()); await Task.Yield();
        c.Check(delayed.Status.CompletedResponses == 0 && !delayed.Status.RequestInFlight, "PROVIDER-no-late-success", "An abandoned response cannot become a completed provider result.");
        using var broken = ProviderRoutingSession.CreateForTesting(Consent, Key, new Handler((_, _) => throw new HttpRequestException(Key + " private canned text")));
        try { await broken.CompleteReviewedAsync(broken.PrepareQuestion("Failure.")); c.Check(false, "PROVIDER-error-redaction", "Transport details must be sanitized."); }
        catch (InvalidOperationException e) { c.Check(!e.ToString().Contains(Key, StringComparison.Ordinal) && !e.ToString().Contains("private canned text", StringComparison.Ordinal), "PROVIDER-error-redaction", "Transport details must be sanitized."); }

        Broker(c);
    }
    private static void Broker(Checks c)
    {
        var now = DateTimeOffset.Parse("2030-01-01T00:00:00Z");
        var broker = new LocalAgentBroker(() => now);
        c.Rejects<InvalidOperationException>(() => broker.Pair("Canned peer", false), "AGENT-default-off", "Pairing needs explicit local status/questions consent.");
        var lease = broker.Pair("Canned peer", true);
        c.Check(!lease.ToString().Contains(lease.Token, StringComparison.Ordinal), "AGENT-redacted-token", "Display never renders the possession token.");
        LocalAgentEvent Event(long seq, string kind, string task = "task1", string text = "Canned status") => new(lease.SessionId, seq, kind, task, text);
        c.Rejects<InvalidOperationException>(() => broker.Receive(new string('0', 64), Event(1, "started")), "AGENT-wrong-token", "A token from outside the paired session cannot send status.");
        broker.Receive(lease.Token, Event(1, "started"));
        c.Rejects<InvalidOperationException>(() => broker.Receive(lease.Token, Event(1, "started")), "AGENT-replay", "Sequence replay refuses without moving the accepted sequence.");
        c.Rejects<InvalidOperationException>(() => broker.Receive(lease.Token, Event(3, "status")), "AGENT-gap", "Skipped sequence numbers are refused.");
        c.Rejects<InvalidOperationException>(() => broker.Receive(lease.Token, Event(2, "status", "other-task")), "AGENT-wrong-task", "A current token does not authorize events for a different task.");
        const string request = "request1", question = "Which canned option?";
        string digest = LocalAgentBroker.RequestDigest(lease.SessionId, "task1", request, "question", question);
        broker.Receive(lease.Token, new(lease.SessionId, 2, "question", "task1", question, request, digest));
        c.Rejects<InvalidOperationException>(() => broker.Decide(lease.SessionId, "other-task", request, digest, "answer", "A"), "AGENT-answer-task", "An answer is bound to exact current task identity.");
        c.Rejects<InvalidOperationException>(() => broker.Decide(lease.SessionId, "task1", request, digest, "allow"), "AGENT-no-execution-approval", "This channel has no allow or command-execution decision.");
        broker.Decide(lease.SessionId, "task1", request, digest, "answer", "A");
        c.Rejects<InvalidOperationException>(() => broker.TakeDecision(lease.SessionId, lease.Token, request, "different-digest"), "AGENT-answer-digest", "Answer retrieval is bound to exact question digest.");
        c.Check(broker.TakeDecision(lease.SessionId, lease.Token, request, digest)?.Text == "A" && broker.TakeDecision(lease.SessionId, lease.Token, request, digest) is null, "AGENT-answer-once", "An exact answer can be retrieved once only.");
        broker.Receive(lease.Token, Event(3, "completed"));
        c.Rejects<InvalidOperationException>(() => broker.Receive(lease.Token, Event(4, "status")), "AGENT-terminal", "Late status cannot reopen a terminal task.");
        broker.Receive(lease.Token, Event(4, "started", "task2"));
        c.Rejects<InvalidOperationException>(() => broker.Receive(lease.Token, Event(5, "completed")), "AGENT-old-terminal", "Old task completion cannot complete its replacement.");
        digest = LocalAgentBroker.RequestDigest(lease.SessionId, "task2", "request2", "approval", "Canned proposed command");
        broker.Receive(lease.Token, new(lease.SessionId, 5, "approval", "task2", "Canned proposed command", "request2", digest));
        broker.Decide(lease.SessionId, "task2", "request2", digest, "deny"); now += TimeSpan.FromMinutes(2);
        c.Rejects<InvalidOperationException>(() => broker.TakeDecision(lease.SessionId, lease.Token, "request2", digest), "AGENT-queued-expiry", "An unread decision keeps the original request expiry.");
        c.Check(!broker.Snapshot.Single().Connected, "AGENT-expired-disconnected", "Expired requests fail closed with no invented answer.");
        var rollback = broker.Pair("Other peer", true); now -= TimeSpan.FromSeconds(1);
        c.Rejects<InvalidOperationException>(() => broker.Receive(rollback.Token, new(rollback.SessionId, 1, "started", "task3", "Canned")), "AGENT-rollback", "Backward time closes authority.");
        now += TimeSpan.FromSeconds(1);
        c.Check(broker.Snapshot.All(s => !s.Connected), "AGENT-rollback-latched", "Restoring UTC does not resurrect invalidated sessions.");
        long ticks = 0; var monotonic = new LocalAgentBroker(() => now, () => ticks);
        var timed = monotonic.Pair("Stationary wall clock", true);
        monotonic.Receive(timed.Token, new(timed.SessionId, 1, "started", "timed-task", "Canned"));
        string timedDigest = LocalAgentBroker.RequestDigest(timed.SessionId, "timed-task", "timed-request", "question", "Canned question");
        monotonic.Receive(timed.Token, new(timed.SessionId, 2, "question", "timed-task", "Canned question", "timed-request", timedDigest));
        monotonic.Decide(timed.SessionId, "timed-task", "timed-request", timedDigest, "answer", "Canned answer");
        ticks = 120L * Stopwatch.Frequency;
        c.Rejects<InvalidOperationException>(() => monotonic.TakeDecision(timed.SessionId, timed.Token, "timed-request", timedDigest), "AGENT-monotonic-request", "Queued replies expire after two elapsed minutes even with stationary UTC.");
        timed = monotonic.Pair("Stationary session", true); ticks += 1800L * Stopwatch.Frequency;
        c.Rejects<InvalidOperationException>(() => monotonic.Receive(timed.Token, new(timed.SessionId, 1, "started", "new-task", "Canned")), "AGENT-monotonic-session", "Possession authority ends after thirty elapsed minutes even with stationary UTC.");
        var bounded = new LocalAgentBroker(); for (int i = 0; i < 8; i++) bounded.Pair("Peer " + i, true);
        c.Rejects<InvalidOperationException>(() => bounded.Pair("Ninth", true), "AGENT-bounded-sessions", "Untrusted events cannot grow the active session set beyond eight.");
    }
}
#endif
