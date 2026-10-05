using System.Collections;
using Buddy.Windows;

var checks = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new InvalidOperationException(description);
    checks++;
}
void Reject(Action action, string description)
{
    try { action(); }
    catch (ArgumentException) { checks++; return; }
    throw new InvalidOperationException(description);
}
void ReadOnly(Action action, string description)
{
    try { action(); }
    catch (NotSupportedException) { checks++; return; }
    throw new InvalidOperationException(description);
}

var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
var journal = new LocalTaskJournal(() => now);
Check(journal.Snapshot.Revision == 0 && journal.Snapshot.Tasks.Count == 0, "No invented initial activity.");
var token = journal.Begin("chat", "Chat reply", "Preparing a reply.");
var initial = journal.Snapshot;
var first = initial.Tasks.Single();
Check(token.Id != Guid.Empty && token.Source == "chat" && token.Generation > 0, "Token has all identity components.");
Check(first.Token == token && first.Source == "chat" && first.Title == "Chat reply" && first.Detail == "Preparing a reply.", "Explicit display labels retained.");
Check(first.Phase == LocalTaskPhase.Running && !first.IsTerminal && first.ObservedSteps.Count == 0, "Begin records running, without fabricated evidence.");
Check(first.StartedAt == now && first.UpdatedAt == now, "Initial timestamps follow injected clock.");
now = now.AddHours(3);
Check(journal.Snapshot.Revision == initial.Revision && journal.Snapshot.Tasks.Single().Phase == LocalTaskPhase.Running, "Elapsed time does not imply completion.");
Check(journal.Update(token, "Plan is ready for review.", LocalTaskPhase.WaitingForReview, true), "Explicit review event accepted.");
var review = journal.Snapshot;
Check(review.Tasks.Single().Phase == LocalTaskPhase.WaitingForReview && !review.Tasks.Single().IsTerminal && review.Tasks.Single().ObservedSteps.Count == 1, "Review is nonterminal and records explicit evidence.");
Check(!journal.Dismiss(token), "Active review cannot be dismissed.");
Check(initial.Tasks.Single() == first && first.ObservedSteps.Count == 0, "Earlier snapshots stay unchanged.");
ReadOnly(() => ((IList)review.Tasks).Clear(), "Snapshot task list must be immutable.");
ReadOnly(() => ((IList)review.Tasks.Single().ObservedSteps).Clear(), "Snapshot step list must be immutable.");
Check(journal.Update(token, "Plan is ready for review.", LocalTaskPhase.WaitingForReview, true) && journal.Snapshot.Revision == review.Revision, "Duplicate consecutive evidence does not mutate revision.");
Check(journal.Update(token, "Approved work started.", observedStep: true), "Explicit resume accepted.");
Check(journal.Snapshot.Tasks.Single().ObservedSteps.Count == 2, "Only explicit stages counted.");
Check(journal.Finish(token, LocalTaskPhase.Completed, "Reply delivered."), "Explicit completion accepted.");
var complete = journal.Snapshot;
Check(complete.Tasks.Single().IsTerminal && complete.Tasks.Single().Phase == LocalTaskPhase.Completed, "Explicit terminal status preserved.");
Check(!journal.Update(token, "Late work.") && !journal.Finish(token, LocalTaskPhase.Failed, "Late failure."), "Late terminal updates are rejected.");
Check(journal.Snapshot.Revision == complete.Revision && journal.Snapshot.Tasks.Single() == complete.Tasks.Single(), "Rejected late updates cannot rewrite completion.");
Check(journal.Dismiss(token) && journal.Snapshot.Tasks.Count == 0, "Terminal display can be dismissed.");
Check(!journal.Dismiss(token) && !journal.Update(token, "Late work."), "Dismissed token expires.");

var old = journal.Begin("refine", "Refine text");
Check(journal.Update(old, "Waiting for review.", LocalTaskPhase.WaitingForReview), "Review may be replaced by explicit new operation.");
var replacement = journal.Begin("refine", "Refine text");
var replaced = journal.Snapshot.Tasks.Single(t => t.Token == old);
Check(replacement.Id != old.Id && replacement.Generation > old.Generation, "Same source receives a new full identity.");
Check(replaced.Phase == LocalTaskPhase.Cancelled && replaced.Detail.Contains("Replaced by a newer request.") && replaced.Detail.Contains("earlier dispatched effect may still need review"), "Replacement does not imply previously dispatched effects were undone.");
Check(!journal.Update(old, "Late result.") && !journal.Finish(old, LocalTaskPhase.Completed, "Late completion."), "Replaced operation cannot overwrite new operation.");
foreach (var forged in new[] { replacement with { Id = Guid.NewGuid() }, replacement with { Source = "other" }, replacement with { Generation = replacement.Generation + 1 }, default })
    Check(!journal.Update(forged, "Forged update.") && !journal.Finish(forged, LocalTaskPhase.Completed, "Forged end.") && !journal.Dismiss(forged), "All token components must match.");
var revisionBeforeInvalid = journal.Snapshot.Revision;
foreach (var phase in new[] { LocalTaskPhase.Completed, LocalTaskPhase.Failed, LocalTaskPhase.Cancelled, (LocalTaskPhase)999 })
    Reject(() => journal.Update(replacement, "Invalid phase.", phase), "Update must reject terminal/undefined phases.");
foreach (var phase in new[] { LocalTaskPhase.Running, LocalTaskPhase.WaitingForReview, (LocalTaskPhase)999 })
    Reject(() => journal.Finish(replacement, phase, "Invalid finish."), "Finish must reject nonterminal/undefined phases.");
Check(journal.Snapshot.Revision == revisionBeforeInvalid, "Phase validation happens before mutation.");
foreach (var phase in new[] { LocalTaskPhase.Failed, LocalTaskPhase.Cancelled })
{
    var terminal = journal.Begin("terminal", "Task result");
    Check(journal.Finish(terminal, phase, "Explicit result.") && journal.Snapshot.Tasks.First().Phase == phase, "Explicit failure/cancel result preserved.");
}

revisionBeforeInvalid = journal.Snapshot.Revision;
foreach (var source in new[] { "", "Home", "has space", "https://example.test", "C:\\private", "../path", "line\nbreak", new string('a', 33) })
    Reject(() => journal.Begin(source, "Title"), "Source identifiers must not carry paths or payload text.");
foreach (var title in new[] { "", "   ", new string('a', 81), "line\nbreak", "nul\0" })
    Reject(() => journal.Begin("valid", title), "Invalid title must be rejected.");
foreach (var detail in new[] { new string('a', 241), "line\nbreak", "tab\t", "nul\0" })
    Reject(() => journal.Update(replacement, detail), "Invalid detail must be rejected.");
Reject(() => journal.Update(replacement, "   ", observedStep: true), "Empty evidence cannot be added.");
Reject(() => journal.Finish(replacement, LocalTaskPhase.Completed, "", observedStep: true), "Empty finish evidence cannot be added.");
Reject(() => journal.Begin(null!, "Title"), "Null source must be rejected.");
Reject(() => journal.Begin("valid", null!), "Null title must be rejected.");
Reject(() => journal.Update(replacement, null!), "Null detail must be rejected.");
Check(journal.Snapshot.Revision == revisionBeforeInvalid, "Invalid display data cannot mutate state.");

var bounded = new LocalTaskJournal(() => now);
var stages = bounded.Begin("stages", "Observed work");
for (var i = 0; i < 20; i++) Check(bounded.Update(stages, $"Observed stage {i}.", observedStep: true), "Observed stage accepted.");
var beforeFinalStep = bounded.Snapshot.Tasks.Single().ObservedSteps;
Check(beforeFinalStep.Count == 8 && beforeFinalStep[0].Detail == "Observed stage 12." && beforeFinalStep[^1].Detail == "Observed stage 19.", "Only eight newest explicit stages retained.");
Check(bounded.Finish(stages, LocalTaskPhase.Completed, "Final observed result.", true), "Final observed result recorded.");
Check(bounded.Snapshot.Tasks.Single().ObservedSteps.Count == 8 && beforeFinalStep[0].Detail == "Observed stage 12.", "Step eviction cannot change earlier snapshot.");
var active = new List<LocalTaskToken>();
for (var i = 0; i < 8; i++) active.Add(bounded.Begin($"source-{i}", "Task"));
Check(bounded.Snapshot.Tasks.Count == 8 && bounded.Snapshot.Tasks.All(t => !t.IsTerminal) && bounded.Snapshot.Tasks.All(t => t.Token != stages), "Oldest terminal is evicted before any active record.");
var ninth = bounded.Begin("source-nine", "Task");
Check(bounded.Snapshot.Tasks.Count == 8 && bounded.Snapshot.Tasks.First().Token == ninth && bounded.Snapshot.Tasks.All(t => !t.IsTerminal), "Capacity expiry does not invent active outcomes.");
Check(!bounded.Update(active[0], "Late event.") && !bounded.Finish(active[0], LocalTaskPhase.Completed, "Late finish."), "Forgotten display identity cannot return.");
var beganAt = bounded.Snapshot.Tasks.First().StartedAt;
now = now.AddDays(-1);
Check(bounded.Update(ninth, "Clock moved backwards.") && bounded.Snapshot.Tasks.First().UpdatedAt >= beganAt && bounded.Snapshot.Tasks.First().StartedAt == beganAt, "Clock rollback cannot regress event timestamps.");

var concurrent = new LocalTaskJournal();
var competing = concurrent.Begin("concurrent", "Concurrent result");
var wins = await Task.WhenAll(Enumerable.Range(0, 24).Select(i => Task.Run(() => concurrent.Finish(competing, i % 2 == 0 ? LocalTaskPhase.Completed : LocalTaskPhase.Failed, "Explicit result."))));
Check(wins.Count(won => won) == 1, "Exactly one concurrent terminal event wins.");
var terminalRevision = concurrent.Snapshot.Revision;
var late = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => Task.Run(() => concurrent.Update(competing, "Late stage.", observedStep: true))));
Check(late.All(accepted => !accepted) && concurrent.Snapshot.Revision == terminalRevision, "Concurrent late events cannot revive terminal work.");
var generations = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => concurrent.Begin("shared", "Shared source"))));
var latest = concurrent.Snapshot;
Check(generations.Select(t => t.Id).Distinct().Count() == 32 && generations.Select(t => t.Generation).Distinct().Count() == 32, "Concurrent Begin returns unique full identities.");
Check(latest.Tasks.Count == 8 && latest.Tasks.Count(t => !t.IsTerminal) == 1 && latest.Tasks.First().Token.Generation == generations.Max(t => t.Generation), "Concurrent replacement serializes to one current operation.");
Check(generations.Where(t => t.Generation != generations.Max(x => x.Generation)).All(t => !concurrent.Update(t, "Late old work.")), "Every older generation remains stale.");

Console.WriteLine($"LOCAL TASK JOURNAL PURE CHECKS PASSED: {checks}; memory-only, no file/network/native/model operations");
