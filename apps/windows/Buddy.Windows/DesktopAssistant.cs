using Buddy.Server;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;

namespace Buddy.Windows;

// A text edit followed by Undo is still a new request revision. Captured plans
// cannot regain authority merely because the visible words happen to match.
internal sealed record AssistantTaskBinding(long Revision, string Query, string Mode)
{
    internal bool Matches(long revision, string query, string mode) =>
        Revision == revision && Query.Equals(query.Trim(), StringComparison.Ordinal) && Mode == mode;
}

internal sealed class DesktopAssistant : IDisposable
{
    private readonly Func<BuddyService?> service;
    private readonly Func<DesktopPreferences> preferences;
    private readonly Func<IntPtr> target;
    private readonly Func<WindowSelection?>? selectedWindow;
    private readonly Action<CompanionMood> mood;
    private readonly Action? starting;
    internal ScreenPerception Perception { get; }
    private readonly VisualGrounding visualGrounding;
    private bool visualGuide;
    private int visualAttempts;
    private readonly GuidanceOverlay overlay;
    private readonly DispatcherTimer stopMonitor = new() { Interval = TimeSpan.FromMilliseconds(25) };
    private readonly DispatcherTimer guideMonitor = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private Window? panel;
    private readonly TextBlock modeLabel = new() { Foreground = BuddyTheme.Ink, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new(0, 0, 0, 8) };
    private readonly TextBlock state = new() { Foreground = BuddyTheme.Deep, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TextBlock selectedApp = new() { Foreground = BuddyTheme.Deep, TextWrapping = TextWrapping.Wrap, FontSize = 12, Text = "Selected app: unavailable" };
    private readonly TextBlock observationScope = new() { Foreground = BuddyTheme.Deep, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TextBox goal = new() { TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, Height = 66, MaxLength = 4000 };
    private readonly TextBox planText = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Background = Brushes.Transparent, Foreground = BuddyTheme.Ink, BorderThickness = new(0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 190 };
    private readonly StackPanel planCards = new();
    private readonly TextBlock stepText = new() { Foreground = BuddyTheme.Ink, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new(0,8,0,8) };
    private ExecutionBanner? executionBanner;
    private readonly StackPanel sourceLinks = new();
    private readonly TextBlock specialistStatus=new(){TextWrapping=TextWrapping.Wrap,Foreground=BuddyTheme.Deep,FontSize=12};
    private readonly TextBlock teachingUpdate=new(){TextWrapping=TextWrapping.Wrap,Foreground=BuddyTheme.Ink,FontSize=13};
    private readonly Dictionary<int,ExecutionJob> executionJobs=[];
    internal IReadOnlyList<ExecutionJob> ExecutionSnapshot=>executionJobs.Values.OrderBy(j=>j.Id).ToArray();
    private void ShowExecutionJobs()=>specialistStatus.Text=string.Join("\n",ExecutionSnapshot.TakeLast(8).Select(j=>$"Job {j.Id}: {j.Specialist} — {j.State}"));
    private Button? run, next, back, skip, approve, decline, undo;
    private CancellationTokenSource? operation;
    private CancellationTokenSource? guideCheck;
    private readonly SemaphoreSlim actionGate = new(1, 1);
    private TaskCompletionSource<bool>? approval;
    private AssistantPlan? plan;
    private AssistantTaskBinding? plannedTask;
    private long taskRevision;
    private GuidePlan? guide;
    private string mode = "guide", guideId = "";
    private int index;
    private int guideRevision, guideMisses, guideReplans, expectationMatches;
    private bool checkingGuide, expectationArmed;
    private GuideReviewProgress guideProgress = new();
    private ScreenElement? drawnGuideTarget;
    private IntPtr sourceWindow;
    private WindowSelection? sourceSelection;
    private bool executing;
    private readonly JobLedger? jobs;
    private string? job;
    private DispatchInterruption? interruption;
    private (AutomationElement Node, string Before, string After, DateTimeOffset At, IntPtr Window)? edit;
    internal bool IsActive => panel?.IsVisible == true;
    internal string CurrentTask => goal.Text;
    internal string CurrentStatus => state.Text;
    internal Action? DrawRegionRequested { get; set; }
    private int LessonCount => guide?.Lessons?.Count ?? guide?.Steps?.Count ?? 0;

    internal DesktopAssistant(Func<BuddyService?> service, Func<DesktopPreferences> preferences, Func<IntPtr> target, Action<CompanionMood> mood, Action<ScreenElement?>? pointing = null, Action? starting = null, JobLedger? jobs = null, Func<WindowSelection?>? selectedWindow = null)
    {
        this.service = service; this.preferences = preferences; this.target = target; this.mood = mood; Perception = new(preferences); visualGrounding = new(Perception, service);
        overlay = new(() => preferences().InkLifetimeSeconds);
        this.starting = starting; this.jobs=jobs; this.selectedWindow = selectedWindow; if (pointing is not null) overlay.TargetChanged += pointing;
        guideMonitor.Tick += async (_, _) => await CheckGuideProgress();
        stopMonitor.Tick += (_, _) => {
            if ((InputNative.GetAsyncKeyState(27) & 0x8000) != 0) Cancel();
        };
    }
    internal async Task Open(string requestedMode, string query)
    {
        if (executing) { Cancel(); return; }
        starting?.Invoke();
        Cancel(); mode = requestedMode; sourceWindow = IntPtr.Zero; sourceSelection = null; plan = null; guide = null; guideReplans = guideMisses = 0;
        EnsurePanel(); specialistStatus.Text=teachingUpdate.Text=""; goal.Text = query; planText.Clear(); planCards.Children.Clear(); sourceLinks.Children.Clear(); stepText.Text = ""; panel!.Title = mode == "agent" ? "Buddy · Agent plan" : "Buddy · Guide"; panel.Width = mode == "agent" ? 420 : 360;
        run!.Visibility = mode == "agent" ? Visibility.Visible : Visibility.Collapsed; run.IsEnabled = false;
        back!.Visibility = next!.Visibility = mode == "guide" ? Visibility.Visible : Visibility.Collapsed;
        skip!.Visibility = back.Visibility; skip.IsEnabled = false;
        ApplyModePresentation();
        back.IsEnabled = next.IsEnabled = false; panel.Show(); panel.Activate(); stopMonitor.Start();
        if (query.Trim().Length > 0) await Plan();
        else {
            try { SelectTarget(); state.Text = mode == "agent" ? "Describe a task. Buddy shows its plan before acting." : "Describe what to learn in the selected app."; }
            catch (InvalidOperationException ex) { ShowUnavailable(ex); }
        }
    }
    private void EnsurePanel()
    {
        if (panel is not null) return;
        BuddyTheme.Ensure();
        panel = new Window { Width = 420, Height = Math.Min(620, SystemParameters.WorkArea.Height), Topmost = true, ShowInTaskbar = false, Background = BuddyTheme.Surface, Foreground = BuddyTheme.Ink, FontFamily = BuddyTheme.Font, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        var p = new StackPanel { Margin = new(20) }; p.Children.Add(modeLabel); p.Children.Add(selectedApp); p.Children.Add(observationScope); p.Children.Add(state); p.Children.Add(goal);
        AutomationProperties.SetName(selectedApp, "Selected app");
        AutomationProperties.SetName(observationScope, "Observation scope");
        AutomationProperties.SetName(goal, "Task to plan"); AutomationProperties.SetLiveSetting(modeLabel, AutomationLiveSetting.Polite);
        Button Add(string label, Action action, Panel parent) { var b = new Button { Content = label, Padding = new(10,6,10,6), Margin = new(0,8,6,0) }; b.Click += (_, _) => action(); parent.Children.Add(b); return b; }
        var commands = new WrapPanel(); Add("Make plan", () => _ = Plan(), commands); run = Add("Run this plan", () => _ = Execute(), commands); Add("Draw an area", () => { Cancel(); DrawRegionRequested?.Invoke(); }, commands); Add("Stop", Cancel, commands); p.Children.Add(commands);
        p.Children.Add(planText); p.Children.Add(planCards); p.Children.Add(sourceLinks); p.Children.Add(specialistStatus); p.Children.Add(teachingUpdate); p.Children.Add(stepText);
        var navigation = new WrapPanel(); back = Add("Back", () => _ = GuideStep(-1), navigation); next = Add("Next", () => _ = AdvanceGuide(false,false), navigation);
        skip = Add("Skip", () => _ = AdvanceGuide(false,true), navigation);
        approve = Add("Allow this step", () => approval?.TrySetResult(true), navigation); approve.Visibility = Visibility.Collapsed;
        decline = Add("Decline this step", () => approval?.TrySetResult(false), navigation); decline.Visibility = Visibility.Collapsed;
        undo = Add("Undo last edit", () => _ = Undo(), navigation); undo.Visibility = Visibility.Collapsed;
        p.Children.Add(navigation); panel.Content = new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Closing += (_, e) => { e.Cancel = true; Cancel(); panel.Hide(); stopMonitor.Stop(); };
        panel.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; Cancel(); } };
        panel.SourceInitialized += (_, _) => CaptureProtection.Apply(new WindowInteropHelper(panel).Handle);
        goal.TextChanged += (_, _) => {
            taskRevision++;
            if (executing || operation is not null) Cancel();
            plan = null; plannedTask = null; guide = null; observationScope.Text = ""; overlay.Clear(); guideMonitor.Stop();
            run!.IsEnabled = back!.IsEnabled = next!.IsEnabled = skip!.IsEnabled = false;
            planCards.Children.Clear(); planText.Clear(); stepText.Text = "";
            state.Text = "Task changed. Choose Make plan to review it.";
        };
    }
    private void ApplyModePresentation()
    {
        bool agent = mode == "agent";
        panel!.Title = agent ? "Buddy - Agent plan" : "Buddy - Guide"; panel.Width = agent ? 420 : 360;
        modeLabel.Text = agent ? "Agent - review actions before Run" : "Guide - follow the steps yourself";
        run!.Visibility = agent ? Visibility.Visible : Visibility.Collapsed;
        back!.Visibility = next!.Visibility = skip!.Visibility = agent ? Visibility.Collapsed : Visibility.Visible;
    }
    private async Task Plan()
    {
        if (executing) return; Cancel(); var host = service(); if (host is null) return;
        starting?.Invoke();
        var task = goal.Text.Trim(); if (task.Length == 0) { state.Text = "Describe the task first."; return; }
        mode = AssistantIntent.PlanningMode(mode, task); ApplyModePresentation();
        var binding = new AssistantTaskBinding(taskRevision, task, mode);
        plan = null; guide = null; job = null; planCards.Children.Clear(); planText.Clear(); stepText.Text = ""; sourceLinks.Children.Clear();
        run!.IsEnabled = false; next!.IsEnabled = back!.IsEnabled = skip!.IsEnabled = false;
        var cts = new CancellationTokenSource(); operation = cts; string? plannedJob=null;
        try {
            SelectTarget();
            if(mode=="agent") job=plannedJob=jobs?.Begin("action",task,InputNative.ProcessName(sourceWindow));
            if (mode == "agent" && !preferences().AgentEnabled) throw new InvalidOperationException("Agent mode is off. Enable it in Home → Assistant settings, or choose Guide.");
            state.Text = "● Looking at the selected window…"; mood(CompanionMood.Looking);
            ScreenContext context; string captureIssue=""; bool browserChromeVerified = false;
            try {
                var captured = await CaptureSelected(cts.Token, GuideSafety.RequestedText(task));
                RequireCurrentTask(binding, cts.Token);
                ShowObservationScope(captured); context = captured.Context;
                if (mode == "guide" && context.Elements.Count == 0) {
                    ShowUnavailable(new WindowSelectionException("no-controls", "The selected app exposed no accessible controls. Open the intended view and try again.", sourceWindow));
                    return;
                }
                if (mode == "guide" && BrowserGuideLessons.IsSupported(new(task, context))) {
                    context = await BrowserChromeObservation.Qualify(captured, cts.Token);
                    RequireCurrentTask(binding, cts.Token); browserChromeVerified = true;
                }
                await host.Audit("capture", context.App, "UIA planning snapshot; no image stored");
                RequireCurrentTask(binding, cts.Token);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) {
                if (mode == "guide") { ShowUnavailable(ex); return; }
                context = new("", "No accessible window. No current controls were observed.", []); captureIssue=ex.Message;
            }
            state.Text = "Planning on your PC…"; mood(CompanionMood.Thinking);
            if (mode == "agent") {
                PreparedWorkflow? prepared=null;
                AssistantPlan generatedPlan;
                if(SpecialistWork.Requested(task)) {
                    var states=new Dictionary<string,string>();
                    var progress=new Progress<SpecialistStatus>(item=>{if(cts.IsCancellationRequested||!ReferenceEquals(operation,cts))return;states[item.Name]=item.State;state.Text=string.Join(" | ",states.Select(p=>p.Key+": "+p.Value));if(plannedJob is not null)jobs?.Progress(plannedJob,state.Text);});
                    prepared=await host.PrepareWorkflow(new(task,context,preferences().AllowWebResearch),progress,cts.Token);generatedPlan=prepared.Plan;
                } else generatedPlan = await host.PlanAgent(new(task, context, preferences().AllowWebResearch), cts.Token);
                RequireCurrentTask(binding, cts.Token);
                if (!ReferenceEquals(operation, cts)) throw new OperationCanceledException(cts.Token);
                // Prepared workflows and direct responses share host request
                // binding before any review card can enable Run.
                plan = ActionPolicy.ValidateForReview(generatedPlan, binding.Query);
                plannedTask = binding;
                if (plan.Actions is { Count: 0 }) {
                    planText.Text = plan.Summary;
                    state.Text = "Clarification needed - no actions are ready to run.";
                    stepText.Text = "Answer the question in the task box, then choose Make plan.";
                    run.IsEnabled = false; mood(CompanionMood.Unsure);
                    if (plannedJob is not null) jobs!.Move(plannedJob, JobState.ReviewNeeded, plan.Summary);
                    return;
                }
                planText.Text = plan.Summary + "\n\n" + string.Join("\n", plan.Actions!.Select((a,i) => $"{i+1}. [{a.Risk.ToUpperInvariant()} · {a.Kind}] {a.Description}\n    {a.Target} {a.Value}"));
                ShowPlanCards(plan.Actions!); planText.Text = plan.Summary + string.Concat(plan.Actions!.Where(a=>a.Kind=="type").Select(a=>"\n\nText to insert (review before Run):\n"+a.Value));
                if(prepared is not null) {
                    stepText.Text="Teacher: "+prepared.Lesson.Summary+"\n"+string.Join("\n",prepared.Lesson.Lessons?.Select(l=>l.Instruction)??prepared.Lesson.Steps?.Select(s=>s.Instruction)??[])+$"\nImported-notes researcher: {prepared.Notes.Count} matching excerpts. Preparation is complete; no action has run.";
                    SourceLinks.Fill(sourceLinks,new MessageEvidence(Sources:prepared.Lesson.Sources?.Select(s=>new SourceLink(s.Title,s.Url)).ToList()));
                }
                run.IsEnabled = true; state.Text = "Review the plan. Consequential or uncertain actions pause for approval.";
                if(plannedJob is not null)jobs!.Move(plannedJob,JobState.AwaitingApproval,"Plan ready. Use Run this plan, then approve each consequential step.");
            } else {
                var generatedGuide = await host.PlanGuide(new(task, context, preferences().AllowWebResearch), cts.Token, browserChromeVerified);
                RequireCurrentTask(binding, cts.Token);
                if (!ReferenceEquals(operation, cts)) throw new OperationCanceledException(cts.Token);
                guide = generatedGuide; guideId = Guid.NewGuid().ToString(); index = 0; guideProgress = new();
                planText.Text = guide.Summary;
                SourceLinks.Fill(sourceLinks,new MessageEvidence(Sources:guide.Sources?.Select(s=>new SourceLink(s.Title,s.Url)).ToList()));
                if(LessonCount==0){next.IsEnabled=skip!.IsEnabled=false;state.Text="General guidance - no pointer verified on this screen.";stepText.Text=captureIssue.Length>0?"Screen observation unavailable: "+captureIssue:"Focus the intended app and choose Make plan for a fresh observation, or Draw an area to explain a selected region.";mood(CompanionMood.Unsure);}
                else {next.IsEnabled=true;await DrawStep(cts.Token);}
            }
            if (mode == "agent") mood(CompanionMood.Idle);
        } catch (OperationCanceledException) { if(ReferenceEquals(operation,cts))state.Text = "Stopped"; if(plannedJob is not null)jobs!.Move(plannedJob,JobState.Cancelled,"Planning cancelled; no actions ran."); }
        catch (Exception ex) { if(ReferenceEquals(operation,cts)){if(mode=="guide")ShowUnavailable(ex);else{state.Text = ex.Message; mood(CompanionMood.Unsure);}} if(plannedJob is not null)jobs!.Move(plannedJob,JobState.Failed,ex.Message); }
        finally { if (ReferenceEquals(operation, cts)) operation = null; cts.Dispose(); }
    }
    private void RequireCurrentTask(AssistantTaskBinding binding, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!binding.Matches(taskRevision, goal.Text, mode))
            throw new InvalidOperationException("The task changed. Choose Make plan and review a fresh plan before any action.");
    }
    private void SelectTarget()
    {
        sourceSelection = null; sourceWindow = IntPtr.Zero; selectedApp.Text = "Selected app: unavailable"; observationScope.Text = "";
        // A supplied tracker is authoritative. Never fall back to the raw cached HWND on refusal.
        var selected = selectedWindow is null ? WindowSelection.Capture(target()) : selectedWindow();
        if (selected is null) throw new WindowSelectionException("no-selection", "No app is selected. " + WindowSelection.Refocus);
        selected.Validate(); selectedApp.Text = "Selected app: " + selected.App; Perception.Check(selected.Window); selected.Validate();
        sourceSelection = selected; sourceWindow = selected.Window; selectedApp.Text = "Selected app: " + selected.App;
    }
    private Task<ScreenSnapshot> CaptureSelected(CancellationToken ct, string? requestedText = null)
    {
        var selected = sourceSelection ?? throw new WindowSelectionException("no-selection", "No app is selected. " + WindowSelection.Refocus);
        return Perception.Capture(sourceWindow, ct, requestedText, selected);
    }
    private void ShowUnavailable(Exception error)
    {
        observationScope.Text = "";
        overlay.Clear(); guideMonitor.Stop(); plan = null; plannedTask = null; guide = null;
        run!.IsEnabled = next!.IsEnabled = back!.IsEnabled = skip!.IsEnabled = false;
        planText.Clear(); planCards.Children.Clear(); sourceLinks.Children.Clear();
        state.Text = "Screen observation unavailable: " + error.Message;
        stepText.Text = "No guide or pointer was prepared. " + WindowSelection.Refocus;
        mood(CompanionMood.Unsure);
    }
    private void ShowObservationScope(ScreenSnapshot snapshot)
    {
        observationScope.Text = mode == "guide" && !snapshot.Complete && snapshot.Context.Elements.Count > 0
            ? "Some controls could not be read; guidance uses the controls observed." : "";
    }
    private async Task AdvanceGuide(bool verified,bool skipped){
        if(LessonCount==0||operation is not null)return;
        guideProgress.Advance(index,verified,skipped);await GuideStep(1);
    }
    private async Task GuideStep(int direction)
    {
        if (LessonCount==0 || operation is not null) return;
        guideRevision++; guideCheck?.Cancel(); expectationArmed = false; expectationMatches = 0;
        if (index + direction >= LessonCount) { await FinishGuide(); return; }
        index = Math.Clamp(index + direction, 0, LessonCount - 1);
        var cts = new CancellationTokenSource(); operation = cts;
        try { await DrawStep(cts.Token); } catch (OperationCanceledException) { state.Text = "Stopped"; } catch (Exception ex) { ShowUnavailable(ex); }
        finally { if (ReferenceEquals(operation, cts)) operation = null; cts.Dispose(); }
    }
    private async Task DrawStep(CancellationToken ct)
    {
        if(guide?.Lessons is {Count:>0}) { await DrawLesson(ct); return; }
        SourceLinks.Fill(sourceLinks, new MessageEvidence(Sources: guide?.Sources?.Select(s => new SourceLink(s.Title, s.Url)).ToList()));
        var step = guide!.Steps![index]; overlay.Clear(); visualGuide = false; visualAttempts = 0; stepText.Text = step.Instruction;
        state.Text = $"Guide · step {index+1} of {guide.Steps.Count}"; back!.IsEnabled = index > 0; next!.IsEnabled = skip!.IsEnabled = true; next.Content = index + 1 == guide.Steps.Count ? "Finish review" : "Next (reviewed)";
        // Keep the selected window pinned; a different foreground app is not a new target.
        var snapshot = await CaptureSelected(ct, GuideSafety.RequestedText(goal.Text)); ct.ThrowIfCancellationRequested(); ShowObservationScope(snapshot);
        var element = GroundingResolver.Resolve(snapshot.Context.Elements, step.Ref, step.Target, step.Role)
            ?? GroundingResolver.Resolve(snapshot.Context.Elements, "", step.Target, step.Role);
        expectationArmed = snapshot.Complete && !GuideExpectations.Matches(step.Expect, snapshot.Context); expectationMatches = 0;
        if (element is null && !await DrawVisualStep(snapshot, step, ct)) { guideMisses++; state.Text += " · target not verified; focus or scroll the app, then choose Make plan."; mood(CompanionMood.Unsure); }
        else if (element is not null) { guideMisses = 0; Draw(snapshot, element, step.Primitive, step.Instruction); mood(CompanionMood.Pointing); }
        guideMonitor.Start();
        if (service() is { } host) await host.Store.Update(s => { s.Guides.RemoveAll(g => g.Id == guideId); s.Guides.Add(new(guideId, goal.Text, guide, index, DateTimeOffset.UtcNow)); if (s.Guides.Count > 20) s.Guides.RemoveAt(0); return true; });
    }
    private async Task DrawLesson(CancellationToken ct)
    {
        var lesson=guide!.Lessons![index]; overlay.Clear(); guideMonitor.Stop();
        stepText.Text=lesson.Instruction; back!.IsEnabled=index>0; next!.IsEnabled=skip!.IsEnabled=true;
        next.Content=index+1==LessonCount?"Finish review":"Next section";
        state.Text=$"Lesson {index+1} of {LessonCount} - general instructions; no pointer verified.";
        try {
            var foreground=Native.GetForegroundWindow();
            if(foreground!=sourceWindow&&!Native.IsOwnWindow(foreground)) { state.Text+=" Return to the selected app to show its controls."; return; }
            var snapshot=await CaptureSelected(ct);ct.ThrowIfCancellationRequested();ShowObservationScope(snapshot);
            var lessonContext = snapshot.Context;
            if (BrowserGuideLessons.IsSupported(new(goal.Text, lessonContext))) lessonContext = await BrowserChromeObservation.Qualify(snapshot, ct);
            ct.ThrowIfCancellationRequested();
            var element=GuideLessons.Resolve(lesson,lessonContext,goal.Text);
            if(element is not null) { Draw(snapshot,element,"ring",lesson.Target);state.Text=$"Lesson {index+1} of {LessonCount} - pointer verified in {snapshot.Context.App}; your turn.";mood(CompanionMood.Pointing); }
            else { mood(CompanionMood.Idle);state.Text+=" Open the relevant view, then revisit this section, or Draw an area."; }
        } catch(Exception ex) when(ex is not OperationCanceledException) { ShowUnavailable(ex); return; }
        if(service() is {} host) await host.Store.Update(s=>{s.Guides.RemoveAll(g=>g.Id==guideId);s.Guides.Add(new(guideId,goal.Text,guide,index,DateTimeOffset.UtcNow));if(s.Guides.Count>20)s.Guides.RemoveAt(0);return true;});
    }
    private async Task FinishGuide()
    {
        guideMonitor.Stop(); overlay.Clear(); mood(CompanionMood.Idle); state.Text = guideProgress.EndMessage(LessonCount); next!.IsEnabled = skip!.IsEnabled = false;
        if (service() is { } host) await host.Store.Update(s => { int i = s.Guides.FindIndex(g => g.Id == guideId); if (i >= 0) s.Guides[i] = s.Guides[i] with { Completed = guideProgress.AllObserved(LessonCount), UpdatedAt = DateTimeOffset.UtcNow }; return true; });
    }
    private async Task CheckGuideProgress()
    {
        if (checkingGuide || operation is not null || mode != "guide" || guide?.Steps is null || guide.Steps.Count==0 || panel?.IsVisible != true || Native.GetForegroundWindow() != sourceWindow) return;
        checkingGuide = true; int revision = guideRevision;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3)); guideCheck = timeout;
        try {
            var snapshot = await CaptureSelected(timeout.Token, GuideSafety.RequestedText(goal.Text));
            if (revision != guideRevision || operation is not null || guide?.Steps is null) return;
            ShowObservationScope(snapshot);
            var step = guide.Steps[index]; var matches = snapshot.Complete && GuideExpectations.Matches(step.Expect, snapshot.Context);
            if (snapshot.Complete && !matches) expectationArmed = true;
            expectationMatches = expectationArmed && matches ? expectationMatches + 1 : 0;
            if (preferences().GuideAutoAdvance && expectationMatches >= 2) { await AdvanceGuide(true,false); return; }
            var element = GroundingResolver.Resolve(snapshot.Context.Elements, step.Ref, step.Target, step.Role) ?? GroundingResolver.Resolve(snapshot.Context.Elements, "", step.Target, step.Role);
            if (element is not null) { visualGuide = false; guideMisses = 0; if (!overlay.IsVisible || drawnGuideTarget != element) Draw(snapshot, element, step.Primitive, step.Instruction); }
            else if (visualGuide && overlay.IsVisible && drawnGuideTarget is { } visual && await visualGrounding.StillVisible(snapshot, visual, step.Role, timeout.Token)) { return; }
            else if (await DrawVisualStep(snapshot, step, timeout.Token)) { return; }
            else {
                overlay.Clear(); mood(CompanionMood.Unsure);
                if (++guideMisses >= 2 && guideReplans < 3 && service() is { } host) {
                    guideReplans++; guideMisses = 0;
                    state.Text = "The interface changed. Updating the remaining guide…";
                    var updated = await host.PlanGuide(new(goal.Text + "\nPreviously shown suggestions (not proof of completion): " + string.Join("; ", guide.Steps.Take(index).Select(s => s.Instruction)), snapshot.Context), timeout.Token);
                    if (revision != guideRevision) return;
                    guide = updated; index = 0; guideProgress=new(); guideRevision++; if(guide.Steps?.Count>0)await DrawStep(timeout.Token);else{guideMonitor.Stop();state.Text=guide.Summary;}
                } else state.Text = "I cannot identify the next control. Focus or scroll the app, or use Next / Skip.";
            }
        } catch (OperationCanceledException) { overlay.Clear(); }
        catch (Exception ex) { ShowUnavailable(ex); }
        finally { if (ReferenceEquals(guideCheck, timeout)) guideCheck = null; checkingGuide = false; }
    }
    private void Draw(ScreenSnapshot snapshot, ScreenElement element, string primitive, string label)
    {
        drawnGuideTarget = element;
        if(snapshot.WordTargets?.TryGetValue(element.Ref,out var word)==true){overlay.Draw(snapshot.Window,element,primitive,label,()=>{try{snapshot.Selection?.Validate();return word.IsCurrent(element);}catch{return false;}});return;}
        var node = snapshot.Nodes[element.Ref]; var original = new Rect(element.X, element.Y, element.Width, element.Height);
        overlay.Draw(snapshot.Window, element, primitive, label, () => { try { snapshot.Selection?.Validate(); return !node.Current.IsOffscreen && node.Current.BoundingRectangle == original; } catch { return false; } });
    }
    private async Task<bool> DrawVisualStep(ScreenSnapshot snapshot, GuideStep step, CancellationToken ct)
    {
        if (!snapshot.Complete || Native.GetForegroundWindow() != snapshot.Window || string.IsNullOrWhiteSpace(step.Target) || visualAttempts >= 3) return false;
        overlay.Clear();
        int revision = guideRevision; visualAttempts++;
        state.Text = "Looking for a verified visual label on this PC…"; mood(CompanionMood.Looking);
        var grounded = await visualGrounding.Resolve(snapshot, step, ct); ct.ThrowIfCancellationRequested();
        if (revision != guideRevision) throw new OperationCanceledException("The guide changed while grounding the target.");
        if (grounded is null || grounded.CanExecute) return false;
        var bounds = WindowCapture.Bounds(snapshot.Window); var title = snapshot.Context.Title;
        overlay.Draw(snapshot.Window, grounded.Element, step.Primitive, step.Instruction, () => {
            try { snapshot.Selection?.Validate(); return WindowCapture.Bounds(snapshot.Window) == bounds && Security.Redact(Native.Label(snapshot.Window)) == title; } catch { return false; }
        });
        visualGuide = true; guideMisses = 0; mood(CompanionMood.Pointing);
        drawnGuideTarget = grounded.Element;
        state.Text = $"Guide · step {index+1} of {guide!.Steps!.Count} · visual label, manual action";
        return true;
    }
    private async Task<bool> Confirm(string text, CancellationToken ct)
    {
        executionBanner?.Hide();
        interruption?.DisarmPointer(); state.Text = "Approval needed · " + text; approve!.Visibility = Visibility.Visible;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); approval = completion;
        decline!.Visibility=Visibility.Visible;
        using var registration = ct.Register(() => completion.TrySetCanceled(ct));
        if(job is not null)jobs?.Move(job,JobState.AwaitingApproval,text);
        try { var allowed=await completion.Task; if(job is not null){if(allowed)jobs?.Approve(job);else jobs?.Move(job,JobState.Cancelled,"Approval declined; this step was not executed.");} if(!allowed)state.Text="Approval declined; this step was not executed.";return allowed; } finally { if (ReferenceEquals(approval, completion)) approval = null; approve.Visibility = decline.Visibility = Visibility.Collapsed; }
    }
    private async Task Execute()
    {
        if (mode != "agent" || plan?.Actions is null || executing || operation is not null) return;
        if (plan.Actions.Count == 0) { run!.IsEnabled = false; state.Text = "Clarification needed - no actions are ready to run."; return; }
        var binding = plannedTask;
        if (binding is null || !binding.Matches(taskRevision, goal.Text, mode)) {
            plan = null; plannedTask = null; run!.IsEnabled = false;
            state.Text = "The task changed or its plan expired. Choose Make plan to review a fresh plan."; return;
        }
        AssistantPlan approvedPlan;
        try { approvedPlan = ActionPolicy.Validate(plan, binding.Query); }
        catch (BuddyException ex) { run!.IsEnabled = false; state.Text = ex.Message; mood(CompanionMood.Unsure); return; }
        if(job is not null&&jobs?.Snapshot.FirstOrDefault(j=>j.Id==job)?.State!=JobState.AwaitingApproval){state.Text="This plan was stopped or expired. Choose Make plan to review a fresh job.";run!.IsEnabled=false;return;}
        starting?.Invoke();
        var host = service()!; var cts = new CancellationTokenSource(); operation = cts; executing = true; run!.IsEnabled = false;
        var results = new List<ActionResult>(); int replans = 0; var runningJob=job; bool framedBatchEnded = false;
        executionJobs.Clear();using var specialists=new ExecutionSpecialists(new Progress<ExecutionJob>(item=>{
            if(cts.IsCancellationRequested || !ReferenceEquals(operation,cts) || !binding.Matches(taskRevision,goal.Text,mode))return;
            executionJobs[item.Id]=item;ShowExecutionJobs();
            if(runningJob is not null)jobs?.Progress(runningJob,$"Job {item.Id}: {item.Specialist} — {item.State}");
        }));
        if(runningJob is not null)jobs?.Approve(runningJob);
        interruption = new DispatchInterruption(() => {
            if (!OverlayNative.GetCursorPos(out var position)) throw new InvalidOperationException("Pointer unavailable.");
            return new(position.X, position.Y, (InputNative.GetAsyncKeyState(27) & 0x8000) != 0);
        }, reason => { cts.Cancel(); panel!.Dispatcher.BeginInvoke(new Action(() => { executionBanner?.Hide(); state.Text = reason; })); });
        try {
            var pending = new Queue<AssistantAction>(approvedPlan.Actions!);
            while (pending.Count > 0 && results.Count < 25) {
                RequireCurrentTask(binding, cts.Token); if (!preferences().AgentEnabled) throw new InvalidOperationException("Agent mode was disabled.");
                var action = pending.Dequeue(); state.Text = $"Buddy is controlling — Esc to stop · action {results.Count+1}/25"; stepText.Text = action.Description; mood(CompanionMood.AgentWorking);
                state.Text = "Buddy is controlling — Esc to stop · " + action.Description;
                if(runningJob is not null)jobs?.Progress(runningJob,$"Action {results.Count+1}/25: {action.Description}");
                bool framedLaunch = false;
                var receipt=await specialists.Dispatch(action,results.Count+1,token=>DispatchAssignment(binding,action,results.Count+1,token, () => framedLaunch = true),cts.Token);
                RequireCurrentTask(binding, cts.Token);
                if(receipt is null)break;
                results.Add(receipt);if(!receipt.Success)pending.Clear();
                executionBanner?.Hide(); interruption.DisarmPointer(); overlay.Clear();
                if (framedLaunch) {
                    // A verified app launch grants no authority to inspect or act
                    // through its host frame. End before CaptureSelected/replanning.
                    var completion = FramedLaunchCompletion.Decide(binding.Query, approvedPlan.Actions!, receipt, results.Count, pending.Count);
                    pending.Clear(); plan = null; plannedTask = null;
                    sourceSelection = null; sourceWindow = IntPtr.Zero; framedBatchEnded = true;
                    selectedApp.Text = "Choose an app again for further work.";
                    if(runningJob is not null)jobs?.Receipt(runningJob,receipt.Observation,receipt.Success);
                    await FramedLaunchCompletion.AfterAudit(
                        () => host.Audit(action.Kind, action.Target, receipt.Success ? "completed" : "not executed: target unavailable"),
                        () => ReferenceEquals(operation, cts) && binding.Matches(taskRevision, goal.Text, mode),
                        () => {
                            state.Text = completion.Message;
                            if (runningJob is not null) {
                                if (completion.Completed) jobs?.Move(runningJob, JobState.Verifying, "Verified the requested framed application.");
                                jobs?.Move(runningJob, completion.Completed ? JobState.Completed : JobState.ReviewNeeded, completion.Message);
                            }
                        }, cts.Token);
                    break;
                }
                await host.Audit(action.Kind, action.Target, results[^1].Success ? "completed" : "not executed: target unavailable");
                if(runningJob is not null)jobs?.Receipt(runningJob,results[^1].Observation,results[^1].Success);
                // Observe actual state before completing a batch or proposing its replacement.
                var observed = await specialists.Read("Observer",results.Count,token=>CaptureSelected(token),cts.Token);
                if (pending.Count == 0) {
                    state.Text = "Verifying the result on your PC.";
                    if(runningJob is not null)jobs?.Move(runningJob,JobState.Verifying,state.Text);
                    RequireCurrentTask(binding, cts.Token);
                    var continuation=new AgentContinuation(binding.Query, observed.Context, results.ToList(), 25-results.Count);
                    var verification=specialists.Read("Result verifier",results.Count,token=>host.ContinueAgent(continuation,token),cts.Token);
                    if(SpecialistWork.Requested(binding.Query)) {
                        var teaching=specialists.Read("Teacher",results.Count,token=>host.ExplainExecution(continuation,token),cts.Token);
                        try{await Task.WhenAll(verification,teaching);RequireCurrentTask(binding,cts.Token);teachingUpdate.Text=await teaching;}
                        catch(Exception ex) when(ex is not OperationCanceledException && verification.IsCompletedSuccessfully){teachingUpdate.Text="The teaching update was unavailable; review the observed receipts.";}
                    }
                    var decision=await verification;
                    RequireCurrentTask(binding, cts.Token);
                    if (decision.Status == "done") { state.Text = "Completed · " + decision.Summary; if(runningJob is not null)jobs?.Move(runningJob,JobState.Completed,decision.Summary); break; }
                    if (decision.Status == "clarify" || replans >= 3) { state.Text = "Review needed · " + decision.Summary; if(runningJob is not null)jobs?.Move(runningJob,JobState.ReviewNeeded,decision.Summary); break; }
                    replans++;
                    var nextPlan = ActionPolicy.Validate(new(decision.Summary, decision.Actions), binding.Query);
                    ShowPlanCards(nextPlan.Actions!); planText.Text = nextPlan.Summary + string.Concat(nextPlan.Actions!.Where(a=>a.Kind=="type").Select(a=>"\n\nText to insert (review before continuing):\n"+a.Value));
                    if (!await Confirm("Review the updated plan, then allow it to continue.", cts.Token)) break;
                    RequireCurrentTask(binding, cts.Token);
                    pending = new(nextPlan.Actions!);
                }
            }
            if(!framedBatchEnded && runningJob is not null)jobs?.Move(runningJob,JobState.ReviewNeeded,"Run ended; verify the recorded results before another task.");
            stepText.Text = string.Join("\n", results.Select(r => r.Observation[..Math.Min(300, r.Observation.Length)]));
            if (edit is not null) undo!.Visibility = Visibility.Visible;
        } catch (OperationCanceledException) { if(runningJob is not null)jobs?.Move(runningJob,JobState.Cancelled,"Stopped. Verify any step already dispatched; no further actions run."); if(ReferenceEquals(operation,cts) && binding.Matches(taskRevision,goal.Text,mode))state.Text = "Stopped. No further actions will run."; await host.Audit("stop", "agent", "cancelled"); }
        catch (Exception ex) { cts.Cancel(); if(runningJob is not null)jobs?.Move(runningJob,JobState.ReviewNeeded,"Stopped; verify the last dispatched action. "+ex.Message); if(ReferenceEquals(operation,cts) && binding.Matches(taskRevision,goal.Text,mode))state.Text = "Stopped · " + ex.Message; await host.Audit("stop", "agent", ex is TimeoutException ? "provider timeout; verify the last action" : "action failed"); }
        finally { foreach(var item in specialists.Snapshot)executionJobs[item.Id]=item;ShowExecutionJobs(); interruption?.Dispose(); interruption = null; executionBanner?.Hide(); executing = false; overlay.Clear(); mood(CompanionMood.Idle); if (ReferenceEquals(operation, cts)) operation = null; cts.Dispose(); run.IsEnabled = false; }
    }
    private async Task<ActionResult?> DispatchAssignment(AssistantTaskBinding binding,AssistantAction action,int sequence,CancellationToken ct, Action onFramedLaunch)
    {
                RequireCurrentTask(binding, ct);
                if (action.Kind == "open") {
                    ActionPolicy.Validate(new("Approved launch", [action]), binding.Query);
                    if ((preferences().StrictAgentConfirmations || ActionPolicy.LiveRisk(action, null) == "high") && !await Confirm(action.Description + "\n" + action.Value, ct)) return null;
                    RequireCurrentTask(binding, ct);
                    ShowExecutionBanner(); await Task.Delay(600, ct); RequireCurrentTask(binding, ct);
                    if (!preferences().AgentEnabled) throw new InvalidOperationException("Agent mode was disabled.");
                    await actionGate.WaitAsync(ct);
                    try {
                        RequireCurrentTask(binding, ct);
                        if (ActionPolicy.Apps.Contains(action.Value)) {
                            var result = await RoutineAppOpen.RunApprovedAsync(binding.Query, action, () => preferences().AgentEnabled, () => preferences().BlockedApps, ct);
                            RequireCurrentTask(binding, ct);
                            if (!result.Verified || result.After is null) throw new InvalidOperationException(result.Message);
                            if (result.After.Frame is { } frame) {
                                if (!await WindowsRoutineAppBackend.ValidateFrameAsync(frame, () => preferences().AgentEnabled, () => preferences().BlockedApps, ct))
                                    throw new InvalidOperationException("The verified app frame changed; no next action will run.");
                                RequireCurrentTask(binding, ct);
                                sourceSelection = null; sourceWindow = IntPtr.Zero;
                                onFramedLaunch();
                                return new(sequence, action, true, result.Message);
                            }
                            var after = result.After.Window;
                            var selected = new WindowSelection(after.Window, after.ThreadId, after.ProcessId, after.ProcessStarted, after.App);
                            selected.Validate(); Perception.Check(selected.Window, true); selected.Validate();
                            if (Native.GetForegroundWindow() != selected.Window) throw new InvalidOperationException("The verified app lost focus; no next action will run.");
                            RequireCurrentTask(binding, ct);
                            sourceSelection = selected; sourceWindow = selected.Window; selectedApp.Text = "Selected app: " + selected.App;
                            return new(sequence, action, true, result.Message);
                        }
                        sourceWindow = await OpenPublicUrl(binding, action, ct); sourceSelection = WindowSelection.Capture(sourceWindow);
                        selectedApp.Text = "Selected app: " + sourceSelection.App;
                        return new(sequence, action, true, "Dispatched the exact approved HTTPS address; browser window observed. Page content is not verified.");
                    } finally { actionGate.Release(); }
                } else {
                    sourceSelection?.Validate(); Perception.Check(sourceWindow, true);
                    if (!InputNative.SetForegroundWindow(sourceWindow)) throw new InvalidOperationException("Activate the target app, then plan again.");
                    await Task.Delay(150, ct);
                    if (Native.GetForegroundWindow() != sourceWindow) throw new InvalidOperationException("The target app did not gain focus. Stopped.");
                    var snapshot = await CaptureSelected(ct);
                    var element = action.Kind is "keys" or "wait" ? null : GroundingResolver.Resolve(snapshot.Context.Elements, action.Ref, action.Target, action.Role);
                    if (action.Kind is not ("keys" or "wait") && element is null) {
                        return new(sequence, action, false, "Not executed: target no longer exists or is ambiguous");
                    } else {
                        var risk = await BoundedAction(riskToken => {
                            riskToken.ThrowIfCancellationRequested();
                            if (element is null) return ActionPolicy.LiveRisk(action, null);
                            var node = snapshot.Nodes[element.Ref];
                            // Writable does not imply reversible: web forms and settings may autosave.
                            bool reversible = snapshot.Context.App.Equals("notepad", StringComparison.OrdinalIgnoreCase) && node.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) && pattern is ValuePattern value && !value.Current.IsReadOnly;
                            bool navigation = node.TryGetCurrentPattern(SelectionItemPattern.Pattern, out _) || node.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out _);
                            return ActionPolicy.LiveRisk(action, element, reversible, navigation);
                        }, ct);
                        if ((preferences().StrictAgentConfirmations && action.Kind is not ("read" or "wait") || risk == "high") && !await Confirm(action.Description + "\n" + action.Target + " " + action.Value, ct)) return null;
                        RequireCurrentTask(binding, ct);
                        if (!InputNative.SetForegroundWindow(sourceWindow)) throw new InvalidOperationException("Activate the target app before continuing.");
                        if (element is not null) Draw(snapshot, element, "ring", action.Description);
                        ShowExecutionBanner(); await Task.Delay(600, ct);
                        var result = await BoundedAction(token => Apply(action, snapshot, element, token, risk), ct);
                        return new(sequence, action, true, result);
                    }
                }
    }
    private async Task<T> BoundedAction<T>(Func<CancellationToken, T> action, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var token = timeout.Token;
        try {
            await actionGate.WaitAsync(token);
            var worker = Task.Run(() => { try { token.ThrowIfCancellationRequested(); return action(token); } finally { actionGate.Release(); } }, CancellationToken.None);
            return await worker.WaitAsync(token);
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("The app's accessibility provider timed out. Verify the last action before continuing."); }
    }
    internal string Apply(AssistantAction action, ScreenSnapshot snapshot, ScreenElement? targetElement, CancellationToken ct, string authorizedRisk = "high")
    {
        ct.ThrowIfCancellationRequested(); snapshot.Selection?.Validate(); Perception.Check(snapshot.Window, true); snapshot.Selection?.Validate();
        if (Native.GetForegroundWindow() != snapshot.Window) throw new InvalidOperationException("Focus changed before the action.");
        if (action.Kind == "wait") { ct.WaitHandle.WaitOne(700); ct.ThrowIfCancellationRequested(); return "Waited for the interface"; }
        if (action.Kind == "keys") { InputNative.Keys(action.Value, ct); return "Pressed " + action.Value; }
        var node = snapshot.Nodes[targetElement!.Ref]; var current = node.Current;
        var liveName = Security.Redact(current.Name ?? ""); liveName = liveName[..Math.Min(120, liveName.Length)];
        if (!current.IsEnabled || current.IsOffscreen || current.IsPassword || liveName != targetElement.Name || current.ControlType.ProgrammaticName != "ControlType." + targetElement.Role || current.BoundingRectangle != new Rect(targetElement.X,targetElement.Y,targetElement.Width,targetElement.Height)) throw new InvalidOperationException("The target is no longer safe or in the expected position.");
        if (authorizedRisk == "low") {
            bool reversible = InputNative.ProcessName(snapshot.Window).Equals("notepad", StringComparison.OrdinalIgnoreCase) && node.TryGetCurrentPattern(ValuePattern.Pattern, out var editable) && editable is ValuePattern value && !value.Current.IsReadOnly;
            bool navigation = node.TryGetCurrentPattern(SelectionItemPattern.Pattern, out _) || node.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out _);
            if (ActionPolicy.LiveRisk(action, targetElement, reversible, navigation) != "low") throw new InvalidOperationException("The target's action effects changed. Review a fresh plan before proceeding.");
        }
        ct.ThrowIfCancellationRequested();
        if (action.Kind == "read") return "Read " + Security.Redact(current.Name ?? "");
        if (action.Kind == "type") {
            if (node.TryGetCurrentPattern(ValuePattern.Pattern, out var value) && value is ValuePattern field && !field.Current.IsReadOnly) {
                var before = field.Current.Value;
                if(action.RequireEmpty)NotepadWriting.CheckEmptyEditor(snapshot.Context.App,InputNative.ProcessName(snapshot.Window),snapshot.Window==ScreenPerception.PracticeHandle,field.Current.IsReadOnly,before);
                ct.ThrowIfCancellationRequested(); field.SetValue(action.Value);
                if (field.Current.Value != action.Value) throw new InvalidOperationException("The application did not retain the expected text. Review the field before continuing.");
                edit = (node, before, action.Value, DateTimeOffset.UtcNow, snapshot.Window); return "Updated and verified " + targetElement.Name;
            }
            throw new InvalidOperationException("This field cannot verify a text replacement. Use Guide and enter the text yourself.");
        }
        if (node.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var select)) { ct.ThrowIfCancellationRequested(); ((SelectionItemPattern)select).Select(); }
        else if (node.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var expand)) { ct.ThrowIfCancellationRequested(); ((ExpandCollapsePattern)expand).Expand(); }
        else if (node.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke)) { ct.ThrowIfCancellationRequested(); ((InvokePattern)invoke).Invoke(); }
        else if (node.TryGetCurrentPattern(TogglePattern.Pattern, out var toggle)) { ct.ThrowIfCancellationRequested(); ((TogglePattern)toggle).Toggle(); }
        else throw new InvalidOperationException("This control has no supported accessibility action. Use Guide and click it yourself.");
        return "Activated " + targetElement.Name;
    }
    private async Task<IntPtr> OpenPublicUrl(AssistantTaskBinding binding, AssistantAction action, CancellationToken ct)
    {
        RequireCurrentTask(binding, ct);
        var validated = ActionPolicy.Validate(new("Approved public address", [action]), binding.Query).Actions!.Single();
        if (validated.Kind != "open" || ActionPolicy.Apps.Contains(validated.Value))
            throw new InvalidOperationException("Native apps must use the verified installed-app launcher.");
        string url = WebResearch.ValidateUrl(validated.Value).AbsoluteUri;
        // This is exclusively the already-reviewed exact HTTPS destination.
        // No native alias, executable, arguments or arbitrary protocol enters it.
        var launch = new ProcessStartInfo(url) { UseShellExecute = true };
        var before = Native.GetForegroundWindow();
        var checkpoint = WindowSelection.Capture(before);
        Native.CheckWindow(before); InputNative.CheckDesktopAndElevation(before);
        if (preferences().BlockedApps.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(app => checkpoint.App.Contains(app, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("The current app is in your privacy blocklist.");
        checkpoint.Validate(); RequireCurrentTask(binding, ct);
        if (!preferences().AgentEnabled || Native.GetForegroundWindow() != before) throw new InvalidOperationException("Agent or foreground changed before opening the approved address.");
        using var launched = Process.Start(launch);
        for (int i = 0; i < 30; i++) {
            await Task.Delay(200, ct); RequireCurrentTask(binding, ct);
            if (!preferences().AgentEnabled) throw new InvalidOperationException("Agent mode was disabled; verify the already-dispatched browser request.");
            var window = Native.GetForegroundWindow();
            if (window == IntPtr.Zero || Native.IsOwnWindow(window)) continue;
            var name = InputNative.ProcessName(window).ToLowerInvariant();
            bool matches = window != before && name is "msedge" or "chrome" or "firefox" or "brave" or "opera" or "comet";
            if (matches) { Perception.Check(window, true); return window; }
        }
        throw new InvalidOperationException("The approved address was dispatched but its browser window was not verified. Inspect it before making another request; no launch was repeated.");
    }
    private async Task Undo()
    {
        if (edit is not { } change || DateTimeOffset.UtcNow - change.At > TimeSpan.FromSeconds(30)) { state.Text = "Undo expired after 30 seconds."; undo!.Visibility = Visibility.Collapsed; return; }
        try {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await BoundedAction(token => {
                Perception.Check(change.Window, true);
                if (change.Node.Current.IsPassword || !change.Node.Current.IsEnabled || change.Node.Current.IsOffscreen) throw new InvalidOperationException("The field is no longer available for Undo.");
                var field = (ValuePattern)change.Node.GetCurrentPattern(ValuePattern.Pattern);
                if (field.Current.Value != change.After) throw new InvalidOperationException("The field changed since Buddy edited it. Undo was not applied.");
                token.ThrowIfCancellationRequested(); field.SetValue(change.Before);
                if (field.Current.Value != change.Before) throw new InvalidOperationException("The app did not retain the original text. Check the field.");
                return true;
            }, cts.Token);
            state.Text = "Previous field value restored."; edit = null; undo!.Visibility = Visibility.Collapsed;
        } catch (Exception ex) { state.Text = ex.Message; }
    }
    internal void Cancel() { if(job is not null)jobs?.Move(job,JobState.Cancelled,executing ? "Stopped. Verify any step already dispatched; nothing further is approved." : "Planning/review cancelled; no further actions run."); plannedTask = null; plan = null; if (run is not null) run.IsEnabled = false; executionBanner?.Hide(); guideRevision++; guideCheck?.Cancel(); guideMonitor.Stop(); operation?.Cancel(); approval?.TrySetCanceled(); interruption?.DisarmPointer(); overlay.Clear(); observationScope.Text = ""; state.Text = "Stopped"; mood(CompanionMood.Idle); }
    internal async Task ResumeLatest()
    {
        var saved = await service()!.Store.Read(s => s.Guides.LastOrDefault(g => !g.Completed));
        if (saved is null) { await Open("guide", ""); state.Text = "No saved walkthrough yet."; return; }
        await Resume(saved.Id);
    }
    internal async Task Resume(string id)
    {
        var saved = await service()!.Store.Read(s => s.Guides.FirstOrDefault(g => g.Id == id));
        if (saved is null) { await Open("guide", ""); state.Text = "This walkthrough is no longer available."; return; }
        await Open("guide", ""); goal.Text = saved.Query;
        state.Text = "Refreshing the saved request against the current app. Earlier suggestions are not proof of completion.";
        await Plan();
    }
    private void ShowPlanCards(IReadOnlyList<AssistantAction> actions)
    {
        planCards.Children.Clear();
        for (int i = 0; i < actions.Count; i++) {
            var action = actions[i]; bool needsReview = ActionPolicy.LiveRisk(action, null) == "high";
            var p = new StackPanel();
            p.Children.Add(new TextBlock { Text = $"{i + 1} · {action.Kind} · " + (needsReview ? "verify / approve" : "navigation / read"), Foreground = needsReview ? BuddyTheme.Risk : BuddyTheme.Deep, FontSize = 12, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            p.Children.Add(new TextBlock { Text = action.Description, Foreground = BuddyTheme.Ink, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 0) });
            var card = BuddyTheme.Card(p, 16); card.Background = needsReview ? BuddyTheme.RiskSoft : BuddyTheme.Canvas; planCards.Children.Add(card);
        }
    }
    private void ShowExecutionBanner() { interruption?.ArmPointer(); executionBanner ??= new ExecutionBanner(); executionBanner.Open(); }
    public void Dispose() { Cancel(); stopMonitor.Stop(); overlay.Dispose(); executionBanner?.Close(); panel?.Hide(); }
}
