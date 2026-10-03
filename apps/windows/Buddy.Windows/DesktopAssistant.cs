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

internal sealed class DesktopAssistant : IDisposable
{
    private readonly Func<BuddyService?> service;
    private readonly Func<DesktopPreferences> preferences;
    private readonly Func<IntPtr> target;
    private readonly Action<CompanionMood> mood;
    private readonly Action? starting;
    internal ScreenPerception Perception { get; }
    private readonly VisualGrounding visualGrounding;
    private bool visualGuide;
    private int visualAttempts;
    private readonly GuidanceOverlay overlay = new();
    private readonly DispatcherTimer stopMonitor = new() { Interval = TimeSpan.FromMilliseconds(25) };
    private readonly DispatcherTimer guideMonitor = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private Window? panel;
    private readonly TextBlock state = new() { Foreground = BuddyTheme.Deep, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
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
    private GuidePlan? guide;
    private string mode = "guide", guideId = "";
    private int index;
    private int guideRevision, guideMisses, guideReplans, expectationMatches;
    private bool checkingGuide, expectationArmed;
    private GuideReviewProgress guideProgress = new();
    private ScreenElement? drawnGuideTarget;
    private IntPtr sourceWindow;
    private bool executing;
    private readonly JobLedger? jobs;
    private string? job;
    private DispatchInterruption? interruption;
    private (AutomationElement Node, string Before, string After, DateTimeOffset At, IntPtr Window)? edit;
    internal bool IsActive => panel?.IsVisible == true;
    internal Action? DrawRegionRequested { get; set; }
    private int LessonCount => guide?.Lessons?.Count ?? guide?.Steps?.Count ?? 0;

    internal DesktopAssistant(Func<BuddyService?> service, Func<DesktopPreferences> preferences, Func<IntPtr> target, Action<CompanionMood> mood, Action<ScreenElement?>? pointing = null, Action? starting = null, JobLedger? jobs = null)
    {
        this.service = service; this.preferences = preferences; this.target = target; this.mood = mood; Perception = new(preferences); visualGrounding = new(Perception, service);
        this.starting = starting; this.jobs=jobs; if (pointing is not null) overlay.TargetChanged += pointing;
        guideMonitor.Tick += async (_, _) => await CheckGuideProgress();
        stopMonitor.Tick += (_, _) => {
            if ((InputNative.GetAsyncKeyState(27) & 0x8000) != 0) Cancel();
        };
    }
    internal async Task Open(string requestedMode, string query)
    {
        if (executing) { Cancel(); return; }
        starting?.Invoke();
        Cancel(); mode = requestedMode; sourceWindow = target(); plan = null; guide = null; guideReplans = guideMisses = 0;
        EnsurePanel(); specialistStatus.Text=teachingUpdate.Text=""; goal.Text = query; planText.Clear(); planCards.Children.Clear(); sourceLinks.Children.Clear(); stepText.Text = ""; panel!.Title = mode == "agent" ? "Buddy · Agent plan" : "Buddy · Guide"; panel.Width = mode == "agent" ? 420 : 360;
        run!.Visibility = mode == "agent" ? Visibility.Visible : Visibility.Collapsed; run.IsEnabled = false;
        back!.Visibility = next!.Visibility = mode == "guide" ? Visibility.Visible : Visibility.Collapsed;
        skip!.Visibility = back.Visibility; skip.IsEnabled = false;
        back.IsEnabled = next.IsEnabled = false; panel.Show(); panel.Activate(); stopMonitor.Start();
        if (query.Trim().Length > 0) await Plan();
        else state.Text = mode == "agent" ? "Describe a task. Buddy shows its plan before acting." : "Focus your tool, summon Buddy, and describe what to learn.";
    }
    private void EnsurePanel()
    {
        if (panel is not null) return;
        BuddyTheme.Ensure();
        panel = new Window { Width = 420, Height = Math.Min(620, SystemParameters.WorkArea.Height), Topmost = true, ShowInTaskbar = false, Background = BuddyTheme.Surface, Foreground = BuddyTheme.Ink, FontFamily = BuddyTheme.Font, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        var p = new StackPanel { Margin = new(20) }; p.Children.Add(state); p.Children.Add(goal);
        Button Add(string label, Action action, Panel parent) { var b = new Button { Content = label, Padding = new(10,6,10,6), Margin = new(0,8,6,0) }; b.Click += (_, _) => action(); parent.Children.Add(b); return b; }
        var commands = new WrapPanel(); Add("Make plan", () => { var selected=target(); if(selected!=IntPtr.Zero&&!Native.IsOwnWindow(selected))sourceWindow=selected; _ = Plan(); }, commands); run = Add("Run this plan", () => _ = Execute(), commands); Add("Draw an area", () => { Cancel(); DrawRegionRequested?.Invoke(); }, commands); Add("Stop", Cancel, commands); p.Children.Add(commands);
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
    }
    private async Task Plan()
    {
        if (executing) return; Cancel(); var host = service(); if (host is null) return;
        starting?.Invoke();
        var task = goal.Text.Trim(); if (task.Length == 0) { state.Text = "Describe the task first."; return; }
        plan = null; guide = null; run!.IsEnabled = false; next!.IsEnabled = back!.IsEnabled = false;
        var cts = new CancellationTokenSource(); operation = cts; string? plannedJob=null;
        try {
            if(mode=="agent") job=plannedJob=jobs?.Begin("action",task,InputNative.ProcessName(sourceWindow));
            if (mode == "agent" && !preferences().AgentEnabled) throw new InvalidOperationException("Agent mode is off. Enable it in Home → Assistant settings, or choose Guide.");
            state.Text = "● Looking at the selected window…"; mood(CompanionMood.Looking);
            ScreenContext context; string captureIssue="";
            try { context = (await Perception.Capture(sourceWindow, cts.Token, GuideSafety.RequestedText(task))).Context; await host.Audit("capture", context.App, "UIA planning snapshot; no image stored"); }
            catch (Exception ex) when (ex is not OperationCanceledException) { context = new("", "No accessible window. No current controls were observed.", []); captureIssue=ex.Message; }
            state.Text = "Planning on your PC…"; mood(CompanionMood.Thinking);
            if (mode == "agent") {
                PreparedWorkflow? prepared=null;
                if(SpecialistWork.Requested(task)) {
                    var states=new Dictionary<string,string>();
                    var progress=new Progress<SpecialistStatus>(item=>{if(cts.IsCancellationRequested||!ReferenceEquals(operation,cts))return;states[item.Name]=item.State;state.Text=string.Join(" | ",states.Select(p=>p.Key+": "+p.Value));if(plannedJob is not null)jobs?.Progress(plannedJob,state.Text);});
                    prepared=await host.PrepareWorkflow(new(task,context,preferences().AllowWebResearch),progress,cts.Token);plan=prepared.Plan;
                } else plan = await host.PlanAgent(new(task, context, preferences().AllowWebResearch), cts.Token);
                cts.Token.ThrowIfCancellationRequested();
                planText.Text = plan.Summary + "\n\n" + string.Join("\n", plan.Actions!.Select((a,i) => $"{i+1}. [{a.Risk.ToUpperInvariant()} · {a.Kind}] {a.Description}\n    {a.Target} {a.Value}"));
                ShowPlanCards(plan.Actions!); planText.Text = plan.Summary + string.Concat(plan.Actions!.Where(a=>a.Kind=="type").Select(a=>"\n\nText to insert (review before Run):\n"+a.Value));
                if(prepared is not null) {
                    stepText.Text="Teacher: "+prepared.Lesson.Summary+"\n"+string.Join("\n",prepared.Lesson.Lessons?.Select(l=>l.Instruction)??prepared.Lesson.Steps?.Select(s=>s.Instruction)??[])+$"\nImported-notes researcher: {prepared.Notes.Count} matching excerpts. Preparation is complete; no action has run.";
                    SourceLinks.Fill(sourceLinks,new MessageEvidence(Sources:prepared.Lesson.Sources?.Select(s=>new SourceLink(s.Title,s.Url)).ToList()));
                }
                run.IsEnabled = true; state.Text = "Review the plan. Consequential or uncertain actions pause for approval.";
                if(plannedJob is not null)jobs!.Move(plannedJob,JobState.AwaitingApproval,"Plan ready. Use Run this plan, then approve each consequential step.");
            } else {
                guide = await host.PlanGuide(new(task, context, preferences().AllowWebResearch), cts.Token); cts.Token.ThrowIfCancellationRequested(); guideId = Guid.NewGuid().ToString(); index = 0; guideProgress = new();
                planText.Text = guide.Summary;
                SourceLinks.Fill(sourceLinks,new MessageEvidence(Sources:guide.Sources?.Select(s=>new SourceLink(s.Title,s.Url)).ToList()));
                if(LessonCount==0){next.IsEnabled=skip!.IsEnabled=false;state.Text="General guidance - no pointer verified on this screen.";stepText.Text=captureIssue.Length>0?"Screen observation unavailable: "+captureIssue:"Focus the intended app and choose Make plan for a fresh observation, or Draw an area to explain a selected region.";mood(CompanionMood.Unsure);}
                else {next.IsEnabled=true;await DrawStep(cts.Token);}
            }
            if (mode == "agent") mood(CompanionMood.Idle);
        } catch (OperationCanceledException) { state.Text = "Stopped"; if(plannedJob is not null)jobs!.Move(plannedJob,JobState.Cancelled,"Planning cancelled; no actions ran."); }
        catch (Exception ex) { state.Text = ex.Message; mood(CompanionMood.Unsure); if(plannedJob is not null)jobs!.Move(plannedJob,JobState.Failed,ex.Message); }
        finally { if (ReferenceEquals(operation, cts)) operation = null; cts.Dispose(); }
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
        try { await DrawStep(cts.Token); } catch (Exception ex) { state.Text = ex is OperationCanceledException ? "Stopped" : ex.Message; }
        finally { if (ReferenceEquals(operation, cts)) operation = null; cts.Dispose(); }
    }
    private async Task DrawStep(CancellationToken ct)
    {
        if(guide?.Lessons is {Count:>0}) { await DrawLesson(ct); return; }
        SourceLinks.Fill(sourceLinks, new MessageEvidence(Sources: guide?.Sources?.Select(s => new SourceLink(s.Title, s.Url)).ToList()));
        var step = guide!.Steps![index]; overlay.Clear(); visualGuide = false; visualAttempts = 0; stepText.Text = step.Instruction;
        state.Text = $"Guide · step {index+1} of {guide.Steps.Count}"; back!.IsEnabled = index > 0; next!.IsEnabled = skip!.IsEnabled = true; next.Content = index + 1 == guide.Steps.Count ? "Finish review" : "Next (reviewed)";
        // Keep the selected window pinned; a different foreground app is not a new target.
        var snapshot = await Perception.Capture(sourceWindow, ct, GuideSafety.RequestedText(goal.Text)); ct.ThrowIfCancellationRequested();
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
            var snapshot=await Perception.Capture(sourceWindow,ct);ct.ThrowIfCancellationRequested();
            var element=GuideLessons.Resolve(lesson,snapshot.Context,goal.Text);
            if(element is not null) { Draw(snapshot,element,"ring",lesson.Target);state.Text=$"Lesson {index+1} of {LessonCount} - pointer verified in {snapshot.Context.App}; your turn.";mood(CompanionMood.Pointing); }
            else { mood(CompanionMood.Idle);state.Text+=" Open the relevant view, then revisit this section, or Draw an area."; }
        } catch(Exception ex) when(ex is not OperationCanceledException) { state.Text+=" Screen observation unavailable: "+ex.Message; }
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
            var snapshot = await Perception.Capture(sourceWindow, timeout.Token, GuideSafety.RequestedText(goal.Text));
            if (revision != guideRevision || operation is not null || guide?.Steps is null) return;
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
        catch (Exception ex) { overlay.Clear(); state.Text = ex.Message; }
        finally { if (ReferenceEquals(guideCheck, timeout)) guideCheck = null; checkingGuide = false; }
    }
    private void Draw(ScreenSnapshot snapshot, ScreenElement element, string primitive, string label)
    {
        drawnGuideTarget = element;
        if(snapshot.WordTargets?.TryGetValue(element.Ref,out var word)==true){overlay.Draw(snapshot.Window,element,primitive,label,()=>word.IsCurrent(element));return;}
        var node = snapshot.Nodes[element.Ref]; var original = new Rect(element.X, element.Y, element.Width, element.Height);
        overlay.Draw(snapshot.Window, element, primitive, label, () => { try { return !node.Current.IsOffscreen && node.Current.BoundingRectangle == original; } catch { return false; } });
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
            try { return WindowCapture.Bounds(snapshot.Window) == bounds && Security.Redact(Native.Label(snapshot.Window)) == title; } catch { return false; }
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
        if (plan?.Actions is null || executing || operation is not null) return;
        if(job is not null&&jobs?.Snapshot.FirstOrDefault(j=>j.Id==job)?.State!=JobState.AwaitingApproval){state.Text="This plan was stopped or expired. Choose Make plan to review a fresh job.";run!.IsEnabled=false;return;}
        starting?.Invoke();
        var host = service()!; var approvedPlan = ActionPolicy.Validate(plan); var cts = new CancellationTokenSource(); operation = cts; executing = true; run!.IsEnabled = false;
        var results = new List<ActionResult>(); int replans = 0; var runningJob=job;
        executionJobs.Clear();using var specialists=new ExecutionSpecialists(new Progress<ExecutionJob>(item=>{
            if(!ReferenceEquals(operation,cts))return;
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
                cts.Token.ThrowIfCancellationRequested(); if (!preferences().AgentEnabled) throw new InvalidOperationException("Agent mode was disabled.");
                var action = pending.Dequeue(); state.Text = $"Buddy is controlling — Esc to stop · action {results.Count+1}/25"; stepText.Text = action.Description; mood(CompanionMood.AgentWorking);
                state.Text = "Buddy is controlling — Esc to stop · " + action.Description;
                if(runningJob is not null)jobs?.Progress(runningJob,$"Action {results.Count+1}/25: {action.Description}");
                var receipt=await specialists.Dispatch(action,results.Count+1,token=>DispatchAssignment(action,results.Count+1,token),cts.Token);
                if(receipt is null)break;
                results.Add(receipt);if(!receipt.Success)pending.Clear();
                executionBanner?.Hide(); interruption.DisarmPointer(); overlay.Clear(); await host.Audit(action.Kind, action.Target, results[^1].Success ? "completed" : "not executed: target unavailable");
                if(runningJob is not null)jobs?.Receipt(runningJob,results[^1].Observation,results[^1].Success);
                // Observe actual state before completing a batch or proposing its replacement.
                var observed = await specialists.Read("Observer",results.Count,token=>Perception.Capture(sourceWindow,token),cts.Token);
                if (pending.Count == 0) {
                    state.Text = "Verifying the result on your PC.";
                    if(runningJob is not null)jobs?.Move(runningJob,JobState.Verifying,state.Text);
                    var continuation=new AgentContinuation(goal.Text, observed.Context, results.ToList(), 25-results.Count);
                    var verification=specialists.Read("Result verifier",results.Count,token=>host.ContinueAgent(continuation,token),cts.Token);
                    if(SpecialistWork.Requested(goal.Text)) {
                        var teaching=specialists.Read("Teacher",results.Count,token=>host.ExplainExecution(continuation,token),cts.Token);
                        try{await Task.WhenAll(verification,teaching);teachingUpdate.Text=await teaching;}
                        catch(Exception ex) when(ex is not OperationCanceledException && verification.IsCompletedSuccessfully){teachingUpdate.Text="The teaching update was unavailable; review the observed receipts.";}
                    }
                    var decision=await verification;
                    if (decision.Status == "done") { state.Text = "Completed · " + decision.Summary; if(runningJob is not null)jobs?.Move(runningJob,JobState.Completed,decision.Summary); break; }
                    if (decision.Status == "clarify" || replans >= 3) { state.Text = "Review needed · " + decision.Summary; if(runningJob is not null)jobs?.Move(runningJob,JobState.ReviewNeeded,decision.Summary); break; }
                    replans++;
                    planText.Text = decision.Summary + "\n\n" + string.Join("\n", decision.Actions!.Select((a,i) => $"{i+1}. {a.Description} · {a.Kind} {a.Target} {a.Value}"));
                    ShowPlanCards(decision.Actions!); planText.Text = decision.Summary + string.Concat(decision.Actions!.Where(a=>a.Kind=="type").Select(a=>"\n\nText to insert (review before continuing):\n"+a.Value));
                    if (!await Confirm("Review the updated plan, then allow it to continue.", cts.Token)) break;
                    pending = new(decision.Actions!);
                }
            }
            if(runningJob is not null)jobs?.Move(runningJob,JobState.ReviewNeeded,"Run ended; verify the recorded results before another task.");
            stepText.Text = string.Join("\n", results.Select(r => r.Observation[..Math.Min(300, r.Observation.Length)]));
            if (edit is not null) undo!.Visibility = Visibility.Visible;
        } catch (OperationCanceledException) { if(runningJob is not null)jobs?.Move(runningJob,JobState.Cancelled,"Stopped. Verify any step already dispatched; no further actions run."); state.Text = "Stopped. No further actions will run."; await host.Audit("stop", "agent", "cancelled"); }
        catch (Exception ex) { cts.Cancel(); if(runningJob is not null)jobs?.Move(runningJob,JobState.ReviewNeeded,"Stopped; verify the last dispatched action. "+ex.Message); state.Text = "Stopped · " + ex.Message; await host.Audit("stop", "agent", ex is TimeoutException ? "provider timeout; verify the last action" : "action failed"); }
        finally { foreach(var item in specialists.Snapshot)executionJobs[item.Id]=item;ShowExecutionJobs(); interruption?.Dispose(); interruption = null; executionBanner?.Hide(); executing = false; overlay.Clear(); mood(CompanionMood.Idle); if (ReferenceEquals(operation, cts)) operation = null; cts.Dispose(); run.IsEnabled = false; }
    }
    private async Task<ActionResult?> DispatchAssignment(AssistantAction action,int sequence,CancellationToken ct)
    {
                if (action.Kind == "open") {
                    if ((preferences().StrictAgentConfirmations || ActionPolicy.LiveRisk(action, null) == "high") && !await Confirm(action.Description + "\n" + action.Value, ct)) return null;
                    ShowExecutionBanner(); await Task.Delay(600, ct); sourceWindow = await OpenApp(action.Value, ct);
                    return new(sequence, action, true, "Opened " + action.Value + "; verified foreground application");
                } else {
                    Perception.Check(sourceWindow, true);
                    if (!InputNative.SetForegroundWindow(sourceWindow)) throw new InvalidOperationException("Activate the target app, then plan again.");
                    await Task.Delay(150, ct);
                    if (Native.GetForegroundWindow() != sourceWindow) throw new InvalidOperationException("The target app did not gain focus. Stopped.");
                    var snapshot = await Perception.Capture(sourceWindow, ct);
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
                        ct.ThrowIfCancellationRequested();
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
        ct.ThrowIfCancellationRequested(); Perception.Check(snapshot.Window, true);
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
    private async Task<IntPtr> OpenApp(string value, CancellationToken ct)
    {
        if (!ActionPolicy.Apps.Contains(value)) { WebResearch.ValidateUrl(value); }
        var executable = value switch { "notepad" => "notepad.exe", "calculator" => "calc.exe", "explorer" => "explorer.exe", _ => value };
        var launch = value == "comet" ? InstalledAppResolver.CometStartInfo() : new ProcessStartInfo(executable) { UseShellExecute = true };
        var before = Native.GetForegroundWindow();
        ct.ThrowIfCancellationRequested(); using var launched = Process.Start(launch);
        for (int i = 0; i < 30; i++) {
            await Task.Delay(200, ct); var window = Native.GetForegroundWindow();
            if (window == IntPtr.Zero || Native.IsOwnWindow(window)) continue;
            var name = InputNative.ProcessName(window).ToLowerInvariant();
            bool matches = value switch { "comet" => name == "comet" && InstalledAppResolver.MatchesComet(window, launch.FileName), "notepad" => name == "notepad", "calculator" => name is "calculatorapp" or "calculator", "explorer" => name == "explorer", _ => window != before && name is "msedge" or "chrome" or "firefox" or "brave" or "opera" };
            if (matches) { Perception.Check(window, true); return window; }
        }
        throw new InvalidOperationException("The app opened without a foreground window. Focus it and plan the next step.");
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
    internal void Cancel() { if(job is not null)jobs?.Move(job,JobState.Cancelled,executing ? "Stopped. Verify any step already dispatched; nothing further is approved." : "Planning/review cancelled; no further actions run."); executionBanner?.Hide(); guideRevision++; guideCheck?.Cancel(); guideMonitor.Stop(); operation?.Cancel(); approval?.TrySetCanceled(); interruption?.DisarmPointer(); overlay.Clear(); state.Text = "Stopped"; mood(CompanionMood.Idle); }
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
