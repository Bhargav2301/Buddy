using System.Windows.Controls;

namespace Buddy.Windows;

public sealed partial class MainWindow
{
    private RefineWindow? refineWindow;
    private FocusedFieldEditor? fieldEditor;
    private CancellationTokenSource? fieldCapture;
    private bool refineShortcutReady;
    private Task Refine()
    {
        if (host is null || busy || string.IsNullOrWhiteSpace(input.Text)) return Task.CompletedTask;
        var edit = new GuardedEdit(new HomeDraftField(input), input.Text);
        OpenRefine(input.Text, (text, ct) => { edit.Apply(text, DateTimeOffset.UtcNow, ct); return Task.CompletedTask; }, ct => { edit.Undo(DateTimeOffset.UtcNow, ct); return Task.CompletedTask; }, "Buddy draft");
        return Task.CompletedTask;
    }
    private async Task RefineFocusedField()
    {
        if (host is null || assistant is null || fieldCapture is not null) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3)); fieldCapture = cts;
        try {
            var window = Native.GetForegroundWindow();
            if (Native.IsOwnWindow(window)) window = previousWindow;
            if (Native.GetForegroundWindow() != window) {
                if (!InputNative.SetForegroundWindow(window)) throw new InvalidOperationException("Focus the prompt field and press Ctrl+Alt+R.");
                await Task.Delay(100, cts.Token);
            }
            fieldEditor ??= new FocusedFieldEditor(assistant.Perception);
            var draft = await fieldEditor.Capture(window, cts.Token); cts.Token.ThrowIfCancellationRequested();
            OpenRefine(draft.Edit.Original, (text, ct) => fieldEditor.Apply(draft, text, ct), ct => fieldEditor.Undo(draft, ct), draft.App + " · " + draft.FieldName);
        } catch (Exception e) {
            Summon(); ShowChat(); status.Text = e is OperationCanceledException ? "Refine stopped." : e.Message;
        } finally { if (ReferenceEquals(fieldCapture, cts)) fieldCapture = null; }
    }
    private void OpenRefine(string original, Func<string, CancellationToken, Task> apply, Func<CancellationToken, Task> undo, string source)
    {
        refineWindow?.Close();
        var window = new RefineWindow(host!.Service, original, apply, undo, source);
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
