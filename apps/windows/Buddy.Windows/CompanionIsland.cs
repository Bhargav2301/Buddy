using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;

namespace Buddy.Windows;

internal sealed record IslandActivity(string Task, string Status, CompanionMood Mood, string BrainStatus = "Local service unavailable");

// Independently authored Buddy surface. It owns presentation only; existing routes own every task.
internal sealed class CompanionIsland : Window, IDisposable
{
    private readonly Func<IslandActivity> activity;
    private readonly Action<string> route;
    private readonly Func<bool> suppressed;
    private readonly Func<bool> hideInFullscreen;
    private readonly Action<string>? modeChanged;
    private readonly Func<LocalTaskSnapshot>? localTasks;
    private readonly Func<LocalTaskToken, bool>? openTaskSource;
    private readonly Func<LocalTaskToken, bool>? dismissTask;
    private readonly TextBlock localSummary = new() { FontSize = 11, Foreground = BuddyTheme.Muted, TextTrimming = TextTrimming.CharacterEllipsis, Visibility = Visibility.Collapsed };
    private readonly TextBlock taskNotice = new() { FontSize = 11, Foreground = BuddyTheme.Muted, TextWrapping = TextWrapping.Wrap };
    private readonly WrapPanel taskPills = new() { Margin = new(0, 6, 0, 6) };
    private readonly StackPanel taskCard = new();
    private long taskRevision = -1;
    private Guid? focusedTask;
    private bool taskRefreshFailed;
    private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly TextBlock compactTask = new() { FontSize = 13, Foreground = BuddyTheme.Ink, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock compactStatus = new() { FontSize = 11, Foreground = BuddyTheme.Deep, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock expandedTask = new() { FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = BuddyTheme.Ink, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock expandedStatus = new() { FontSize = 12, Foreground = BuddyTheme.Deep, TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 10) };
    private readonly StackPanel expanded = new() { Margin = new(8, 4, 8, 4) };
    private readonly Button expand;
    private readonly TextBlock brainStatus = new() { FontSize = 11, Foreground = BuddyTheme.Muted, Margin = new(0, 6, 0, 6), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock notice = new() { FontSize = 11, Foreground = BuddyTheme.Muted, TextWrapping = TextWrapping.Wrap, Text = "Stays open until you collapse or hide it. No microphone starts here." };
    private readonly CompanionFace mascot = new();
    private readonly ScrollViewer details;
    private readonly NotchInteractionState interaction = new();
    private readonly NotchWorkspace? workspace;
    private readonly NotchWorkspacePanel? workspacePanel;
    private readonly Button pin;
    private readonly Button? edit;
    private readonly TextBlock greeting = new() { FontSize = 12, Foreground = BuddyTheme.Secondary, TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 6) };
    private readonly TextBlock ticker = new() { FontSize = 11, Foreground = BuddyTheme.Muted, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly NotchPlacementState placementState;
    private readonly Func<NotchPlacementPreference, NotchPlacementPreference>? placementChanged;
    private readonly Button moveHandle;
    private readonly TextBlock placementNotice = new() { FontSize = 11, Foreground = BuddyTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 6) };
    private bool placing;
    private OverlayNative.Point monitorAnchor;
    private string mode = "Hidden";
    private bool disposed;
    internal string Mode => mode;
    internal IslandVisibility VisibilityReason { get; private set; } = IslandVisibility.HiddenByChoice;
    internal CompanionIsland(Func<IslandActivity> activity, Action<string> route, Func<bool>? suppressed = null, Action<string>? modeChanged = null, Func<bool>? hideInFullscreen = null,
        Func<LocalTaskSnapshot>? localTasks = null, Func<LocalTaskToken, bool>? openTaskSource = null, Func<LocalTaskToken, bool>? dismissTask = null, string? taskCoverage = null,
        NotchWorkspace? workspace = null, NotchPlacementPreference? placement = null, Func<NotchPlacementPreference, NotchPlacementPreference>? placementChanged = null)
    {
        this.activity = activity; this.route = route; this.suppressed = suppressed ?? (() => false); this.modeChanged = modeChanged;
        this.hideInFullscreen = hideInFullscreen ?? (() => false);
        this.localTasks = localTasks; this.openTaskSource = openTaskSource; this.dismissTask = dismissTask;
        this.workspace = workspace;
        placementState = new(placement); this.placementChanged = placementChanged;
        Title = "Buddy companion bar"; Width = 350; SizeToContent = SizeToContent.Height;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        ShowInTaskbar = false; ShowActivated = false; Topmost = true; FontFamily = BuddyTheme.Font; Foreground = BuddyTheme.Ink;
        var panel = new StackPanel();
        var header = new DockPanel();
        var stop = Command("Stop", "Stop Buddy task", Stop); stop.Margin = new(6, 0, 0, 0); DockPanel.SetDock(stop, Dock.Right); header.Children.Add(stop);
        moveHandle = Command("Move", "Move Buddy bar", DetachBar); moveHandle.Focusable = false;
        moveHandle.Cursor = System.Windows.Input.Cursors.SizeAll; moveHandle.ToolTip = "Drag to place Buddy on the desktop; right-click during the drag to cancel. Use the placement buttons for step-by-step movement.";
        moveHandle.Margin = new(4, 0, 0, 0); DockPanel.SetDock(moveHandle, Dock.Right); header.Children.Add(moveHandle);
        moveHandle.PreviewMouseLeftButtonDown += (_, e) => { if (BeginMove()) e.Handled = true; };
        moveHandle.PreviewMouseMove += (_, e) => {
            if (!placementState.IsDragging) return;
            if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) { CancelMove(); return; }
            if (OverlayNative.GetCursorPos(out var pointer)) { placementState.Move(new(pointer.X, pointer.Y)); PlaceBar(); }
            e.Handled = true;
        };
        moveHandle.PreviewMouseLeftButtonUp += (_, e) => { if (placementState.IsDragging) { FinishMove(); e.Handled = true; } };
        moveHandle.PreviewMouseRightButtonDown += (_, e) => { if (placementState.IsDragging) { CancelMove(); e.Handled = true; } };
        moveHandle.LostMouseCapture += (_, _) => { if (placementState.IsDragging) CancelMove(); };
        var summary = new StackPanel(); summary.Children.Add(compactTask); summary.Children.Add(compactStatus); summary.Children.Add(localSummary); summary.Children.Add(ticker);
        var heading = new DockPanel(); var character = new Viewbox { Child = mascot, Width = 40, Height = 40, Stretch = Stretch.Uniform, Margin = new(0, 0, 8, 0) }; DockPanel.SetDock(character, Dock.Left); heading.Children.Add(character); heading.Children.Add(summary);
        expand = Command(heading, "Expand Buddy bar", () => SetMode(mode == "Expanded" ? "Compact" : "Expanded", true));
        expand.HorizontalContentAlignment = HorizontalAlignment.Stretch; header.Children.Add(expand); panel.Children.Add(header);
        AutomationProperties.SetName(greeting, "Buddy greeting"); AutomationProperties.SetName(ticker, "Observed local task ticker");
        expanded.Children.Add(greeting); expanded.Children.Add(expandedTask); expanded.Children.Add(expandedStatus);
        var presentation = new WrapPanel();
        pin = Command("Pin open", "Pin Buddy bar open for this session", () => { interaction.TogglePin(); PresentInteraction(); }); presentation.Children.Add(pin);
        if (workspace is not null) {
            edit = Command("Edit here", "Edit local note and chat", () => { if (interaction.Editing) EndEditing(); else BeginEditing(); }); presentation.Children.Add(edit);
        }
        expanded.Children.Add(presentation);
        var placementActions = new WrapPanel();
        placementActions.Children.Add(Command("Place on desktop", "Place Buddy on the desktop", DetachBar));
        placementActions.Children.Add(Command("Return to top", "Return Buddy to top edge", ReturnToTop));
        foreach (var move in new[] { (Name: "Left", X: -32d, Y: 0d), (Name: "Right", X: 32d, Y: 0d), (Name: "Up", X: 0d, Y: -32d), (Name: "Down", X: 0d, Y: 32d) }) {
            var direction = move;
            placementActions.Children.Add(Command(move.Name, "Move Buddy " + move.Name.ToLowerInvariant(), () => NudgeBar(direction.X, direction.Y)));
        }
        AutomationProperties.SetName(placementNotice, "Bar placement status"); expanded.Children.Add(placementActions); expanded.Children.Add(placementNotice);
        var actions = new WrapPanel();
        foreach (string action in new[] { "Type", "Voice", "Guide", "Refine" }) {
            string selected = action; var button = Command(selected, selected == "Refine" ? "Refine source field" : selected, () => Navigate(selected));
            button.MinWidth = 72; button.Margin = new(0, 0, 6, 6); actions.Children.Add(button);
        }
        expanded.Children.Add(actions);
        var context = Command("Select area", "Select an area for Buddy", () => Navigate("Select area")); context.Margin = new(0, 0, 6, 6);
        var utilities = new WrapPanel(); utilities.Children.Add(context); utilities.Children.Add(Command("Home", "Open Buddy Home", () => Navigate("Home"))); utilities.Children.Add(Command("Settings", "Open Buddy Settings", () => Navigate("Settings")));
        expanded.Children.Add(utilities);
        if (workspace is not null) { workspacePanel = new(workspace); expanded.Children.Add(workspacePanel); }
        var recentHeading = new TextBlock { Text = "Recent local tasks", FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = BuddyTheme.Ink, Margin = new(0, 4, 0, 0) };
        AutomationProperties.SetName(recentHeading, "Recent local tasks"); expanded.Children.Add(recentHeading);
        expanded.Children.Add(new TextBlock { Text = "This Buddy session · on this PC", FontSize = 11, Foreground = BuddyTheme.Muted });
        var coverage = new TextBlock { Text = taskCoverage ?? "Only operations recorded by this Buddy session appear here.", FontSize = 11, Foreground = BuddyTheme.Muted, TextWrapping = TextWrapping.Wrap };
        AutomationProperties.SetName(coverage, "Local task coverage"); expanded.Children.Add(coverage);
        AutomationProperties.SetName(taskPills, "Local task choices"); expanded.Children.Add(taskPills);
        AutomationProperties.SetName(taskCard, "Focused local task"); expanded.Children.Add(taskCard);
        AutomationProperties.SetName(taskNotice, "Local task notice"); expanded.Children.Add(taskNotice);
        expanded.Children.Add(brainStatus);
        expanded.Children.Add(notice);
        var hide = Command("Hide bar", "Hide Buddy bar until enabled again", () => SetMode("Hidden", true)); hide.HorizontalAlignment = HorizontalAlignment.Right; expanded.Children.Add(hide);
        details = new ScrollViewer { Content = expanded, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        panel.Children.Add(details);
        Content = new Border { Child = panel, Background = BuddyTheme.Surface, BorderBrush = BuddyTheme.Line, BorderThickness = new(1), CornerRadius = new(0, 0, 18, 18), Padding = new(10) };
        AutomationProperties.SetLiveSetting(compactStatus, AutomationLiveSetting.Polite);
        SourceInitialized += (_, _) => {
            var handle = new WindowInteropHelper(this).Handle; OverlayNative.Configure(handle, false, noActivate: true);
            HwndSource.FromHwnd(handle).AddHook((IntPtr h, int message, IntPtr w, IntPtr l, ref bool handled) => {
                if (message == 0x0021 && !interaction.Editing) { handled = true; return new IntPtr(3); }
                return IntPtr.Zero;
            });
        };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(new Action(PlaceBar));
        SizeChanged += (_, _) => { if (IsVisible) PlaceBar(); };
        MouseEnter += (_, _) => { if (!placementState.IsDragging && NotchPlacementPolicy.HoverExpands(placementState.Saved)) { interaction.Hover(true); PresentInteraction(); } };
        MouseLeave += (_, _) => { if (!placementState.IsDragging) { interaction.Hover(false); PresentInteraction(); } };
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { Stop(); EndEditing(); SetMode("Compact", true); e.Handled = true; } };
        Closing += ClosingBar;
        refresh.Tick += (_, _) => Refresh();
        SetMode("Hidden");
    }
    private static Button Command(object content, string name, Action click)
    {
        var button = new Button { Content = content, Padding = new(8, 6, 8, 6), MinHeight = 44, Background = BuddyTheme.Surface, Foreground = BuddyTheme.Ink, BorderBrush = BuddyTheme.ControlBorder };
        AutomationProperties.SetName(button, name); button.Click += (_, _) => click(); return button;
    }
    internal void SetMode(string value, bool notify = false)
    {
        if (disposed) return;
        string next = CompanionPresentation.Mode(value);
        if (notify && next != mode) {
            try { modeChanged?.Invoke(next); }
            catch { notice.Text = "Could not save the bar setting. Try again in Settings."; return; }
        }
        mode = next;
        if (mode == "Hidden") CancelMove();
        interaction.SetMode(mode);
        if (mode != "Expanded") EndEditing();
        PresentInteraction();
        if (mode == "Hidden") { VisibilityReason = IslandVisibility.HiddenByChoice; refresh.Stop(); Hide(); return; }
        OverlayNative.GetCursorPos(out monitorAnchor); Refresh(); refresh.Start();
    }
    internal void Refresh()
    {
        if (disposed || mode == "Hidden") return;
        var current = activity();
        string task = string.IsNullOrWhiteSpace(current.Task) ? "Ready when you are" : current.Task;
        task = task[..Math.Min(180, task.Length)];
        compactTask.Text = task;
        expandedTask.Text = string.IsNullOrWhiteSpace(current.Task) ? "Ready when you are" : current.Task;
        compactStatus.Text = expandedStatus.Text = current.Status;
        RefreshLocalTasks();
        workspacePanel?.Refresh();
        brainStatus.Text = current.BrainStatus + " · Local chat";
        mascot.SetMood(current.Mood);
        mascot.LookToward(0, 0);
        AutomationProperties.SetName(this, $"Buddy bar. {task}. {current.Status}");
        bool hideFullscreen = hideInFullscreen();
        VisibilityReason = CompanionPresentation.Visibility(mode, suppressed(), hideFullscreen && OverlayNative.IsFullscreenForeground(), hideFullscreen);
        if (VisibilityReason != IslandVisibility.Visible) { CancelMove(); EndEditing(); Hide(); return; }
        if (!IsVisible) { Show(); UpdateLayout(); }
        PlaceBar();
    }
    private void PresentInteraction()
    {
        if (disposed || details is null) return;
        details.Visibility = interaction.Expanded ? Visibility.Visible : Visibility.Collapsed;
        Width = interaction.Expanded ? 420 : 350;
        greeting.Text = interaction.Greeting;
        pin.Content = interaction.Pinned ? "Unpin" : "Pin open";
        AutomationProperties.SetName(pin, interaction.Pinned ? "Unpin Buddy bar" : "Pin Buddy bar open for this session");
        AutomationProperties.SetName(expand, mode == "Expanded" ? "Collapse Buddy bar" : "Expand Buddy bar");
        if (edit is not null) edit.Content = interaction.Editing ? "Done editing" : "Edit here";
        notice.Text = interaction.Editing ? "Typing is enabled here. Done editing returns the bar to passive mode. Ctrl+Enter sends local chat."
            : placementState.Saved.Mode == "Detached" ? "Expand to see details; pin to keep them open. Edit here enables typing. No microphone starts here."
            : "Hover to see details; pin to keep them open. Edit here enables typing. No microphone starts here.";
        if (IsVisible) PlaceBar();
    }
    private void BeginEditing()
    {
        if (disposed || workspacePanel is null || !IsVisible || !interaction.BeginEditing()) return;
        OverlayNative.Configure(new WindowInteropHelper(this).Handle, false, noActivate: false);
        workspacePanel.SetEditing(true); PresentInteraction(); Activate(); workspacePanel.FocusDraft();
    }
    private void EndEditing()
    {
        interaction.EndEditing(); workspacePanel?.SetEditing(false);
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero) OverlayNative.Configure(handle, false, noActivate: true);
        PresentInteraction();
    }
    private void Stop() { CancelMove(); workspace?.Chat.Stop(); route("Stop"); }
    private void Navigate(string destination) { CancelMove(); EndEditing(); route(destination); }
    internal bool OpenChatSession(string sessionId)
    {
        if (disposed || workspace is null || workspace.Chat.Snapshot.SessionId != sessionId) return false;
        SetMode("Expanded");
        if (!IsVisible) return false; // Respect instance/fullscreen suppression.
        BeginEditing(); return interaction.Editing; // Existing view only; no dispatch or capture.
    }
    private static string PhaseLabel(LocalTaskPhase phase) => phase == LocalTaskPhase.WaitingForReview ? "Review" : phase.ToString();
    private static Brush PhaseBrush(LocalTaskPhase phase) => phase switch {
        LocalTaskPhase.WaitingForReview => BuddyTheme.Warn, LocalTaskPhase.Failed => BuddyTheme.Risk,
        LocalTaskPhase.Completed => BuddyTheme.Positive, LocalTaskPhase.Cancelled => BuddyTheme.Muted, _ => BuddyTheme.Deep
    };
    private static string At(DateTimeOffset value) => value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz", System.Globalization.CultureInfo.CurrentCulture);
    private void RefreshLocalTasks(bool force = false)
    {
        if (disposed) return;
        LocalTaskSnapshot snapshot;
        try { snapshot = localTasks?.Invoke() ?? new(0, Array.Empty<LocalTaskRecord>()); }
        catch {
            taskRefreshFailed = true; localSummary.Visibility = Visibility.Visible; localSummary.Text = "Task updates unavailable";
            taskNotice.Text = "Local tasks could not refresh. Open Home to check the original view."; return;
        }
        if (!force && !taskRefreshFailed && snapshot.Revision == taskRevision) return;
        taskRefreshFailed = false;
        taskRevision = snapshot.Revision;
        var records = snapshot.Tasks;
        ticker.Text = NotchInteractionState.Ticker(records.FirstOrDefault(t => !t.IsTerminal) ?? records.FirstOrDefault());
        focusedTask = CompanionPresentation.FocusTask(records.Select(t => t.Token.Id).ToArray(), focusedTask,
            records.FirstOrDefault(t => !t.IsTerminal)?.Token.Id);
        int running = records.Count(t => t.Phase == LocalTaskPhase.Running), review = records.Count(t => t.Phase == LocalTaskPhase.WaitingForReview);
        localSummary.Visibility = records.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        localSummary.Text = $"{running} running · {review} review · {records.Count} recent";
        taskPills.Children.Clear(); taskCard.Children.Clear(); taskNotice.Text = "";
        if (records.Count == 0) {
            taskNotice.Text = "No local tasks recorded in this Buddy session.";
            return;
        }
        foreach (var task in records) {
            var label = new StackPanel();
            label.Children.Add(new TextBlock { Text = task.Title, FontSize = 12, Foreground = BuddyTheme.Ink, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 245 });
            label.Children.Add(new TextBlock { Text = PhaseLabel(task.Phase), FontSize = 11, Foreground = PhaseBrush(task.Phase) });
            var pill = Command(label, $"Show task: {task.Title}. {PhaseLabel(task.Phase)}", () => {
                focusedTask = task.Token.Id; RefreshLocalTasks(true);
                if (!disposed && interaction.Expanded) { details.UpdateLayout(); taskCard.BringIntoView(); }
            });
            pill.Margin = new(0, 0, 6, 4); pill.MaxWidth = 290;
            pill.Background = focusedTask == task.Token.Id ? BuddyTheme.Soft : BuddyTheme.Surface;
            AutomationProperties.SetHelpText(pill, focusedTask == task.Token.Id ? "Selected local task" : "Show recorded task details"); taskPills.Children.Add(pill);
        }
        var selected = records.First(t => t.Token.Id == focusedTask);
        var content = new StackPanel();
        void Text(string value, string name, Brush? color = null, double size = 12) {
            var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, Foreground = color ?? BuddyTheme.Ink, FontSize = size, Margin = new(0, 0, 0, 6) };
            AutomationProperties.SetName(text, name); content.Children.Add(text);
        }
        Text(selected.Title, "Local task title", size: 15);
        Text(PhaseLabel(selected.Phase), "Local task state", PhaseBrush(selected.Phase));
        Text("Source: " + CompanionPresentation.TaskSource(selected.Source), "Local task source", BuddyTheme.Muted);
        Text("Updated: " + At(selected.UpdatedAt), "Local task updated", BuddyTheme.Muted);
        Text(selected.Detail, "Local task detail");
        Text("Observed stages", "Local task stages heading", BuddyTheme.Secondary);
        if (selected.ObservedSteps.Count == 0) Text("No intermediate stages were recorded.", "Local task stages", BuddyTheme.Muted);
        else foreach (var step in selected.ObservedSteps) Text(At(step.At) + " · " + step.Detail, "Observed local task stage", BuddyTheme.Muted);
        var actions = new WrapPanel();
        var open = Command(selected.Phase == LocalTaskPhase.WaitingForReview ? "Open source to review" : "Open source", "Open local task source", () => OpenTask(selected.Token));
        open.IsEnabled = openTaskSource is not null; open.ToolTip = "Open the original Buddy view. This does not repeat the task or approve a change.";
        actions.Children.Add(open);
        if (selected.IsTerminal) {
            var dismiss = Command("Dismiss", "Dismiss finished local task", () => DismissTask(selected.Token)); dismiss.IsEnabled = dismissTask is not null; actions.Children.Add(dismiss);
        }
        content.Children.Add(actions);
        if (!selected.IsTerminal) Text(selected.Source.StartsWith("agent-", StringComparison.Ordinal)
            ? "Stop controls Buddy tasks only. Stop external work in its originating tool; open local sessions to review questions."
            : "Use Stop to cancel work. Review decisions stay in the original view.", "Local task review boundary", BuddyTheme.Muted, 11);
        taskCard.Children.Add(new Border { Background = BuddyTheme.Raised, BorderBrush = BuddyTheme.Line, BorderThickness = new(1), CornerRadius = new(10), Padding = new(12), Margin = new(0, 0, 0, 10), Child = content });
    }
    private void OpenTask(LocalTaskToken token)
    {
        if (disposed || !interaction.Expanded || !IsVisible) return;
        EndEditing();
        try {
            if (localTasks?.Invoke().Tasks.Any(t => t.Token == token) != true || openTaskSource?.Invoke(token) != true)
                taskNotice.Text = "The original task view is unavailable. Open Home to review history.";
        } catch { taskNotice.Text = "The original task view could not open. No task was repeated."; }
    }
    private void DismissTask(LocalTaskToken token)
    {
        if (disposed || !interaction.Expanded || !IsVisible) return;
        try {
            if (localTasks?.Invoke().Tasks.Any(t => t.Token == token && t.IsTerminal) != true || dismissTask?.Invoke(token) != true) {
                taskNotice.Text = "Only a finished local task can be dismissed. Its current state is kept."; return;
            }
            RefreshLocalTasks(true);
        } catch { taskNotice.Text = "The task could not be dismissed. Its record is kept."; }
    }
    private bool BeginMove()
    {
        if (disposed || !IsVisible || mode == "Hidden") return false;
        EndEditing(); interaction.Hover(false); PresentInteraction(); UpdateLayout();
        try {
            if (!OverlayNative.GetCursorPos(out var point) || !placementState.Begin(new(point.X, point.Y), NotchPlacementNative.Bounds(this), OverlayNative.Scale(new WindowInteropHelper(this).Handle))) return false;
            if (!System.Windows.Input.Mouse.Capture(moveHandle)) { placementState.Cancel(); return false; }
            return true;
        } catch { placementState.Refuse("The bar could not start moving. Its position is unchanged."); placementNotice.Text = placementState.Error; return false; }
    }
    private void FinishMove()
    {
        try {
            // Revalidate the display map and last pointer at release; do not
            // persist a stale preview from before a monitor/work-area change.
            if (OverlayNative.GetCursorPos(out var pointer)) placementState.Move(new(pointer.X, pointer.Y));
            double scale = OverlayNative.Scale(new WindowInteropHelper(this).Handle);
            CurrentPlacement(NotchPlacementNative.Monitors(), scale);
            placementState.Finish(placementChanged);
        } catch { placementState.Refuse("The move could not finish. Its previous placement is kept."); }
        finally { ReleaseMoveCapture(); interaction.Hover(IsMouseOver && NotchPlacementPolicy.HoverExpands(placementState.Saved)); PresentInteraction(); PlaceBar(); }
    }
    internal void CancelPlacement() => CancelMove();
    private void CancelMove()
    {
        placementState.Cancel(); ReleaseMoveCapture(); if (IsVisible) PlaceBar();
    }
    private void ReleaseMoveCapture()
    {
        if (ReferenceEquals(System.Windows.Input.Mouse.Captured, moveHandle)) System.Windows.Input.Mouse.Capture(null);
    }
    private NotchPlacementResult CurrentPlacement(IReadOnlyList<NotchPlacementMonitor> monitors, double scale)
        => placementState.Resolve(monitors, new(monitorAnchor.X, monitorAnchor.Y), Width * scale, Math.Max(1, ActualHeight) * scale, scale);
    private void DetachBar()
    {
        if (disposed || !IsVisible || mode == "Hidden") return;
        CancelMove(); EndEditing();
        try {
            double scale = OverlayNative.Scale(new WindowInteropHelper(this).Handle); var monitors = NotchPlacementNative.Monitors(); var current = CurrentPlacement(monitors, scale);
            var work = current.Monitor.WorkArea;
            var next = NotchPlacementPolicy.At(new(work.Left + (work.Width - current.Bounds.Width) / 2, work.Top + work.Height / 4),
                new(work.Left + work.Width / 2, work.Top + work.Height / 2), monitors, Width * scale, Math.Max(1, ActualHeight) * scale, scale);
            placementState.Commit(next.Preference, placementChanged); interaction.Hover(false); PresentInteraction(); PlaceBar();
        } catch { placementState.Refuse("The display is unavailable. The previous bar placement is kept."); placementNotice.Text = placementState.Error; }
    }
    private void NudgeBar(double x, double y)
    {
        if (disposed || !IsVisible || mode == "Hidden") return;
        CancelMove(); EndEditing();
        try {
            double scale = OverlayNative.Scale(new WindowInteropHelper(this).Handle); var monitors = NotchPlacementNative.Monitors(); var current = CurrentPlacement(monitors, scale);
            var work = current.Monitor.WorkArea;
            var next = NotchPlacementPolicy.At(new(current.Bounds.Left + x * scale, current.Bounds.Top + y * scale),
                new(work.Left + work.Width / 2, work.Top + work.Height / 2), monitors, Width * scale, Math.Max(1, ActualHeight) * scale, scale);
            placementState.Commit(next.Preference, placementChanged); interaction.Hover(false); PresentInteraction(); PlaceBar();
        } catch { placementState.Refuse("The display is unavailable. The previous bar placement is kept."); placementNotice.Text = placementState.Error; }
    }
    private void ReturnToTop()
    {
        if (disposed || !IsVisible || mode == "Hidden") return;
        CancelMove(); EndEditing();
        try {
            double scale = OverlayNative.Scale(new WindowInteropHelper(this).Handle);
            var current = CurrentPlacement(NotchPlacementNative.Monitors(), scale);
            placementState.Commit(new() { MonitorId = current.Monitor.Id }, placementChanged); PlaceBar();
        } catch { placementState.Refuse("The display is unavailable. The previous bar placement is kept."); placementNotice.Text = placementState.Error; }
    }
    private void PlaceBar()
    {
        if (disposed || mode == "Hidden" || placing) return;
        placing = true;
        try {
            var handle = new WindowInteropHelper(this).EnsureHandle(); double scale = OverlayNative.Scale(handle);
            var monitors = NotchPlacementNative.Monitors(); var current = CurrentPlacement(monitors, scale); var work = current.Monitor.WorkArea;
            MaxWidth = Math.Max(1, work.Width / scale - 16); MaxHeight = Math.Max(1, work.Height / scale - 16);
            // Keep Stop outside the scrollable content. A final window bound also
            // prevents a stale size from extending beyond a smaller work area.
            details.MaxHeight = Math.Max(1, MaxHeight - 120);
            current = placementState.Resolve(monitors, new(monitorAnchor.X, monitorAnchor.Y), Math.Min(Width, MaxWidth) * scale,
                Math.Min(Math.Max(1, ActualHeight), MaxHeight) * scale, scale);
            OverlayNative.Move(this, new(current.Bounds.Left, current.Bounds.Top));
            placementNotice.Text = placementState.Error ?? (placementState.IsDragging ? "Moving Buddy. Release to keep the position."
                : current.RecoveredMissingMonitor ? "The saved display is unavailable. Buddy is kept on an available display; move it to save a new position."
                : placementState.Saved.Mode == "Detached" ? "Placed on this desktop. Drag Move, use the direction buttons, or Return to top."
                : "At the top edge. Drag Move or choose Place on desktop.");
        } catch { placementNotice.Text = "The display work area is unavailable. The current bar position is kept."; }
        finally { placing = false; }
    }
    private void ClosingBar(object? sender, CancelEventArgs e) { if (!disposed) { e.Cancel = true; SetMode("Hidden", true); } }
    public void Dispose() { if (disposed) return; disposed = true; placementState.Cancel(); ReleaseMoveCapture(); refresh.Stop(); workspacePanel?.Dispose(); workspace?.Chat.Stop(); Close(); }
}
