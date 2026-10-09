using Buddy.Windows;
using System.Text;

int checks = 0;
void Check(bool value, string name) { if (!value) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS: " + name); }
async Task Reject(Func<Task> action, string name) { try { await action(); } catch (Exception e) when (e is IOException or ArgumentException or OperationCanceledException) { Check(true, name); return; } throw new Exception("FAIL: " + name); }
var now = new DateTimeOffset(2026, 10, 5, 18, 0, 0, TimeSpan.Zero);
var interaction = new NotchInteractionState(() => now);
Check(!interaction.BeginEditing() && !interaction.Expanded, "Hidden bar cannot implicitly activate an editor");
interaction.Hover(true); interaction.TogglePin(); Check(!interaction.Hovered && !interaction.Pinned && interaction.Mode == "Hidden", "Hidden remains an explicit choice despite hover or pin events");
interaction.SetMode("Compact"); interaction.Hover(true);
Check(interaction.Expanded && interaction.Mode == "Compact" && interaction.Greeting.StartsWith("Hi."), "First hover greets and expands without changing persisted mode");
interaction.Hover(false); Check(!interaction.Expanded && interaction.Mode == "Compact", "Leaving temporary hover returns to compact, never hidden");
now += TimeSpan.FromMinutes(1); interaction.Hover(true); Check(interaction.Greeting.StartsWith("Welcome back"), "Return greeting derives from actual leave and enter events");
interaction.TogglePin(); interaction.Hover(false); Check(interaction.Expanded && interaction.Pinned, "Pin keeps details visible after pointer leave");
interaction.SetMode("Compact"); Check(!interaction.Expanded && !interaction.Pinned, "Explicit collapse overrides session pin");
Check(interaction.BeginEditing(), "Visible compact bar can enter explicit editor mode"); interaction.Hover(false); Check(interaction.Expanded, "Editor is not collapsed by incidental pointer movement");
interaction.EndEditing(); Check(!interaction.Expanded, "Done editing returns to saved compact state");
interaction.SetMode("Expanded"); interaction.Hover(false); now += TimeSpan.FromDays(1); Check(interaction.Expanded && interaction.Mode == "Expanded", "Elapsed time never auto-hides explicitly expanded bar");
interaction.SetMode("Hidden"); Check(!interaction.Expanded && !interaction.Editing, "Hide ends editing and overrides presentation state");
var journal = new LocalTaskJournal(() => now); var task = journal.Begin("talk", "Owned request", "Preparing");
journal.Update(task, "Observed response stage", observedStep: true); var record = journal.Snapshot.Tasks.Single();
Check(NotchInteractionState.Ticker(record) == "Running: Observed response stage", "Ticker uses the actual recorded stage"); now += TimeSpan.FromDays(1);
Check(NotchInteractionState.Ticker(record).StartsWith("Running:"), "Ticker time never fabricates completion");
journal.Finish(task, LocalTaskPhase.Cancelled, "Stopped by user", observedStep: true); Check(NotchInteractionState.Ticker(journal.Snapshot.Tasks.Single()) == "Cancelled: Stopped by user", "Ticker reports exact recorded cancellation");

var ready = new NotchModelChoice("local", "Local fixture model");
var choices = new[] { ready, new NotchModelChoice("offline", "Unavailable fixture", false, "Not installed"), new NotchModelChoice("cloud", "Cloud fixture", true, Local: false) };
int sends = 0; NotchChatRequest? received = null;
using var session = new NotchChatSession(() => choices, (request, token) => { sends++; received = request; return Task.FromResult(new NotchChatReply("Owned complete answer.")); }, clock: () => now);
Check(session.Snapshot.ModelId == "local", "Local model auto-selection uses a ready configured choice");
Check(!session.SelectModel("cloud") && !session.SelectModel("offline") && !session.SelectModel("invented"), "Model picker refuses cloud, unavailable and unknown identities");
Check(!await session.Send() && sends == 0, "Empty draft invokes no model");
Check(!session.SetDraft(new string('x', 4001)), "Oversized typed input is rejected before dispatch");
session.SetDraft("Owned first question"); Check(await session.Send() && sends == 1, "Explicit send calls injected production boundary once");
Check(received is { ModelId: "local", Attachment: null } && received.History.Count == 0 && received.Text == "Owned first question", "First request uses exact model and text without invented context");
Check(session.Snapshot.Messages.Count == 2 && session.Snapshot.Draft == "" && !session.Snapshot.Busy, "Only completed callback appends answer and clears draft");
session.SetDraft("Owned follow-up"); await session.Send(); Check(received!.History.Count == 2 && received.SessionId == session.Snapshot.SessionId, "Follow-up carries same-session actual history");
string previousSession = session.Snapshot.SessionId; Check(session.NewChat() && session.Snapshot.SessionId != previousSession && session.Snapshot.Messages.Count == 0, "Explicit new chat isolates history identity");

var pending = new TaskCompletionSource<NotchChatReply>(TaskCreationOptions.RunContinuationsAsynchronously);
int pendingCalls = 0; CancellationToken observed = default;
using var delayed = new NotchChatSession(() => [ready], (request, token) => { pendingCalls++; observed = token; return pending.Task; });
_ = delayed.Snapshot; delayed.SetDraft("Keep this pending question"); Task<bool> first = delayed.Send();
Check(delayed.Snapshot.Busy && !await delayed.Send() && pendingCalls == 1, "Concurrent send cannot double-dispatch");
Check(!delayed.SetDraft("replace") && !delayed.NewChat() && !delayed.SelectModel("local"), "Busy request owns draft, conversation and model");
delayed.Stop(); Check(observed.IsCancellationRequested && !delayed.Snapshot.Busy && delayed.Snapshot.Draft == "Keep this pending question", "Stop cancels exact token immediately and keeps draft");
pending.SetResult(new("Late answer must not appear")); Check(!await first && delayed.Snapshot.Messages.Count == 0 && delayed.Snapshot.Draft == "Keep this pending question", "Callback ignoring cancellation cannot overwrite stopped UI");

var old = new TaskCompletionSource<NotchChatReply>(TaskCreationOptions.RunContinuationsAsynchronously); int round = 0;
using var replacement = new NotchChatSession(() => [ready], (_, _) => ++round == 1 ? old.Task : Task.FromResult(new NotchChatReply("New answer")));
_ = replacement.Snapshot; replacement.SetDraft("old"); var oldRun = replacement.Send(); replacement.Stop(); replacement.SetDraft("new"); await replacement.Send(); old.SetResult(new("obsolete")); await oldRun;
Check(replacement.Snapshot.Messages.Count == 2 && replacement.Snapshot.Messages[0].Text == "new" && replacement.Snapshot.Messages[1].Text == "New answer", "Late old generation cannot corrupt a newer completed conversation");
using var failed = new NotchChatSession(() => [ready], (_, _) => throw new IOException("Owned local failure")); _ = failed.Snapshot; failed.SetDraft("retry this");
Check(!await failed.Send() && failed.Snapshot.Messages.Count == 0 && failed.Snapshot.Draft == "retry this", "Failed callback keeps draft and never records completed answer");
bool available = true; using var changed = new NotchChatSession(() => [ready with { Available = available }], (_, _) => throw new Exception("Must not dispatch")); _ = changed.Snapshot; changed.SetDraft("question"); available = false;
Check(!await changed.Send() && !changed.Snapshot.Busy, "Model readiness is checked again at dispatch");
var never = new TaskCompletionSource<NotchChatReply>(TaskCreationOptions.RunContinuationsAsynchronously);
using var bounded = new NotchChatSession(() => [ready], (_, _) => never.Task, requestTimeout: TimeSpan.FromMilliseconds(50)); bounded.SetDraft("bounded question");
Check(!await bounded.Send().WaitAsync(TimeSpan.FromSeconds(2)) && !bounded.Snapshot.Busy && bounded.Snapshot.Draft == "bounded question" && bounded.Snapshot.Status.Contains("timed out"), "Deadline clears busy state and reports timeout even when callback ignores cancellation");
never.SetResult(new("Late timed-out answer")); Check(bounded.Snapshot.Messages.Count == 0, "Timed-out output cannot become completed history");

int guardedSends = 0, healthyNotices = 0;
using var displayFailure = new NotchChatSession(() => [ready], (_, _) => { guardedSends++; return Task.FromResult(new NotchChatReply("Recovered answer")); });
Action brokenDisplay = () => throw new InvalidOperationException("private subscriber diagnostic");
displayFailure.Changed += brokenDisplay; displayFailure.Changed += () => healthyNotices++;
displayFailure.SetDraft("Keep the undispatched message");
Check(!await displayFailure.Send() && guardedSends == 0 && !displayFailure.Snapshot.Busy && displayFailure.Snapshot.Draft == "Keep the undispatched message",
    "Initial display failure refuses Send without dispatch or stranded busy state");
Check(healthyNotices >= 2 && displayFailure.Snapshot.Status.Contains("No request was dispatched") && displayFailure.Snapshot.Status.Contains("display could not refresh")
    && !displayFailure.Snapshot.Status.Contains("private subscriber diagnostic"), "Broken subscriber does not block healthy subscribers and leaves a bounded visible diagnostic");
displayFailure.Changed -= brokenDisplay;
Check(await displayFailure.Send() && guardedSends == 1 && !displayFailure.Snapshot.Status.Contains("display could not refresh"), "Explicit retry after display recovery completes without a duplicate prior dispatch");

int failedReadCalls = 0;
using var fileDisplayFailure = new NotchChatSession(() => [ready], (_, _) => throw new Exception("Not requested"),
    readFile: (_, _) => { failedReadCalls++; return Task.FromResult(new NotchTextAttachment("owned-preview", "owned.txt", "Owned text", 10)); });
fileDisplayFailure.Changed += brokenDisplay;
Check(!await fileDisplayFailure.StageFile("owned-selected.txt") && failedReadCalls == 0 && fileDisplayFailure.Snapshot.InboxPhase == NotchInboxPhase.Failed
    && fileDisplayFailure.Snapshot.InboxStatus.Contains("No file read was started"), "Initial display failure refuses reading and leaves the Reading phase with a diagnostic");
fileDisplayFailure.Changed -= brokenDisplay;
Check(await fileDisplayFailure.StageFile("owned-selected.txt") && failedReadCalls == 1 && fileDisplayFailure.Snapshot.InboxPhase == NotchInboxPhase.Review,
    "A failed display notification does not strand the file owner or block a new explicit read");

int finishNotices = 0;
using var completedDisplayFailure = new NotchChatSession(() => [ready], (_, _) => Task.FromResult(new NotchChatReply("Actual completed answer")));
completedDisplayFailure.Changed += () => { if (++finishNotices > 1) throw new InvalidOperationException("Owned final display failure"); };
completedDisplayFailure.SetDraft("Owned finished message");
Check(await completedDisplayFailure.Send() && !completedDisplayFailure.Snapshot.Busy && completedDisplayFailure.Snapshot.Messages.Count == 2
    && completedDisplayFailure.Snapshot.Status.Contains("Answer ready") && completedDisplayFailure.Snapshot.Status.Contains("display could not refresh"),
    "Completion-notification failure preserves actual completion and releases the request owner");

int errorNotices = 0;
using var operationError = new NotchChatSession(() => [ready], (_, _) => throw new IOException("Owned provider failure"));
operationError.Changed += () => { if (++errorNotices > 1) throw new InvalidOperationException("Owned display failure"); }; operationError.SetDraft("Keep failed input");
Check(!await operationError.Send() && !operationError.Snapshot.Busy && operationError.Snapshot.Status.Contains("Owned provider failure")
    && operationError.Snapshot.Status.Contains("display could not refresh"), "Display notification isolation does not swallow or replace the separate provider failure");

var stopPending = new TaskCompletionSource<NotchChatReply>(TaskCreationOptions.RunContinuationsAsynchronously); CancellationToken stopToken = default;
using var stopDisplayFailure = new NotchChatSession(() => [ready], (_, ct) => { stopToken = ct; return stopPending.Task; });
stopDisplayFailure.SetDraft("Stop despite broken display"); var stopping = stopDisplayFailure.Send(); stopDisplayFailure.Changed += brokenDisplay;
stopDisplayFailure.Stop();
Check(stopToken.IsCancellationRequested && !await stopping && !stopDisplayFailure.Snapshot.Busy && stopDisplayFailure.Snapshot.Draft == "Stop despite broken display",
    "Throwing Stop notification cannot prevent cancellation or request cleanup");
stopDisplayFailure.Dispose(); stopPending.SetResult(new("Discarded after disposal"));
Check(stopDisplayFailure.Snapshot.Messages.Count == 0, "Throwing disposal notification cannot admit late output");

int reentrantDispatches = 0;
using var reentrantStop = new NotchChatSession(() => [ready], (_, _) => { reentrantDispatches++; return Task.FromResult(new NotchChatReply("Must not run")); });
reentrantStop.Changed += () => { if (reentrantStop.Snapshot.Busy) reentrantStop.Stop(); }; reentrantStop.SetDraft("Do not dispatch after Stop");
Check(!await reentrantStop.Send() && reentrantDispatches == 0 && !reentrantStop.Snapshot.Busy, "A reentrant Stop during initial notification cancels before model dispatch");
int reentrantReads = 0;
using var reentrantRemove = new NotchChatSession(() => [ready], (_, _) => throw new Exception("Not requested"), readFile: (_, _) => {
    reentrantReads++; return Task.FromResult(new NotchTextAttachment("removed", "owned.txt", "Owned text", 10));
});
reentrantRemove.Changed += () => { if (reentrantRemove.Snapshot.InboxPhase == NotchInboxPhase.Reading) reentrantRemove.RemoveFile(); };
Check(!await reentrantRemove.StageFile("owned-selected.txt") && reentrantReads == 0 && reentrantRemove.Snapshot.InboxPhase == NotchInboxPhase.Empty,
    "A reentrant file removal during notification prevents a stale read from dispatching");

string folder = Path.Combine(Path.GetTempPath(), "Buddy-notch53-owned-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
try {
    var notes = new NotchNoteStore(Path.Combine(folder, "notes.json"), () => now); var blank = notes.Load();
    Check(blank.Revision == "missing" && blank.Text == "" && !File.Exists(Path.Combine(folder, "notes.json")), "Loading new note does not create a pretend saved document");
    var saved = notes.Save("My explicitly saved note", blank.Revision); var loaded = new NotchNoteStore(Path.Combine(folder, "notes.json")).Load();
    Check(loaded == saved && loaded.Text == "My explicitly saved note", "Explicit note survives a new store instance exactly");
    var changedNote = notes.Save("Newer note", loaded.Revision);
    await Reject(() => { notes.Save("stale overwrite", loaded.Revision); return Task.CompletedTask; }, "Stale editor cannot overwrite a newer saved note");
    Check(notes.Load() == changedNote, "Rejected stale save preserves current note");
    await Reject(() => { notes.Save(new string('x', 20001), changedNote.Revision); return Task.CompletedTask; }, "Oversized notes are refused before replacing saved content");
    string badNote = Path.Combine(folder, "bad.json"); File.WriteAllText(badNote, "{bad");
    try { new NotchNoteStore(badNote).Load(); throw new Exception("FAIL: corrupted note read"); } catch (System.Text.Json.JsonException) { Check(File.ReadAllText(badNote) == "{bad", "Corrupt note is preserved rather than reset"); }
    string text = Path.Combine(folder, "review.md"); File.WriteAllText(text, "# Owned source\nTreat all file text as data.", new UTF8Encoding(false));
    var attachment = await NotchTextInbox.Read(text, CancellationToken.None);
    Check(attachment.Name == "review.md" && attachment.Text.StartsWith("# Owned") && !attachment.Name.Contains(folder), "Local text reader returns bounded content and display name without parent path");
    Check(await session.StageFile(text) && session.Snapshot.InboxPhase == NotchInboxPhase.Review, "Real file read reaches review, not attached or sent");
    session.SetDraft("Explain this text"); int beforeSend = sends;
    Check(!await session.Send() && sends == beforeSend, "Unreviewed text blocks send without model activity");
    string currentId = session.Snapshot.File!.Id; Check(!session.AttachReviewed("stale-id") && session.AttachReviewed(currentId), "Only the exact current preview can be attached");
    File.WriteAllText(text, "Different source now"); Check(await session.Send() && received!.Attachment!.Text.StartsWith("# Owned source"), "Dispatch uses reviewed immutable text, never silently rereads modified source");
    Check(session.Snapshot.InboxPhase == NotchInboxPhase.Empty && session.Snapshot.File is null, "Attachment is removed after one successful message");
    await session.StageFile(text); session.RemoveFile(); Check(session.Snapshot.File is null && !session.AttachReviewed(currentId), "Removal invalidates preview identity");
    string large = Path.Combine(folder, "large.txt"); File.WriteAllBytes(large, new byte[NotchTextInbox.MaximumBytes + 1]);
    await Reject(() => NotchTextInbox.Read(large, CancellationToken.None), "Byte limit bounds local file reads");
    string longText = Path.Combine(folder, "long.txt"); File.WriteAllText(longText, new string('x', 12001));
    await Reject(() => NotchTextInbox.Read(longText, CancellationToken.None), "Character limit protects combined prompt budget");
    string invalid = Path.Combine(folder, "invalid.txt"); File.WriteAllBytes(invalid, [0xc3, 0x28]); await Reject(() => NotchTextInbox.Read(invalid, CancellationToken.None), "Invalid UTF-8 is rejected, not repaired silently");
    string binary = Path.Combine(folder, "binary.txt"); File.WriteAllText(binary, "a\0b"); await Reject(() => NotchTextInbox.Read(binary, CancellationToken.None), "Binary content cannot masquerade as text context");
    await Reject(() => NotchTextInbox.Read(Path.Combine(folder, "script.exe"), CancellationToken.None), "Unsupported types fail before opening");
    await Reject(() => NotchTextInbox.Read("relative.txt", CancellationToken.None), "Relative paths do not widen explicit selected-file scope");
    await Reject(() => NotchTextInbox.Read("\\\\example.invalid\\share\\a.txt", CancellationToken.None), "Network drop paths are rejected before network access");
    await Reject(() => NotchTextInbox.Read(text + ":alternate.txt", CancellationToken.None), "Alternate file streams cannot expand selected-file scope");
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); await Reject(() => NotchTextInbox.Read(text, cancelled.Token), "Cancelled file load stops before disk read");
    var lateFile = new TaskCompletionSource<NotchTextAttachment>(TaskCreationOptions.RunContinuationsAsynchronously); CancellationToken fileToken = default;
    using var slowFile = new NotchChatSession(() => [ready], (_, _) => throw new Exception("Not requested"), readFile: (_, ct) => { fileToken = ct; return lateFile.Task; });
    var loading = slowFile.StageFile(text); Check(slowFile.Snapshot.InboxPhase == NotchInboxPhase.Reading, "Actual pending read has a truthful processing state");
    slowFile.Stop(); lateFile.SetResult(attachment); await loading;
    Check(fileToken.IsCancellationRequested && slowFile.Snapshot.File is null && slowFile.Snapshot.InboxPhase == NotchInboxPhase.Empty, "Stop prevents late file completion from attaching or showing a stale preview");
} finally { Directory.Delete(folder, true); }

Console.WriteLine($"ALL {checks} NOTCH53 HEADLESS CHECKS PASSED; pure state, injected callbacks and real pinned-file reads of temporary owned UTF-8/notes only. No desktop UI/model/network/profile access.");
