using Buddy.Server;
using Buddy.Windows;
using System.Reflection;

internal static partial class Program
{
    private static void Field(LocalPromptWatcher watcher, string name, object value) => typeof(LocalPromptWatcher).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(watcher, value);
    private static Task Tick(LocalPromptWatcher watcher) => (Task)typeof(LocalPromptWatcher).GetMethod("Check", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(watcher, null)!;
    private static async Task WatcherChecks()
    {
        using var fixture = new Fixture(RefinementRequestLimits.Default);
        var a = new FocusedDraft(new("A supplied draft"), new(new(1), "A"));
        var b = new FocusedDraft(new("B supplied draft"), new(new(1), "B"));
        {
            var editor = new FocusedFieldEditor(); using var watcher = new LocalPromptWatcher(editor, () => fixture.Service, () => { });
            await watcher.ShowReview(a); var oldCard = InlinePromptWindow.Cards[^1];
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            oldCard.Current = _ => { entered.TrySetResult(); return release.Task; }; Field(watcher, "changedAt", Environment.TickCount64);
            var pending = Tick(watcher); await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await watcher.ShowReview(b); var current = InlinePromptWindow.Cards[^1]; release.TrySetResult(false); await pending.WaitAsync(TimeSpan.FromSeconds(2));
            Check(oldCard.ClosedState && !current.ClosedState, "old IsCurrent=false cannot close replacement review");
            oldCard.RepeatOldClosed(); Check(!current.ClosedState, "late old Closed handler cannot detach replacement review");
        }
        {
            var editor = new FocusedFieldEditor(); var pendingSubscription = new Subscription(); var currentSubscription = new Subscription();
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource<IDisposable?>(TaskCreationOptions.RunContinuationsAsynchronously);
            editor.WatchCall = (anchor, _, _) => { if (anchor.Identity == "A") { entered.TrySetResult(); return release.Task; } return Task.FromResult<IDisposable?>(currentSubscription); };
            using var watcher = new LocalPromptWatcher(editor, () => fixture.Service, () => { }); watcher.SetEnabled(true); Field(watcher, "cooldown", 0L);
            var pending = Tick(watcher); await entered.Task.WaitAsync(TimeSpan.FromSeconds(2)); await watcher.ShowReview(b); var current = InlinePromptWindow.Cards[^1];
            release.TrySetResult(pendingSubscription); await pending.WaitAsync(TimeSpan.FromSeconds(2));
            Check(pendingSubscription.Disposals == 1 && currentSubscription.Disposals == 0 && !current.ClosedState, "late polling Watch disposes only its stale subscription");
        }
        {
            var editor = new FocusedFieldEditor(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource<FieldAnchor?>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var watcher = new LocalPromptWatcher(editor, () => fixture.Service, () => { }); await watcher.ShowReview(a);
            editor.ProbeCall = (_, _) => { entered.TrySetResult(); return release.Task; };
            var pending = Tick(watcher); await entered.Task.WaitAsync(TimeSpan.FromSeconds(2)); await watcher.ShowReview(b); var current = InlinePromptWindow.Cards[^1];
            release.TrySetException(new InvalidOperationException("Synthetic old probe failure")); await pending.WaitAsync(TimeSpan.FromSeconds(2));
            Check(!current.ClosedState, "old Probe failure cannot suspend newer review");
        }
        {
            var editor = new FocusedFieldEditor(); var stale = new Subscription(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource<IDisposable?>(TaskCreationOptions.RunContinuationsAsynchronously);
            editor.WatchCall = (anchor, _, _) => { if (anchor.Identity == "A") { entered.TrySetResult(); return release.Task; } return Task.FromResult<IDisposable?>(new Subscription()); };
            using var watcher = new LocalPromptWatcher(editor, () => fixture.Service, () => { }); var pending = watcher.ShowReview(a); await entered.Task.WaitAsync(TimeSpan.FromSeconds(2)); await watcher.ShowReview(b); var current = InlinePromptWindow.Cards[^1];
            release.TrySetResult(stale); bool cancelled = false; try { await pending; } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && stale.Disposals == 1 && !current.ClosedState, "stale explicit Watch cannot publish over new subscription/card");
        }
    }
}
