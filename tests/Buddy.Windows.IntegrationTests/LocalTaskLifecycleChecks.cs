using Buddy.Server;
using Buddy.Windows;
using Microsoft.AspNetCore.DataProtection;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

// Root-only owned WPF fixture. The accepted review is seeded deliberately: this
// tests real Apply/Undo/Stop ownership, not model acceptance or external editing.
internal static class LocalTaskLifecycleChecks
{
    private const string Original = "Canned original Buddy draft.";
    private const string Proposed = "Canned reviewed Buddy draft.";
    private static FieldInfo Field(string name) => typeof(RefineWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new MissingFieldException(name);
    private static Task Call(RefineWindow window, string method) => (Task)typeof(RefineWindow).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
    private static void Set(RefineWindow window, string name, object? value) => Field(name).SetValue(window, value);
    private static T Get<T>(RefineWindow window, string name) => (T)Field(name).GetValue(window)!;
    internal static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        BuddyTheme.Apply("Black", true);
        var field = new TextBox { Text = Original, Margin = new(20) };
        var owner = new Window { Title = "Buddy owned task lifecycle fixture", Width = 480, Height = 220, Content = field };
        string folder = Path.Combine(Path.GetTempPath(), "Buddy-task-lifecycle50-" + Guid.NewGuid().ToString("N"));
        int checks = 0, exit = 1;
        var opened = new List<RefineWindow>();
        void Check(bool pass, string text) { if (!pass) throw new InvalidOperationException("FAIL: " + text); checks++; Console.WriteLine("PASS: " + text); }
        owner.Loaded += async (_, _) => {
            try {
                using var forbidden = new RefuseTransport();
                using var http = new HttpClient(forbidden) { BaseAddress = new("http://127.0.0.1:11434/") };
                var service = new BuddyService(new StateStore(folder, new EphemeralDataProtectionProvider()), new(http)) { WebEnabled = false, AgentEnabled = false };
                (RefineWindow Window, LocalTaskToken Token) Review(LocalTaskJournal journal, Func<string, CancellationToken, Task> apply, Func<CancellationToken, Task> undo)
                {
                    field.Text = Original;
                    var window = new RefineWindow(service, Original, apply, undo, "Owned Buddy draft only", localTasks: journal) { Owner = owner };
                    opened.Add(window); window.Show();
                    var token = journal.Begin("refine", "Buddy-draft refinement", "Preparing owned test proposal.");
                    journal.Update(token, "Proposal ready; original unchanged.", LocalTaskPhase.WaitingForReview);
                    Set(window, "displayedTask", token);
                    Set(window, "result", new RefinementResult(Proposed, "mock-review", "quick", "zero-shot", true, 1, null, null, [], [], "Seeded accepted review; no model called."));
                    Set(window, "acceptedGeneration", window.RequestGeneration);
                    Set(window, "frozenRequestFingerprint", RefinementDraftOptions.Fingerprint(window.Options.Snapshot().ToRequest(Original, "quick")));
                    Check(window.CanApply, "Seeded accepted owned review enables actual guarded Apply");
                    return (window, token);
                }

                var journal = new LocalTaskJournal();
                var applyEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var applyRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var undoEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var undoRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                CancellationToken applyingToken = default, undoingToken = default;
                int writes = 0;
                var (review, token) = Review(journal,
                    async (text, ct) => { applyingToken = ct; applyEntered.TrySetResult(); await applyRelease.Task; field.Text = text; writes++; },
                    async ct => { undoingToken = ct; undoEntered.TrySetResult(); await undoRelease.Task; field.Text = Original; writes++; });
                var applyTask = Call(review, "Apply"); await applyEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
                review.Cancel();
                Check(applyingToken.IsCancellationRequested && !journal.Snapshot.Tasks.Single(t => t.Token == token).IsTerminal, "Stop requests cancellation but cannot assert outcome while approved Apply is unsettled");
                Check(!Get<TextBlock>(review, "progress").Text.Contains("Original unchanged", StringComparison.OrdinalIgnoreCase), "Unsettled Apply does not display a false unchanged claim");
                applyRelease.TrySetResult(); await applyTask.WaitAsync(TimeSpan.FromSeconds(2));
                Check(field.Text == Proposed && writes == 1 && journal.Snapshot.Tasks.Single(t => t.Token == token).Phase == LocalTaskPhase.Completed, "Late completed owned write records the actual applied result after Stop");
                Check(Get<Button>(review, "undo").IsEnabled && !review.CanApply, "Completed late write keeps Undo and refuses duplicate Apply");

                var undoTask = Call(review, "Undo"); await undoEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
                var undoToken = journal.Snapshot.Tasks[0].Token;
                review.Cancel();
                Check(undoToken != token && undoingToken.IsCancellationRequested && !journal.Snapshot.Tasks.Single(t => t.Token == undoToken).IsTerminal, "Undo has a distinct owner and remains unsettled through Stop");
                undoRelease.TrySetResult(); await undoTask.WaitAsync(TimeSpan.FromSeconds(2));
                Check(field.Text == Original && writes == 2 && journal.Snapshot.Tasks.Single(t => t.Token == undoToken).Phase == LocalTaskPhase.Completed, "Late completed Undo records exact restoration without rewriting prior Apply history");
                Check(journal.Snapshot.Tasks.Single(t => t.Token == token).Phase == LocalTaskPhase.Completed && !review.CanApply, "Undo preserves historical Apply receipt and requires another reviewed proposal");
                review.Close();
                Check(!review.OpenTaskSource(undoToken), "Closed source cannot be reacquired from a journal card");

                var staleJournal = new LocalTaskJournal(); int staleWrites = 0;
                var (stale, staleToken) = Review(staleJournal, (_, _) => { staleWrites++; return Task.CompletedTask; }, _ => Task.CompletedTask);
                stale.Cancel(); await Call(stale, "Apply");
                Check(staleWrites == 0 && field.Text == Original && staleJournal.Snapshot.Tasks.Single().Phase == LocalTaskPhase.Cancelled, "Forced Apply after Stop cannot approve or write the old proposal");
                Check(!stale.OpenTaskSource(staleToken with { Generation = staleToken.Generation + 1 }), "Source opening checks the complete task token");
                stale.Close();

                var failureJournal = new LocalTaskJournal();
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var (failed, failedToken) = Review(failureJournal, async (_, ct) => { entered.TrySetResult(); await released.Task; throw new OperationCanceledException(ct); }, _ => Task.CompletedTask);
                var failedApply = Call(failed, "Apply"); await entered.Task.WaitAsync(TimeSpan.FromSeconds(2)); failed.Cancel(); released.TrySetResult(); await failedApply.WaitAsync(TimeSpan.FromSeconds(2));
                var receipt = failureJournal.Snapshot.Tasks.Single(t => t.Token == failedToken);
                Check(receipt.Phase == LocalTaskPhase.Cancelled && field.Text == Original && !Get<Button>(failed, "undo").IsEnabled, "An uncompleted cancelled write never gains a successful Apply or Undo receipt");
                Check(!receipt.Detail.Contains("applied to", StringComparison.OrdinalIgnoreCase) && !receipt.Detail.Contains("original unchanged", StringComparison.OrdinalIgnoreCase), "Uncertain cancelled callback does not claim a write or prove absence of one");
                failed.Close();
                var retryJournal = new LocalTaskJournal(); int retryAttempts = 0;
                var (retry, failedAttempt) = Review(retryJournal, (text, _) => {
                    if (++retryAttempts == 1) throw new InvalidOperationException("Owned first attempt failed before writing.");
                    field.Text = text; return Task.CompletedTask;
                }, _ => Task.CompletedTask);
                await Call(retry, "Apply");
                Check(retry.CanApply && retryJournal.Snapshot.Tasks.Single(t => t.Token == failedAttempt).Phase == LocalTaskPhase.Failed && field.Text == Original,
                    "Failed Apply remains a failed historical attempt with an explicit retry available");
                await Call(retry, "Apply");
                var succeededAttempt = retryJournal.Snapshot.Tasks[0];
                Check(retryAttempts == 2 && field.Text == Proposed && succeededAttempt.Token != failedAttempt && succeededAttempt.Phase == LocalTaskPhase.Completed,
                    "Successful retry gets a fresh task token and reports the actual completed write");
                Check(retryJournal.Snapshot.Tasks.Single(t => t.Token == failedAttempt).Phase == LocalTaskPhase.Failed && Get<Button>(retry, "undo").IsEnabled,
                    "Retry preserves failed history while keeping Undo for the successful write");
                retry.Close();
                Check(forbidden.Calls == 0, "Lifecycle fixture made zero model, web or other HTTP requests");
                Console.WriteLine($"PASS: {checks} owned task lifecycle checks; seeded review, no model, external fields or app actions."); exit = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally {
                foreach (var window in opened) try { window.Close(); } catch { }
                // StateStore is never read/written in this fixture; remove only its
                // explicitly owned temporary directory if a future constructor creates it.
                var full = Path.GetFullPath(folder); var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("Buddy-task-lifecycle50-", StringComparison.Ordinal) && Directory.Exists(full)) Directory.Delete(full, true);
                owner.Close(); app.Shutdown(exit);
            }
        };
        owner.Show(); app.Run(); return exit;
    }
    private sealed class RefuseTransport : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Calls++; throw new InvalidOperationException("No transport allowed in owned lifecycle test."); }
    }
}
