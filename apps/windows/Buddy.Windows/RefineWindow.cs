using Buddy.Server;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;

namespace Buddy.Windows;

internal sealed class RefineWindow : Window
{
    private readonly BuddyService service;
    private readonly string original;
    private readonly Func<string, CancellationToken, Task> applyEdit;
    private readonly Func<CancellationToken, Task> undoEdit;
    private readonly TextBlock progress = Label("Choose a mode to refine on this PC.", 12), score = Label("", 18), changes = Label("", 12);
    private readonly TextBlock preflightSummary = Label("", 12), resultDetails = Label("", 12);
    private readonly TextBox before, after, preflightText;
    private readonly Button apply, undo, copy;
    private readonly List<Button> modeButtons = [];
    private CancellationTokenSource? operation;
    private RefinementRequest? refinement;
    private RefinementResult? result;
    private bool applied, closed, applying;
    private int generation, acceptedGeneration = -1;
    private string selectedMode = "quick";
    private string? frozenRequestFingerprint;
    private readonly Action<CompanionMood>? mood;
    private readonly Action? starting;
    private readonly LocalTaskJournal? localTasks;
    private readonly Func<bool> contextCurrent;
    private LocalTaskToken? displayedTask;
    internal RefinementOptionsPanel Options { get; }
    internal RefinementPreparationResult? CurrentPreparation { get; private set; }
    internal int RequestGeneration => generation;
    internal bool CanApply
    {
        get {
            if (closed || !contextCurrent() || applied || applying || operation is not null || refinement?.IsRunning == true || result is not { Accepted: true } || acceptedGeneration != generation || CurrentPreparation?.Ready != true || frozenRequestFingerprint is null) return false;
            try { return frozenRequestFingerprint == RefinementDraftOptions.Fingerprint(Options.Snapshot().ToRequest(original, selectedMode)); }
            catch { return false; }
        }
    }
    internal RefineWindow(BuddyService service, string original, Func<string, CancellationToken, Task> applyEdit, Func<CancellationToken, Task> undoEdit,
        string source, Action<CompanionMood>? mood = null, Action? starting = null, RefinementOptionsPanel? optionsPanel = null, LocalTaskJournal? localTasks = null, Func<bool>? contextCurrent = null)
    {
        BuddyTheme.Ensure(); this.contextCurrent = contextCurrent ?? (() => true); this.localTasks = localTasks; this.service = service; this.original = original; this.applyEdit = applyEdit; this.undoEdit = undoEdit; this.mood = mood; this.starting = starting;
        Title = "Buddy - Refine"; Width = 620; MinWidth = 380; Height = 780; MinHeight = 420; MaxHeight = SystemParameters.WorkArea.Height; FontFamily = BuddyTheme.Font; Foreground = BuddyTheme.Ink;
        Background = BuddyTheme.Surface; ResizeMode = ResizeMode.CanResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        before = Editor(original); after = Editor("");
        var layout = new DockPanel(); var footer = new StackPanel { Margin = new(20, 8, 20, 12) }; DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        var actions = new WrapPanel();
        apply = BuddyTheme.Button("Apply", () => _ = Apply(), true); apply.IsEnabled = false; actions.Children.Add(apply);
        copy = BuddyTheme.Button("Copy", () => { if (!closed && this.contextCurrent() && result is { Accepted: true } && !string.IsNullOrEmpty(after.Text)) System.Windows.Clipboard.SetText(after.Text); }); copy.IsEnabled = false; actions.Children.Add(copy);
        undo = BuddyTheme.Button("Undo", () => _ = Undo()); undo.IsEnabled = false; actions.Children.Add(undo);
        actions.Children.Add(BuddyTheme.Button("Stop", Cancel)); footer.Children.Add(actions);
        footer.Children.Add(Label("Undo for 30 seconds after Apply. Buddy never submits the form.", 12));
        footer.Children.Add(BuddyTheme.Button("Copy original", () => { if (!closed) System.Windows.Clipboard.SetText(original); }));
        var panel = new StackPanel { Margin = new(20, 16, 20, 0) };
        var title = Label("Refine - on this PC", 16); title.Foreground = BuddyTheme.Deep; title.FontWeight = FontWeights.SemiBold; panel.Children.Add(title); panel.Children.Add(Label(source, 12));
        var modes = new WrapPanel();
        foreach (var name in new[] { "Auto", "Quick", "Guided", "Council" }) {
            var button = BuddyTheme.Button(name, () => _ = Refine(name.ToLowerInvariant())); button.Padding = new(10, 6, 10, 6); modeButtons.Add(button); modes.Children.Add(button);
        }
        panel.Children.Add(modes); panel.Children.Add(Label("Choose a mode to generate again after changing any option or reference.", 12));
        Options = optionsPanel ?? new RefinementOptionsPanel(); panel.Children.Add(Options);
        preflightText = Editor(""); AutomationProperties.SetName(preflightText, "Exact prepared prompt");
        AutomationProperties.SetName(preflightSummary, "Refinement preparation status"); AutomationProperties.SetLiveSetting(preflightSummary, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(resultDetails, "Refinement result details");
        panel.Children.Add(preflightSummary);
        panel.Children.Add(new Expander { Header = "Exact prepared prompt", Content = preflightText, Margin = new(0, 0, 0, 10), Foreground = BuddyTheme.Ink });
        panel.Children.Add(Label("This preparation checks your inputs and destination limit. Local model context and preservation checks still run during refinement. Supporting-data markers are part of the exact output and count.", 12));
        score.Foreground = BuddyTheme.Deep; score.FontWeight = FontWeights.Bold; panel.Children.Add(score); panel.Children.Add(changes);
        AutomationProperties.SetName(before, "Original prompt"); AutomationProperties.SetName(after, "Refined prompt");
        panel.Children.Add(new Expander { Header = "Original", Content = before, IsExpanded = false, Margin = new(0, 0, 0, 8), Foreground = BuddyTheme.Ink });
        panel.Children.Add(after); panel.Children.Add(resultDetails); panel.Children.Add(progress); AutomationProperties.SetLiveSetting(progress, AutomationLiveSetting.Polite);
        layout.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = layout;
        Options.Changed += OptionsChanged; UpdatePreparation();
        SourceInitialized += (_, _) => CaptureProtection.Apply(new WindowInteropHelper(this).Handle);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Cancel(); } };
        Closed += (_, _) => { closed = true; Cancel(); Options.Dispose(); };
    }
    private void OptionsChanged()
    {
        InvalidateResult(); UpdatePreparation();
        if (!closed && !applied) progress.Text = "Options changed. Choose a mode to prepare a new result; the original is unchanged.";
    }
    private void InvalidateResult()
    {
        if (displayedTask is { } oldTask) {
            if (applying) localTasks?.Update(oldTask, "Stop requested; checking the result of an already approved edit.");
            else localTasks?.Finish(oldTask, LocalTaskPhase.Cancelled, "Review cancelled or inputs changed; no new edit approved.");
        }
        generation++; refinement?.Cancel(); refinement = null; operation?.Cancel(); result = null; acceptedGeneration = -1; frozenRequestFingerprint = null; apply.IsEnabled = false; copy.IsEnabled = false;
        if (!applied) { after.Clear(); score.Text = changes.Text = resultDetails.Text = ""; }
        mood?.Invoke(CompanionMood.Idle);
    }
    private void UpdatePreparation()
    {
        CurrentPreparation = null;
        try {
            var options = Options.Snapshot(); var prepared = RefinementPreparation.Prepare(options.ToRequest(original, selectedMode)); CurrentPreparation = prepared;
            preflightText.Text = prepared.AssembledText;
            string readiness = prepared.Ready ? "Inputs prepared" : "Preparation needs attention";
            preflightSummary.Text = $"{readiness} · {prepared.Mode} · {prepared.Choice.Technique}\n{prepared.Choice.Rationale}\n" + BudgetText(prepared.Budget, options) + Warnings(prepared.Warnings);
            preflightSummary.Foreground = prepared.Ready ? BuddyTheme.Muted : BuddyTheme.Warn;
        } catch (Exception ex) { preflightText.Clear(); preflightSummary.Text = ex.Message; preflightSummary.Foreground = BuddyTheme.Risk; }
        UpdateControls();
    }
    private static string Warnings(IEnumerable<string> values)
    {
        var warnings = values.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray(); return warnings.Length == 0 ? "" : "\n" + string.Join("\n", warnings.Select(x => "! " + x));
    }
    private static string BudgetText(RefinementBudgetResult? budget, RefinementDraftOptions options)
    {
        if (budget is null) return "";
        string unit = budget.Unit switch { "unicode-scalars" => "Unicode code points", "utf8-bytes" => "UTF-8 bytes", _ => "UTF-16 code units" };
        string text = budget.Limit is { } limit ? $"{budget.Count:N0} / {limit:N0} {unit} · user-supplied limit for {options.Destination}" : $"{budget.Count:N0} {unit} · no destination limit";
        if (budget.Removed.Count > 0) text += "\nOmitted optional references: " + string.Join(", ", budget.Removed.Select(options.DescribeBlock));
        if (budget.Conflict is { Length: > 0 } conflict) text += "\n! " + conflict;
        return text;
    }
    private void UpdateControls()
    {
        bool canConfigure = !closed && contextCurrent() && !applied && !applying;
        Options.IsEnabled = canConfigure; foreach (var button in modeButtons) button.IsEnabled = canConfigure;
        apply.IsEnabled = CanApply; copy.IsEnabled = !closed && contextCurrent() && !applying && result is { Accepted: true } && acceptedGeneration == generation;
    }
    internal async Task Refine(string mode)
    {
        if (closed || !contextCurrent() || applied || applying) return;
        selectedMode = mode; Options.CancelResourceRead(); InvalidateResult(); UpdatePreparation();
        if (CurrentPreparation is not { Ready: true } prepared) { progress.Text = "Review the preparation messages before refining. Original unchanged."; return; }
        starting?.Invoke(); if (closed || !contextCurrent() || applied || applying) return;
        // The preparation API owns the deep snapshot; editable controls never mutate these lists.
        int current = ++generation; var options = Options.Snapshot(); var request = new RefinementRequest(); refinement = request;
        var activity = localTasks?.Begin("refine", "Buddy-draft refinement", "Preparing a proposal; original unchanged."); displayedTask = activity;
        string fingerprint = RefinementDraftOptions.Fingerprint(prepared.Request); mood?.Invoke(CompanionMood.Thinking);
        try {
            bool Current() => generation == current && !closed && contextCurrent() && ReferenceEquals(refinement, request);
            var outcome = await request.Run(token => service.RefineStream(prepared.Request, token),
                item => { if (Current() && item.Type == "delta") after.AppendText(item.Text); },
                text => { if (Current()) progress.Text = text; });
            if (!Current()) return;
            if (outcome.Result is { } completed) {
                    if (activity is { } ready) {
                        if (completed.Accepted) localTasks?.Update(ready, "Proposal ready for your review; no edit applied.", LocalTaskPhase.WaitingForReview, observedStep: true);
                        else localTasks?.Finish(ready, LocalTaskPhase.Completed, "Review finished; original retained without an accepted rewrite.", observedStep: true);
                    }
                    result = completed; after.Text = completed.RefinedPrompt; acceptedGeneration = current; frozenRequestFingerprint = fingerprint;
                    score.Text = !completed.Accepted ? "Original retained" : completed.Method == "source-structure"
                        ? "Task structure ready for review" : completed.ScoreBefore is { } initial && completed.ScoreAfter is { } final
                            ? $"{initial} to {final} - estimated" : "Refinement ready for review";
                    changes.Text = string.Join("\n", completed.Changes); progress.Text = completed.Message;
                    resultDetails.Text = $"Technique: {completed.Technique}\n{completed.TechniqueRationale}\n" + BudgetText(completed.DestinationBudget, options) + Warnings(completed.Warnings);
            } else {
                result = null; acceptedGeneration = -1; frozenRequestFingerprint = null;
                if (activity is { } ended) localTasks?.Finish(ended, outcome.State == RefinementRequestState.Cancelled ? LocalTaskPhase.Cancelled : LocalTaskPhase.Failed, "Refinement ended without a proposal; original unchanged.");
                after.Text = original; score.Text = "Original retained"; changes.Text = resultDetails.Text = ""; progress.Text = outcome.Message;
            }
        } catch (Exception ex) {
            if (activity is { } failed) localTasks?.Finish(failed, ex is OperationCanceledException ? LocalTaskPhase.Cancelled : LocalTaskPhase.Failed, "Refinement did not finish; original unchanged.");
            if (generation == current && !closed) { result = null; acceptedGeneration = -1; frozenRequestFingerprint = null; progress.Text = ex is OperationCanceledException ? "Stopped. Original unchanged." : ex.Message; }
        } finally {
            if (ReferenceEquals(refinement, request)) { refinement = null; mood?.Invoke(CompanionMood.Idle); }
            request.Dispose(); UpdateControls();
        }
    }
    internal void Cancel()
    {
        Options.CancelResourceRead(true); InvalidateResult(); UpdateControls();
        if (!closed) progress.Text = applying ? "Stop requested. Checking the result of the already approved edit; verify the draft before retrying." : applied ? "Stopped. Undo remains available for 30 seconds after applying." : "Stopped. Original unchanged. Choose a mode to try again.";
    }
    private async Task Apply()
    {
        if (!CanApply || result is not { } completed) return;
        int current = generation; string fingerprint = frozenRequestFingerprint!;
        // A failed attempt is historical. A user-approved retry needs its own
        // current token so a later successful write can report its real outcome.
        if (localTasks is not null && (displayedTask is not { } prior ||
            !localTasks.Snapshot.Tasks.Any(item => item.Token == prior && !item.IsTerminal)))
            displayedTask = localTasks.Begin("refine", "Apply reviewed Buddy draft", "Applying the reviewed change without sending.");
        var activity = displayedTask;
        if (activity is { } pending) localTasks?.Update(pending, "Applying the reviewed change without sending.");
        applying = true; UpdateControls();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3)); operation = cts;
        try {
            cts.Token.ThrowIfCancellationRequested();
            if (closed || generation != current || fingerprint != RefinementDraftOptions.Fingerprint(Options.Snapshot().ToRequest(original, selectedMode))) {
                if (activity is { } stale) localTasks?.Finish(stale, LocalTaskPhase.Cancelled, "The review changed before Apply; no edit dispatched.");
                return;
            }
            await applyEdit(completed.RefinedPrompt, cts.Token);
            // A completed guarded write retains Undo even if Stop arrived at completion.
            applied = true;
            if (activity is { } appliedTask) localTasks?.Finish(appliedTask, LocalTaskPhase.Completed, "Reviewed edit applied to the Buddy draft without sending.", observedStep: true);
            if (!closed) { undo.IsEnabled = true; progress.Text = "Applied without sending. Undo is available for 30 seconds."; }
        } catch (Exception ex) {
            if (activity is { } failed) localTasks?.Finish(failed, ex is OperationCanceledException ? LocalTaskPhase.Cancelled : LocalTaskPhase.Failed, "Edit did not report completion; verify the current Buddy draft before retrying.");
            if (!closed) progress.Text = ex.Message + " Use Copy instead.";
        }
        finally { applying = false; if (ReferenceEquals(operation, cts)) operation = null; UpdateControls(); }
    }
    private async Task Undo()
    {
        if (closed || !applied || applying) return;
        applying = true; using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3)); operation = cts; undo.IsEnabled = false; UpdateControls();
        var activity = localTasks?.Begin("refine", "Undo Buddy draft edit", "Checking the original draft before Undo."); displayedTask = activity;
        try {
            await undoEdit(cts.Token); applied = false;
            if (activity is { } restored) localTasks?.Finish(restored, LocalTaskPhase.Completed, "Exact original Buddy draft restored.", observedStep: true);
            InvalidateResult(); if (!closed) progress.Text = "Exact original restored. Choose a mode to prepare another result.";
        }
        catch (Exception ex) { if (activity is { } failed) localTasks?.Finish(failed, ex is OperationCanceledException ? LocalTaskPhase.Cancelled : LocalTaskPhase.Failed, "Undo did not complete; review the current draft."); if (!closed) progress.Text = ex.Message + " Copy original remains available."; }
        finally { applying = false; if (ReferenceEquals(operation, cts)) operation = null; UpdateControls(); }
    }
    internal bool OpenTaskSource(LocalTaskToken token)
    {
        if (closed || displayedTask != token || localTasks?.Snapshot.Tasks.Any(item => item.Token == token) != true) return false;
        Show(); Activate(); return true; // Only this Buddy-owned draft review, never an external field.
    }
    private static TextBlock Label(string text, double size) => new() { Text = text, FontSize = size, Foreground = BuddyTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) };
    private static TextBox Editor(string text) => new() { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100, MaxHeight = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(0, 0, 0, 12) };
}
