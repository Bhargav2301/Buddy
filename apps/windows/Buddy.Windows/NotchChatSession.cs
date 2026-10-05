namespace Buddy.Windows;

internal sealed record NotchModelChoice(string Id, string Label, bool Available = true, string? UnavailableReason = null, bool Local = true)
{
    public bool CanSelect => Available && Local;
    public override string ToString() => Label + (Available && Local ? "" : " (unavailable)");
}
internal sealed record NotchChatMessage(string Role, string Text, DateTimeOffset At);
internal sealed record NotchChatRequest(string SessionId, string RequestId, string Text, string ModelId, NotchTextAttachment? Attachment, IReadOnlyList<NotchChatMessage> History);
internal sealed record NotchChatReply(string Text);
internal enum NotchInboxPhase { Empty, Reading, Review, Attached, Failed }
internal sealed record NotchChatSnapshot(long Revision, string SessionId, string Draft, string ModelId, bool Busy, string Status,
    IReadOnlyList<NotchChatMessage> Messages, IReadOnlyList<NotchModelChoice> Models, NotchInboxPhase InboxPhase, NotchTextAttachment? File, string InboxStatus);

// Actual model/service work is injected by the production owner. This session
// cannot capture a screen, use the network, open an app, or infer completion.
internal sealed class NotchChatSession : IDisposable
{
    private readonly object gate = new();
    private readonly Func<IReadOnlyList<NotchModelChoice>> models;
    private readonly Func<NotchChatRequest, CancellationToken, Task<NotchChatReply>> send;
    private readonly Func<string, CancellationToken, Task<NotchTextAttachment>> readFile;
    private readonly Func<DateTimeOffset> clock;
    private readonly TimeSpan requestTimeout;
    private readonly List<NotchChatMessage> messages = [];
    private string sessionId = Guid.NewGuid().ToString("N"), draft = "", selectedModel = "", status = "Choose a local model and write a message.";
    private string inboxStatus = "TXT or Markdown only. Review the contents before attaching.";
    private string? displayError;
    private NotchTextAttachment? file;
    private NotchInboxPhase inboxPhase;
    private CancellationTokenSource? request, reading;
    private long generation, fileGeneration, revision;
    private bool disposed;
    internal event Action? Changed;
    internal string RetentionNotice { get; }
    internal NotchChatSession(Func<IReadOnlyList<NotchModelChoice>> models,
        Func<NotchChatRequest, CancellationToken, Task<NotchChatReply>> send,
        string retentionNotice = "This bar shows this local chat session. The configured Buddy service controls saved conversation history.",
        Func<string, CancellationToken, Task<NotchTextAttachment>>? readFile = null, Func<DateTimeOffset>? clock = null, TimeSpan? requestTimeout = null)
    {
        this.models = models; this.send = send; this.readFile = readFile ?? NotchTextInbox.Read;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow); RetentionNotice = retentionNotice;
        this.requestTimeout = requestTimeout ?? TimeSpan.FromMinutes(2);
        if (this.requestTimeout <= TimeSpan.Zero || this.requestTimeout > TimeSpan.FromMinutes(5)) throw new ArgumentOutOfRangeException(nameof(requestTimeout));
    }
    private IReadOnlyList<NotchModelChoice> Choices()
    {
        try { return models().Where(m => !string.IsNullOrWhiteSpace(m.Id)).GroupBy(m => m.Id, StringComparer.Ordinal).Select(g => g.First()).Take(32).ToArray(); }
        catch { return []; }
    }
    internal NotchChatSnapshot Snapshot
    {
        get {
            var choices = Choices();
            lock (gate) {
                if (selectedModel.Length == 0) selectedModel = choices.FirstOrDefault(m => m.Available && m.Local)?.Id ?? "";
                return new(revision, sessionId, draft, selectedModel, request is not null, DisplayStatus(status), messages.ToArray(), choices, inboxPhase, file, DisplayStatus(inboxStatus));
            }
        }
    }
    private string DisplayStatus(string operationStatus) => displayError is null ? operationStatus : operationStatus + " " + displayError;
    private bool Notify()
    {
        Action? handlers; lock (gate) { revision++; handlers = Changed; }
        bool success = true;
        foreach (Action handler in handlers?.GetInvocationList() ?? []) {
            // A presentation callback must not strand an operation, prevent Stop,
            // or keep another display from observing the current state. This
            // exception boundary applies only to subscribers, not model/file work.
            try { handler(); } catch (Exception) { success = false; }
        }
        lock (gate) {
            string? next = success ? null : "The display could not refresh. Reopen the bar to refresh its status.";
            if (displayError != next) { displayError = next; revision++; }
        }
        return success;
    }
    internal bool SetDraft(string text)
    {
        lock (gate) { if (disposed || request is not null || text.Length > 4000) return false; draft = text; }
        return true; // Keystrokes do not rebuild the rest of the editor.
    }
    internal bool SelectModel(string id)
    {
        var choice = Choices().SingleOrDefault(m => m.Id == id);
        lock (gate) { if (disposed || request is not null || choice is not { Available: true, Local: true }) return false; selectedModel = id; }
        Notify(); return true;
    }
    internal async Task<bool> Send()
    {
        var choices = Choices(); CancellationTokenSource owner; NotchChatRequest input; long version;
        lock (gate) {
            if (disposed || request is not null || string.IsNullOrWhiteSpace(draft)) return false;
            if (selectedModel.Length == 0) selectedModel = choices.FirstOrDefault(m => m.CanSelect)?.Id ?? "";
            if (inboxPhase is NotchInboxPhase.Reading or NotchInboxPhase.Review) { status = "Finish reviewing or remove the selected file before sending."; input = null!; owner = null!; version = 0; }
            else if (!choices.Any(m => m.Id == selectedModel && m.Available && m.Local)) { status = "The selected local model is unavailable. Choose a ready model."; input = null!; owner = null!; version = 0; }
            else {
                owner = new CancellationTokenSource(); request = owner; version = ++generation;
                owner.CancelAfter(requestTimeout);
                input = new(sessionId, Guid.NewGuid().ToString("N"), draft.Trim(), selectedModel, inboxPhase == NotchInboxPhase.Attached ? file : null, messages.ToArray());
                status = "Waiting for the local Buddy response. Stop is available.";
                if (input.Attachment is not null) inboxStatus = "Reviewed text is selected for this local request.";
            }
        }
        if (owner is null) { Notify(); return false; }
        bool dispatched = false;
        try {
            if (!Notify()) throw new InvalidOperationException("The display could not refresh. No request was dispatched.");
            owner.Token.ThrowIfCancellationRequested();
            lock (gate) { if (disposed || request != owner || version != generation) return false; }
            dispatched = true;
            var reply = await send(input, owner.Token).WaitAsync(owner.Token).ConfigureAwait(false);
            owner.Token.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(reply.Text) || reply.Text.Length > 20000) throw new InvalidOperationException("The local answer was empty or exceeded the display limit. Your draft is kept.");
            lock (gate) {
                if (disposed || request != owner || version != generation) return false;
                messages.Add(new("You", input.Text, clock())); messages.Add(new("Buddy", reply.Text, clock()));
                while (messages.Count > 40) messages.RemoveRange(0, 2);
                draft = ""; status = "Answer ready. Local history stays here until you start a new chat.";
                // Attachments are one-message context, never silently reused.
                file = null; inboxPhase = NotchInboxPhase.Empty; inboxStatus = "Attached text was used for that message only.";
                return true;
            }
        } catch (Exception error) {
            lock (gate) {
                if (!disposed && request == owner && version == generation) {
                    status = error is OperationCanceledException
                        ? owner.IsCancellationRequested ? "The local answer timed out. Your draft is kept." : "Stopped. Your draft is kept."
                        : "The local answer did not finish. Your draft is kept. " + SafeError(error.Message);
                    if (input.Attachment is not null) inboxStatus = dispatched
                        ? "Reviewed text remains attached for retry. The prior request may have processed it."
                        : "Reviewed text is still attached. No request was dispatched.";
                }
            }
            return false;
        } finally {
            bool current; lock (gate) { current = request == owner && version == generation; if (current) request = null; }
            owner.Dispose(); if (current) Notify();
        }
    }
    internal void Stop()
    {
        CancellationTokenSource? current, loading;
        lock (gate) {
            current = request; request = null; generation++;
            loading = reading; reading = null; fileGeneration++;
            if (current is not null) {
                status = "Stopped. Your draft is kept.";
                if (inboxPhase == NotchInboxPhase.Attached) inboxStatus = "Reviewed text remains attached for retry. The prior request may have processed it.";
            }
            if (loading is not null) { file = null; inboxPhase = NotchInboxPhase.Empty; inboxStatus = "Local file reading stopped. Nothing is attached."; }
        }
        Cancel(current); Cancel(loading); Notify();
    }
    private static void Cancel(CancellationTokenSource? source) { try { source?.Cancel(); } catch (Exception error) when (error is ObjectDisposedException or AggregateException) { } }
    private static string SafeError(string value) => new(value.Where(c => !char.IsControl(c)).Take(240).ToArray());
    internal bool NewChat()
    {
        lock (gate) {
            if (disposed || request is not null || reading is not null) return false;
            sessionId = Guid.NewGuid().ToString("N"); messages.Clear(); draft = ""; file = null; inboxPhase = NotchInboxPhase.Empty;
            status = "New local chat. Previous saved conversations remain in Home."; inboxStatus = "No file attached.";
        }
        Notify(); return true;
    }
    internal async Task<bool> StageFile(string path)
    {
        CancellationTokenSource owner; CancellationTokenSource? previous; long version;
        lock (gate) {
            if (disposed || request is not null) return false;
            previous = reading; reading = owner = new CancellationTokenSource(); version = ++fileGeneration;
            file = null; inboxPhase = NotchInboxPhase.Reading; inboxStatus = "Reading the selected text locally. Nothing has been sent.";
        }
        try {
            Cancel(previous);
            if (!Notify()) throw new InvalidOperationException("The display could not refresh. No file read was started.");
            owner.Token.ThrowIfCancellationRequested();
            lock (gate) { if (disposed || reading != owner || version != fileGeneration) return false; }
            var loaded = await readFile(path, owner.Token).ConfigureAwait(false); owner.Token.ThrowIfCancellationRequested();
            if (loaded.Text.Length == 0 || loaded.Text.Length > NotchTextInbox.MaximumCharacters || loaded.ByteCount is < 0 or > NotchTextInbox.MaximumBytes)
                throw new IOException("The selected text is outside the supported limits.");
            lock (gate) {
                if (disposed || reading != owner || version != fileGeneration) return false;
                file = loaded; inboxPhase = NotchInboxPhase.Review; inboxStatus = "Read the complete preview, then choose Attach reviewed text. Nothing has been sent.";
            }
            return true;
        } catch (Exception error) {
            lock (gate) {
                if (!disposed && reading == owner && version == fileGeneration) {
                    file = null; inboxPhase = NotchInboxPhase.Failed; inboxStatus = "File not attached. " + SafeError(error.Message);
                }
            }
            return false;
        } finally {
            bool current; lock (gate) { current = reading == owner && version == fileGeneration; if (current) reading = null; }
            owner.Dispose(); if (current) Notify();
        }
    }
    internal bool AttachReviewed(string id)
    {
        lock (gate) {
            if (disposed || request is not null || reading is not null || inboxPhase != NotchInboxPhase.Review || file?.Id != id) return false;
            inboxPhase = NotchInboxPhase.Attached; inboxStatus = "Reviewed local text will accompany your next message. It has not been sent.";
        }
        Notify(); return true;
    }
    internal bool RemoveFile()
    {
        CancellationTokenSource? loading;
        lock (gate) {
            if (disposed || request is not null) return false;
            loading = reading; reading = null; fileGeneration++; file = null; inboxPhase = NotchInboxPhase.Empty; inboxStatus = "No file attached.";
        }
        Cancel(loading); Notify(); return true;
    }
    public void Dispose() { lock (gate) { if (disposed) return; disposed = true; } Stop(); lock (gate) Changed = null; }
}

internal sealed record NotchWorkspace(NotchChatSession Chat, NotchNoteStore Notes);
