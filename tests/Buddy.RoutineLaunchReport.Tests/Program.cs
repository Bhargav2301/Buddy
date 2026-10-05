using Buddy.Windows;
using System.ComponentModel;

int checks = 0;
void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; }
foreach (var alias in new[] { "calculator", "notepad", "explorer", "comet", "camera", "spotify" }) {
    string title = RoutineLaunchReport.Title(alias);
    Check(title.StartsWith("Open ") && title.Length <= LocalTaskJournal.MaxTitleLength && !title.Contains("requested app"), "Supported launch retains display target.");
}
try { RoutineLaunchReport.Title("calculator\nprivate query"); throw new Exception("Arbitrary target was accepted."); }
catch (ArgumentException) { checks++; }
foreach (var (message, code) in new[] {
    ("Windows app publisher verification failed.", "PUBLISHER_UNVERIFIED"),
    ("The foreground window changed before launch; make a fresh request.", "FOREGROUND_CHANGED"),
    ("The requested app registration is not a healthy signed package; nothing was launched.", "PACKAGE_UNAVAILABLE"),
    ("The requested app has no unique verified launch registration.", "REGISTRATION_UNAVAILABLE"),
    ("The signed package has no unique supported main application executable.", "MAIN_EXECUTABLE_UNAVAILABLE"),
    ("The exact signed app registration is no longer available; nothing was launched.", "REGISTRATION_CHANGED"),
    ("This app is in your privacy blocklist; no automatic launch or inspection is available.", "PRIVACY_BLOCKED"),
    ("Enable Agent in Settings before requesting an app launch.", "AGENT_DISABLED"),
    ("A verified Calculator installation was not found. No other app or website was opened.", "APP_NOT_FOUND")
}) {
    var report = RoutineLaunchReport.Failure("calculator", new InvalidOperationException(message));
    Check(report.Code == code && report.Reason == message, "Actual fixed host reason and code survive reporting.");
    Check(report.Detail.Length <= LocalTaskJournal.MaxDetailLength && !report.Detail.Any(char.IsControl), "Report fits the bounded journal.");
}
foreach (var privateMessage in new[] { "C:\\Private\\document.txt", "https://secret.invalid/?token=abc", "my private request", "token=abc\npassword=secret", new string('x', 20000), "Windows app publisher verification failed. private suffix" }) {
    var error = new InvalidOperationException(privateMessage);
    var report = RoutineLaunchReport.Failure("calculator", error);
    Check(report.Code == "APP_ERROR_80131509" && !report.Detail.Contains(privateMessage), "Unknown exception data is never copied; real HRESULT retained.");
    Check(report.Detail.Length <= 240 && !report.Detail.Any(char.IsControl), "Hostile reason stays bounded and single-line.");
}
var native = RoutineLaunchReport.Failure("calculator", new Win32Exception(5, "C:\\Private\\secret"));
Check(native.Code == "WINDOWS_ERROR_5" && !native.Detail.Contains("Private"), "Win32 code retained without system path.");
Check(RoutineLaunchReport.Failure("calculator", new UnauthorizedAccessException("secret")).Code == "ACCESS_DENIED", "Access denial remains actionable without private text.");
var timedOut = RoutineLaunchReport.Failure("calculator", new TimeoutException("secret"));
Check(timedOut.Code == "LAUNCH_TIMEOUT" && timedOut.Reason.Contains("may have opened"), "Timeout does not claim no side effect.");
var cancelled = RoutineLaunchReport.Failure("calculator", new OperationCanceledException("secret"));
Check(cancelled.Code == "LAUNCH_STOPPED" && cancelled.Reason.Contains("cannot be undone"), "Stop does not imply reversal.");

var requestId = Guid.NewGuid(); var beforeId = Guid.NewGuid();
var after = new ComputerObservation(requestId, Guid.NewGuid(), DateTimeOffset.UtcNow, new(new(1), 2, 3, 4, "CalculatorApp"));
var success = new ComputerUseResult(true, true, "private model text", requestId, beforeId, after, 1, 1);
Check(RoutineLaunchReport.Result("calculator", success).Code == "APP_VERIFIED", "Verified native result is distinct from dispatch.");
Check(!RoutineLaunchReport.Result("calculator", success).Detail.Contains("private model"), "Success wording comes from verified alias, not untrusted text.");
Check(RoutineLaunchReport.Result("calculator", success with { After = null }).Code != "APP_VERIFIED", "Missing after observation cannot be reported as verified.");
Check(RoutineLaunchReport.Result("calculator", success with { ActionDispatched = false }).Code != "APP_VERIFIED", "No-dispatch result cannot be reported as verified.");
var uncertainty = success with { Verified = false, After = null, Message = "The launch was attempted but its result is uncertain; inspect the app before trying again." };
Check(RoutineLaunchReport.Result("calculator", uncertainty).Code == "LAUNCH_UNCERTAIN", "Actual uncertain dispatch reason survives.");
Check(RoutineLaunchReport.Result("calculator", uncertainty with { Message = "private exception" }).Code == "LAUNCH_UNVERIFIED", "Unknown result cannot manufacture a failure reason or success.");

var journal = new LocalTaskJournal();
var old = journal.Begin("routine", RoutineLaunchReport.Title("calculator"), "Checking the exact supported app request.");
Check(journal.Snapshot.Tasks.Single().ObservedSteps.Count == 0, "Begin does not invent intermediate stages.");
var failure = RoutineLaunchReport.Failure("calculator", new InvalidOperationException("Windows app publisher verification failed."));
Check(journal.Finish(old, LocalTaskPhase.Failed, failure.Detail, true), "Observed actual failure can finish the bound task.");
var next = journal.Begin("routine", RoutineLaunchReport.Title("spotify"));
var displayed = new List<LocalTaskRecord>();
Check(RoutineLaunchReport.OpenSource(journal, old, displayed.Add), "Historical routine source remains reviewable after a newer operation.");
Check(displayed.Single().Token == old && displayed[0].Title == "Open Calculator" && displayed[0].Detail == failure.Detail, "Source opens the selected result, not latest task or query.");
Check(displayed[0].ObservedSteps.Count == 1 && displayed[0].ObservedSteps[0].Detail == failure.Detail, "Only the observed failure stage is recorded.");
Check(journal.Snapshot.Tasks.First().Token == next && journal.Snapshot.Tasks.First().Phase == LocalTaskPhase.Running, "Review callback does not replay or mutate operations.");
Check(!journal.Finish(old, LocalTaskPhase.Completed, "Late success") && !journal.Update(old, "Late stage"), "Late events cannot replace recorded failure.");
Check(!RoutineLaunchReport.OpenSource(journal, old with { Generation = old.Generation + 1 }, displayed.Add), "Forged or stale generation cannot open another result.");
var other = journal.Begin("home", "Home chat");
Check(!RoutineLaunchReport.OpenSource(journal, other, displayed.Add), "Routine review cannot capture another source.");
Check(journal.Dismiss(old) && !RoutineLaunchReport.OpenSource(journal, old, displayed.Add), "Dismissed result cannot reopen.");
Check(displayed.Count == 1, "Rejected navigation never calls the view callback.");
Console.WriteLine($"ROUTINE LAUNCH REPORT PURE CHECKS PASSED: {checks}; no native/app/model/profile operations");
