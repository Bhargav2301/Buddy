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
    private LocalPromptWatcher? promptWatcher;
    private SummonedField? summonedField;
    private CancellationTokenSource? summonCapture;
    private bool voiceHeld;
    private async Task<bool> RememberSourceField()
    {
        var active=Native.GetForegroundWindow();
        if(active==IntPtr.Zero||Native.IsOwnWindow(active))return true;
        if(previousWindow!=active)ClearContext();previousWindow=active;summonedField=null;
        summonCapture?.Cancel();using var cts=new CancellationTokenSource(TimeSpan.FromSeconds(3));summonCapture=cts;
        try {
            if(fieldEditor is not null&&await fieldEditor.Probe(active,cts.Token,explicitInvocation:true) is {} anchor){
                cts.Token.ThrowIfCancellationRequested();summonedField=new(anchor,Native.Label(active),Environment.TickCount64);
            }
            return !cts.IsCancellationRequested;
        }catch(OperationCanceledException){return false;}catch{return !cts.IsCancellationRequested;}
        finally{if(ReferenceEquals(summonCapture,cts))summonCapture=null;}
    }
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
    private async Task RefineFocusedField(string? expectedIdentity = null, bool dictation = false, bool useSummonedField = false)
    {
        if (host is null || assistant is null || fieldCapture is not null) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3)); fieldCapture = cts;
        try {
            var window = Native.GetForegroundWindow();
            SummonedField? pinned=null;
            if (useSummonedField || Native.IsOwnWindow(window)) {
                pinned=summonedField??throw new InvalidOperationException("No external prompt field was selected. Focus the original AI prompt and press Ctrl+Alt+R. Buddy drafts are separate.");
                window=pinned.Anchor.Window;pinned.Validate(window,Native.Label(window),Environment.TickCount64);
                expectedIdentity=pinned.Anchor.Identity;
            }
            if(window==IntPtr.Zero||Native.IsOwnWindow(window))throw new InvalidOperationException("Refine source field cannot target Buddy's own composer. Focus the original external prompt and press Ctrl+Alt+R.");
            PrepareDesktopActivity("field");quick?.Dismiss();voiceOverlay?.Dismiss();
            if (Native.GetForegroundWindow() != window) {
                if (!InputNative.SetForegroundWindow(window)) throw new InvalidOperationException("Focus the prompt field and press Ctrl+Alt+R.");
                await Task.Delay(100, cts.Token);
            }
            fieldEditor ??= new FocusedFieldEditor(assistant.Perception);
            var draft = await fieldEditor.Capture(window, cts.Token, dictation, expectedIdentity,strictFocus:!dictation,explicitInvocation:true); cts.Token.ThrowIfCancellationRequested();
            pinned?.Validate(window,Native.Label(window),Environment.TickCount64);
            if (dictation) {
                quick?.Cancel(); voiceOverlay?.Cancel(); StopMainDictation(); tts?.Cancel(); dictationWindow?.Close();
                var overlay = new DictationWindow(fieldEditor, draft, mood => companionState.Set("dictation", mood), () => PrepareDesktopActivity("dictation"));
                dictationWindow = overlay; overlay.Closed += (_, _) => { if (ReferenceEquals(dictationWindow, overlay)) dictationWindow = null; };
                overlay.Show(); _ = overlay.Start();
            } else {
                promptWatcher??=new(fieldEditor,()=>host?.Service,()=>OpenQuick(true),()=>desktop.VoiceShortcut);
                await promptWatcher.ShowReview(draft);
            }
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
