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
    internal ScreenPerception Perception { get; }
    private readonly GuidanceOverlay overlay = new();
    private readonly DispatcherTimer stopMonitor = new() { Interval = TimeSpan.FromMilliseconds(25) };
    private readonly DispatcherTimer guideMonitor = new() { Interval = TimeSpan.FromMilliseconds(700) };
    private Window? panel;
    private readonly TextBlock state = new() { Foreground = Brushes.Turquoise, TextWrapping = TextWrapping.Wrap, FontSize = 15 };
    private readonly TextBox goal = new() { TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, Height = 66, MaxLength = 4000 };
    private readonly TextBox planText = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Background = Brushes.Transparent, Foreground = Brushes.White, BorderThickness = new(0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 190 };
    private readonly TextBlock stepText = new() { Foreground = Brushes.White, FontSize = 15, TextWrapping = TextWrapping.Wrap, Margin = new(0,8,0,8) };
    private Button? run, next, back, skip, approve, undo;
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
    private ScreenElement? drawnGuideTarget;
    private IntPtr sourceWindow;
    private bool executing, movingGuard;
    private OverlayNative.Point pointer;
    private (AutomationElement Node, string Before, string After, DateTimeOffset At, IntPtr Window)? edit;
    internal bool IsActive => panel?.IsVisible == true;

    internal DesktopAssistant(Func<BuddyService?> service, Func<DesktopPreferences> preferences, Func<IntPtr> target, Action<CompanionMood> mood)
    {
        this.service = service; this.preferences = preferences; this.target = target; this.mood = mood; Perception = new(preferences);
        guideMonitor.Tick += async (_, _) => await CheckGuideProgress();
        stopMonitor.Tick += (_, _) => {
            if ((InputNative.GetAsyncKeyState(27) & 0x8000) != 0) Cancel();
            if (movingGuard && OverlayNative.GetCursorPos(out var p) && Math.Pow(p.X - pointer.X, 2) + Math.Pow(p.Y - pointer.Y, 2) > 1600) { Cancel(); state.Text = "Paused because you moved the mouse. Review and plan again."; }
        };
    }
    internal async Task Open(string requestedMode, string query)
    {
        if (executing) { Cancel(); return; }
        Cancel(); mode = requestedMode; sourceWindow = target(); plan = null; guide = null; guideReplans = guideMisses = 0;
        EnsurePanel(); goal.Text = query; planText.Clear(); stepText.Text = ""; panel!.Title = mode == "agent" ? "Buddy · Agent plan" : "Buddy · Guide";
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
        panel = new Window { Width = 470, Height = 550, Topmost = true, ShowInTaskbar = false, Background = new SolidColorBrush(Color.FromRgb(18,30,36)), Foreground = Brushes.White, FontFamily = new("Segoe UI"), WindowStartupLocation = WindowStartupLocation.CenterScreen };
        var p = new StackPanel { Margin = new(20) }; p.Children.Add(state); p.Children.Add(goal);
        Button Add(string label, Action action, Panel parent) { var b = new Button { Content = label, Padding = new(10,6,10,6), Margin = new(0,8,6,0) }; b.Click += (_, _) => action(); parent.Children.Add(b); return b; }
        var commands = new WrapPanel(); Add("Make plan", () => _ = Plan(), commands); run = Add("Run this plan", () => _ = Execute(), commands); Add("Stop", Cancel, commands); p.Children.Add(commands);
        p.Children.Add(planText); p.Children.Add(stepText);
        var navigation = new WrapPanel(); back = Add("Back", () => _ = GuideStep(-1), navigation); next = Add("Next", () => _ = GuideStep(1), navigation);
        skip = Add("Skip", () => _ = GuideStep(1), navigation);
        approve = Add("Allow this step", () => approval?.TrySetResult(true), navigation); approve.Visibility = Visibility.Collapsed;
        undo = Add("Undo last edit", () => _ = Undo(), navigation); undo.Visibility = Visibility.Collapsed;
        p.Children.Add(navigation); panel.Content = new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Closing += (_, e) => { e.Cancel = true; Cancel(); panel.Hide(); stopMonitor.Stop(); };
        panel.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; Cancel(); } };
        panel.SourceInitialized += (_, _) => Native.SetWindowDisplayAffinity(new WindowInteropHelper(panel).Handle, 0x11);
    }
    private async Task Plan()
    {
        if (executing) return; Cancel(); var host = service(); if (host is null) return;
        var task = goal.Text.Trim(); if (task.Length == 0) { state.Text = "Describe the task first."; return; }
        plan = null; guide = null; run!.IsEnabled = false; next!.IsEnabled = back!.IsEnabled = false;
        var cts = new CancellationTokenSource(); operation = cts;
        try {
            if (mode == "agent" && !preferences().AgentEnabled) throw new InvalidOperationException("Agent mode is off. Enable it in Home → Assistant settings, or choose Guide.");
            state.Text = "● Looking at the selected window…"; mood(CompanionMood.Looking);
            ScreenContext context;
            try { context = (await Perception.Capture(sourceWindow, cts.Token)).Context; await host.Audit("capture", context.App, "UIA planning snapshot; no image stored"); }
            catch (Exception ex) when (mode == "agent" && ex is not OperationCanceledException) { context = new("", "No accessible window. Only plan opening an allowed app; never guess current controls.", []); }
            state.Text = "Planning on your PC…"; mood(CompanionMood.Thinking);
            if (mode == "agent") {
                plan = await host.PlanAgent(new(task, context), cts.Token); cts.Token.ThrowIfCancellationRequested();
                planText.Text = plan.Summary + "\n\n" + string.Join("\n", plan.Actions!.Select((a,i) => $"{i+1}. [{a.Risk.ToUpperInvariant()} · {a.Kind}] {a.Description}\n    {a.Target} {a.Value}"));
                run.IsEnabled = true; state.Text = "Review the plan. Consequential or uncertain actions pause for approval.";
            } else {
                guide = await host.PlanGuide(new(task, context, preferences().AllowWebResearch), cts.Token); cts.Token.ThrowIfCancellationRequested(); guideId = Guid.NewGuid().ToString(); index = 0;
                planText.Text = guide.Summary + "\n\n" + string.Join("\n", guide.Steps!.Select((s,i) => $"{i+1}. {s.Instruction}"));
                next.IsEnabled = true; await DrawStep(cts.Token);
            }
            mood(CompanionMood.Idle);
        } catch (OperationCanceledException) { state.Text = "Stopped"; }
        catch (Exception ex) { state.Text = ex.Message; mood(CompanionMood.Unsure); }
        finally { if (ReferenceEquals(operation, cts)) operation = null; cts.Dispose(); }
    }
    private async Task GuideStep(int direction)
    {
        if (guide?.Steps is null || operation is not null) return;
        guideRevision++; expectationArmed = false; expectationMatches = 0;
        if (index + direction >= guide.Steps.Count) { await FinishGuide(); return; }
        index = Math.Clamp(index + direction, 0, guide.Steps.Count - 1);
        var cts = new CancellationTokenSource(); operation = cts;
        try { await DrawStep(cts.Token); } catch (Exception ex) { state.Text = ex is OperationCanceledException ? "Stopped" : ex.Message; }
        finally { if (ReferenceEquals(operation, cts)) operation = null; cts.Dispose(); }
    }
    private async Task DrawStep(CancellationToken ct)
    {
        var step = guide!.Steps![index]; overlay.Clear(); stepText.Text = step.Instruction;
        state.Text = $"Guide · step {index+1} of {guide.Steps.Count}"; back!.IsEnabled = index > 0; next!.IsEnabled = skip!.IsEnabled = true; next.Content = index + 1 == guide.Steps.Count ? "Done" : "Next";
        var foreground = Native.GetForegroundWindow(); if (!Native.IsOwnWindow(foreground) && foreground != IntPtr.Zero) sourceWindow = foreground;
        var snapshot = await Perception.Capture(sourceWindow, ct); ct.ThrowIfCancellationRequested();
        var element = GroundingResolver.Resolve(snapshot.Context.Elements, step.Ref, step.Target, step.Role)
            ?? GroundingResolver.Resolve(snapshot.Context.Elements, "", step.Target, step.Role);
        expectationArmed = snapshot.Complete && !GuideExpectations.Matches(step.Expect, snapshot.Context); expectationMatches = 0;
        if (element is null) { guideMisses++; state.Text += " · target not visible; focus or scroll the app, then choose Make plan."; mood(CompanionMood.Unsure); }
        else { guideMisses = 0; Draw(snapshot, element, step.Primitive, step.Instruction); mood(CompanionMood.Pointing); }
        guideMonitor.Start();
        if (service() is { } host) await host.Store.Update(s => { s.Guides.RemoveAll(g => g.Id == guideId); s.Guides.Add(new(guideId, goal.Text, guide, index, DateTimeOffset.UtcNow)); if (s.Guides.Count > 20) s.Guides.RemoveAt(0); return true; });
    }
    private async Task FinishGuide()
    {
        guideMonitor.Stop(); overlay.Clear(); state.Text = "Walkthrough complete"; next!.IsEnabled = skip!.IsEnabled = false;
        if (service() is { } host) await host.Store.Update(s => { int i = s.Guides.FindIndex(g => g.Id == guideId); if (i >= 0) s.Guides[i] = s.Guides[i] with { Completed = true, UpdatedAt = DateTimeOffset.UtcNow }; return true; });
    }
    private async Task CheckGuideProgress()
    {
        if (checkingGuide || operation is not null || mode != "guide" || guide?.Steps is null || panel?.IsVisible != true || Native.GetForegroundWindow() != sourceWindow) return;
        checkingGuide = true; int revision = guideRevision;
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3)); guideCheck = timeout;
        try {
            var snapshot = await Perception.Capture(sourceWindow, timeout.Token);
            if (revision != guideRevision || operation is not null || guide?.Steps is null) return;
            var step = guide.Steps[index]; var matches = snapshot.Complete && GuideExpectations.Matches(step.Expect, snapshot.Context);
            if (snapshot.Complete && !matches) expectationArmed = true;
            expectationMatches = expectationArmed && matches ? expectationMatches + 1 : 0;
            if (preferences().GuideAutoAdvance && expectationMatches >= 2) { await GuideStep(1); return; }
            var element = GroundingResolver.Resolve(snapshot.Context.Elements, step.Ref, step.Target, step.Role) ?? GroundingResolver.Resolve(snapshot.Context.Elements, "", step.Target, step.Role);
            if (element is not null) { guideMisses = 0; if (!overlay.IsVisible || drawnGuideTarget != element) Draw(snapshot, element, step.Primitive, step.Instruction); }
            else {
                overlay.Clear(); mood(CompanionMood.Unsure);
                if (++guideMisses >= 2 && guideReplans < 3 && service() is { } host) {
                    guideReplans++; guideMisses = 0;
                    state.Text = "The interface changed. Updating the remaining guide…";
                    var updated = await host.PlanGuide(new(goal.Text + "\nCompleted steps: " + string.Join("; ", guide.Steps.Take(index).Select(s => s.Instruction)), snapshot.Context), timeout.Token);
                    if (revision != guideRevision) return;
                    guide = updated; index = 0; guideRevision++; await DrawStep(timeout.Token);
                } else state.Text = "I cannot identify the next control. Focus or scroll the app, or use Next / Skip.";
            }
        } catch (OperationCanceledException) { }
        catch (Exception ex) { state.Text = ex.Message; }
        finally { if (ReferenceEquals(guideCheck, timeout)) guideCheck = null; checkingGuide = false; }
    }
    private void Draw(ScreenSnapshot snapshot, ScreenElement element, string primitive, string label)
    {
        drawnGuideTarget = element;
        var node = snapshot.Nodes[element.Ref]; var original = new Rect(element.X, element.Y, element.Width, element.Height);
        overlay.Draw(snapshot.Window, element, primitive, label, () => { try { return !node.Current.IsOffscreen && node.Current.BoundingRectangle == original; } catch { return false; } });
    }
    private async Task<bool> Confirm(string text, CancellationToken ct)
    {
        movingGuard = false; state.Text = "Approval needed · " + text; approve!.Visibility = Visibility.Visible;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously); approval = completion;
        using var registration = ct.Register(() => completion.TrySetCanceled(ct));
        try { return await completion.Task; } finally { if (ReferenceEquals(approval, completion)) approval = null; approve.Visibility = Visibility.Collapsed; }
    }
    private async Task Execute()
    {
        if (plan?.Actions is null || executing || operation is not null) return;
        var host = service()!; var approvedPlan = ActionPolicy.Validate(plan); var cts = new CancellationTokenSource(); operation = cts; executing = true; run!.IsEnabled = false;
        var results = new List<ActionResult>(); int replans = 0;
        try {
            var pending = new Queue<AssistantAction>(approvedPlan.Actions!);
            while (pending.Count > 0 && results.Count < 25) {
                cts.Token.ThrowIfCancellationRequested(); if (!preferences().AgentEnabled) throw new InvalidOperationException("Agent mode was disabled.");
                var action = pending.Dequeue(); state.Text = $"Buddy is controlling — Esc to stop · action {results.Count+1}/25"; stepText.Text = action.Description; mood(CompanionMood.AgentWorking);
                state.Text = "Buddy is controlling — Esc to stop · " + action.Description;
                if (OverlayNative.GetCursorPos(out pointer)) movingGuard = true;
                if (action.Kind == "open") {
                    if ((preferences().StrictAgentConfirmations || ActionPolicy.LiveRisk(action, null) == "high") && !await Confirm(action.Description + "\n" + action.Value, cts.Token)) break;
                    if (OverlayNative.GetCursorPos(out pointer)) movingGuard = true;
                    await Task.Delay(600, cts.Token); sourceWindow = await OpenApp(action.Value, cts.Token);
                    results.Add(new(results.Count + 1, action, true, "Opened " + action.Value + "; verified foreground application"));
                } else {
                    Perception.Check(sourceWindow, true);
                    if (!InputNative.SetForegroundWindow(sourceWindow)) throw new InvalidOperationException("Activate the target app, then plan again.");
                    await Task.Delay(150, cts.Token);
                    if (Native.GetForegroundWindow() != sourceWindow) throw new InvalidOperationException("The target app did not gain focus. Stopped.");
                    var snapshot = await Perception.Capture(sourceWindow, cts.Token);
                    var element = action.Kind is "keys" or "wait" ? null : GroundingResolver.Resolve(snapshot.Context.Elements, action.Ref, action.Target, action.Role);
                    if (action.Kind is not ("keys" or "wait") && element is null) {
                        results.Add(new(results.Count+1, action, false, "Not executed: target no longer exists or is ambiguous")); pending.Clear();
                    } else {
                        var risk = await BoundedAction(riskToken => {
                            riskToken.ThrowIfCancellationRequested();
                            if (element is null) return ActionPolicy.LiveRisk(action, null);
                            var node = snapshot.Nodes[element.Ref];
                            // Writable does not imply reversible: web forms and settings may autosave.
                            bool reversible = snapshot.Context.App.Equals("notepad", StringComparison.OrdinalIgnoreCase) && node.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern) && pattern is ValuePattern value && !value.Current.IsReadOnly;
                            bool navigation = node.TryGetCurrentPattern(SelectionItemPattern.Pattern, out _) || node.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out _);
                            return ActionPolicy.LiveRisk(action, element, reversible, navigation);
                        }, cts.Token);
                        if ((preferences().StrictAgentConfirmations && action.Kind is not ("read" or "wait") || risk == "high") && !await Confirm(action.Description + "\n" + action.Target + " " + action.Value, cts.Token)) break;
                        cts.Token.ThrowIfCancellationRequested();
                        if (!InputNative.SetForegroundWindow(sourceWindow)) throw new InvalidOperationException("Activate the target app before continuing.");
                        if (OverlayNative.GetCursorPos(out pointer)) movingGuard = true;
                        if (element is not null) Draw(snapshot, element, "ring", action.Description);
                        await Task.Delay(600, cts.Token);
                        var result = await BoundedAction(token => Apply(action, snapshot, element, token), cts.Token);
                        results.Add(new(results.Count + 1, action, true, result));
                    }
                }
                movingGuard = false; overlay.Clear(); await host.Audit(action.Kind, action.Target, results[^1].Success ? "completed" : "not executed: target unavailable");
                // Observe actual state before completing a batch or proposing its replacement.
                var observed = await Perception.Capture(sourceWindow, cts.Token);
                if (pending.Count == 0) {
                    state.Text = "Verifying the result on your PC…";
                    var decision = await host.ContinueAgent(new(goal.Text, observed.Context, results, 25 - results.Count), cts.Token);
                    if (decision.Status == "done") { state.Text = "Completed · " + decision.Summary; break; }
                    if (decision.Status == "clarify" || replans >= 3) { state.Text = "Review needed · " + decision.Summary; break; }
                    replans++;
                    planText.Text = decision.Summary + "\n\n" + string.Join("\n", decision.Actions!.Select((a,i) => $"{i+1}. {a.Description} · {a.Kind} {a.Target} {a.Value}"));
                    if (!await Confirm("Review the updated plan, then allow it to continue.", cts.Token)) break;
                    pending = new(decision.Actions!);
                }
            }
            stepText.Text = string.Join("\n", results.Select(r => r.Observation[..Math.Min(300, r.Observation.Length)]));
            if (edit is not null) undo!.Visibility = Visibility.Visible;
        } catch (OperationCanceledException) { state.Text = "Stopped. No further actions will run."; await host.Audit("stop", "agent", "cancelled"); }
        catch (Exception ex) { cts.Cancel(); state.Text = "Stopped · " + ex.Message; await host.Audit("stop", "agent", ex is TimeoutException ? "provider timeout; verify the last action" : "action failed"); }
        finally { executing = movingGuard = false; overlay.Clear(); mood(CompanionMood.Idle); if (ReferenceEquals(operation, cts)) operation = null; cts.Dispose(); run.IsEnabled = false; }
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
    internal string Apply(AssistantAction action, ScreenSnapshot snapshot, ScreenElement? targetElement, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); Perception.Check(snapshot.Window, true);
        if (Native.GetForegroundWindow() != snapshot.Window) throw new InvalidOperationException("Focus changed before the action.");
        if (action.Kind == "wait") { ct.WaitHandle.WaitOne(700); ct.ThrowIfCancellationRequested(); return "Waited for the interface"; }
        if (action.Kind == "keys") { InputNative.Keys(action.Value, ct); return "Pressed " + action.Value; }
        var node = snapshot.Nodes[targetElement!.Ref]; var current = node.Current;
        var liveName = Security.Redact(current.Name ?? ""); liveName = liveName[..Math.Min(120, liveName.Length)];
        if (!current.IsEnabled || current.IsOffscreen || current.IsPassword || liveName != targetElement.Name || current.ControlType.ProgrammaticName != "ControlType." + targetElement.Role || current.BoundingRectangle != new Rect(targetElement.X,targetElement.Y,targetElement.Width,targetElement.Height)) throw new InvalidOperationException("The target is no longer safe or in the expected position.");
        ct.ThrowIfCancellationRequested();
        if (action.Kind == "read") return "Read " + Security.Redact(current.Name ?? "");
        if (action.Kind == "type") {
            if (node.TryGetCurrentPattern(ValuePattern.Pattern, out var value) && value is ValuePattern field && !field.Current.IsReadOnly) {
                var before = field.Current.Value; ct.ThrowIfCancellationRequested(); field.SetValue(action.Value);
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
        var before = Native.GetForegroundWindow();
        ct.ThrowIfCancellationRequested(); using var launched = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
        for (int i = 0; i < 30; i++) {
            await Task.Delay(200, ct); var window = Native.GetForegroundWindow();
            if (window == IntPtr.Zero || Native.IsOwnWindow(window)) continue;
            var name = InputNative.ProcessName(window).ToLowerInvariant();
            bool matches = value switch { "notepad" => name == "notepad", "calculator" => name is "calculatorapp" or "calculator", "explorer" => name == "explorer", _ => window != before && name is "msedge" or "chrome" or "firefox" or "brave" or "opera" };
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
    internal void Cancel() { guideRevision++; guideCheck?.Cancel(); guideMonitor.Stop(); operation?.Cancel(); approval?.TrySetCanceled(); movingGuard = false; overlay.Clear(); state.Text = "Stopped"; mood(CompanionMood.Idle); }
    internal async Task ResumeLatest()
    {
        var saved = await service()!.Store.Read(s => s.Guides.LastOrDefault(g => !g.Completed));
        if (saved is null) { await Open("guide", ""); state.Text = "No saved walkthrough yet."; return; }
        await Open("guide", ""); goal.Text = saved.Query; guide = saved.Plan; guideId = saved.Id; index = saved.Index;
        planText.Text = guide.Summary; await GuideStep(0);
    }
    public void Dispose() { Cancel(); stopMonitor.Stop(); overlay.Dispose(); panel?.Hide(); }
}
