using Buddy.Server;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Automation;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;

namespace Buddy.Windows;

internal sealed class QuickChatWindow : Window, IDisposable
{
    private readonly Func<BuddyService?> service;
    private readonly Func<Task<string?>> conversation;
    private readonly Func<DesktopPreferences> preferences;
    private readonly Action<CompanionMood> mood;
    private readonly Action<string,string> workflow;
    private readonly Func<CancellationToken,Task<ScreenSnapshot?>> screen;
    private readonly Action? starting;
    private readonly StackPanel sources = new();
    private readonly TextBox draft = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 4000, MinHeight = 96, MaxHeight = 144, Padding = new(10,8,10,8), VerticalContentAlignment = VerticalAlignment.Top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBox answer = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Background = Brushes.Transparent, Foreground = BuddyTheme.Ink, BorderThickness = new(0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 14, Text = "Ask Buddy, choose Guide to learn, or Agent to do a task." };
    private readonly TextBlock status = new() { Foreground = BuddyTheme.Deep, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox includeScreen = new() { Content = "Screen", Foreground = BuddyTheme.Ink, Margin = new(5) };
    private readonly Button send;
    private readonly ScrollViewer bodyScroll;
    private CancellationTokenSource? request;
    private readonly LocalTaskJournal? localTasks;
    private LocalTaskToken? displayedTask;
    private bool closed;
    private bool opened;
    private long requestRevision;
    private string? pendingDraft;
    private string? displayedConversation;
    internal bool IsBusy => request is not null;
    internal bool IsListening => false;
    internal QuickChatWindow(Func<BuddyService?> service, Func<Task<string?>> conversation, Func<DesktopPreferences> preferences,
        Action<CompanionMood> mood, Action openHome, Action openSettings, Action openVoice, Action<string,string> workflow, Func<CancellationToken,Task<ScreenSnapshot?>> screen, Action<TextBox>? refine = null, Action? starting = null, LocalTaskJournal? localTasks = null)
    {
        this.localTasks = localTasks; this.service = service; this.conversation = conversation; this.preferences = preferences; this.mood = mood; this.workflow = workflow; this.screen = screen;
        this.starting = starting; BuddyTheme.Ensure();
        Title = "Buddy · quick chat"; Width = 440; Height = 560; MinWidth = 320; MinHeight = 300; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.CanResizeWithGrip;
        AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false; FontFamily = BuddyTheme.Font;
        var grid = new Grid { Margin = new(16) }; grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new());
        AutomationProperties.SetName(draft, "Quick chat message"); AutomationProperties.SetName(answer, "Quick chat answer");
        AutomationProperties.SetName(status, "Quick chat status");
        Button Add(string name, Action action, Panel parent) { var b = new Button { Content = name, Margin = new(0,0,5,5), Padding = new(9,5,9,5) }; b.Click += (_,_) => action(); parent.Children.Add(b); return b; }
        var header = new WrapPanel(); Add("Home", () => { Dismiss(); openHome(); }, header); Add("Settings", () => { Dismiss(); openSettings(); }, header); Add("Voice", () => { Dismiss(); openVoice(); }, header); Add("×", Dismiss, header); grid.Children.Add(header);
        answer.MinHeight = 80; answer.MaxHeight = 240;
        var body = new StackPanel(); body.Children.Add(answer); body.Children.Add(sources); body.Children.Add(draft);
        bodyScroll = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        AutomationProperties.SetName(bodyScroll, "Quick chat contents"); Grid.SetRow(bodyScroll, 1); grid.Children.Add(bodyScroll);
        var footer = new StackPanel(); var tools = new WrapPanel { Margin = new(0,8,0,0) };
        send = Add("Send", () => _ = Send(), tools); Add("Guide", () => BeginWorkflow("guide"), tools); Add("Agent", () => BeginWorkflow("agent"), tools); Add("Stop", Cancel, tools); tools.Children.Add(includeScreen); footer.Children.Add(tools); footer.Children.Add(status); body.Children.Add(footer);
        Add("Copy", () => { if (answer.Text.Length > 0) System.Windows.Clipboard.SetText(answer.Text); }, tools);
        Add("Refine source field", () => { if (!IsBusy) refine?.Invoke(draft); }, tools);
        Add("Follow up", () => draft.Focus(), tools);
        footer.Children.Add(new TextBlock { Text = "Stays open while you use other apps. Close with × or Esc.", FontSize = 11, Foreground = BuddyTheme.Muted, TextWrapping = TextWrapping.Wrap });
        Content = new Border { Background = BuddyTheme.Surface, BorderBrush = BuddyTheme.Line, BorderThickness = new(1), CornerRadius = new(20), Child = grid };
        SourceInitialized += (_,_) => OverlayNative.Configure(new WindowInteropHelper(this).Handle, false);
        PreviewKeyDown += (_,e) => { if (e.Key == Key.Escape) { e.Handled = true; Dismiss(); } else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && draft.IsKeyboardFocusWithin) { e.Handled = true; _ = Send(); } };
        Closing += (_,e) => { if (!closed) { e.Cancel = true; Dismiss(); } };
    }
    internal void Open(string? conversationId)
    {
        if (closed) return;
        Cancel();
        if (!opened || conversationId != displayedConversation) { displayedTask = null; answer.Clear(); sources.Children.Clear(); includeScreen.IsChecked = false; }
        opened = true; displayedConversation = conversationId;
        if (OverlayNative.GetCursorPos(out var point)) {
            var handle = new WindowInteropHelper(this).EnsureHandle(); var work = OverlayNative.WorkArea(point); double scale = OverlayNative.Scale(handle);
            var fit = CompanionPresentation.FitPanel(Width, Height, work.Width / scale, work.Height / scale);
            MinWidth = Math.Min(320, fit.Width); MinHeight = Math.Min(300, fit.Height); Width = fit.Width; Height = fit.Height;
            MaxWidth = Math.Max(1, work.Width / scale - 16); MaxHeight = Math.Max(1, work.Height / scale - 16);
            OverlayNative.Place(this, point);
        }
        Show(); Activate(); draft.Focus();
        if (answer.Text.Length == 0) status.Text = preferences().AllowWebResearch ? "Internet research enabled · Enter to send" : "Local AI · Enter to send · Esc to close";
    }
    private void BeginWorkflow(string mode) { if (IsBusy) return; var text = draft.Text; Dismiss(); workflow(mode,text); }
    private async Task Send()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(draft.Text) || service() is not { } host) return;
        starting?.Invoke();
        var text = draft.Text.Trim(); var route = AssistantIntent.Mode(text);
        if (route is "guide" or "agent" or "knowledge") { BeginWorkflow(route); return; }
        var cts = new CancellationTokenSource(); request = cts; long revision = ++requestRevision; pendingDraft = text;
        var activity = localTasks?.Begin("talk", "Typed chat", "Preparing an answer on this PC."); displayedTask = activity;
        bool Current() => !closed && ReferenceEquals(request, cts) && revision == requestRevision;
        bool completedStream = false;
        send.IsEnabled = false; draft.IsReadOnly = true; mood(CompanionMood.Thinking);
        try {
            var id = await conversation() ?? throw new InvalidOperationException("Open Home to finish setup.");
            cts.Token.ThrowIfCancellationRequested(); if (!Current()) return; displayedConversation = id;
            answer.Clear();sources.Children.Clear();
            status.Text = includeScreen.IsChecked == true ? "● Looking at the selected window…" : "Thinking on your PC…";
            var context = includeScreen.IsChecked == true ? await screen(cts.Token) : null; cts.Token.ThrowIfCancellationRequested(); if (!Current()) return; answer.Clear(); draft.Clear(); sources.Children.Clear();
            await foreach (var item in host.Chat(new(id,text,Guid.NewGuid().ToString(),Context:context?.PromptText,UseWeb:preferences().AllowWebResearch,ScreenApp:context?.Context.App),cts.Token)) {
                cts.Token.ThrowIfCancellationRequested(); if (!Current()) return;
                if (item.Type == "delta") { answer.Text += item.Text; answer.ScrollToEnd(); }
                if (item.Type == "status") { status.Text = item.Text; if (item.Text?.StartsWith("Research") == true) mood(CompanionMood.Researching); }
                if (item.Type == "evidence") SourceLinks.Fill(sources, item.Evidence);
                if (item.Type == "done") completedStream = true;
            }
            cts.Token.ThrowIfCancellationRequested();
            if (!completedStream) throw new InvalidOperationException("The answer did not finish. Your draft is kept.");
            if (Current() && activity is { } completed) localTasks?.Finish(completed, LocalTaskPhase.Completed, "Answer displayed in typed chat.", observedStep: true);
            if (Current()) status.Text = "Saved to your conversation · kept here until you close it";
        } catch (Exception ex) { if (activity is { } ended) localTasks?.Finish(ended, ex is OperationCanceledException ? LocalTaskPhase.Cancelled : LocalTaskPhase.Failed, ex is OperationCanceledException ? "Answer stopped; draft restored." : "Answer unavailable; review typed chat for details."); if (Current()) { draft.Text = text; status.Text = ex is OperationCanceledException ? "Stopped · draft restored" : ex.Message; } }
        finally { if (Current()) { request = null; pendingDraft = null; send.IsEnabled = true; draft.IsReadOnly = false; mood(CompanionMood.Idle); } cts.Dispose(); }
    }
    internal void Cancel()
    {
        var active = request; request = null; requestRevision++;
        active?.Cancel();
        if (active is not null && displayedTask is { } stopped) localTasks?.Finish(stopped, LocalTaskPhase.Cancelled, "Answer stopped; draft restored.");
        if (pendingDraft is { } text) { draft.Text = text; status.Text = "Stopped · draft restored"; pendingDraft = null; }
        send.IsEnabled = true; draft.IsReadOnly = false; mood(CompanionMood.Idle);
    }
    internal bool OpenTaskSource(LocalTaskToken token)
    {
        if (closed || displayedTask != token || localTasks?.Snapshot.Tasks.Any(item => item.Token == token) != true) return false;
        Show(); Activate(); return true; // Existing content only; no new request or source-field capture.
    }
    internal void Dismiss() { Cancel(); Hide(); }
    public void Dispose() { closed = true; Cancel(); Close(); }
}
