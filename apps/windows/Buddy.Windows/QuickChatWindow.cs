using Buddy.Server;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
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
    private readonly TextBox draft = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 4000, Height = 65, Padding = new(10) };
    private readonly TextBox answer = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Background = Brushes.Transparent, Foreground = BuddyTheme.Ink, BorderThickness = new(0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 14, Text = "Ask Buddy, choose Guide to learn, or Agent to do a task." };
    private readonly TextBlock status = new() { Foreground = BuddyTheme.Deep, FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly CheckBox includeScreen = new() { Content = "Screen", Foreground = BuddyTheme.Ink, Margin = new(5) };
    private readonly Button send;
    private CancellationTokenSource? request;
    private bool closed;
    private string? displayedConversation;
    internal bool IsBusy => request is not null;
    internal bool IsListening => false;
    internal QuickChatWindow(Func<BuddyService?> service, Func<Task<string?>> conversation, Func<DesktopPreferences> preferences,
        Action<CompanionMood> mood, Action openHome, Action openSettings, Action openVoice, Action<string,string> workflow, Func<CancellationToken,Task<ScreenSnapshot?>> screen, Action<TextBox>? refine = null, Action? starting = null)
    {
        this.service = service; this.conversation = conversation; this.preferences = preferences; this.mood = mood; this.workflow = workflow; this.screen = screen;
        this.starting = starting; BuddyTheme.Ensure();
        Title = "Buddy · quick chat"; Width = 420; Height = 430; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false; FontFamily = BuddyTheme.Font;
        var grid = new Grid { Margin = new(16) }; grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new()); grid.RowDefinitions.Add(new() { Height = GridLength.Auto }); grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        Button Add(string name, Action action, Panel parent) { var b = new Button { Content = name, Margin = new(0,0,5,5), Padding = new(9,5,9,5) }; b.Click += (_,_) => action(); parent.Children.Add(b); return b; }
        var header = new WrapPanel(); Add("Home", () => { Dismiss(); openHome(); }, header); Add("Settings", () => { Dismiss(); openSettings(); }, header); Add("Voice", () => { Dismiss(); openVoice(); }, header); Add("×", Dismiss, header); grid.Children.Add(header);
        var result = new StackPanel(); result.Children.Add(answer); result.Children.Add(sources);
        var scroll = new ScrollViewer { Content = result, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetRow(scroll, 1); grid.Children.Add(scroll); Grid.SetRow(draft, 2); grid.Children.Add(draft);
        var footer = new StackPanel(); var tools = new WrapPanel { Margin = new(0,8,0,0) };
        send = Add("Send", () => _ = Send(), tools); Add("Guide", () => BeginWorkflow("guide"), tools); Add("Agent", () => BeginWorkflow("agent"), tools); Add("Stop", Cancel, tools); tools.Children.Add(includeScreen); footer.Children.Add(tools); footer.Children.Add(status); Grid.SetRow(footer, 3); grid.Children.Add(footer);
        Add("Copy", () => { if (answer.Text.Length > 0) System.Windows.Clipboard.SetText(answer.Text); }, tools);
        Add("Refine source field", () => { if (!IsBusy) refine?.Invoke(draft); }, tools);
        Add("Follow up", () => draft.Focus(), tools);
        Content = new Border { Background = BuddyTheme.Surface, BorderBrush = BuddyTheme.Line, BorderThickness = new(1), CornerRadius = new(20), Child = grid };
        SourceInitialized += (_,_) => OverlayNative.Configure(new WindowInteropHelper(this).Handle, false);
        PreviewKeyDown += (_,e) => { if (e.Key == Key.Escape) { e.Handled = true; Dismiss(); } else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && draft.IsKeyboardFocusWithin) { e.Handled = true; _ = Send(); } };
        Closing += (_,e) => { if (!closed) { e.Cancel = true; Dismiss(); } };
    }
    internal void Open(string? conversationId)
    {
        if (closed) return;
        Cancel(); displayedConversation = conversationId; answer.Clear(); sources.Children.Clear(); includeScreen.IsChecked=false;
        if (OverlayNative.GetCursorPos(out var point)) { new WindowInteropHelper(this).EnsureHandle(); OverlayNative.Place(this, point); }
        Show(); Activate(); draft.Focus(); status.Text = preferences().AllowWebResearch ? "Internet research enabled · Enter to send" : "Local AI · Enter to send · Esc to close";
    }
    private void BeginWorkflow(string mode) { if (IsBusy) return; var text = draft.Text; Dismiss(); workflow(mode,text); }
    private async Task Send()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(draft.Text) || service() is not { } host) return;
        starting?.Invoke();
        var text = draft.Text.Trim(); var route = AssistantIntent.Mode(text);
        if (route is "guide" or "agent" or "knowledge") { BeginWorkflow(route); return; }
        var cts = new CancellationTokenSource(); request = cts; send.IsEnabled = false; draft.IsReadOnly = true; mood(CompanionMood.Thinking);
        try {
            var id = await conversation() ?? throw new InvalidOperationException("Open Home to finish setup.");
            answer.Clear();sources.Children.Clear();
            status.Text = includeScreen.IsChecked == true ? "● Looking at the selected window…" : "Thinking on your PC…";
            var context = includeScreen.IsChecked == true ? await screen(cts.Token) : null; cts.Token.ThrowIfCancellationRequested(); answer.Clear(); draft.Clear(); sources.Children.Clear();
            await foreach (var item in host.Chat(new(id,text,Guid.NewGuid().ToString(),Context:context?.PromptText,UseWeb:preferences().AllowWebResearch,ScreenApp:context?.Context.App),cts.Token)) {
                cts.Token.ThrowIfCancellationRequested();
                if (item.Type == "delta") { answer.Text += item.Text; answer.ScrollToEnd(); }
                if (item.Type == "status") { status.Text = item.Text; if (item.Text?.StartsWith("Research") == true) mood(CompanionMood.Researching); }
                if (item.Type == "evidence") SourceLinks.Fill(sources, item.Evidence);
            }
            status.Text = "Saved to your conversation";
        } catch (Exception ex) { draft.Text = text; status.Text = ex is OperationCanceledException ? "Stopped · draft restored" : ex.Message; }
        finally { request = null; cts.Dispose(); send.IsEnabled = true; draft.IsReadOnly = false; mood(CompanionMood.Idle); }
    }
    internal void Cancel() { request?.Cancel(); mood(CompanionMood.Idle); }
    internal void Dismiss() { Cancel(); Hide(); }
    public void Dispose() { closed = true; Cancel(); Close(); }
}
