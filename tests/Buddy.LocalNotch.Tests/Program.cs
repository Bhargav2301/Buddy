using Buddy.Windows;

int count = 0;
void Check(bool pass, string message) { if (!pass) throw new Exception("FAIL: " + message); count++; Console.WriteLine("PASS: " + message); }
var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
var journal = new LocalTaskJournal(() => now);
Guid? Focus(Guid? selected) { var snapshot = journal.Snapshot; return CompanionPresentation.FocusTask(snapshot.Tasks.Select(t => t.Token.Id).ToArray(), selected, snapshot.Tasks.FirstOrDefault(t => !t.IsTerminal)?.Token.Id); }
Check(Focus(null) is null, "Empty journal produces no fake task selection");
var running = journal.Begin("talk", "Owned conversation", "Preparing answer");
Check(Focus(null) == running.Id, "Actual running operation is selectable");
now = now.AddSeconds(2); var finished = journal.Begin("region", "Owned selected area", "Reading selection");
journal.Finish(finished, LocalTaskPhase.Completed, "Result ready");
Check(Focus(null) == running.Id, "Initial focus prefers an actual active operation over newer terminal history");
Check(Focus(finished.Id) == finished.Id, "Explicit terminal-card selection remains stable beside active work");
now = now.AddSeconds(2); var review = journal.Begin("refine", "Owned refinement", "Preparing proposal");
journal.Update(review, "Review the proposed edit", LocalTaskPhase.WaitingForReview, observedStep: true);
Check(Focus(finished.Id) == finished.Id, "New review event does not steal an existing selected card");
var frozen = journal.Snapshot; var item = frozen.Tasks.Single(t => t.Token == review);
Check(item.Phase == LocalTaskPhase.WaitingForReview && !item.IsTerminal && item.UpdatedAt == now, "Review status and exact update timestamp come from recorded operation");
Check(item.ObservedSteps.Count == 1 && item.ObservedSteps[0].At == now && item.ObservedSteps[0].Detail == "Review the proposed edit", "Only explicitly observed stage details populate the card history");
now = now.AddDays(2);
Check(journal.Snapshot.Revision == frozen.Revision && journal.Snapshot.Tasks.Single(t => t.Token == review).Phase == LocalTaskPhase.WaitingForReview,
    "Elapsed time cannot fabricate completion or a recent update");
Check(!journal.Dismiss(review) && !journal.Dismiss(running), "Running and review operations cannot be dismissed as finished");
Check(journal.Dismiss(finished) && Focus(finished.Id) == review.Id, "Explicit terminal dismissal selects remaining active record without replay");
foreach (var phase in new[] { LocalTaskPhase.Completed, LocalTaskPhase.Failed, LocalTaskPhase.Cancelled }) {
    var task = journal.Begin("test-" + phase.ToString().ToLowerInvariant(), phase.ToString(), "Started"); journal.Finish(task, phase, "Observed terminal result");
    Check(journal.Snapshot.Tasks.Single(t => t.Token == task).Phase == phase, "Terminal state remains exact: " + phase);
}
Check(CompanionPresentation.FocusTask([], Guid.NewGuid(), Guid.NewGuid()) is null, "Missing identities cannot produce a phantom card");
var first = Guid.NewGuid(); var second = Guid.NewGuid();
Check(CompanionPresentation.FocusTask([first, second], Guid.NewGuid(), second) == second, "A removed selection falls back to an existing active identity");
Check(CompanionPresentation.FocusTask([first, second], Guid.NewGuid(), Guid.NewGuid()) == first, "Unknown active identity falls back only to an actual record");
Check(CompanionPresentation.TaskSource("refine") == "Buddy-draft refinement" && CompanionPresentation.TaskSource("field") == "Source-field refinement", "Source labels distinguish Buddy drafts from guarded external fields");
Check(CompanionPresentation.TaskSource("custom-local-source") == "custom-local-source", "Unknown source remains explicit rather than pretending a connected app");
Check(CompanionPresentation.TaskSource("notch") == "Notch chat", "Embedded local chat source has its own truthful label");
Check(CompanionPresentation.Visibility("Expanded", false, true, false) == IslandVisibility.Visible, "Default fullscreen policy does not dismiss the task card");
Check(CompanionPresentation.Visibility("Expanded", true, false, false) == IslandVisibility.OtherInstance, "Task cards retain companion ownership suppression");
Console.WriteLine($"ALL {count} LOCAL NOTCH PURE CHECKS PASSED; actual journal/presentation policy, no WPF/native/model/profile/transport");
