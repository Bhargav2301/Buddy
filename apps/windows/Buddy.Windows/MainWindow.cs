using Buddy.Server;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Speech.Recognition;
using System.Speech.Synthesis;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using QRCoder;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using CheckBox = System.Windows.Controls.CheckBox;
using MessageBox = System.Windows.MessageBox;
using Orientation = System.Windows.Controls.Orientation;

namespace Buddy.Windows;

public sealed partial class MainWindow : Window
{
    private static readonly SolidColorBrush Bg = Brush("#111820"), Panel = Brush("#1A242E"), Ink = Brush("#EBF3F7"), Muted = Brush("#9DB0BF"), Accent = Brush("#8EE4C5");
    private BuddyHost? host;
    private readonly TextBlock status = Text("Starting Buddy…", 13, Muted), contextLabel = Text("", 12, Accent);
    private readonly TextBox input = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 76, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly StackPanel messages = new(), rail = new();
    private readonly ScrollViewer scroller;
    private readonly ListBox conversations = new() { BorderThickness = new(0), Background = System.Windows.Media.Brushes.Transparent, Foreground = Ink, DisplayMemberPath = "Title", Margin = new(0, 16, 0, 8) };
    private readonly ComboBox mode = new() { ItemsSource = new[] { "Type", "Hybrid", "Voice" }, SelectedIndex = 0, Width = 105 };
    private readonly CheckBox speak = new() { Content = "Read aloud", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button send, stop;
    private readonly System.Windows.Forms.NotifyIcon tray = new();
    private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromSeconds(6) }, foreground = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private SpeechSynthesizer? tts;
    private SpeechRecognitionEngine? recognizer;
    private CancellationTokenSource? request;
    private string? currentId, context;
    private byte[]? image;
    private IntPtr hwnd, previousWindow;
    private bool busy, shuttingDown, refreshing;
    private DesktopPreferences desktop = DesktopPreferences.Load();
    private CursorCompanionWindow? companion;
    private QuickChatWindow? quick;
    private VoiceOverlayWindow? voiceOverlay;
    private DesktopAssistant? assistant;
    private PushToTalkHook? ptt;
    private ShortcutRegistration? shortcut;
    private bool voiceShortcutReady;
    private readonly TextBlock shortcutHint = Text("Starting shortcuts…", 11, Muted);
    private static SolidColorBrush Brush(string hex) => new((System.Windows.Media.Color)ColorConverter.ConvertFromString(hex));
    private static TextBlock Text(string value, double size = 15, System.Windows.Media.Brush? color = null) => new() { Text = value, FontSize = size, Foreground = color ?? Ink, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) };
    private static Button Btn(string label, Action action, bool primary = false)
    {
        var b = new Button { Content = label, Padding = new(14, 9, 14, 9), Margin = new(0, 0, 8, 8), Background = primary ? Accent : Panel, Foreground = primary ? Bg : Ink, BorderThickness = new(0), Cursor = System.Windows.Input.Cursors.Hand };
        b.Click += (_, _) => action(); return b;
    }
    private static StackPanel Row(params UIElement[] children) { var row = new StackPanel { Orientation = Orientation.Horizontal }; foreach (var c in children) row.Children.Add(c); return row; }
    private static void StyleBox(TextBox box) { box.Background = Panel; box.Foreground = Ink; box.CaretBrush = Accent; box.BorderBrush = Brush("#344553"); box.Padding = new(12); box.FontSize = 15; }

    public MainWindow() : this(true) { }
    internal MainWindow(bool startService)
    {
        Title = "Buddy — your local AI companion"; Width = 1160; Height = 820; MinWidth = 850; MinHeight = 620; Background = Bg; Foreground = Ink; FontFamily = new("Segoe UI"); WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var layout = new Grid(); layout.ColumnDefinitions.Add(new() { Width = new(250) }); layout.ColumnDefinitions.Add(new());
        var side = new DockPanel { Background = Panel, Margin = new(0) }; Grid.SetColumn(side, 0); layout.Children.Add(side);
        var brand = new StackPanel { Margin = new(24, 28, 16, 8) }; brand.Children.Add(Text("◉  Buddy", 28)); brand.Children.Add(Text("A little clarity. Any time.", 12, Muted)); brand.Children.Add(Btn("＋ New conversation", () => _ = NewChat(), true)); brand.Children.Add(Btn("Settings", OpenSettings)); DockPanel.SetDock(brand, Dock.Top); side.Children.Add(brand);
        var nav = new StackPanel { Margin = new(18, 12, 12, 16) };
        nav.Children.Add(Btn("Saved prompts", () => _ = Notes(false))); nav.Children.Add(Btn("What Buddy remembers", () => _ = Notes(true))); nav.Children.Add(Btn("Pair Android phone", Pair)); nav.Children.Add(Btn("PC setup & models", () => _ = Setup())); nav.Children.Add(Btn("Export conversation", () => _ = Export())); nav.Children.Add(Btn("Delete conversation", () => _ = Delete())); nav.Children.Add(Btn("Quit Buddy", () => _ = Quit()));
        nav.Children.Insert(0, Btn("Open quick chat", () => OpenQuick(false), true));
        nav.Children.Insert(1, Btn("Cursor & shortcuts", CursorSettings));
        nav.Children.Insert(2, Btn("Assistant settings", AssistantSettings));
        nav.Children.Insert(3, Btn("Guide this screen", () => StartWorkflow("guide", input.Text)));
        nav.Children.Insert(4, Btn("Agent task", () => StartWorkflow("agent", input.Text)));
        nav.Children.Insert(5, Btn("Resume walkthrough", () => { if (assistant is not null) _ = assistant.ResumeLatest(); }));
        nav.Children.Insert(6, Btn("Try pointing tutorial", ShowPractice));
        var navigation = new ScrollViewer { Content = nav, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        DockPanel.SetDock(navigation, Dock.Bottom); side.Children.Add(navigation); side.Children.Add(conversations);
        var main = new Grid { Margin = new(36, 28, 36, 24) }; Grid.SetColumn(main, 1); layout.Children.Add(main);
        main.RowDefinitions.Add(new() { Height = GridLength.Auto }); main.RowDefinitions.Add(new()); main.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new StackPanel(); header.Children.Add(Text("YOUR EVERYDAY THINKING PARTNER", 11, Accent)); header.Children.Add(Text("What’s on your mind?", 30)); header.Children.Add(status); main.Children.Add(header);
        scroller = new ScrollViewer { Content = messages, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(0, 18, 0, 20) }; Grid.SetRow(scroller, 1); main.Children.Add(scroller);
        var composer = new StackPanel(); Grid.SetRow(composer, 2); main.Children.Add(composer); composer.Children.Add(contextLabel);
        StyleBox(input); composer.Children.Add(input);
        send = Btn("Send  ↗", () => _ = Send(), true); stop = Btn("Stop", Cancel); stop.IsEnabled = false;
        composer.Children.Add(Row(mode, Btn("Dictate", Dictate), Btn("Refine", () => _ = Refine()), speak, stop, send));
        composer.Children.Add(Row(Btn("Read screen text", () => _ = ReadContext()), Btn("Capture window", () => _ = Capture()), Btn("Clear context", ClearContext)));
        composer.Children.Add(shortcutHint);
        Content = layout;
        input.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter && System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control)) { e.Handled = true; _ = Send(); } };
        mode.SelectionChanged += (_, _) => { StopMainDictation(); tts?.SpeakAsyncCancelAll(); };
        conversations.SelectionChanged += (_, _) => { if (!refreshing && !busy && conversations.SelectedItem is Conversation c) _ = Select(c.Id); };
        SourceInitialized += (_, _) => {
            hwnd = new WindowInteropHelper(this).Handle; HwndSource.FromHwnd(hwnd).AddHook(Hook); Native.SetWindowDisplayAffinity(hwnd, 0x11);
            if (!startService) return; // Isolated window tests must not register user hotkeys or start the shared store.
            shortcut = new ShortcutRegistration((id, modifiers) => Native.RegisterHotKey(hwnd, id, modifiers, 0x20), id => Native.UnregisterHotKey(hwnd, id));
            var preferred = ShortcutChoice.Choices.FirstOrDefault(c => c.Label == desktop.Shortcut) ?? ShortcutChoice.Choices[0];
            if (!shortcut.TrySet(preferred))
                foreach (var choice in ShortcutChoice.Choices.Where(c => c.Modifiers != 8)) if (shortcut.TrySet(choice)) break;
            voiceShortcutReady = Native.RegisterHotKey(hwnd, 3, 0x4006, 0x20);
            Native.RegisterHotKey(hwnd, 2, 0x4003, 0x1B);
            UpdateShortcutHint();
        };
        if (startService) Loaded += async (_, _) => await Start();
        Closing += (_, e) => { if (!shuttingDown) { e.Cancel = true; StopMainDictation(); tts?.SpeakAsyncCancelAll(); Hide(); tray.ShowBalloonTip(2500, "Buddy is beside your cursor", "Use your shortcut for quick chat, or the tray icon for Home. Choose Quit Buddy to stop phone access.", System.Windows.Forms.ToolTipIcon.Info); } };
        foreground.Tick += (_, _) => { var current = Native.GetForegroundWindow(); if (current != IntPtr.Zero && !Native.IsOwnWindow(current)) previousWindow = current; };
        refresh.Tick += async (_, _) => { if (!busy && host is not null && IsVisible) { await RefreshList(); if (currentId is not null) await ShowHistory(); } };
    }
    private async Task Start()
    {
        try
        {
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Buddy"); host = await BuddyHost.Start(data);
            tray.Icon = System.Drawing.SystemIcons.Application; tray.Text = "Buddy — local AI"; tray.Visible = true; tray.DoubleClick += (_, _) => Dispatcher.Invoke(Summon);
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Quick chat", null, (_, _) => Dispatcher.Invoke(() => OpenQuick(false)));
            menu.Items.Add("Voice", null, (_, _) => Dispatcher.Invoke(() => OpenQuick(true)));
            menu.Items.Add("Open Buddy Home", null, (_, _) => Dispatcher.Invoke(Summon));
            menu.Items.Add("Settings", null, (_, _) => Dispatcher.Invoke(OpenSettings));
            menu.Items.Add("Cursor & shortcuts", null, (_, _) => Dispatcher.Invoke(() => { Summon(); CursorSettings(); }));
            menu.Items.Add("Quit", null, (_, _) => Dispatcher.Invoke(() => _ = Quit())); tray.ContextMenuStrip = menu;
            host.Service.WebEnabled = desktop.AllowWebResearch; host.Service.AgentEnabled = desktop.AgentEnabled;
            assistant = new DesktopAssistant(() => host?.Service, () => desktop, () => previousWindow, mood => companion?.SetMood(mood));
            voiceOverlay = new VoiceOverlayWindow(() => host?.Service, EnsureConversation, () => desktop, mood => companion?.SetMood(mood), () => previousWindow,
                assistant.Perception, StartWorkflow, () => OpenQuick(false));
            quick = new QuickChatWindow(() => host?.Service, EnsureConversation, () => desktop,
                mood => companion?.SetMood(mood), Summon, OpenSettings, () => OpenQuick(true), StartWorkflow,
                async ct => { var snapshot = await assistant.Perception.Capture(previousWindow, ct); await host.Service.Audit("capture", snapshot.Context.App, "UIA text; no screenshot stored"); return snapshot.PromptText; });
            companion = new CursorCompanionWindow(() => quick?.IsVisible == true && quick.IsMouseOver);
            companion.SetEnabled(desktop.ShowCompanion);
            ConfigurePtt();
            foreground.Start(); refresh.Start(); await RefreshList();
            if (conversations.Items.Count > 0) await Select(((Conversation)conversations.Items[0]).Id); else await NewChat();
            var s = await host.Service.Store.Read(s => s); var modelStatus = await host.Service.Engine.Status(s.Model, s.VisionModel);
            var availableDefault = OllamaEngine.AvailableDefault(s.Model, modelStatus.Installed);
            if (availableDefault != s.Model) {
                await host.Service.Store.Update(settings => { settings.Model = availableDefault; return true; });
                modelStatus = await host.Service.Engine.Status(availableDefault, s.VisionModel);
            }
            status.Text = modelStatus.Message;
            if (modelStatus.Ready && desktop.StartInCompanionMode && !keepHomeOpen) Hide();
        }
        catch (Exception ex) { Diagnostics.Write("Service startup failed", ex); status.Text = "Could not start Buddy: " + ex.Message; MessageBox.Show(status.Text + "\n\nError log: " + Diagnostics.LogPath, "Buddy startup"); }
    }
    private IntPtr Hook(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == 0x0312)
        {
            int id = w.ToInt32();
            if (id == shortcut?.ActiveId) { OpenQuick(desktop.ShortcutStartsVoice); handled = true; }
            else if (id == 3) { OpenQuick(true); handled = true; }
            else if (id == 2) { Cancel(); handled = true; }
        }
        return IntPtr.Zero;
    }
    private void Summon() { keepHomeOpen = true; quick?.Dismiss(); voiceOverlay?.Dismiss(); Show(); WindowState = WindowState.Normal; Activate(); input.Focus(); if (host is not null) { _ = RefreshList(); _ = ShowHistory(); } }
    private async Task<string?> EnsureConversation() { if (currentId is null) await NewChat(); return currentId; }
    private void OpenQuick(bool voice)
    {
        var active = Native.GetForegroundWindow(); if (active != IntPtr.Zero && !Native.IsOwnWindow(active)) previousWindow = active;
        if (quick is null) { status.Text = "Buddy is still starting. Please wait."; Summon(); return; }
        StopMainDictation(); tts?.SpeakAsyncCancelAll();
        if (voice) { quick.Dismiss(); if (voiceOverlay?.IsListening == true) voiceOverlay.Dismiss(); else voiceOverlay?.Open(false); return; }
        voiceOverlay?.Dismiss();
        if (!voice && quick.IsVisible && quick.IsActive) { quick.Dismiss(); return; }
        quick.Open(currentId);
    }
    private void UpdateShortcutHint()
    {
        shortcutHint.Text = (ptt is not null ? desktop.Shortcut + " tap for chat / hold to talk" : shortcut?.Active?.Label ?? "Main shortcut unavailable — use tray") +
            (desktop.ShortcutStartsVoice ? " for voice" : " for quick chat") +
            (voiceShortcutReady ? " · Ctrl+Shift+Space for voice" : " · Voice shortcut unavailable — use tray") + " · Ctrl+Alt+Esc stops";
    }
    private void CursorSettings()
    {
        var p = new StackPanel(); p.Children.Add(Text("Buddy, beside your cursor", 25));
        p.Children.Add(Text("A small companion follows your pointer. Use a shortcut to open chat or speak.", 14, Muted));
        var enabled = new CheckBox { Content = "Show the cursor companion", IsChecked = desktop.ShowCompanion, Foreground = Ink, Margin = new(0, 12, 0, 12) };
        var start = new CheckBox { Content = "Hide Home for background launches when the AI model is ready", IsChecked = desktop.StartInCompanionMode, Foreground = Ink, Margin = new(0, 0, 0, 14) };
        p.Children.Add(enabled); p.Children.Add(start); p.Children.Add(Text("Main shortcut", 14));
        var keys = new ComboBox { ItemsSource = ShortcutChoice.Choices, SelectedItem = ShortcutChoice.Choices.FirstOrDefault(c => c.Label == desktop.Shortcut) ?? ShortcutChoice.Choices[0], Margin = new(0, 0, 0, 14) }; p.Children.Add(keys);
        p.Children.Add(Text("Windows + Space normally switches keyboard layouts. Buddy can use it only if Windows makes it available. A conflicting selection leaves your working shortcut active.", 12, Muted));
        p.Children.Add(Text("When I press the main shortcut", 14));
        var action = new ComboBox { ItemsSource = new[] { "Open chat bar", "Start voice" }, SelectedIndex = desktop.ShortcutStartsVoice ? 1 : 0, Margin = new(0, 0, 0, 14) }; p.Children.Add(action);
        var read = new CheckBox { Content = "Read voice answers aloud", IsChecked = desktop.ReadVoiceAnswers, Foreground = Ink, Margin = new(0, 0, 0, 14) }; p.Children.Add(read);
        var hold = new CheckBox { Content = "Enable hold-to-talk (uses only the selected shortcut)", IsChecked = desktop.HoldToTalk, Foreground = Ink, Margin = new(0,0,0,14) }; p.Children.Add(hold);
        var screen = new CheckBox { Content = "Include active-window context when talking", IsChecked = desktop.CaptureOnVoice, Foreground = Ink, Margin = new(0,0,0,14) }; p.Children.Add(screen);
        p.Children.Add(Text("Voice opens a separate speech bubble. Hold at least 250 ms to talk; release to send. Tap opens typed chat. Ctrl+Shift+Space starts one utterance. Esc or Ctrl+Alt+Esc stops. Screen context stays on this PC.", 12, Muted));
        var notice = Text("", 12, Accent); p.Children.Add(notice);
        p.Children.Add(Btn("Save preferences", () =>
        {
            if (keys.SelectedItem is not ShortcutChoice choice || shortcut is null) return;
            var old = shortcut.Active;
            if (!shortcut.TrySet(choice)) { notice.Text = "That shortcut is already in use or reserved by Windows. Your current shortcut is unchanged."; return; }
            try
            {
                var next = desktop with { ShowCompanion = enabled.IsChecked == true, StartInCompanionMode = start.IsChecked == true, Shortcut = choice.Label, ShortcutStartsVoice = action.SelectedIndex == 1, ReadVoiceAnswers = read.IsChecked == true, HoldToTalk = hold.IsChecked == true, CaptureOnVoice = screen.IsChecked == true };
                next.Save(); desktop = next; companion?.SetEnabled(desktop.ShowCompanion); ConfigurePtt(); UpdateShortcutHint(); notice.Text = "Saved. " + shortcutHint.Text;
            }
            catch (Exception ex) { if (old is not null) shortcut.TrySet(old); UpdateShortcutHint(); notice.Text = "Could not save: " + ex.Message; }
        }, true));
        Dialog("Buddy · Cursor & shortcuts", p, 620, 760);
    }
    private async Task RefreshList()
    {
        if (host is null) return; refreshing = true;
        try { conversations.ItemsSource = await host.Service.Store.Read(s => s.Conversations.OrderByDescending(c => c.UpdatedAt).Select(c => c with { Messages = [] }).ToList()); conversations.SelectedItem = conversations.Items.Cast<Conversation>().FirstOrDefault(c => c.Id == currentId); }
        finally { refreshing = false; }
    }
    private async Task NewChat() { if (host is null || busy) return; currentId = (await host.Service.CreateConversation(null)).Id; ClearContext(); await RefreshList(); await ShowHistory(); input.Focus(); }
    private async Task Select(string id) { if (busy) return; currentId = id; ClearContext(); await ShowHistory(); }
    private async Task ShowHistory()
    {
        if (host is null) return; var c = await host.Service.Store.Read(s => s.Conversations.FirstOrDefault(c => c.Id == currentId)); messages.Children.Clear();
        if (c is null) { currentId = null; messages.Children.Add(Text("Choose or create a conversation.", 20, Muted)); return; }
        foreach (var m in c.Messages) Bubble(m.Role, m.Text);
        if (c.Messages.Count == 0)
        {
            messages.Children.Add(Text("Less friction. More room to think.", 24)); messages.Children.Add(Text("Ask a question, untangle an idea, or bring the app you’re using into the conversation.", 15, Muted));
            foreach (var hint in new[] { "Help me plan a focused day", "Explain a concept with a simple example", "Turn my rough idea into a clear plan" }) messages.Children.Add(Btn(hint + "  ↗", () => { input.Text = hint; input.Focus(); }));
            messages.Children.Add(Text("Your model runs on this PC. Start with PC setup & models.", 13, Accent));
        }
        scroller.ScrollToEnd();
    }
    private TextBox Bubble(string role, string value)
    {
        var content = new StackPanel(); content.Children.Add(Text(role == "user" ? "YOU" : "BUDDY", 11, role == "user" ? Muted : Accent));
        var text = new TextBox { Text = value, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), Background = System.Windows.Media.Brushes.Transparent, Foreground = Ink, FontSize = 16, Padding = new(0) }; content.Children.Add(text);
        messages.Children.Add(new Border { Child = content, Padding = new(18), Margin = new(0, 0, 8, 12), Background = role == "user" ? Panel : Bg, CornerRadius = new(12) }); return text;
    }
    private void SetBusy(bool value) { busy = value; input.IsReadOnly = value; send.IsEnabled = !value; stop.IsEnabled = value; conversations.IsEnabled = !value; mode.IsEnabled = !value; }
    private async Task Send()
    {
        if (host is null || busy || string.IsNullOrWhiteSpace(input.Text)) return; if (currentId is null) await NewChat();
        var draft = input.Text.Trim(); var selectedMode = mode.SelectedItem?.ToString()?.ToLowerInvariant() ?? "type";
        var route = AssistantIntent.Mode(draft); if (route is "agent" or "guide") { StartWorkflow(route, draft); return; }
        var payload = new ChatRequest(currentId!, draft, Guid.NewGuid().ToString(), selectedMode, context, image is null ? null : Convert.ToBase64String(image), desktop.AllowWebResearch);
        request = new(); SetBusy(true); tts?.SpeakAsyncCancelAll(); input.Clear(); messages.Children.Clear();
        var old = await host.Service.Store.Read(s => s.Conversations.First(c => c.Id == currentId)); foreach (var m in old.Messages) Bubble(m.Role, m.Text);
        Bubble("user", draft); var answer = Bubble("assistant", "");
        try
        {
            await foreach (var item in host.Service.Chat(payload, request.Token)) { if (item.Type == "delta") { answer.Text += item.Text; scroller.ScrollToEnd(); } if (item.Type == "status") status.Text = item.Text; }
            status.Text = "Answered on your PC · " + DateTime.Now.ToShortTimeString(); ClearContext();
            if (speak.IsChecked == true || selectedMode == "voice") ReadAloud(answer.Text);
        }
        catch (OperationCanceledException) { status.Text = "Stopped. Your draft has been restored."; input.Text = draft; answer.Text += "\n[Stopped — partial answer not saved]"; }
        catch (Exception ex) { status.Text = ex.Message; input.Text = draft; answer.Text += "\n[Answer failed — not saved]"; }
        finally { request.Dispose(); request = null; SetBusy(false); await RefreshList(); }
    }
    private void Cancel() { request?.Cancel(); host?.Service.StopAll(); StopMainDictation(); tts?.SpeakAsyncCancelAll(); quick?.Cancel(); voiceOverlay?.Cancel(); assistant?.Cancel(); }
    private void StopMainDictation()
    {
        var engine = recognizer; recognizer = null;
        if (engine is not null) { try { engine.RecognizeAsyncCancel(); } catch (InvalidOperationException) { } engine.Dispose(); }
        stop.IsEnabled = busy;
    }
    private void ReadAloud(string text)
    {
        if (voiceOverlay?.IsListening == true) return;
        try { tts ??= new SpeechSynthesizer(); tts.SpeakAsync(text); }
        catch (Exception ex)
        {
            Diagnostics.Write("Optional read-aloud unavailable", ex);
            status.Text = "Answer saved. Read-aloud is unavailable: " + ex.Message;
        }
    }
    private void Dictate()
    {
        if (busy) return; quick?.Cancel(); voiceOverlay?.Cancel(); tts?.SpeakAsyncCancelAll();
        try
        {
            recognizer?.Dispose(); var installed = SpeechRecognitionEngine.InstalledRecognizers();
            if (installed.Count == 0) throw new InvalidOperationException("No Windows speech language is installed. Install one in Windows Settings, or press Win+H in the message box.");
            recognizer = new SpeechRecognitionEngine(installed.First()); var engine = recognizer; recognizer.LoadGrammar(new DictationGrammar()); recognizer.SetInputToDefaultAudioDevice();
            recognizer.SpeechRecognized += (_, e) => Dispatcher.BeginInvoke(new Action(() => { if (!ReferenceEquals(recognizer, engine)) return; input.Text = e.Result.Text; status.Text = "Dictation ready"; if (mode.SelectedItem?.ToString() == "Voice") _ = Send(); }));
            recognizer.RecognizeCompleted += (_, e) => Dispatcher.BeginInvoke(new Action(() => { if (!ReferenceEquals(recognizer, engine)) return; if (!busy) { if (e.Error is not null) status.Text = "Microphone: " + e.Error.Message; else if (e.Result is null) status.Text = "No speech recognized. Try again or type."; } stop.IsEnabled = busy; }));
            recognizer.RecognizeAsync(RecognizeMode.Single); status.Text = "Listening… Speak now. Stop ends listening."; stop.IsEnabled = true;
        }
        catch (Exception ex) { status.Text = ex.Message; input.Focus(); }
    }
    private Window Dialog(string title, UIElement content, int width = 580, int height = 650)
    {
        var w = new Window { Title = title, Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(24) }, Width = Math.Min(width, SystemParameters.WorkArea.Width), Height = Math.Min(height, SystemParameters.WorkArea.Height), Background = Bg, Foreground = Ink, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        w.Show(); return w;
    }
    private async Task Refine()
    {
        if (host is null || busy || string.IsNullOrWhiteSpace(input.Text)) return; var original = input.Text; request = new(); SetBusy(true); status.Text = "Refining with local AI…";
        try
        {
            var result = await host.Service.RefineDetailed(new(original), request.Token); var p = new StackPanel(); p.Children.Add(Text("Review your refined prompt · on this PC", 23));
            var notice = Text(result.Message, 13, Muted); p.Children.Add(notice);
            var editor = new TextBox { Text = result.RefinedPrompt, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 260 }; StyleBox(editor); p.Children.Add(editor); Window? w = null;
            DateTimeOffset? applied = null;
            var apply = Btn("Apply to draft", () => {
                if (input.Text != original || applied is not null) { notice.Text = "The draft changed. Copy the rewrite instead."; return; }
                input.Text = result.RefinedPrompt; applied = DateTimeOffset.UtcNow; notice.Text = "Applied without sending. Undo is available for 30 seconds.";
            }, true); apply.IsEnabled = result.Accepted; p.Children.Add(apply);
            p.Children.Add(Btn("Undo", () => {
                if (applied is null || DateTimeOffset.UtcNow - applied > TimeSpan.FromSeconds(30) || input.Text != result.RefinedPrompt) { notice.Text = "Undo unavailable: the field changed or 30 seconds elapsed."; return; }
                input.Text = original; applied = null; notice.Text = "Exact original restored.";
            }));
            p.Children.Add(Btn("Copy", () => System.Windows.Clipboard.SetText(editor.Text)));
            p.Children.Add(Btn("Close", () => w?.Close())); w = Dialog("Buddy · Refine", p);
        }
        catch (Exception ex) { status.Text = ex is OperationCanceledException ? "Refinement stopped." : ex.Message; }
        finally { request?.Dispose(); request = null; SetBusy(false); }
    }
    private void ClearContext() { context = null; if (image is not null) Array.Clear(image); image = null; contextLabel.Text = ""; }
    private async Task ReadContext()
    {
        if (busy) return;
        try
        {
            Native.CheckWindow(previousWindow); status.Text = "Reading visible screen text…"; var target = previousWindow;
            var task = Task.Run(() => Native.ReadWindow(target)); var text = await task.WaitAsync(TimeSpan.FromSeconds(3));
            var p = new StackPanel(); p.Children.Add(Text("Review screen text before sharing", 23)); p.Children.Add(Text("Password fields and common sensitive tokens are omitted. Review the remaining text.", 13, Muted));
            var box = new TextBox { Text = text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; StyleBox(box); p.Children.Add(box); Window? w = null;
            p.Children.Add(Btn("Use this context", () => { context = box.Text; contextLabel.Text = "Screen text attached · sent with your next message"; w?.Close(); }, true)); w = Dialog("Buddy · Screen text", p); status.Text = "Review the context, then ask your question.";
        }
        catch (Exception ex) { status.Text = ex is TimeoutException ? "This app did not expose its screen text in time." : ex.Message; }
    }
    private async Task Capture()
    {
        if (busy || host is null) return;
        try
        {
            var s = await host.Service.Store.Read(s => s); var available = await host.Service.Engine.Status(s.Model, s.VisionModel);
            if (!available.Installed.Contains(s.VisionModel)) { status.Text = "Download the optional vision model in PC setup first."; return; }
            Native.CheckWindow(previousWindow); Hide(); byte[] bytes;
            using var captureTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            try {
                if (!InputNative.SetForegroundWindow(previousWindow)) throw new InvalidOperationException("Activate the window you want to capture first.");
                await Task.Delay(150, captureTimeout.Token);
                var perception = assistant?.Perception ?? new ScreenPerception(() => desktop);
                var snapshot = await perception.Capture(previousWindow, captureTimeout.Token);
                bytes = await perception.Image(snapshot, captureTimeout.Token) ?? throw new InvalidOperationException("Buddy could not establish a private capture scope for this window. Use reviewed screen text instead.");
            } finally { Summon(); }
            var p = new StackPanel(); p.Children.Add(Text("Check this image before sharing", 23)); p.Children.Add(Text("Detected private fields are masked. Review the image and cancel if anything private remains. This capture stays on this PC and is not saved in chat history.", 13, Muted));
            p.Children.Add(new System.Windows.Controls.Image { Source = Bitmap(bytes), MaxHeight = 390 }); Window? w = null; bool retained = false;
            p.Children.Add(Btn("Attach image", () => { ClearContext(); image = bytes; retained = true; contextLabel.Text = "Window image attached · sent with your next message"; w?.Close(); }, true)); p.Children.Add(Btn("Discard", () => w?.Close()));
            w = Dialog("Buddy · Capture preview", p, 720); w.Closed += (_, _) => { if (!retained) Array.Clear(bytes); };
        }
        catch (Exception ex) { status.Text = ex.Message; }
    }
    private static BitmapImage Bitmap(byte[] bytes) { using var stream = new MemoryStream(bytes); var b = new BitmapImage(); b.BeginInit(); b.CacheOption = BitmapCacheOption.OnLoad; b.StreamSource = stream; b.EndInit(); b.Freeze(); return b; }
    private async Task Setup()
    {
        if (host is null) return; var p = new StackPanel(); p.Children.Add(Text("Make Buddy yours", 27)); p.Children.Add(Text("1. Install or start Ollama\n2. Download the chat model\n3. Ask Buddy a question\n4. Pair your phone on the same Wi-Fi", 15, Muted));
        var info = Text("Checking…", 14, Accent); p.Children.Add(info);
        p.Children.Add(Btn("Install Ollama (official installer)", () => Process.Start(new ProcessStartInfo("https://ollama.com/download/windows") { UseShellExecute = true })));
        p.Children.Add(Btn("Start installed Ollama", () => {
            var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe");
            try { Process.Start(new ProcessStartInfo(File.Exists(exe) ? exe : "ollama", "serve") { UseShellExecute = false, CreateNoWindow = true }); info.Text = "Starting Ollama. Click Check connection in a moment."; } catch (Exception) { info.Text = "Install Ollama first, then reopen this panel."; }
        }));
        var s = await host.Service.Store.Read(s => s); var model = new ComboBox { ItemsSource = new[] { "qwen3:4b-instruct-2507-q4_K_M", "qwen3:1.7b", "qwen3:0.6b", "qwen3:8b" }, SelectedItem = s.Model, Margin = new(0, 8, 0, 12) }; p.Children.Add(Text("Chat model", 13, Muted)); p.Children.Add(model);
        p.Children.Add(Text("Start with 4B. Choose 1.7B or 0.6B on a slower PC. Model downloads need internet and several GB of disk space; subsequent chat runs locally.", 13, Muted));
        CancellationTokenSource? download = null;
        async Task Pull(string selected, bool select)
        {
            if (download is not null) return; download = new();
            try { await foreach (var progress in host.Service.Engine.Pull(selected, download.Token)) info.Text = progress; if (select) await host.Service.Store.Update(st => { st.Model = selected; return true; }); info.Text = "Model ready: " + selected; status.Text = info.Text; }
            catch (Exception ex) { info.Text = ex is OperationCanceledException ? "Download paused. Start again to resume." : "Download failed: " + ex.Message; }
            finally { download.Dispose(); download = null; }
        }
        p.Children.Add(Btn("Download & use chat model", () => _ = Pull(model.SelectedItem?.ToString() ?? "qwen3:4b-instruct-2507-q4_K_M", true), true));
        p.Children.Add(Btn("Download vision model (optional)", () => _ = Pull("gemma3:4b", false)));
        p.Children.Add(Btn("Download local Refine intent-check model", () => _ = Pull(s.EmbeddingModel, false)));
        p.Children.Add(Btn("Pause download", () => download?.Cancel()));
        async Task Check() { var st = await host.Service.Store.Read(st => st); var result = await host.Service.Engine.Status(st.Model, st.VisionModel); info.Text = result.Message + "\nSelected: " + st.Model + "\nInstalled: " + string.Join(", ", result.Installed); }
        p.Children.Add(Btn("Check connection", () => _ = Check())); p.Children.Add(Text("Closing Buddy to the tray keeps phone access available. Quit stops it. No cloud API key is needed.", 13, Muted));
        var w = Dialog("Buddy · PC setup", p); w.Closed += (_, _) => download?.Cancel(); await Check();
    }
    private void Pair()
    {
        if (host is null) return; var p = new StackPanel(); p.Children.Add(Text("Take Buddy with you", 26)); p.Children.Add(Text("Connect your phone and PC to the same Wi-Fi. In the Android app, tap Scan PC code. Each code works once and expires in 5 minutes.", 14, Muted));
        var ips = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up).SelectMany(n => n.GetIPProperties().UnicastAddresses).Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address)).Select(a => a.Address.ToString()).Distinct().ToList();
        if (ips.Count == 0) { p.Children.Add(Text("Connect the PC to Wi-Fi first.")); Dialog("Buddy · Pair phone", p); return; }
        var addresses = new ComboBox { ItemsSource = ips, SelectedIndex = 0 }; p.Children.Add(addresses);
        var qr = new System.Windows.Controls.Image { Height = 260, Margin = new(0, 12, 0, 12) }; var codeText = Text("", 22, Accent); var manual = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap }; StyleBox(manual); p.Children.Add(qr); p.Children.Add(codeText); p.Children.Add(manual);
        void Renew()
        {
            var pairing = host.Service.Pairing.Open(); var url = $"buddy://pair?host={addresses.SelectedItem}&port={host.Port}&pin={host.Fingerprint}&code={pairing.Code}";
            using var data = QRCodeGenerator.GenerateQrCode(url, QRCodeGenerator.ECCLevel.M); using var png = new PngByteQRCode(data); qr.Source = Bitmap(png.GetGraphic(5)); codeText.Text = "Pairing code  " + pairing.Code; manual.Text = url;
        }
        addresses.SelectionChanged += (_, _) => Renew(); Renew(); p.Children.Add(Btn("Refresh pairing code", Renew)); p.Children.Add(Text("If connection times out, allow Buddy through Windows Firewall on Private networks. The installation folder contains Enable-Phone-Access.ps1. Do not expose this port on your router.", 13, Muted));
        p.Children.Add(Btn("Manage paired phones", () => _ = Devices())); Dialog("Buddy · Pair phone", p, 600, 760);
    }
    private async Task Devices()
    {
        if (host is null) return; var p = new StackPanel(); p.Children.Add(Text("Paired phones", 24));
        foreach (var device in await host.Service.Store.Read(s => s.Devices)) { var row = new StackPanel(); row.Children.Add(Text(device.Name)); row.Children.Add(Btn("Revoke access", async () => { await host.Service.Store.Update(s => s.Devices.RemoveAll(d => d.Id == device.Id)); row.IsEnabled = false; })); p.Children.Add(row); } Dialog("Buddy · Devices", p);
    }
    private async Task Notes(bool memory)
    {
        if (host is null) return; var p = new StackPanel(); p.Children.Add(Text(memory ? "What Buddy remembers" : "Saved prompts", 25)); p.Children.Add(Text(memory ? "Only facts you explicitly save are remembered across chats." : "Keep useful prompts and reuse them in your draft.", 14, Muted));
        var items = await host.Service.Store.Read(s => memory ? s.Memories : s.Prompts);
        foreach (var n in items) { var card = new StackPanel(); card.Children.Add(Text(n.Title, 17, Accent)); card.Children.Add(Text(n.Text)); if (!memory) card.Children.Add(Btn("Use prompt", () => input.Text = n.Text)); card.Children.Add(Btn("Delete", async () => { await host.Service.Store.Update(s => (memory ? s.Memories : s.Prompts).RemoveAll(x => x.Id == n.Id)); card.IsEnabled = false; })); p.Children.Add(card); }
        var title = new TextBox { Text = "New item", Margin = new(0, 14, 0, 8) }; StyleBox(title); var body = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 100 }; StyleBox(body); p.Children.Add(title); p.Children.Add(body); var notice = Text("", 12, Accent); p.Children.Add(notice);
        p.Children.Add(Btn("Save", async () => {
            try { var note = new Note(Guid.NewGuid().ToString(), Security.Text(title.Text, 100, "Title"), Security.Text(body.Text, 2000, "Text")); await host.Service.Store.Update(s => { var list = memory ? s.Memories : s.Prompts; if (list.Count >= 50) throw new BuddyException("LIMIT", "Remove an old item first."); list.Add(note); return true; }); notice.Text = "Saved. Reopen this panel to view it."; body.Clear(); } catch (Exception ex) { notice.Text = ex.Message; }
        }, true)); Dialog(memory ? "Buddy · Memory" : "Buddy · Prompts", p);
    }
    private async Task Delete()
    {
        if (host is null || currentId is null || busy) return;
        if (MessageBox.Show("Delete this conversation from Buddy on all paired devices?", "Delete conversation", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        await host.Service.DeleteConversation(currentId); currentId = null; await RefreshList(); await NewChat();
    }
    private async Task Export()
    {
        if (host is null || currentId is null) return; var c = await host.Service.Store.Read(s => s.Conversations.FirstOrDefault(c => c.Id == currentId)); if (c is null) return;
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "Buddy-conversation.md", Filter = "Markdown|*.md" }; if (dialog.ShowDialog(this) != true) return;
        await File.WriteAllTextAsync(dialog.FileName, "# " + c.Title + "\n\n" + string.Join("\n\n", c.Messages.Select(m => "## " + m.Role + "\n\n" + m.Text))); status.Text = "Conversation exported.";
    }
    internal async Task Quit()
    {
        if (shuttingDown) return; shuttingDown = true; Cancel(); ptt?.Dispose(); voiceOverlay?.Dispose(); assistant?.Dispose(); quick?.Dispose(); companion?.Dispose(); refresh.Stop(); foreground.Stop(); tray.Dispose(); tts?.Dispose(); shortcut?.Dispose(); Native.UnregisterHotKey(hwnd, 2); Native.UnregisterHotKey(hwnd, 3); if (host is not null) await host.DisposeAsync(); System.Windows.Application.Current.Shutdown();
    }
}
