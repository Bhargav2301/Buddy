#if HAS_NOTCH53
using Buddy.Windows;

internal static class NotchCases
{
    private static readonly NotchModelChoice[] Models = [new("local-a", "Local A"), new("local-b", "Local B"), new("missing", "Unavailable", false), new("cloud", "Cloud", true, Local: false)];
    private static TaskCompletionSource<T> Signal<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static async Task Run(Checks c)
    {
        var old = Signal<NotchChatReply>(); var sent = new List<NotchChatRequest>();
        using var session = new NotchChatSession(() => Models, (input, _) => { sent.Add(input); return sent.Count == 1 ? old.Task : Task.FromResult(new NotchChatReply("Canned current answer.")); });
        c.Check(!session.SelectModel("cloud") && !session.SelectModel("missing"), "NOTCH-model-unavailable", "Unavailable and cloud entries cannot become local selections.");
        session.SelectModel("local-a"); session.SetDraft("Canned old request."); var first = session.Send();
        c.Check(session.Snapshot.Busy && !session.SetDraft("Replacement while busy") && !session.SelectModel("local-b") && !session.NewChat(), "NOTCH-active-owner", "An active request freezes draft, model and session identity.");
        session.Stop();
        c.Check(!session.Snapshot.Busy && session.Snapshot.Draft == "Canned old request.", "NOTCH-stop-draft", "Stop releases visible busy ownership while keeping the draft.");
        string previousSession = session.Snapshot.SessionId; c.Check(session.NewChat() && session.Snapshot.SessionId != previousSession, "NOTCH-new-identity", "A new chat receives a distinct identity after Stop.");
        session.SelectModel("local-b"); session.SetDraft("Canned current request."); c.Check(await session.Send(), "NOTCH-current-completes", "A replacement request can complete independently of an abandoned callback.");
        old.SetResult(new("Stale old answer.")); c.Check(!await first.WaitAsync(TimeSpan.FromSeconds(2)), "NOTCH-late-result-refused", "An ignored-cancellation callback cannot complete an old request.");
        c.Check(session.Snapshot.Messages.Count == 2 && session.Snapshot.Messages[1].Text == "Canned current answer." && sent[1].History.Count == 0 && sent[1].ModelId == "local-b", "NOTCH-no-cross-session-history", "A replaced session does not inherit stale output or prior context.");

        var stalled = Signal<NotchChatReply>();
        using var timeout = new NotchChatSession(() => Models, (_, _) => stalled.Task, requestTimeout: TimeSpan.FromMilliseconds(40));
        timeout.SetDraft("Canned bounded wait.");
        c.Check(!await timeout.Send().WaitAsync(TimeSpan.FromSeconds(2)) && !timeout.Snapshot.Busy && timeout.Snapshot.Draft.Length > 0, "NOTCH-timeout", "The absolute request deadline clears busy state even if the callback ignores cancellation.");
        stalled.SetResult(new("Late timeout answer.")); await Task.Yield();
        c.Check(timeout.Snapshot.Messages.Count == 0, "NOTCH-timeout-no-history", "A timed-out reply never appears in history.");

        var fileA = Signal<NotchTextAttachment>(); int reads = 0; sent.Clear();
        using var attachments = new NotchChatSession(() => Models, (input, _) => { sent.Add(input); return Task.FromResult(new NotchChatReply("Canned file answer.")); },
            readFile: (_, _) => ++reads == 1 ? fileA.Task : Task.FromResult(new NotchTextAttachment("file-b", "Canned.md", "Exact reviewed Ω text.\n`x <= 2`", 34)));
        var loadingA = attachments.StageFile("injected-canned-a"); var loadingB = attachments.StageFile("injected-canned-b");
        c.Check(await loadingB && attachments.Snapshot.InboxPhase == NotchInboxPhase.Review, "NOTCH-file-review", "An injected local read creates pending review only.");
        fileA.SetResult(new("file-a", "Old.md", "Old text", 8));
        c.Check(!await loadingA && attachments.Snapshot.File?.Id == "file-b", "NOTCH-late-file", "A superseded file read cannot replace the newer snapshot.");
        attachments.SetDraft("Use the reviewed file."); c.Check(!await attachments.Send() && sent.Count == 0, "NOTCH-no-auto-ingestion", "Pending file content cannot be sent before explicit attachment approval.");
        c.Check(!attachments.AttachReviewed("file-a") && attachments.AttachReviewed("file-b"), "NOTCH-exact-file-review", "Attachment approval binds the current immutable read ID.");
        c.Check(await attachments.Send() && sent[0].Attachment?.Text == "Exact reviewed Ω text.\n`x <= 2`", "NOTCH-reviewed-payload", "The one approved snapshot reaches the injected local request unchanged.");
        attachments.SetDraft("Next message."); await attachments.Send();
        c.Check(sent[1].Attachment is null && attachments.Snapshot.File is null, "NOTCH-file-once", "A successful attachment is not silently reused on later messages.");
        var untrustedSnapshot = attachments.Snapshot;
        if (untrustedSnapshot.Messages is NotchChatMessage[] mutable) mutable[0] = new("Buddy", "Injected snapshot mutation", DateTimeOffset.UtcNow);
        c.Check(attachments.Snapshot.Messages[0].Text != "Injected snapshot mutation", "NOTCH-snapshot-isolation", "Mutating returned collection storage cannot change session history.");

        var fileStop = Signal<NotchTextAttachment>();
        using var stoppedRead = new NotchChatSession(() => Models, (_, _) => Task.FromResult(new NotchChatReply("Unused.")), readFile: (_, _) => fileStop.Task);
        var stage = stoppedRead.StageFile("injected-file"); stoppedRead.Stop(); fileStop.SetResult(new("stale", "Canned.txt", "Canned", 6));
        c.Check(!await stage && stoppedRead.Snapshot.File is null && stoppedRead.Snapshot.InboxPhase == NotchInboxPhase.Empty, "NOTCH-stop-read", "Stop invalidates a late file result without attaching it.");
        var disposing = Signal<NotchChatReply>(); var disposed = new NotchChatSession(() => Models, (_, _) => disposing.Task); disposed.SetDraft("Close while busy."); var disposalRun = disposed.Send(); disposed.Dispose(); disposing.SetResult(new("Closed result."));
        c.Check(!await disposalRun && disposed.Snapshot.Messages.Count == 0 && !disposed.SetDraft("New"), "NOTCH-close-owner", "Closing refuses both late output and new drafts.");

        var state = new NotchInteractionState(); c.Check(!state.BeginEditing() && !state.Expanded, "NOTCH-hidden-default", "Hidden presentation cannot grant editing activation.");
        state.SetMode("Compact"); state.Hover(true); c.Check(state.Expanded && state.Mode == "Compact", "NOTCH-hover-transient", "Hover expands presentation without changing the saved mode.");
        state.Hover(false); c.Check(!state.Expanded, "NOTCH-hover-collapse", "Leaving a temporary hover collapses without fabricating task completion.");
        state.BeginEditing(); c.Check(state.Expanded, "NOTCH-edit-explicit", "Only an explicit edit request pins expansion while editing.");
        state.SetMode("Hidden"); c.Check(!state.Expanded && !state.Editing, "NOTCH-hide-revokes-edit", "Hiding revokes temporary editing state.");
        var journal = new LocalTaskJournal(); var token = journal.Begin("canned", "Canned task", "Still running.");
        c.Check(NotchInteractionState.Ticker(journal.Snapshot.Tasks[0]).StartsWith("Running", StringComparison.Ordinal), "NOTCH-ticker-truth", "Ticker reflects journal state instead of inferred completion.");
        journal.Finish(token, LocalTaskPhase.Cancelled, "Stopped by fixture.");
        c.Check(NotchInteractionState.Ticker(journal.Snapshot.Tasks[0]).StartsWith("Cancelled", StringComparison.Ordinal), "NOTCH-ticker-cancelled", "Cancelled tasks remain distinctly cancelled.");
        int dispatches = 0, readers = 0, laterNotifications = 0;
        using var displayFailure = new NotchChatSession(() => Models, (_, _) => { dispatches++; return Task.FromResult(new NotchChatReply("Must not run.")); },
            readFile: (_, _) => { readers++; return Task.FromResult(new NotchTextAttachment("must-not-read", "Canned.txt", "Canned", 6)); });
        displayFailure.Changed += () => throw new InvalidOperationException("Injected presentation failure.");
        displayFailure.Changed += () => laterNotifications++;
        displayFailure.SetDraft("Keep this draft after display failure.");
        c.Check(!await displayFailure.Send() && !displayFailure.Snapshot.Busy && dispatches == 0 && displayFailure.Snapshot.Draft == "Keep this draft after display failure.", "NOTCH-display-failure-send", "A failed initial presentation notification refuses dispatch and clears request ownership.");
        c.Check(!await displayFailure.StageFile("injected-only") && readers == 0 && displayFailure.Snapshot.InboxPhase != NotchInboxPhase.Reading, "NOTCH-display-failure-read", "A failed initial file-review notification refuses the read and clears reading ownership.");
        c.Check(laterNotifications > 0, "NOTCH-subscriber-isolation", "A faulty display subscriber does not suppress all later observers.");
        int reentrantDispatches = 0; bool stopped = false;
        using var reentrant = new NotchChatSession(() => Models, (_, _) => { reentrantDispatches++; return Task.FromResult(new NotchChatReply("Must not run.")); });
        reentrant.Changed += () => { if (!stopped && reentrant.Snapshot.Busy) { stopped = true; reentrant.Stop(); } };
        reentrant.SetDraft("Reentrant Stop before dispatch.");
        c.Check(!await reentrant.Send() && reentrantDispatches == 0 && !reentrant.Snapshot.Busy, "NOTCH-reentrant-stop", "Stop during initial presentation cannot be followed by a late backend dispatch.");
        int reentrantReads = 0; bool removed = false;
        using var removedRead = new NotchChatSession(() => Models, (_, _) => Task.FromResult(new NotchChatReply("Unused.")), readFile: (_, _) => { reentrantReads++; return Task.FromResult(new NotchTextAttachment("x", "Canned.txt", "Canned", 6)); });
        removedRead.Changed += () => { if (!removed && removedRead.Snapshot.InboxPhase == NotchInboxPhase.Reading) { removed = true; removedRead.RemoveFile(); } };
        c.Check(!await removedRead.StageFile("injected-only") && reentrantReads == 0 && removedRead.Snapshot.File is null, "NOTCH-reentrant-remove", "Removing a file during its initial notification prevents later reader dispatch.");
        Notes(c);
    }
    private static void Notes(Checks c)
    {
        string directory = Path.Combine(Path.GetTempPath(), "buddy-qa53-notes-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        try {
            string path = Path.Combine(directory, "note.json"); var a = new NotchNoteStore(path); var b = new NotchNoteStore(path);
            var initial = a.Load(); c.Check(!File.Exists(path) && initial.Text == "", "NOTCH-notes-explicit-save", "Constructing and reading a missing note create no persisted content.");
            var saved = a.Save("Explicit note Ω only.", initial.Revision); c.Check(b.Load().Text == saved.Text, "NOTCH-notes-roundtrip", "Only an explicit save persists the authored note.");
            c.Rejects<IOException>(() => b.Save("Stale overwrite", initial.Revision), "NOTCH-note-stale-writer", "A stale editor cannot replace a newer explicit save.");
            c.Check(a.Load().Text == saved.Text, "NOTCH-note-stale-preserved", "Rejected stale writes preserve the latest disk note.");
            c.Rejects<ArgumentException>(() => a.Save(new string('x', 20001), saved.Revision), "NOTCH-note-bounds", "Oversized notes refuse without truncating the existing note.");
            File.WriteAllText(path, "{broken"); string corrupt = File.ReadAllText(path);
            try { a.Save("Overwrite corruption", saved.Revision); c.Check(false, "NOTCH-note-corrupt", "Corrupt documents must be preserved."); }
            catch (System.Text.Json.JsonException) { c.Check(File.ReadAllText(path) == corrupt, "NOTCH-note-corrupt", "Corrupt documents must be preserved."); }
            c.Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "NOTCH-no-temporary-residue", "Explicit-save success/refusal leaves no temporary text files.");
        } finally {
            // Only the unique directory created by this fixture is eligible for removal.
            if (Path.GetFullPath(directory).StartsWith(Path.GetFullPath(Path.GetTempPath()) + "buddy-qa53-notes-", StringComparison.OrdinalIgnoreCase)) Directory.Delete(directory, true);
        }
    }
}
#endif
