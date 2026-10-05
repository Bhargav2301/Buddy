using Buddy.Server;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Text;
using System.Threading.Channels;
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
internal sealed partial class VoiceOverlayWindow : Window, IDisposable
{
    private readonly Func<BuddyService?> service;
    private readonly Func<Task<string?>> conversation;
    private readonly Func<DesktopPreferences> preferences;
    private readonly Action<CompanionMood> mood;
    private readonly Func<IntPtr> target;
    private readonly ScreenPerception perception;
    private readonly Action<string, string> workflow;
    private readonly Action? starting;
    private readonly Func<CancellationToken,DesktopPreferences,Task<LocalRecognizer>> createRecognizer;
    private readonly VoiceTeaching teaching;
    private RegionLease? pendingRegion;
    private readonly Button nextTeaching = new() { Content = "Next step", Visibility = Visibility.Collapsed, Margin = new(0,0,5,4), Padding = new(7,4,7,4) };
    private readonly TextBlock state = new() { Foreground = BuddyTheme.Deep, FontSize = 12, Text = "Voice · microphone off" };
    private readonly TextBlock transcript = new() { Foreground = BuddyTheme.Muted, FontSize = 14, TextWrapping = TextWrapping.Wrap, MaxHeight = 54 };
    private readonly TextBlock answer = new() { Foreground = BuddyTheme.Ink, FontSize = 14, TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel sources = new();
    private readonly ProgressBar level = new() { Minimum = 0, Maximum = 100, Height = 4, Margin = new(0, 8, 0, 8), Foreground = BuddyTheme.Deep };
    private readonly DispatcherTimer monitor = new() { Interval = TimeSpan.FromMilliseconds(25) };
    private readonly DispatcherTimer collapse = new() { Interval = TimeSpan.FromSeconds(6) };
    private readonly DispatcherTimer regionalSpeechLimit = new() { Interval = TimeSpan.FromSeconds(30) };
    private LocalRecognizer? recognizer;
    private LocalVoiceOutput? speaker;
    private readonly System.Windows.Controls.TextBox review = new() { Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MinHeight = 78, MaxHeight = 144, MaxLength = 2000, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly Button confirmTranscript = new() { Content = "Use these words", Visibility = Visibility.Collapsed };
    private readonly System.Windows.Controls.CheckBox includeWindowImage = new() { Content=new TextBlock { Text="Include an image of the selected window for this question", TextWrapping=TextWrapping.Wrap }, IsChecked=false, Margin=new(0,5,0,5) };
    private bool uncertain;
    private CancellationTokenSource? request;
    private readonly HashSet<CancellationTokenSource> sending = [];

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
        Action<CompanionMood> mood, Func<IntPtr> target, ScreenPerception perception, Action<string,string> workflow, Action openChat, Action<string>? refine = null, Action? openHome = null, Action? starting = null, Action<ScreenElement?>? pointing = null, Func<CancellationToken,DesktopPreferences,Task<LocalRecognizer>>? createRecognizer = null)
    {
        this.service = service; this.conversation = conversation; this.preferences = preferences; this.mood = mood; this.target = target; this.perception = perception; this.workflow = workflow;
        this.starting = starting; BuddyTheme.Ensure();
        this.createRecognizer=createRecognizer??((ct,p)=>LocalSpeechInput.Create(ct,p));
        teaching = new(perception, service, pointing, () => preferences().CaptureOnVoice, () => preferences().RegionSelectionEnabled,()=>preferences().RememberTeaching, inkLifetimeSeconds: () => preferences().InkLifetimeSeconds);
        teaching.Invalidated += () => { speaker?.Cancel(); if (!teaching.CanContinue) { request?.Cancel(); nextTeaching.Visibility = Visibility.Collapsed; } Status(teaching.CanContinue ? "Screen changed - choose Next step for a fresh observation" : "Selected area changed - circle it again", CompanionMood.Idle); };
        Title = "Buddy · voice overlay"; Width = 360; SizeToContent = SizeToContent.Height; MaxHeight = 400;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false; FontFamily = new("Segoe UI Variable");
        var p = new StackPanel { Margin = new(14) }; p.Children.Add(state); p.Children.Add(level); p.Children.Add(transcript); p.Children.Add(review); p.Children.Add(confirmTranscript);
        AutomationProperties.SetName(review, "Review uncertain transcript");
        p.Children.Add(includeWindowImage);AutomationProperties.SetName(includeWindowImage,"Include selected-window image for this question");
        confirmTranscript.Click += (_, _) => SubmitReview();
        review.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None) { e.Handled = true; SubmitReview(); } };
        var result = new StackPanel(); result.Children.Add(answer); result.Children.Add(sources);
        p.Children.Add(new ScrollViewer { Content = result, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var row = new WrapPanel { Margin = new(0,8,0,0) };
        void Add(string title, Action click) { var b = new Button { Content = title, Margin = new(0,0,5,4), Padding = new(7,4,7,4) }; b.Click += (_, _) => click(); row.Children.Add(b); }
        Add("Talk", () => { var region = pendingRegion; pendingRegion = null; Open(false, region); }); Add("Finish", Finish); Add("Stop", Cancel); Add("Type", () => { if (regionSession) EditRegionQuestion(); else { Dismiss(); openChat(); } });
        nextTeaching.Click += async (_, _) => await ContinueTeaching(); row.Children.Add(nextTeaching); Add("New lesson",()=>{Cancel();Status("Lesson cleared - Talk to begin again",CompanionMood.Idle);}); Add("Close", Dismiss);
        Add("Copy", () => { if (answer.Text.Length > 0) System.Windows.Clipboard.SetText(answer.Text); });
        Add("Open in Buddy", () => { Dismiss(); openHome?.Invoke(); });
        p.Children.Add(row);
        AddRegionResearch(p);
        var extras = new WrapPanel();
        void Extra(string title, Action action) { var button = new Button { Content = title, Margin = new(0,0,5,4), Padding = new(7,4,7,4) }; button.Click += (_, _) => action(); extras.Children.Add(button); }
        Extra("Full guide", () => StartWorkflow("guide")); Extra("Action plan", () => StartWorkflow("agent"));
        Extra("Refine source field", () => { Dismiss(); refine?.Invoke(lastText); });
        p.Children.Add(new Expander { Header = "Optional tools", Content = extras, IsExpanded = false });
        Content = new Border { Background = BuddyTheme.Surface, BorderBrush = BuddyTheme.Line, BorderThickness = new(1), CornerRadius = new(12), Child = new ScrollViewer { Content = p, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
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
        // Answers remain available until explicit Close/Escape; only ink has a bounded lifetime.
        collapse.Tick += (_, _) => collapse.Stop();
        Closing += (_, e) => { if (!closed) { e.Cancel = true; Dismiss(); } };
    }
    private void StartWorkflow(string mode) { var text = lastText; Dismiss(); if (text.Length > 0) workflow(mode, text); }
    internal Func<string,bool>? RefinementReply { get; set; }
    internal void OpenRegion(RegionLease region, bool autoExplain = false)
    {
        starting?.Invoke(); Cancel(); regionSession=true; pendingRegion=region; sourceWindow=region.Selection.Window;includeWindowImage.IsChecked=false;includeWindowImage.Visibility=Visibility.Collapsed;
        request=new(); lastText=""; answer.Text=""; sources.Children.Clear(); transcript.Text="Selected area - optional question; Enter explains it locally, Shift+Enter adds a line";
        review.Text=""; review.Visibility=confirmTranscript.Visibility=Visibility.Visible; confirmTranscript.Content="Explain selected area"; regionResearch.Visibility=Visibility.Visible;
        if(OverlayNative.GetCursorPos(out var point)){anchor=point;new WindowInteropHelper(this).EnsureHandle();OverlayNative.Place(this,point);}
        Show(); Activate(); review.Focus(); monitor.Start(); collapse.Stop(); Status("Area selected - microphone off",CompanionMood.Idle);
        if (autoExplain) {
            int revision = generation; var owner = request;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
                if (!closed && IsVisible && regionSession && revision == generation && ReferenceEquals(pendingRegion, region) && ReferenceEquals(request, owner) && owner?.IsCancellationRequested == false) SubmitReview();
            }));
        }
    }
    internal void Open(bool hold, RegionLease? selectedRegion = null)
    {
        starting?.Invoke();
        Cancel(preserveLesson:true); regionSession = selectedRegion is not null; regionResearch.Visibility=regionSession?Visibility.Visible:Visibility.Collapsed; uncertain = false; review.Visibility = confirmTranscript.Visibility = Visibility.Collapsed; held = hold; pendingRegion = selectedRegion; sourceWindow = selectedRegion?.Selection.Window ?? target(); confirmTranscript.Content="Use these words"; utterance.Clear(); transcript.Text = ""; answer.Text = ""; sources.Children.Clear(); level.Value = 0;
        includeWindowImage.IsChecked=false;includeWindowImage.Visibility=selectedRegion is null?Visibility.Visible:Visibility.Collapsed;
        if (OverlayNative.GetCursorPos(out var point)) { anchor = point; new WindowInteropHelper(this).EnsureHandle(); Height = 220; OverlayNative.Place(this, point); }
        Show(); monitor.Start(); collapse.Stop(); listening = true; int token = ++generation;
        Status("● Listening · preparing microphone…", CompanionMood.Listening);
        request = new();
        if(selectedRegion is not null) { regionalSpeechLimit.Tick-=RegionSpeechTimeout;regionalSpeechLimit.Tick+=RegionSpeechTimeout;regionalSpeechLimit.Start(); }
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => _ = StartRecognizer(token)));
    }
    private void RegionSpeechTimeout(object? sender,EventArgs e){regionalSpeechLimit.Stop();Finish();}
    private async Task StartRecognizer(int token)
    {
        LocalRecognizer? created = null;
        try {
            if (closed || generation != token || !listening || request is null) return;
            created = await createRecognizer(request.Token, preferences());
            if (closed || generation != token || !listening) { LocalSpeechInput.Stop(created); return; }
            recognizer = created; created = null; var engine = recognizer;
            engine.Processing += (_, _) => Dispatcher.BeginInvoke(new Action(() => { if (generation == token) Status("Microphone off - transcribing locally with Whisper", CompanionMood.Thinking); }));
            engine.InitialSilenceTimeout = TimeSpan.FromSeconds(8); engine.EndSilenceTimeout = TimeSpan.FromMilliseconds(650);
            engine.AudioLevelUpdated += (_, e) => Dispatcher.BeginInvoke(new Action(() => { if (generation == token) level.Value = e.AudioLevel; }));
            engine.SpeechHypothesized += (_, e) => Dispatcher.BeginInvoke(new Action(() => { if (generation == token) transcript.Text = utterance + e.Result.Text; }));
            engine.SpeechRecognized += (_, e) => Dispatcher.BeginInvoke(new Action(() => { if (generation == token && listening) { uncertain |= e.Result.RequiresReview || SpeechReview.Required(e.Result.Confidence, e.Result.Alternates.Select(a => (a.Text, a.Confidence))); utterance.Append(e.Result.Text).Append(' '); transcript.Text = utterance.ToString(); } }));
            engine.SpeechRecognitionRejected += (_, _) => Dispatcher.BeginInvoke(new Action(() => { if (generation == token) uncertain = true; }));
            engine.RecognizeCompleted += (_, e) => Dispatcher.BeginInvoke(new Action(() => {
                if (generation != token || !listening) return;
                var text = e.Result?.Text ?? utterance.ToString().Trim();
                uncertain |= e.Error is not null || e.Result is null || e.Result.RequiresReview || SpeechReview.Required(e.Result.Confidence, e.Result.Alternates.Select(a => (a.Text, a.Confidence)));
                StopMic();
                if (e.Cancelled) { Cancel(); return; }
                if (text.Length == 0) { Cancel(); Status(e.Error?.Message ?? "No speech heard · try Talk or Type", CompanionMood.Unsure); return; }
                transcript.Text = text;
                if (uncertain || AssistantIntent.Mode(text) == "agent") { review.Text = text; review.Visibility = confirmTranscript.Visibility = Visibility.Visible; Status("Not sure I heard correctly — review, retry Talk, or Type", CompanionMood.Unsure); return; }
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
        if(RefinementReply?.Invoke(text)==true){Dismiss();return;}
        lastText = text; transcript.Text = "You: " + text; var source = request ??= new(); int token = generation;
        source.CancelAfter(TimeSpan.FromMinutes(4));
        sending.Add(source); Status("Thinking on your PC.", CompanionMood.Thinking);
        try {
            var mode = AssistantIntent.Mode(text);
            if (mode is "agent" or "knowledge" && pendingRegion is null) { Dismiss(); workflow(mode, text); return; }
            if (regionSession && pendingRegion is null && teaching.HasRegion) { teaching.ChangeQuestion(text); await ShowTeaching(source.Token, token); return; }
            if (regionSession && pendingRegion is null) throw new InvalidOperationException("Select the area again before asking another local question.");
            if (preferences().CaptureOnVoice || pendingRegion is not null || includeWindowImage.IsChecked==true) {
                if(pendingRegion is not null) {
                    pendingRegion.Check();
                    var foreground = Native.GetForegroundWindow();
                    if (foreground != sourceWindow && !Native.IsOwnWindow(foreground)) throw new InvalidOperationException("The active app changed. Return to the selected app and ask again.");
                    InputNative.CheckDesktopAndElevation(sourceWindow);
                    if(!InputNative.SetForegroundWindow(sourceWindow)) throw new InvalidOperationException("Focus the selected app, then ask again.");
                    await Task.Delay(120,source.Token);
                }
                var selectedRegion=pendingRegion; pendingRegion=null;
                teaching.Begin(sourceWindow, text, selectedRegion,includeWindowImage.IsChecked==true);
                await ShowTeaching(source.Token, token); return;
            }
            var host = service() ?? throw new InvalidOperationException("Buddy is still starting.");
            var id = await conversation() ?? throw new InvalidOperationException("Open Home to finish setup.");
            source.Token.ThrowIfCancellationRequested();
            // Screen capture off means a text-only local answer, with no implicit web research.
            var speechPreferences = preferences();
            bool staged = false, spokenOk = true;
            Task? playback = null;
            Exception? playbackError = null;
            var sentences = Channel.CreateBounded<string>(new BoundedChannelOptions(1) {
                SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait
            });
            async Task PlaySentences()
            {
                try {
                    speaker ??= new LocalVoiceOutput();
                    pendingSpeech = 1;
                    Status("Speaking - microphone off", CompanionMood.Speaking);
                    await speaker.SpeakSentencesAsync(sentences.Reader.ReadAllAsync(source.Token), speechPreferences, source);
                } catch (Exception ex) {
                    playbackError = ex;
                    source.Cancel();
                } finally { if (generation == token) pendingSpeech = 0; }
            }
            try {
                await foreach (var item in host.Chat(new(id, text, Guid.NewGuid().ToString(), "voice", UseWeb: false,
                    StreamSentences: speechPreferences.StreamVoiceSentences && speechPreferences.ReadVoiceAnswers && IsVisible), source.Token)) {
                    source.Token.ThrowIfCancellationRequested(); if (generation != token) throw new OperationCanceledException(source.Token);
                    if (item.Type == "status" && !staged) Status(item.Text ?? "Thinking.", CompanionMood.Thinking);
                    if (item.Type == "delta") answer.Text += item.Text;
                    if (item.Type == "sentence") {
                        if (!StagedConversation.IsSentence(item.Text)) throw new InvalidOperationException("Sentence review failed; speech stopped.");
                        staged = true;
                        answer.Text += (answer.Text.Length > 0 ? " " : "") + item.Text;
                        await sentences.Writer.WriteAsync(item.Text!, source.Token);
                        playback ??= PlaySentences();
                    }
                }
                sentences.Writer.TryComplete();
                if (playback is not null) await playback;
                if (playbackError is not null) throw playbackError;
                source.Token.ThrowIfCancellationRequested();
                if (!staged) spokenOk = await Speak(answer.Text);
            } catch {
                // Never retry or replay a lead that has already been emitted.
                source.Cancel();
                sentences.Writer.TryComplete();
                if (playback is not null) await playback;
                if (playbackError is not null && playbackError is not OperationCanceledException) throw playbackError;
                throw;
            }
            if (generation == token && pendingSpeech == 0 && spokenOk) { Status("Microphone off - Talk to follow up", CompanionMood.Idle); collapse.Start(); }
        } catch (OperationCanceledException) { if (generation == token) Status("Stopped or timed out - microphone off; your question is kept. Choose Type to review it.", CompanionMood.Idle); }
        catch (Exception ex) { if (generation == token) Status(ex.Message, CompanionMood.Error); }
        finally { if (ReferenceEquals(request, source)) request = null; sending.Remove(source); source.Dispose(); }
    }
    private async Task ContinueTeaching()
    {
        if (request is not null || !teaching.CanContinue) return;
        if (!preferences().CaptureOnVoice && !preferences().RegionSelectionEnabled && !teaching.HasExplicitImage) { Cancel(); Status("Screen context is off. Summon Buddy for a text-only answer.", CompanionMood.Idle); return; }
        speaker?.Cancel(); var source = new CancellationTokenSource(TimeSpan.FromMinutes(4)); request = source; sending.Add(source); int token = generation;
        nextTeaching.IsEnabled = false; collapse.Stop();
        try { await ShowTeaching(source.Token, token); }
        catch (OperationCanceledException) { if (generation == token) Status("Stopped or timed out - microphone off; your question is kept. Choose Type to review it.", CompanionMood.Idle); }
        catch (Exception ex) { if (generation == token) { nextTeaching.IsEnabled = teaching.CanContinue; Status(ex.Message, CompanionMood.Unsure); } }
        finally { if (ReferenceEquals(request, source)) request = null; sending.Remove(source); source.Dispose(); }
    }
    private async Task ShowTeaching(CancellationToken ct, int token)
    {
        Status("Looking at the selected window on your PC.", CompanionMood.Looking); nextTeaching.IsEnabled = false;
        // This explicit teaching request may return focus from Buddy to its pinned source.
        // Never steal focus from an unrelated app, and never relax Frame's foreground guard.
        if (Native.IsOwnWindow(Native.GetForegroundWindow())) {
            InputNative.CheckDesktopAndElevation(sourceWindow);
            if (!InputNative.SetForegroundWindow(sourceWindow)) throw new InvalidOperationException("Focus the selected app, then choose Next step.");
            await Task.Delay(120,ct);
        }
        var turn = await teaching.Next(ct).WaitAsync(ct); ct.ThrowIfCancellationRequested(); if (generation != token) return;
        if (!IsVisible) Show();
        answer.Text = turn.Speech; nextTeaching.Visibility = teaching.CanContinue ? Visibility.Visible : Visibility.Collapsed;
        bool linkedReferences = SourceLinks.Fill(sources, new MessageEvidence(Sources: turn.Sources?.Select(s => new SourceLink(s.Title, s.Url)).ToList()));
        if (!string.IsNullOrWhiteSpace(turn.Origin)) sources.Children.Insert(0, new TextBlock { Text = turn.Origin, TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = BuddyTheme.Muted });
        foreach(var source in turn.Knowledge??[])sources.Children.Add(new TextBlock{Text=$"Imported reference: {source.RelativePath}:{source.Line} | app version {source.AppVersion} | {source.Revision[..Math.Min(12,source.Revision.Length)]}",TextWrapping=TextWrapping.Wrap,FontSize=11,Foreground=BuddyTheme.Muted});
        await Speak(turn.Speech, linkedReferences || turn.Knowledge?.Count > 0); ct.ThrowIfCancellationRequested(); if (generation != token) return;
        nextTeaching.IsEnabled = teaching.CanContinue;
        Status(teaching.CanContinue ? "Your turn - Next step observes the selected app again" : "Microphone off - Talk to follow up",
            teaching.IsPointing ? CompanionMood.Pointing : CompanionMood.Idle);
        if (!teaching.CanContinue) collapse.Start();
    }
    private async Task<bool> Speak(string text, bool sourcesAttached = false)
    {
        if (!preferences().ReadVoiceAnswers || !IsVisible) return true;
        int token = generation;
        try {
            speaker ??= new LocalVoiceOutput(); pendingSpeech = 1; Status("Speaking — microphone off", CompanionMood.Speaking);
            await speaker.SpeakAsync(text, preferences(), sourcesAttached);
            return true;
        } catch (OperationCanceledException) { if (generation == token) Status("Speech stopped — audio output changed or Stop was pressed", CompanionMood.Idle); }
        catch (Exception ex) { if (generation == token) Status("Answer shown — " + ex.Message, CompanionMood.Idle); }
        finally { if (generation == token) pendingSpeech = 0; }
        return false;
    }
    private void Status(string value, CompanionMood valueMood) { state.Text = value; AutomationProperties.SetName(state, value); mood(valueMood); }
    private void StopMic() { regionalSpeechLimit.Stop(); listening = false; ++generation; var old = recognizer; recognizer = null; LocalSpeechInput.Stop(old); level.Value = 0; }
    internal void Cancel() =>Cancel(preserveLesson:false);
    private void Cancel(bool preserveLesson) {
        StopMic(); regionSession = false; regionResearch.Visibility = Visibility.Collapsed; EnableRegionResearch(true); researchQuery.Clear(); var old = request; request = null; old?.Cancel();
        if (old is not null && !sending.Contains(old)) old.Dispose();
        pendingRegion?.Dispose(); pendingRegion=null;if(preserveLesson)teaching.Pause();else teaching.Cancel(); nextTeaching.Visibility = Visibility.Collapsed;
        speaker?.Cancel(); review.Visibility = confirmTranscript.Visibility = Visibility.Collapsed; pendingSpeech = 0; sentences.Clear(); collapse.Stop(); Status("Stopped - microphone off", CompanionMood.Idle);
    }
    internal void Dismiss() { Cancel(preserveLesson:true); Hide(); monitor.Stop(); }
    public void Dispose() { if (closed) return; closed = true; Cancel(); monitor.Stop(); collapse.Stop(); teaching.Dispose(); speaker?.Dispose(); Close(); }
}
