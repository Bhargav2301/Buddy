using Buddy.Server;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;

namespace Buddy.Windows;

// The voice surface owns recognition and speech. It never opens or focuses the typed composer.
internal sealed class VoiceOverlayWindow : Window, IDisposable
{
    private readonly Func<BuddyService?> service;
    private readonly Func<Task<string?>> conversation;
    private readonly Func<DesktopPreferences> preferences;
    private readonly Action<CompanionMood> mood;
    private readonly Func<IntPtr> target;
    private readonly ScreenPerception perception;
    private readonly Action<string, string> workflow;
    private readonly TextBlock state = new() { Foreground = Brushes.White, FontSize = 14, Text = "Listening · preparing microphone…" };
    private readonly TextBlock transcript = new() { Foreground = Brushes.LightGray, FontSize = 14, TextWrapping = TextWrapping.Wrap, MaxHeight = 54 };
    private readonly TextBlock answer = new() { Foreground = Brushes.White, FontSize = 14, TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar level = new() { Minimum = 0, Maximum = 100, Height = 4, Margin = new(0, 8, 0, 8), Foreground = Brushes.Turquoise };
    private readonly DispatcherTimer monitor = new() { Interval = TimeSpan.FromMilliseconds(25) };
    private readonly DispatcherTimer collapse = new() { Interval = TimeSpan.FromSeconds(6) };
    private SpeechRecognitionEngine? recognizer;
    private SpeechSynthesizer? speaker;
    private CancellationTokenSource? request;
    private sealed record VoiceContext(ScreenSnapshot Screen, byte[]? Image) : IDisposable { public void Dispose() { if (Image is not null) Array.Clear(Image); } }
    private Task<VoiceContext?>? snapshot;
    private readonly HashSet<CancellationTokenSource> sending = [];
    private readonly HashSet<Prompt> spoken = [];
    private readonly StringBuilder utterance = new();
    private readonly SentenceBuffer sentences = new();
    private int generation, pendingSpeech;
    private bool listening, held, closed;
    private IntPtr sourceWindow;
    private OverlayNative.Point anchor;
    private string lastText = "";
    internal bool IsListening => listening;
    internal bool IsBusy => request is not null;
    internal VoiceOverlayWindow(Func<BuddyService?> service, Func<Task<string?>> conversation, Func<DesktopPreferences> preferences,
        Action<CompanionMood> mood, Func<IntPtr> target, ScreenPerception perception, Action<string,string> workflow, Action openChat)
    {
        this.service = service; this.conversation = conversation; this.preferences = preferences; this.mood = mood; this.target = target; this.perception = perception; this.workflow = workflow;
        Title = "Buddy · voice overlay"; Width = 360; SizeToContent = SizeToContent.Height; MaxHeight = 290;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false; FontFamily = new("Segoe UI Variable");
        var p = new StackPanel { Margin = new(14) }; p.Children.Add(state); p.Children.Add(level); p.Children.Add(transcript);
        p.Children.Add(new ScrollViewer { Content = answer, MaxHeight = 112, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var row = new WrapPanel { Margin = new(0,8,0,0) };
        void Add(string title, Action click) { var b = new Button { Content = title, Margin = new(0,0,5,4), Padding = new(7,4,7,4) }; b.Click += (_, _) => click(); row.Children.Add(b); }
        Add("Talk", () => Open(false)); Add("Finish", Finish); Add("Stop", Cancel); Add("Type", () => { Dismiss(); openChat(); });
        Add("Guide", () => StartWorkflow("guide")); Add("Do it", () => StartWorkflow("agent")); Add("×", Dismiss);
        p.Children.Add(row); Content = new Border { Background = new SolidColorBrush(Color.FromArgb(240,18,34,38)), BorderBrush = Brushes.Turquoise, BorderThickness = new(1), CornerRadius = new(16), Child = p };
        AutomationProperties.SetLiveSetting(state, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(level, "Microphone level");
        SourceInitialized += (_, _) => OverlayNative.Configure(new WindowInteropHelper(this).Handle, false);
        SizeChanged += (_, _) => { if (IsVisible) OverlayNative.Place(this, anchor); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Dismiss(); } };
        monitor.Tick += (_, _) => {
            if ((InputNative.GetAsyncKeyState(27) & 0x8000) != 0) Dismiss();
            var foreground = Native.GetForegroundWindow();
            if (listening && foreground != sourceWindow && !Native.IsOwnWindow(foreground)) { Cancel(); Status("Microphone stopped · focus changed", CompanionMood.Idle); }
        };
        collapse.Tick += (_, _) => { if (!IsMouseOver && !listening && !IsBusy && pendingSpeech == 0) { Hide(); monitor.Stop(); collapse.Stop(); } };
        Closing += (_, e) => { if (!closed) { e.Cancel = true; Dismiss(); } };
    }
    private void StartWorkflow(string mode) { var text = lastText; Dismiss(); if (text.Length > 0) workflow(mode, text); }
    internal void Open(bool hold)
    {
        Cancel(); held = hold; sourceWindow = target(); utterance.Clear(); transcript.Text = ""; answer.Text = ""; level.Value = 0;
        if (OverlayNative.GetCursorPos(out var point)) { anchor = point; new WindowInteropHelper(this).EnsureHandle(); Height = 220; OverlayNative.Place(this, point); }
        Show(); monitor.Start(); collapse.Stop(); listening = true; int token = ++generation;
        Status("● Listening · preparing microphone…", CompanionMood.Listening);
        request = new(); snapshot = ReadScreen(request.Token);
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => _ = StartRecognizer(token)));
    }
    private async Task<VoiceContext?> ReadScreen(CancellationToken ct)
    {
        if (!preferences().CaptureOnVoice) return null;
        try {
            transcript.Text = "● Looking at the active window"; var s = await perception.Capture(sourceWindow, ct); ct.ThrowIfCancellationRequested();
            var image = await perception.Image(s, ct); // Redacted active-window frame, held only in memory.
            await service()!.Audit("capture", s.Context.App, image is null ? "UIA text only; screenshot skipped" : "UIA and redacted memory-only frame");
            return new(s, image);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { transcript.Text = "Screen unavailable · " + ex.Message; return null; }
    }
    private async Task StartRecognizer(int token)
    {
        SpeechRecognitionEngine? created = null;
        try {
            created = await Task.Run(() => {
                var all = SpeechRecognitionEngine.InstalledRecognizers(); if (all.Count == 0) throw new InvalidOperationException("Install a Windows speech language, or choose Type.");
                var r = new SpeechRecognitionEngine(all.FirstOrDefault(r => r.Culture.Name == System.Globalization.CultureInfo.CurrentUICulture.Name) ?? all[0]);
                try { r.LoadGrammar(new DictationGrammar()); r.SetInputToDefaultAudioDevice(); return r; } catch { r.Dispose(); throw; }
            });
            if (closed || generation != token || !listening) { created.Dispose(); return; }
            recognizer = created; created = null; var engine = recognizer;
            engine.InitialSilenceTimeout = TimeSpan.FromSeconds(8); engine.EndSilenceTimeout = TimeSpan.FromMilliseconds(650);
            engine.AudioLevelUpdated += (_, e) => Dispatcher.BeginInvoke(new Action(() => { if (generation == token) level.Value = e.AudioLevel; }));
            engine.SpeechHypothesized += (_, e) => Dispatcher.BeginInvoke(new Action(() => { if (generation == token) transcript.Text = utterance + e.Result.Text; }));
            engine.SpeechRecognized += (_, e) => Dispatcher.BeginInvoke(new Action(() => { if (generation == token && listening) { utterance.Append(e.Result.Text).Append(' '); transcript.Text = utterance.ToString(); } }));
            engine.RecognizeCompleted += (_, e) => Dispatcher.BeginInvoke(new Action(() => {
                if (generation != token || !listening) return;
                var text = utterance.ToString().Trim(); if (text.Length == 0) text = e.Result?.Text ?? "";
                StopMic();
                if (e.Cancelled) { Cancel(); return; }
                if (e.Error is not null || text.Length == 0) { Cancel(); Status(e.Error?.Message ?? "No speech heard · try Talk or Type", CompanionMood.Unsure); return; }
                _ = Send(text);
            }));
            Status(held ? "● Listening · release to send" : "● Listening · speak, then pause", CompanionMood.Listening);
            engine.RecognizeAsync(held ? RecognizeMode.Multiple : RecognizeMode.Single);
        } catch (Exception ex) { created?.Dispose(); if (generation == token) { Cancel(); Status("Microphone: " + ex.Message, CompanionMood.Error); } }
    }
    internal void Finish()
    {
        if (!listening) return;
        if (recognizer is null) { Cancel(); Status("Microphone was preparing · hold a little longer", CompanionMood.Unsure); return; }
        recognizer.RecognizeAsyncStop();
    }
    private async Task Send(string text)
    {
        lastText = text; transcript.Text = "You: " + text; var source = request ??= new(); int token = generation; var captured = snapshot;
        sending.Add(source); VoiceContext? context = null;
        Status("Thinking on your PC…", CompanionMood.Thinking);
        try {
            context = captured is null ? null : await captured;
            var host = service() ?? throw new InvalidOperationException("Buddy is still starting.");
            var id = await conversation() ?? throw new InvalidOperationException("Open Home to finish setup.");
            source.Token.ThrowIfCancellationRequested();
            var mode = AssistantIntent.Mode(text);
            if (mode is "agent" or "guide") { Dismiss(); workflow(mode, text); return; }
            string? image = null;
            if (context?.Image is { } frame) {
                var settings = await host.Store.Read(s => (s.Model, s.VisionModel));
                var available = await host.Engine.Status(settings.Model, settings.VisionModel, source.Token);
                if (available.Installed.Contains(settings.VisionModel)) image = Convert.ToBase64String(frame);
            }
            await foreach (var item in host.Chat(new(id, text, Guid.NewGuid().ToString(), "voice", context?.Screen.PromptText, image, preferences().AllowWebResearch), source.Token)) {
                if (item.Type == "status") Status(item.Text ?? "Thinking…", item.Text?.StartsWith("Research") == true ? CompanionMood.Researching : CompanionMood.Thinking);
                if (item.Type == "delta") { answer.Text += item.Text; foreach (var sentence in sentences.Add(item.Text ?? "")) Speak(sentence); }
            }
            foreach (var sentence in sentences.Flush()) Speak(sentence);
            if (pendingSpeech == 0) { Status("Microphone off · Talk to follow up", CompanionMood.Idle); collapse.Start(); }
        } catch (OperationCanceledException) { if (generation == token) Status("Stopped · microphone off", CompanionMood.Idle); }
        catch (Exception ex) { if (generation == token) Status(ex.Message, CompanionMood.Error); }
        finally { if (ReferenceEquals(request, source)) { request = null; snapshot = null; } context?.Dispose(); sending.Remove(source); source.Dispose(); }
    }
    private void Speak(string text)
    {
        if (!preferences().ReadVoiceAnswers || !IsVisible) return;
        try {
            if (speaker is null) { speaker = new(); speaker.SpeakCompleted += (_, e) => Dispatcher.BeginInvoke(new Action(() => { if (!spoken.Remove(e.Prompt)) return; pendingSpeech = spoken.Count; if (pendingSpeech == 0 && !listening && !IsBusy) { Status("Microphone off · Talk to follow up", CompanionMood.Idle); collapse.Start(); } })); }
            spoken.Add(speaker.SpeakAsync(text)); pendingSpeech = spoken.Count; Status("Speaking · microphone off", CompanionMood.Speaking);
        } catch { pendingSpeech = 0; Status("Answer shown · read-aloud unavailable", CompanionMood.Idle); }
    }
    private void Status(string value, CompanionMood valueMood) { state.Text = value; AutomationProperties.SetName(state, value); mood(valueMood); }
    private void StopMic() { listening = false; ++generation; var old = recognizer; recognizer = null; if (old is not null) { try { old.RecognizeAsyncCancel(); } catch (InvalidOperationException) { } old.Dispose(); } level.Value = 0; }
    internal void Cancel() {
        StopMic(); var old = request; var capture = snapshot; request = null; snapshot = null;
        old?.Cancel();
        if (old is not null && !sending.Contains(old)) {
            if (capture is null) old.Dispose();
            else _ = capture.ContinueWith(t => { if (t.Status == TaskStatus.RanToCompletion) t.Result?.Dispose(); else _ = t.Exception; old.Dispose(); }, TaskScheduler.Default);
        }
        speaker?.SpeakAsyncCancelAll(); spoken.Clear(); pendingSpeech = 0; sentences.Clear(); collapse.Stop(); Status("Stopped · microphone off", CompanionMood.Idle);
    }
    internal void Dismiss() { Cancel(); Hide(); monitor.Stop(); }
    public void Dispose() { if (closed) return; closed = true; Cancel(); monitor.Stop(); collapse.Stop(); speaker?.Dispose(); Close(); }
}
