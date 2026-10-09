using Buddy.Server;
using System.Net;
using System.Text;

const string key = "fixture-not-a-key";
int checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; }
async Task Reject(Func<Task> fn, string name)
{
    try { await fn(); } catch (Exception ex) when (ex is InvalidOperationException or OperationCanceledException)
    { Check(!ex.ToString().Contains(key), name + " no secret in error"); return; }
    throw new Exception(name);
}
var selection = new ProviderConsent("openai", "fixture-model", true, true);
foreach (var bad in new[] { selection with { TextSharingAccepted = false }, selection with { PossibleChargesAccepted = false }, selection with { Provider = "unknown" }, selection with { Model = "bad?model" } })
{
    var handler = new Handler((_, _) => Task.FromResult(Reply()));
    await Reject(() => { using var session = ProviderRoutingSession.CreateForTesting(bad, key, handler); return Task.CompletedTask; }, "invalid setup");
    Check(handler.Calls == 0, "setup sends nothing"); handler.Dispose();
}
var sent = new List<string>();
var wire = new Handler(async (r, ct) => { sent.Add(await r.Content!.ReadAsStringAsync(ct)); return Reply(); });
using var configured = ProviderRoutingSession.CreateForTesting(selection, key, wire);
Check(configured.Status.Configured && configured.Status.CompletedResponses == 0 && wire.Calls == 0, "configured does not claim live acceptance");
await Reject(() => configured.CompleteAsync("openai", "fixture-model", "Question."), "unreviewed route refuses");
var first = configured.PrepareQuestion("First question."); var current = configured.PrepareQuestion("Current question.");
await Reject(() => configured.CompleteReviewedAsync(first), "superseded review rejected");
await Reject(() => configured.CompleteReviewedAsync(current with { Model = "different" }), "selection cannot change after review");
await Reject(() => configured.CompleteReviewedAsync(current with { Text = "Changed content." }), "payload cannot change after review");
Check(await configured.CompleteReviewedAsync(current) == "A complete answer.", "reviewed answer completes");
Check(sent.Count == 1 && sent[0].Contains("Current question.") && !sent[0].Contains("First question."), "only reviewed current question sent");
await Reject(() => configured.CompleteReviewedAsync(current), "review one use");
Check(configured.Status.CompletedResponses == 1, "completed response status");
Check(!current.ToString().Contains("Current question."), "review ToString redacts payload");
await Reject(() => { configured.PrepareQuestion("Bad \ud800 text."); return Task.CompletedTask; }, "malformed UTF16 rejected before review");
Check(configured.PrepareQuestion("A valid boat 🚢.").Text == "A valid boat 🚢.", "valid emoji remains exact");
var now = DateTimeOffset.UtcNow;
using var expired = ProviderRoutingSession.CreateForTesting(selection, key, new Handler((_, _) => Task.FromResult(Reply())), () => now);
var review = expired.PrepareQuestion("Expires."); now = now.AddMinutes(2);
await Reject(() => expired.CompleteReviewedAsync(review), "expiry exact boundary");
using var reversed = ProviderRoutingSession.CreateForTesting(selection, key, new Handler((_, _) => Task.FromResult(Reply())), () => now);
var reversedReview = reversed.PrepareQuestion("Clock test."); now = now.AddSeconds(-1);
await Reject(() => reversed.CompleteReviewedAsync(reversedReview), "clock reversal invalidates review");
long tick = 0;
using var monotonic = ProviderRoutingSession.CreateForTesting(selection, key, new Handler((_, _) => Task.FromResult(Reply())), () => now, () => tick);
var timedReview = monotonic.PrepareQuestion("Clock does not advance."); tick = System.Diagnostics.Stopwatch.Frequency * 120;
await Reject(() => monotonic.CompleteReviewedAsync(timedReview), "monotonic lifetime expires despite stationary wall clock");
var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var late = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
var hostile = new Handler((_, _) => { entered.SetResult(); return late.Task; });
using var disconnected = ProviderRoutingSession.CreateForTesting(selection, key, hostile);
var pending = disconnected.CompleteReviewedAsync(disconnected.PrepareQuestion("Wait.")); await entered.Task;
disconnected.Disconnect(); late.SetResult(Reply());
await Reject(() => pending, "disconnect discards late handler response");
Check(!disconnected.Status.Configured && disconnected.Status.CompletedResponses == 0, "disconnect never marks response success");
using var canceled = new CancellationTokenSource(); canceled.Cancel();
await Reject(() => configured.CompleteReviewedAsync(configured.PrepareQuestion("Cancelled."), canceled.Token), "caller cancellation before send");
configured.Disconnect(); await Reject(() => { configured.PrepareQuestion("No."); return Task.CompletedTask; }, "disconnected review refusal");
Check(ConnectorSetup.Statuses.All(s => !s.CanGrant && !s.CanRead), "connector setup remains truthful and inactive");
Console.WriteLine($"PASS {checks} provider routing assertions; injected handlers only, no network, real credentials, windows or audio.");
static HttpResponseMessage Reply() => new(HttpStatusCode.OK) { Content = new StringContent("{\"choices\":[{\"finish_reason\":\"stop\",\"message\":{\"role\":\"assistant\",\"content\":\"A complete answer.\"}}]}", Encoding.UTF8, "application/json") };
sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> run) : HttpMessageHandler
{
    public int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Calls++; return run(request, cancellationToken); }
}
