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
    private readonly ExternalRefinementOptionsSlot externalRefinementOptions = new();
    private ExternalRefinementOptionsWindow? externalRefinementOptionsWindow;
    private int sourceFieldGeneration;
    private void CancelExternalRefinementOptions()
    {
        sourceFieldGeneration++;
        externalRefinementOptions.Clear();
        promptWatcher?.ClearPreparedOptionsPause();
        externalRefinementOptionsWindow?.Close(); externalRefinementOptionsWindow = null;
    }
    private void PrepareExternalRefinementOptions() => PrepareExternalRefinementOptions(null, null);
    private void PrepareExternalRefinementOptions(IReadOnlyList<Buddy.Server.RefinementContextSource>? sources, Func<bool>? contextCurrent, ExternalContextSelection? contextSelection = null)
    {
        if (host is null) return;
        // Editing options invalidates both an in-flight capture and an existing review.
        fieldCapture?.Cancel(); summonCapture?.Cancel(); summonedField = null;
        promptWatcher?.Suspend(); CancelExternalRefinementOptions();
        PrepareDesktopActivity("external-refinement-options"); quick?.Dismiss(); voiceOverlay?.Dismiss();
        var window = new ExternalRefinementOptionsWindow((options, mode) => {
            externalRefinementOptions.Arm(options, mode, Environment.TickCount64, contextCurrent, contextSelection);
            promptWatcher?.PauseForPreparedOptions();
            status.Text = "Source-field options prepared for one capture. Focus the original prompt and press Ctrl+Alt+R within five minutes.";
        }, sources, contextCurrent);
        externalRefinementOptionsWindow = window;
        window.Closed += (_, _) => { if (ReferenceEquals(externalRefinementOptionsWindow, window)) externalRefinementOptionsWindow = null; };
        window.Show();
    }
    private async Task<bool> RememberSourceField()
    {
        var active=Native.GetForegroundWindow();
        if(active==IntPtr.Zero||Native.IsOwnWindow(active))return true;
        windowSelection.Observe(active);
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
        int currentCapture = ++sourceFieldGeneration;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3)); fieldCapture = cts;
        var feedback = new RefinementCaptureFeedback(
            () => currentCapture == sourceFieldGeneration && ReferenceEquals(fieldCapture, cts),
            () => desktopActivity);
        try {
            var window = Native.GetForegroundWindow();
            ExternalRefinementPlan? preparedOptions = null;
            if (!dictation) {
                if (externalRefinementOptionsWindow is not null)
                    throw new InvalidOperationException("Finish or cancel source-field options before capturing the original prompt.");
                preparedOptions = promptWatcher is null ? externalRefinementOptions.Consume(Environment.TickCount64)
                    : promptWatcher.ConsumePreparedOptions(externalRefinementOptions, Environment.TickCount64);
                // Options are never attached to a field remembered before configuration.
                if (preparedOptions is not null && (useSummonedField || window == IntPtr.Zero || Native.IsOwnWindow(window)))
                    throw new InvalidOperationException("Prepared options require a fresh capture from the original external field. Prepare them again, focus that field and press Ctrl+Alt+R.");
            }
            SummonedField? pinned=null;
            if (useSummonedField || Native.IsOwnWindow(window)) {
                pinned=summonedField??throw new InvalidOperationException("No external prompt field was selected. Focus the original AI prompt and press Ctrl+Alt+R. Buddy drafts are separate.");
                window=pinned.Anchor.Window;pinned.Validate(window,Native.Label(window),Environment.TickCount64);
                expectedIdentity=pinned.Anchor.Identity;
            }
            if(window==IntPtr.Zero||Native.IsOwnWindow(window))throw new InvalidOperationException("Refine source field cannot target Buddy's own composer. Focus the original external prompt and press Ctrl+Alt+R.");
            PrepareDesktopActivity("field");feedback.EnterFieldActivity();quick?.Dismiss();voiceOverlay?.Dismiss();
            if (Native.GetForegroundWindow() != window) {
                if (!InputNative.SetForegroundWindow(window)) throw new InvalidOperationException("Focus the prompt field and press Ctrl+Alt+R.");
                await Task.Delay(100, cts.Token);
            }
            fieldEditor ??= new FocusedFieldEditor(assistant.Perception);
            var draft = await fieldEditor.Capture(window, cts.Token, dictation, expectedIdentity,strictFocus:!dictation,explicitInvocation:true); cts.Token.ThrowIfCancellationRequested();
            if (!feedback.OwnsFieldActivity) return;
            pinned?.Validate(window,Native.Label(window),Environment.TickCount64);
            if (dictation) {
                quick?.Cancel(); voiceOverlay?.Cancel(); StopMainDictation(); tts?.Cancel(); dictationWindow?.Close();
                var overlay = new DictationWindow(fieldEditor, draft, mood => companionState.Set("dictation", mood), () => PrepareDesktopActivity("dictation"));
                dictationWindow = overlay; overlay.Closed += (_, _) => { if (ReferenceEquals(dictationWindow, overlay)) dictationWindow = null; };
                overlay.Show(); _ = overlay.Start();
            } else {
                var prepared = preparedOptions?.Bind(draft.Edit.Original);
                promptWatcher??=new(fieldEditor,()=>host?.Service,()=>OpenQuick(true),()=>desktop.VoiceShortcut,PrepareExternalRefinementOptions);
                await promptWatcher.ShowReview(draft, prepared?.Request, preparedOptions is null ? null : () => preparedOptions.IsCurrent,
                    () => feedback.OwnsFieldActivity && !cts.IsCancellationRequested, preparedOptions?.ContextSelection);
            }
        } catch (Exception e) {
            feedback.Report(e, cts.IsCancellationRequested, message => { Summon(); ShowChat(); status.Text = message; });
        } finally { if (ReferenceEquals(fieldCapture, cts)) fieldCapture = null; }
    }
    private void OpenRefine(string original, Func<string, CancellationToken, Task> apply, Func<CancellationToken, Task> undo, string source)
    {
        refineWindow?.Close();
        var window = new RefineWindow(host!.Service, original, apply, undo, source, mood => companionState.Set("refine", mood), () => PrepareDesktopActivity("refine"), localTasks: localTasks);
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

// Failure presentation belongs to the capture which still owns its entered activity.
// In particular, a normal (not cancelled) provider failure cannot reopen Home over
// a newer Buddy-draft refinement. Invalid input before field entry still gets feedback.
internal sealed class RefinementCaptureFeedback(Func<bool> ownsRequest, Func<string> activity)
{
    private bool enteredField;
    internal void EnterFieldActivity() => enteredField = true;
    internal bool OwnsFieldActivity => enteredField && ownsRequest() && activity() == "field";
    internal bool Report(Exception error, bool cancelled, Action<string> present)
    {
        if (!ownsRequest() || enteredField && activity() != "field" || !enteredField && cancelled) return false;
        present(error is OperationCanceledException ? "Field operation stopped." : error.Message);
        return true;
    }
}
