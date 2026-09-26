using Buddy.Server;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using Orientation = System.Windows.Controls.Orientation;

namespace Buddy.Windows;

internal sealed class QuickChatWindow : Window, IDisposable
{
    private static readonly Brush Ink = ColorBrush("#EDF5F5"), Muted = ColorBrush("#9DAFBB"), Mint = ColorBrush("#8EE4C5"), Surface = ColorBrush("#17232D");
    private readonly Func<BuddyService?> service;
    private readonly Func<Task<string?>> conversation;
    private readonly Func<DesktopPreferences> preferences;
    private readonly Action<CompanionMood> mood;
    private readonly TextBox draft = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = 20000, MinHeight = 52, MaxHeight = 96, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBox answer = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), Background = Brushes.Transparent, FontSize = 14, Foreground = Ink, Text = "A little clarity, right where you are.\nAsk a question or tap Voice." };
    private readonly TextBlock status = new() { Text = "Local AI · microphone off", Foreground = Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock transcript = new() { Foreground = Muted, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new(0, 0, 0, 6) };
    private readonly TextBlock keyboardHint = new() { Text = "Enter to send · Esc to close", VerticalAlignment = VerticalAlignment.Center, Foreground = Muted, FontSize = 10 };
    private readonly ScrollViewer response;
    private readonly Button send, voice, stop, home, settings;
    private SpeechRecognitionEngine? recognizer;
    private SpeechSynthesizer? speaker;
    private Prompt? speakingPrompt;
    private CancellationTokenSource? request;
    private bool listening, closed;
    private int speechGeneration;
    private string? displayedConversation;
    internal bool IsListening => listening;
    internal bool IsBusy => request is not null;

    private static Brush ColorBrush(string hex) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
    private static Button MakeButton(string text, Action action, bool primary = false)
    {
        var button = new Button { Content = text, Padding = new(12, 7, 12, 7), Margin = new(0, 0, 6, 0), Background = primary ? Mint : Surface, Foreground = primary ? Surface : Ink, BorderThickness = new(0), FontSize = 12, Cursor = Cursors.Hand };
        System.Windows.Automation.AutomationProperties.SetName(button, text);
        button.Click += (_, _) => action(); return button;
    }

    internal QuickChatWindow(Func<BuddyService?> service, Func<Task<string?>> conversation, Func<DesktopPreferences> preferences,
        Action<CompanionMood> mood, Action openHome, Action openSettings)
    {
        this.service = service; this.conversation = conversation; this.preferences = preferences; this.mood = mood;
        Title = "Buddy · quick chat"; Width = 480; Height = 354;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
        Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false;
        FontFamily = new("Segoe UI");
        var grid = new Grid { Margin = new(18) };
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new DockPanel { Margin = new(0, 0, 0, 12) };
        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        home = MakeButton("Home", () => { Dismiss(); openHome(); });
        settings = MakeButton("Settings", () => { Dismiss(); openSettings(); });
        tools.Children.Add(home); tools.Children.Add(settings); tools.Children.Add(MakeButton("×", Dismiss));
        DockPanel.SetDock(tools, Dock.Right); header.Children.Add(tools);
        var brand = new StackPanel(); brand.Children.Add(new TextBlock { Text = "◉  Buddy", Foreground = Mint, FontWeight = FontWeights.SemiBold, FontSize = 20 }); brand.Children.Add(status); header.Children.Add(brand); grid.Children.Add(header);
        response = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = answer, Margin = new(0, 0, 0, 12) }; Grid.SetRow(response, 1); grid.Children.Add(response);
        var composer = new StackPanel(); composer.Children.Add(transcript);
        draft.Background = Surface; draft.Foreground = Ink; draft.CaretBrush = Mint; draft.BorderBrush = ColorBrush("#334652"); draft.Padding = new(12); draft.FontSize = 15;
        System.Windows.Automation.AutomationProperties.SetName(draft, "Message Buddy"); composer.Children.Add(draft); Grid.SetRow(composer, 2); grid.Children.Add(composer);
        var footer = new DockPanel { Margin = new(0, 10, 0, 0) };
        send = MakeButton("Send ↗", () => _ = Send(false), true); DockPanel.SetDock(send, Dock.Right); footer.Children.Add(send);
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        voice = MakeButton("Voice", ToggleVoice); stop = MakeButton("Stop", Cancel); stop.IsEnabled = false;
        controls.Children.Add(voice); controls.Children.Add(stop); controls.Children.Add(keyboardHint);
        footer.Children.Add(controls); Grid.SetRow(footer, 3); grid.Children.Add(footer);
        Content = new Border { Background = ColorBrush("#101820"), BorderBrush = ColorBrush("#3A565A"), BorderThickness = new(1), CornerRadius = new(18), Child = grid };
        SourceInitialized += (_, _) => OverlayNative.Configure(new WindowInteropHelper(this).Handle, false);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { e.Handled = true; Dismiss(); }
            else if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None && draft.IsKeyboardFocusWithin)
            { e.Handled = true; _ = Send(false); }
        };
        // Losing focus never leaves a microphone recording unnoticed.
        Deactivated += (_, _) => { if (listening) { StopListening(); status.Text = "Microphone stopped · tap Voice to resume"; } };
        Closing += (_, e) => { if (!closed) { e.Cancel = true; Dismiss(); } };
    }

    internal void Open(bool startVoice, string? conversationId)
    {
        if (closed) return;
        if (!IsBusy && displayedConversation != conversationId)
        {
            displayedConversation = conversationId;
            answer.Text = "A little clarity, right where you are.\nAsk a question or tap Voice.";
            transcript.Text = "";
        }
        if (OverlayNative.GetCursorPos(out var point))
        {
            new WindowInteropHelper(this).EnsureHandle();
            FitAndPlace(point);
            if (!IsVisible) Show();
            // WPF may update its DPI after crossing monitors; position once more after layout.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => { if (IsVisible) FitAndPlace(point); }));
        }
        else if (!IsVisible) Show();
        bool activated = Activate(); draft.Focus();
        if (startVoice && !IsBusy)
        {
            if (activated || IsActive) StartListening();
            else status.Text = "Click Voice to start listening";
        }
    }
    private void FitAndPlace(OverlayNative.Point point)
    {
        // First move changes the window's monitor and its DPI. Then size in the
        // target monitor's logical units and place again using physical pixels.
        OverlayNative.Place(this, point);
        var work = OverlayNative.WorkArea(point); var scale = OverlayNative.Scale(new WindowInteropHelper(this).Handle);
        Width = Math.Min(480, Math.Max(280, work.Width / scale - 24));
        Height = Math.Min(354, Math.Max(240, work.Height / scale - 24));
        keyboardHint.Visibility = Width >= 430 ? Visibility.Visible : Visibility.Collapsed;
        UpdateLayout(); OverlayNative.Place(this, point);
    }
    internal void Dismiss() { Cancel(); Hide(); }
    internal void Cancel()
    {
        StopListening(); StopSpeaking(); request?.Cancel();
        status.Text = IsBusy ? "Stopping…" : "Stopped · microphone off";
        mood(CompanionMood.Idle);
    }
    private void SetBusy(bool value)
    {
        draft.IsReadOnly = value; send.IsEnabled = !value && !listening; voice.IsEnabled = !value;
        home.IsEnabled = settings.IsEnabled = !value;
        stop.IsEnabled = value || listening || speakingPrompt is not null;
    }
    private async Task Send(bool spoken)
    {
        if (IsBusy || string.IsNullOrWhiteSpace(draft.Text)) return;
        var host = service();
        if (host is null) { status.Text = "Buddy is starting. Open Home to check PC setup."; return; }
        StopListening(); StopSpeaking();
        var text = draft.Text.Trim(); var cts = new CancellationTokenSource(); request = cts;
        SetBusy(true); mood(CompanionMood.Thinking); status.Text = "Asking your local AI…";
        try
        {
            var id = await conversation();
            if (id is null) throw new InvalidOperationException("Open Home and finish PC setup first.");
            displayedConversation = id; transcript.Text = "You: " + text; answer.Clear(); draft.Clear();
            await foreach (var item in host.Chat(new ChatRequest(id, text, Guid.NewGuid().ToString(), spoken ? "voice" : "type"), cts.Token))
            {
                if (item.Type == "delta") { answer.Text += item.Text; response.ScrollToEnd(); }
                if (item.Type == "status") status.Text = item.Text;
            }
            status.Text = "Saved to your conversation · microphone off";
            if (spoken && preferences().ReadVoiceAnswers && IsVisible) Speak(answer.Text);
        }
        catch (OperationCanceledException) { draft.Text = text; answer.Text += "\n[Stopped — partial answer not saved]"; status.Text = "Stopped · draft restored"; }
        catch (Exception ex) { draft.Text = text; answer.Text += "\n[Answer failed — not saved]"; status.Text = ex.Message; }
        finally { request = null; cts.Dispose(); SetBusy(false); if (speakingPrompt is null) mood(CompanionMood.Idle); }
    }

    private void ToggleVoice()
    {
        if (listening) recognizer?.RecognizeAsyncStop();
        else StartListening();
    }
    private void StartListening()
    {
        if (IsBusy) return;
        StopListening(); StopSpeaking();
        int generation = ++speechGeneration;
        var prefix = draft.Text.Trim(); string? heard = null;
        try
        {
            var installed = SpeechRecognitionEngine.InstalledRecognizers();
            if (installed.Count == 0) throw new InvalidOperationException("Install a Windows speech language, or type here. Win+H can also dictate into this box.");
            var chosen = installed.FirstOrDefault(r => r.Culture.Name == System.Globalization.CultureInfo.CurrentUICulture.Name) ?? installed[0];
            var engine = new SpeechRecognitionEngine(chosen); recognizer = engine;
            engine.InitialSilenceTimeout = TimeSpan.FromSeconds(8); engine.BabbleTimeout = TimeSpan.FromSeconds(15);
            engine.EndSilenceTimeout = TimeSpan.FromMilliseconds(650); engine.EndSilenceTimeoutAmbiguous = TimeSpan.FromSeconds(1.2);
            engine.LoadGrammar(new DictationGrammar()); engine.SetInputToDefaultAudioDevice();
            engine.SpeechRecognized += (_, e) => { heard = e.Result.Text; };
            engine.RecognizeCompleted += (_, e) => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (closed || generation != speechGeneration || !listening) return;
                var captured = heard ?? e.Result?.Text;
                StopListening();
                if (e.Cancelled) { status.Text = "Microphone stopped"; return; }
                if (e.Error is not null) { status.Text = "Microphone: " + e.Error.Message; return; }
                if (string.IsNullOrWhiteSpace(captured)) { status.Text = "No speech recognized. Tap Voice or type."; return; }
                draft.Text = string.IsNullOrEmpty(prefix) ? captured : prefix + " " + captured;
                _ = Send(true);
            }));
            listening = true; voice.Content = "Finish"; status.Text = "● Listening — speak, then pause to send";
            mood(CompanionMood.Listening); SetBusy(false); engine.RecognizeAsync(RecognizeMode.Single);
        }
        catch (Exception ex) { StopListening(); status.Text = ex.Message; draft.Focus(); }
    }
    private void StopListening()
    {
        ++speechGeneration; listening = false;
        var engine = recognizer; recognizer = null;
        if (engine is not null)
        {
            try { engine.RecognizeAsyncCancel(); } catch (InvalidOperationException) { }
            engine.Dispose();
        }
        voice.Content = "Voice"; SetBusy(IsBusy);
        if (!IsBusy && speakingPrompt is null) mood(CompanionMood.Idle);
    }
    private void Speak(string text)
    {
        try
        {
            if (speaker is null)
            {
                speaker = new SpeechSynthesizer();
                speaker.SpeakCompleted += (_, e) => Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (closed || !ReferenceEquals(e.Prompt, speakingPrompt)) return;
                    speakingPrompt = null; mood(CompanionMood.Idle); SetBusy(IsBusy); status.Text = "Microphone off · press the voice shortcut to reply";
                }));
            }
            speakingPrompt = speaker.SpeakAsync(text); mood(CompanionMood.Speaking);
            status.Text = "Speaking · microphone off";
        }
        catch (Exception ex) { speakingPrompt = null; status.Text = "Answer saved. Read-aloud unavailable: " + ex.Message; }
    }
    private void StopSpeaking() { speakingPrompt = null; speaker?.SpeakAsyncCancelAll(); }
    public void Dispose()
    {
        if (closed) return; closed = true; Cancel(); speaker?.Dispose(); Close();
    }
}
