using Buddy.Server;
using Buddy.Windows;

int checks = 0;
void Check(bool condition, string label) { if (!condition) throw new Exception(label); checks++; }
var open = new AssistantAction("open", Value: "calculator");
var receipt = new ActionResult(1, open, true, "Verified exact Calculator frame and child.");
foreach (var query in new[] { "Open Calculator", "Please open calculator", "Open Calculator app." })
    Check(FramedLaunchCompletion.Decide(query, [open], receipt, 1, 0).Completed, "Exact open-only task completes after verified framed launch.");
foreach (var query in new[] { "Open Calculator and add 1 + 2", "Open Calculator, then open Comet", "Calculate 42", "Open the Calculator app.", "Open Notepad", "Open https://example.com/" })
    Check(!FramedLaunchCompletion.Decide(query, [open], receipt, 1, 0).Completed, "Broader or different task cannot inherit launch completion.");
Check(!FramedLaunchCompletion.Decide("Open Calculator", [open], receipt with { Success = false }, 1, 0).Completed, "Failed receipt stays review-needed.");
Check(!FramedLaunchCompletion.Decide("Open Calculator", [open], receipt, 1, 1).Completed, "Queued work is not reported complete.");
Check(!FramedLaunchCompletion.Decide("Open Calculator", [open, new("wait")], receipt, 1, 0).Completed, "A truncated mixed plan is not reported complete.");
Check(!FramedLaunchCompletion.Decide("Open Calculator", [open], receipt with { Sequence = 2 }, 1, 0).Completed, "Reused later receipt cannot complete an open-only task.");
Check(!FramedLaunchCompletion.Decide("Open Calculator", [open], receipt, 2, 0).Completed, "Earlier actions prevent open-only completion.");
Check(!FramedLaunchCompletion.Decide("Open Calculator", [new("open", Value: "notepad")], receipt, 1, 0).Completed, "Unmatched approved action cannot complete.");
Check(!FramedLaunchCompletion.Decide("Open Calculator", [open], receipt with { Action = new("keys", Value: "calculator") }, 1, 0).Completed, "Only the exact app-open action can complete.");
foreach (var change in new[] { "stop", "task", "operation", "none" }) {
    using var stop = new CancellationTokenSource();
    var audit = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    bool sameTask = true, sameOperation = true, published = false;
    var ledger = new JobLedger();
    string id = ledger.Begin("action", "Open Calculator", "calculator");
    ledger.Move(id, JobState.AwaitingApproval, "Review exact launch"); ledger.Approve(id);
    ledger.Receipt(id, receipt.Observation, true);
    var settling = FramedLaunchCompletion.AfterAudit(() => audit.Task, () => sameTask && sameOperation, () => {
        published = true; ledger.Move(id, JobState.Verifying, "Verified frame"); ledger.Move(id, JobState.Completed, "Opened");
    }, stop.Token);
    Check(!settling.IsCompleted && !published, "Audit suspension cannot publish completion.");
    if (change == "stop") stop.Cancel();
    if (change == "task") sameTask = false;
    if (change == "operation") sameOperation = false;
    audit.SetResult();
    try { await settling; Check(change == "none", "Only the original live task may complete."); }
    catch (OperationCanceledException) { Check(change != "none", "Stop or changed authority cancels late completion."); }
    Check(published == (change == "none"), "Late audit completion respects task/cancellation ownership.");
    Check(ledger.Snapshot.Single().Receipts.Count == 1 && ledger.Snapshot.Single().Receipts[0].Success, "Verified historical launch receipt survives cancellation or task replacement.");
    Check((ledger.Snapshot.Single().State == JobState.Completed) == (change == "none"), "Actual job transitions remain truthful.");
}
Console.WriteLine($"FRAMED LAUNCH COMPLETION CHECKS PASSED: {checks}");
