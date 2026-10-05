using System.Speech.Recognition;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Automation;
using Button = System.Windows.Controls.Button;

namespace Buddy.Windows;

internal sealed class DictationWindow : Window
{
    private readonly FocusedDraft draft;
    private readonly FocusedFieldEditor editor;
    private readonly Action<CompanionMood> mood;
    private readonly Action? starting;
    private readonly TextBlock status = new() { FontSize = 12, Foreground = BuddyTheme.Deep, TextWrapping = TextWrapping.Wrap };
    private readonly System.Windows.Controls.TextBox transcript = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly ProgressBar level = new() { Maximum = 100, Height = 4, Foreground = BuddyTheme.Deep, Margin = new(0, 8, 0, 8) };
    private readonly Button start, finish, insert, undo;
    private readonly StringBuilder recognized = new();
    private readonly DispatcherTimer monitor = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly Rect sourceBounds;
    private LocalRecognizer? recognizer;
    private CancellationTokenSource? operation;
    private int generation;
    private bool closed, listening, applied, writing;
    internal DictationWindow(FocusedFieldEditor editor, FocusedDraft draft, Action<CompanionMood> mood, Action? starting = null)
    {
        this.editor = editor; this.draft = draft; this.mood = mood; this.starting = starting; sourceBounds = WindowCapture.Bounds(draft.Anchor.Window);
        Title = "Buddy · Dictation"; Width = 360; SizeToContent = SizeToContent.Height; MaxHeight = 430; FontFamily = BuddyTheme.Font;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        var p = new StackPanel { Margin = new(14) }; p.Children.Add(status); p.Children.Add(level); p.Children.Add(transcript);
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite); AutomationProperties.SetName(transcript, "Dictation transcript"); AutomationProperties.SetName(level, "Microphone level");
        var actions = new WrapPanel();
        start = BuddyTheme.Button("Start", () => _ = Start()); finish = BuddyTheme.Button("Finish", Finish);
        insert = BuddyTheme.Button("Insert", () => _ = Insert(), true); insert.IsEnabled = false;
        undo = BuddyTheme.Button("Undo", () => _ = Undo()); undo.IsEnabled = false;
        actions.Children.Add(start); actions.Children.Add(finish); actions.Children.Add(BuddyTheme.Button("Stop", Cancel));
        actions.Children.Add(insert); actions.Children.Add(BuddyTheme.Button("Copy", () => { if (transcript.Text.Length > 0) System.Windows.Clipboard.SetText(transcript.Text); }));
        actions.Children.Add(undo); actions.Children.Add(BuddyTheme.Button("Close", Close)); p.Children.Add(actions);
        p.Children.Add(new TextBlock { Text = "Review, then Insert · never submits · Undo for 30 seconds", TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = BuddyTheme.Muted });
        Content = BuddyTheme.Card(p);
        SourceInitialized += (_, _) => {
            OverlayNative.Configure(new WindowInteropHelper(this).Handle, false, noActivate: true);
            Place();
        };
        SizeChanged += (_, _) => { if (IsVisible) Place(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Cancel(); } };
        monitor.Tick += (_, _) => {
            if ((InputNative.GetAsyncKeyState(27) & 0x8000) != 0) Cancel();
            if (listening && Native.GetForegroundWindow() != draft.Anchor.Window && !Native.IsOwnWindow(Native.GetForegroundWindow())) { Cancel(); status.Text = "Microphone off · focus changed. Copy remains available."; }
            if (undo.IsEnabled && !draft.Edit.CanUndo(DateTimeOffset.UtcNow)) undo.IsEnabled = false;
            try { if (listening && WindowCapture.Bounds(draft.Anchor.Window) != sourceBounds) { Cancel(); status.Text = "Microphone off · the window moved."; } } catch { Cancel(); }
        };
        Closed += (_, _) => { closed = true; Cancel(); monitor.Stop(); };
        monitor.Start();
    }
    private void Place() => OverlayNative.Place(this, new() { X = (int)draft.Anchor.Bounds.Left, Y = (int)draft.Anchor.Bounds.Bottom });
    internal async Task Start()
    {
        if (closed || applied || writing) return;
        starting?.Invoke();
        Cancel(); int token = ++generation; var cts = new CancellationTokenSource(); operation = cts;
        recognized.Clear(); transcript.Text = ""; listening = true; start.IsEnabled = false; finish.IsEnabled = true;
        status.Text = "Listening · preparing microphone…"; mood(CompanionMood.Listening);
        try {
            var engine = await LocalSpeechInput.Create(cts.Token);
            if (closed || token != generation) { LocalSpeechInput.Stop(engine); return; }
            recognizer = engine;
            engine.InitialSilenceTimeout = TimeSpan.FromSeconds(8);
            engine.AudioLevelUpdated += (_, e) => Dispatcher.BeginInvoke(new Action(() => { if (token == generation) level.Value = e.AudioLevel; }));
            engine.SpeechHypothesized += (_, e) => Dispatcher.BeginInvoke(new Action(() => { if (token == generation) transcript.Text = recognized + e.Result.Text; }));
            engine.SpeechRecognized += (_, e) => Dispatcher.BeginInvoke(new Action(() => { if (token == generation) { if (recognized.Length > 0) recognized.Append(' '); recognized.Append(e.Result.Text); transcript.Text = recognized.ToString(); } }));
            engine.RecognizeCompleted += (_, e) => Dispatcher.BeginInvoke(new Action(() => {
                if (token != generation || closed) return;
                string text = e.Cancelled ? "" : e.Result?.Text ?? recognized.ToString(); Cancel(); transcript.Text = text;
                if (e.Cancelled || e.Error is not null && text.Length == 0) { status.Text = e.Error?.Message ?? "Stopped."; return; }
                insert.IsEnabled = text.Length > 0; status.Text = text.Length > 0 ? "Microphone off · review your words, then Insert." : "No speech heard. Try Start again.";
            }));
            engine.RecognizeAsync(RecognizeMode.Multiple); status.Text = "Listening · choose Finish when ready.";
        } catch (Exception e) {
            if (token == generation && !closed) { Cancel(); status.Text = e is OperationCanceledException ? "Microphone unavailable or stopped. Try Start again." : e.Message; }
        }
    }
    private void Finish()
    {
        if (!listening) return;
        if (recognizer is null) { Cancel(); return; }
        finish.IsEnabled = false; status.Text = "Finishing dictation…";
        try { recognizer.RecognizeAsyncStop(); } catch (Exception e) { Cancel(); status.Text = e.Message; }
    }
    internal void Cancel()
    {
        generation++; operation?.Cancel(); operation?.Dispose(); operation = null;
        var engine = recognizer; recognizer = null; LocalSpeechInput.Stop(engine);
        listening = false; level.Value = 0; finish.IsEnabled = insert.IsEnabled = false; start.IsEnabled = !applied && !writing; mood(CompanionMood.Idle);
        if (!closed) status.Text = applied ? "Microphone off · Undo remains available." : "Microphone off · stopped.";
    }
    private async Task Insert()
    {
        if (listening || writing || applied || transcript.Text.Length == 0 || draft.Insertion is null) return;
        Cancel(); using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3)); operation = cts; writing = true; start.IsEnabled = insert.IsEnabled = false;
        try {
            await editor.Apply(draft, draft.Insertion.Replace(draft.Edit.Original, transcript.Text), cts.Token);
            applied = true; undo.IsEnabled = true; status.Text = "Inserted without sending. Undo restores the exact original.";
        } catch (Exception e) { status.Text = e.Message + " Copy remains available."; }
        finally { writing = false; start.IsEnabled = !applied; if (ReferenceEquals(operation, cts)) operation = null; }
    }
    private async Task Undo()
    {
        if (writing) return;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3)); operation = cts; writing = true; undo.IsEnabled = false;
        try { await editor.Undo(draft, cts.Token); applied = false; status.Text = "Exact original restored."; }
        catch (Exception e) { status.Text = e.Message; }
        finally { writing = false; start.IsEnabled = !applied; if (ReferenceEquals(operation, cts)) operation = null; }
    }
}
