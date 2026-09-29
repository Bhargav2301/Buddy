using System.Windows.Controls;

namespace Buddy.Windows;

public sealed partial class MainWindow
{
    private RefineWindow? refineWindow;
    private FocusedFieldEditor? fieldEditor;
    private CancellationTokenSource? fieldCapture;
    private bool refineShortcutReady;
    private bool dictationShortcutReady;
    private FocusedFieldBadge? fieldBadge;
    private DictationWindow? dictationWindow;
    private Task Refine()
    {
        if (host is null || busy || string.IsNullOrWhiteSpace(input.Text)) return Task.CompletedTask;
        RefineDraft(input);
        return Task.CompletedTask;
    }
    private void RefineDraft(System.Windows.Controls.TextBox field)
    {
        if (host is null || string.IsNullOrWhiteSpace(field.Text)) return;
        var edit = new GuardedEdit(new HomeDraftField(field), field.Text);
        OpenRefine(field.Text, (text, ct) => { edit.Apply(text, DateTimeOffset.UtcNow, ct); return Task.CompletedTask; }, ct => { edit.Undo(DateTimeOffset.UtcNow, ct); return Task.CompletedTask; }, "Buddy draft");
    }
    private async Task RefineFocusedField(string? expectedIdentity = null, bool dictation = false)
    {
        if (host is null || assistant is null || fieldCapture is not null) return;
        PrepareDesktopActivity("field");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3)); fieldCapture = cts;
        try {
            var window = Native.GetForegroundWindow();
            if (Native.IsOwnWindow(window)) window = previousWindow;
            if (Native.GetForegroundWindow() != window) {
                if (!InputNative.SetForegroundWindow(window)) throw new InvalidOperationException("Focus the prompt field and press Ctrl+Alt+R.");
                await Task.Delay(100, cts.Token);
            }
            fieldEditor ??= new FocusedFieldEditor(assistant.Perception);
            var draft = await fieldEditor.Capture(window, cts.Token, dictation, expectedIdentity); cts.Token.ThrowIfCancellationRequested();
            if (dictation) {
                quick?.Cancel(); voiceOverlay?.Cancel(); StopMainDictation(); tts?.SpeakAsyncCancelAll(); dictationWindow?.Close();
                var overlay = new DictationWindow(fieldEditor, draft, mood => companionState.Set("dictation", mood), () => PrepareDesktopActivity("dictation"));
                dictationWindow = overlay; overlay.Closed += (_, _) => { if (ReferenceEquals(dictationWindow, overlay)) dictationWindow = null; };
                overlay.Show(); _ = overlay.Start();
            } else OpenRefine(draft.Edit.Original, (text, ct) => fieldEditor.Apply(draft, text, ct), ct => fieldEditor.Undo(draft, ct), draft.App + " · " + draft.FieldName);
        } catch (Exception e) {
            Summon(); ShowChat(); status.Text = e is OperationCanceledException ? "Field operation stopped." : e.Message;
        } finally { if (ReferenceEquals(fieldCapture, cts)) fieldCapture = null; }
    }
    private void OpenRefine(string original, Func<string, CancellationToken, Task> apply, Func<CancellationToken, Task> undo, string source)
    {
        refineWindow?.Close();
        var window = new RefineWindow(host!.Service, original, apply, undo, source, mood => companionState.Set("refine", mood), () => PrepareDesktopActivity("refine"));
        refineWindow = window; window.Closed += (_, _) => { if (ReferenceEquals(refineWindow, window)) refineWindow = null; };
        window.Show(); _ = window.Refine("quick");
    }
    private sealed class HomeDraftField(System.Windows.Controls.TextBox field) : IVerifiedTextField
    {
        public string Identity { get; } = Guid.NewGuid().ToString();
        public string Read() => field.Text;
        public void Write(string expected, string value, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (field.Text != expected || field.IsReadOnly) throw new InvalidOperationException("The draft changed. Use Copy instead.");
            field.Text = value;
        }
    }
}
