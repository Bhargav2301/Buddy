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
    private static readonly SolidColorBrush Bg = BuddyTheme.Canvas, Panel = BuddyTheme.Surface, Ink = BuddyTheme.Ink, Muted = BuddyTheme.Muted, Accent = BuddyTheme.Deep;
    private BuddyHost? host;
    private readonly TextBlock status = Text("Starting Buddyâ€¦", 13, Muted), contextLabel = Text("", 12, Accent);
    private readonly TextBlock companionBrand = Text("Buddy", 18, Accent), talkHeading = Text("Talk with Buddy", 28);
    private readonly TextBox input = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 76, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly StackPanel messages = new(), rail = new();
    private readonly ScrollViewer scroller;
    private readonly ListBox conversations = new() { BorderThickness = new(0), Background = System.Windows.Media.Brushes.Transparent, Foreground = Ink, DisplayMemberPath = "Title", Margin = new(0, 16, 0, 8) };
    private readonly CheckBox speak = new() { Content = "Read aloud", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button send, stop;
    private readonly System.Windows.Forms.NotifyIcon tray = new();
    private System.Drawing.Icon? trayArtwork;
    private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromSeconds(6) }, foreground = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private LocalVoiceOutput? tts;
    private bool homeSpeaking;
    private int homeSpeechGeneration;
    private LocalRecognizer? recognizer;
    private CancellationTokenSource? mainMicrophone;
    private GuardedEdit? mainDictationEdit;
    private readonly Button undoDictation = new() { Content = "Undo dictation", Visibility = Visibility.Collapsed };
    private readonly Button copyDictation = new() { Content = "Copy dictation", Visibility = Visibility.Collapsed };
    private string lastDictation = "";
    private CancellationTokenSource? request;
    private string? currentId, context;
    private byte[]? image;
    private IntPtr hwnd, previousWindow;
    private bool busy, shuttingDown, refreshing;
    private DesktopPreferences desktop = DesktopPreferences.Load();
    private CursorCompanionWindow? companion;
    private bool companionPresenceAvailable = true;
    internal void SetCompanionPresence(bool available)
    {
        companionPresenceAvailable = available;
        companion?.SetEnabled(available && desktop.ShowCompanion);
        Diagnostics.Write("Companion ownership: " + (available ? "this preview" : "another Buddy instance; preview companion hidden"));
    }
    private readonly CompanionState companionState = new();
    private QuickChatWindow? quick;
    private VoiceOverlayWindow? voiceOverlay;
    private DesktopAssistant? assistant;
    private OnboardingWindow? onboarding;
    private PushToTalkHook? ptt;
    private ShortcutRegistration? shortcut;
    private ShortcutRegistration? voiceShortcut;
    private bool stopShortcutReady;
    private readonly TextBlock shortcutHint = Text("Starting shortcutsâ€¦", 11, Muted);
    private static SolidColorBrush Brush(string hex) => new((System.Windows.Media.Color)ColorConverter.ConvertFromString(hex));
    private static TextBlock Text(string value, double size = 15, System.Windows.Media.Brush? color = null) => new() { Text = value, FontSize = size, Foreground = color ?? Ink, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) };
    private static Button Btn(string label, Action action, bool primary = false)
    {
        return BuddyTheme.Button(label, action, primary);
    }
    private static StackPanel Row(params UIElement[] children) { var row = new StackPanel { Orientation = Orientation.Horizontal }; foreach (var c in children) row.Children.Add(c); return row; }
    private static void StyleBox(TextBox box) { box.Background = Panel; box.Foreground = Ink; box.CaretBrush = Accent; box.BorderBrush = BuddyTheme.Line; box.Padding = new(12); box.FontSize = 14; }

    public MainWindow() : this(true) { }
    internal MainWindow(bool startService)
    {
        BuddyTheme.Apply(desktop.Appearance, desktop.ReduceMotion);
        AppBranding.Apply(this);
        Title = "Buddy â€” your local AI companion"; Width = 1160; Height = 820; MinWidth = 760; MinHeight = 560; Background = Bg; Foreground = Ink; FontFamily = BuddyTheme.Font; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var layout = new Grid { Background = Bg }; layout.ColumnDefinitions.Add(new() { Width = new(240) }); layout.ColumnDefinitions.Add(new());
        var side = new DockPanel { Background = Panel, Margin = new(0) }; Grid.SetColumn(side, 0); layout.Children.Add(side);
        var brand = new StackPanel { Margin = new(24, 24, 16, 24), Orientation = Orientation.Horizontal }; companionBrand.FontWeight = FontWeights.Bold; var logo = AppBranding.Image(40); logo.Margin = new(0, 0, 10, 0); brand.Children.Add(logo); brand.Children.Add(companionBrand); DockPanel.SetDock(brand, Dock.Top); side.Children.Add(brand);
        var footer = new StackPanel { Margin = new(24, 12, 16, 16) };
        footer.Children.Add(Btn("Talk", () => OpenQuick(false))); footer.Children.Add(Btn("Voice", () => OpenQuick(true))); footer.Children.Add(Btn("Hide Home - keep Buddy running", HideHome)); footer.Children.Add(Btn("Exit Buddy (stops companion)", () => _ = Quit()));
        DockPanel.SetDock(footer, Dock.Bottom); side.Children.Add(footer);
        var nav = new StackPanel { Margin = new(12, 0, 12, 0) };
        foreach (var section in HomeSections) {
            var button = Btn(section, () => NavigateHome(section)); button.HorizontalContentAlignment = HorizontalAlignment.Left;
            button.Background = Brushes.Transparent; button.BorderThickness = new(0); button.Margin = new(0, 0, 0, 4);
            navigationButtons[section] = button; nav.Children.Add(button);
        }
        side.Children.Add(new ScrollViewer { Content = nav, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        homeBody.Margin = new(40, 40, 40, 24); Grid.SetColumn(homeBody, 1); layout.Children.Add(homeBody);
        var main = new Grid(); chatView = main;
        main.RowDefinitions.Add(new() { Height = GridLength.Auto }); main.RowDefinitions.Add(new()); main.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new StackPanel(); header.Children.Add(Btn("Back to conversations", () => NavigateHome("Conversations"))); header.Children.Add(talkHeading); header.Children.Add(status); main.Children.Add(header);
        scroller = new ScrollViewer { Content = messages, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(0, 18, 0, 20) }; Grid.SetRow(scroller, 1); main.Children.Add(scroller);
        var composer = new StackPanel(); Grid.SetRow(composer, 2); main.Children.Add(composer); composer.Children.Add(contextLabel);
        StyleBox(input); composer.Children.Add(input);
        send = Btn("Send  â†—", () => _ = Send(), true); stop = Btn("Stop", Cancel); stop.IsEnabled = false;
        composer.Children.Add(Row(Btn("Dictate", Dictate), Btn("Refine Buddy draft", () => _ = Refine()), Btn("Refine source field", () => _ = RefineFocusedField(useSummonedField:true)), speak, stop, send, undoDictation, copyDictation));
        undoDictation.Click += (_, _) => { try { mainDictationEdit?.Undo(DateTimeOffset.UtcNow, default); status.Text = "Exact draft restored."; } catch (Exception e) { status.Text = e.Message; } undoDictation.Visibility = Visibility.Collapsed; };
        copyDictation.Click += (_, _) => { if (lastDictation.Length > 0) System.Windows.Clipboard.SetText(lastDictation); };
        composer.Children.Add(Row(Btn("Read screen text", () => _ = ReadContext()), Btn("Capture window", () => _ = Capture()), Btn("Clear context", ClearContext)));
        composer.Children.Add(shortcutHint);
        Content = layout; NavigateHome("Conversations");
        SizeChanged += (_, _) => homeBody.Margin = new(ActualWidth < 1000 ? 24 : 40, 32, ActualWidth < 1000 ? 24 : 40, 24);
        input.PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter && System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control)) { e.Handled = true; _ = Send(); } };
        conversations.SelectionChanged += (_, _) => { if (!refreshing && !busy && conversations.SelectedItem is Conversation c) _ = Select(c.Id); };
        SourceInitialized += (_, _) => {
            CaptureProtection.Enabled = desktop.ProtectScreenshots;
            LocalSpeechInput.Preferences = () => desktop;
            hwnd = new WindowInteropHelper(this).Handle; HwndSource.FromHwnd(hwnd).AddHook(Hook); CaptureProtection.Apply(hwnd);
            if (!startService) return; // Isolated window tests must not register user hotkeys or start the shared store.
            shortcut = new ShortcutRegistration((id, modifiers) => Native.RegisterHotKey(hwnd, id, modifiers, 0x20), id => Native.UnregisterHotKey(hwnd, id));
            var preferred = ShortcutChoice.Find(desktop.Shortcut);
            shortcut.TrySet(preferred); // Never silently claim a different chord on startup.
            voiceShortcut = new ShortcutRegistration((id, modifiers) => Native.RegisterHotKey(hwnd, id, modifiers, 0x20), id => Native.UnregisterHotKey(hwnd, id), 3, 7);
            voiceShortcut.TrySet(ShortcutChoice.Find(desktop.VoiceShortcut));
            refineShortcutReady = Native.RegisterHotKey(hwnd, 5, 0x4003, 0x52);
            dictationShortcutReady = Native.RegisterHotKey(hwnd, 6, 0x4003, 0x44);
            stopShortcutReady = Native.RegisterHotKey(hwnd, 2, 0x4003, 0x1B);
            regionShortcut = new ShortcutRegistration((id,mods) => Native.RegisterHotKey(hwnd,id,mods,0x52), id => Native.UnregisterHotKey(hwnd,id),8,9);
            if(desktop.RegionSelectionEnabled && !regionShortcut.TrySet(ShortcutChoice.FindRegion(desktop.RegionShortcut))) status.Text="The area-selection shortcut is already in use.";
            UpdateShortcutHint();
        };
        if (startService) Loaded += async (_, _) => await Start();
        Closing += (_, e) => { if (!shuttingDown) { e.Cancel = true; HideHome(); } };
        Deactivated += (_, _) => { if (mainMicrophone is not null) { StopMainDictation(); status.Text = "Microphone off Â· focus changed."; } };
        foreground.Tick += (_, _) => { var current = Native.GetForegroundWindow(); if (current != IntPtr.Zero && !Native.IsOwnWindow(current)) previousWindow = current; };
        refresh.Tick += async (_, _) => { if (!busy && host is not null && IsVisible) { await RefreshList(); if (currentId is not null) await ShowHistory(); } };
    }
    private async Task Start()
    {
        try
        {
            var data = PreviewEnvironment.DataDirectory; host = await BuddyHost.Start(data, PreviewEnvironment.Enabled ? 47839 : BuddyHost.DefaultPort, loopbackOnly: PreviewEnvironment.Enabled);
            trayArtwork = AppBranding.TrayIcon(); tray.Icon = trayArtwork; tray.Text = "Buddy - local AI"; tray.Visible = true; tray.DoubleClick += (_, _) => Dispatcher.Invoke(Summon);
            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Quick chat", null, (_, _) => Dispatcher.Invoke(() => OpenQuick(false)));
            menu.Items.Add("Voice", null, (_, _) => Dispatcher.Invoke(() => OpenQuick(true)));
            menu.Items.Add("Open Buddy Home", null, (_, _) => Dispatcher.Invoke(Summon));
            menu.Items.Add("Settings", null, (_, _) => Dispatcher.Invoke(OpenSettings));
            menu.Items.Add("Refine focused prompt", null, (_, _) => Dispatcher.Invoke(() => _ = RefineFocusedField()));
            menu.Items.Add("Dictate into focused field", null, (_, _) => Dispatcher.Invoke(() => _ = RefineFocusedField(dictation: true)));
            var companionMenu = new System.Windows.Forms.ToolStripMenuItem("Companion");
            companionMenu.DropDownItems.Add("Settings", null, (_, _) => Dispatcher.Invoke(OpenSettings));
            companionMenu.DropDownItems.Add("Dock", null, (_, _) => Dispatcher.Invoke(() => companion?.SetDocked(true)));
            companionMenu.DropDownItems.Add("Follow pointer", null, (_, _) => Dispatcher.Invoke(() => companion?.SetDocked(false)));
            companionMenu.DropDownItems.Add("Snooze 15 minutes", null, (_, _) => Dispatcher.Invoke(() => companion?.Snooze()));
            menu.Items.Add(companionMenu);
            menu.Items.Add("Cursor & shortcuts", null, (_, _) => Dispatcher.Invoke(() => { Summon(); CursorSettings(); }));
            menu.Items.Add("Exit Buddy (stops companion)", null, (_, _) => Dispatcher.Invoke(() => _ = Quit())); tray.ContextMenuStrip = menu;
            host.Service.WebEnabled = desktop.AllowWebResearch; host.Service.AgentEnabled = desktop.AgentEnabled;
            companionState.Changed += mood => companion?.SetMood(mood);
            tasks = new TaskCenter(host.Service, () => assistant?.Cancel(), text => StartWorkflow("agent",text)); await tasks.Initialize();
            assistant = new DesktopAssistant(() => host?.Service, () => desktop, () => previousWindow, mood => companionState.Set("assistant", mood), target => companion?.PointTo(target), () => PrepareDesktopActivity("assistant"), tasks.Ledger);
            assistant.DrawRegionRequested=()=>BeginRegionSelection(true);
            fieldEditor = new FocusedFieldEditor(assistant.Perception);
            fieldBadge = new FocusedFieldBadge(fieldEditor, identity => _ = RefineFocusedField(identity)); fieldBadge.SetEnabled(desktop.ShowFieldBadge);
            promptWatcher=new(fieldEditor,()=>host?.Service,()=>OpenQuick(true),()=>desktop.VoiceShortcut);promptWatcher.SetEnabled(desktop.LocalPromptSuggestions);
            voiceOverlay = new VoiceOverlayWindow(() => host?.Service, EnsureConversation, () => desktop, mood => companionState.Set("voice", mood), () => previousWindow,
                assistant.Perception, StartWorkflow, () => OpenQuick(false), text => _ = RefineFocusedField(useSummonedField:true), Summon, () => PrepareDesktopActivity("voice"), target => companion?.PointTo(target));
            voiceOverlay.RefinementReply=text=>promptWatcher?.Reply(text)==true;
            quick = new QuickChatWindow(() => host?.Service, EnsureConversation, () => desktop,
                mood => companionState.Set("talk", mood), Summon, OpenSettings, () => OpenQuick(true), StartWorkflow,
                async ct => { var snapshot = await assistant.Perception.Capture(previousWindow, ct); await host.Service.Audit("capture", snapshot.Context.App, "UIA text; no screenshot stored"); return snapshot; }, field => { _ = RefineFocusedField(useSummonedField:true); }, () => PrepareDesktopActivity("talk"));
            companion = new CursorCompanionWindow(() => quick?.IsVisible == true && quick.IsMouseOver, action => {
                switch (action) {
                    case "Talk": OpenQuick(false); break;
                    case "Voice": OpenQuick(true); break;
                    case "Select area": BeginRegionSelection(); break;
                    case "Guide": StartWorkflow("guide", ""); break;
                    case "Agent": StartWorkflow("agent", ""); break;
                    case "Refine": _ = RefineFocusedField(); break;
                    case "Dictate": _ = RefineFocusedField(dictation: true); break;
                    case "Home": Summon(); break;
                    case "Settings": OpenSettings(); break;
                    case "Stop": Cancel(); break;
                }
            });
            companion.SetEnabled(companionPresenceAvailable && desktop.ShowCompanion);
            ApplyDisplayName();
            ConfigurePtt(); ConfigureRegionHook(); companion.SetTriangleEnabled(desktop.TrianglePointerEnabled);
            foreground.Start(); refresh.Start(); await RefreshList();
            if (conversations.Items.Count > 0) { currentId = ((Conversation)conversations.Items[0]).Id; await ShowHistory(); }
            NavigateHome(homeSection);
            var s = await host.Service.Store.Read(s => s); var modelStatus = await host.Service.Engine.Status(s.Model, s.VisionModel);
            var availableDefault = OllamaEngine.AvailableDefault(s.Model, modelStatus.Installed);
            if (availableDefault != s.Model) {
                await host.Service.Store.Update(settings => { settings.Model = availableDefault; return true; });
                modelStatus = await host.Service.Engine.Status(availableDefault, s.VisionModel);
            }
            status.Text = modelStatus.Message;
            if (!desktop.OnboardingCompleted) ShowOnboarding();
            if (modelStatus.Ready && desktop.StartInCompanionMode && !keepHomeOpen) Hide();
        }
        catch (Exception ex) { Diagnostics.Write("Service startup failed", ex); status.Text = "Could not start Buddy: " + ex.Message; MessageBox.Show(status.Text + "\n\nError log: " + Diagnostics.LogPath, "Buddy startup"); }
    }
    private IntPtr Hook(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == 0x0312)
        {
            int id = w.ToInt32();
            if (id == shortcut?.ActiveId) { OpenQuick(false); handled = true; }
            else if (id == voiceShortcut?.ActiveId) { OpenQuick(true); handled = true; }
            else if (id == regionShortcut?.ActiveId) { BeginRegionSelection(); handled = true; }
            else if (id == 5) { _ = RefineFocusedField(); handled = true; }
            else if (id == 6) { _ = RefineFocusedField(dictation: true); handled = true; }
            else if (id == 2) { Cancel(); handled = true; }
        }
        return IntPtr.Zero;
    }
    internal void HideHome()
    {
        keepHomeOpen=false;StopMainDictation();tts?.Cancel();Hide();
        companion?.SetEnabled(companionPresenceAvailable&&desktop.ShowCompanion);
        if(tray.Visible)tray.ShowBalloonTip(1800,"Buddy is still running","Use your voice/chat shortcut or the tray icon. Exit Buddy stops the companion and service.",System.Windows.Forms.ToolTipIcon.Info);
    }
    private void Summon() { keepHomeOpen = true; quick?.Dismiss(); voiceOverlay?.Dismiss(); Show(); WindowState = WindowState.Normal; Activate(); if (homeBody.Content == chatView) input.Focus(); if (host is not null) { _ = RefreshList(); _ = ShowHistory(); } }
    private async Task<string?> EnsureConversation() { if (currentId is null) await NewChat(); return currentId; }
    private async void OpenQuick(bool voice, bool held=false)
    {
        if(!await RememberSourceField()||shuttingDown||(held&&!voiceHeld))return;
        if (quick is null) { status.Text = "Buddy is still starting. Please wait."; Summon(); return; }
        PrepareDesktopActivity(voice ? "voice" : "talk");
        if (voice) { quick.Dismiss(); if (voiceOverlay?.IsListening == true) voiceOverlay.Dismiss(); else voiceOverlay?.Open(held); return; }
        voiceOverlay?.Dismiss();
        if (!voice && quick.IsVisible && quick.IsActive) { quick.Dismiss(); return; }
        quick.Open(currentId);
    }
    private void UpdateShortcutHint()
    {
        shortcutHint.Text = (shortcut?.Active?.Label ?? "Chat shortcut unavailable â€” use tray") + " for chat Â· " +
            (voiceShortcut?.Active?.Label ?? "Voice shortcut unavailable â€” use tray") + (desktop.HoldToTalk ? " tap / hold for voice" : " for voice") +
            (stopShortcutReady ? " Â· Ctrl+Alt+Esc stops" : " Â· Global Stop shortcut is in use: use the Stop buttons") + " Â· Windows+Space belongs to Windows";
    }
    private void CursorSettings() => OpenSettingsSection("Shortcuts");
    private async Task RefreshList()
    {
        if (host is null) return; refreshing = true;
        try { conversations.ItemsSource = await host.Service.Store.Read(s => s.Conversations.OrderByDescending(c => c.UpdatedAt).Select(c => c with { Messages = [] }).ToList()); conversations.SelectedItem = conversations.Items.Cast<Conversation>().FirstOrDefault(c => c.Id == currentId); }
        finally { refreshing = false; }
    }
    private async Task NewChat() { if (host is null || busy) return; currentId = (await host.Service.CreateConversation(null)).Id; ClearContext(); await RefreshList(); await ShowHistory(); ShowChat(); }
    private async Task Select(string id) { if (busy) return; currentId = id; ClearContext(); await ShowHistory(); ShowChat(); }
    private async Task ShowHistory()
    {
        if (host is null) return; var c = await host.Service.Store.Read(s => s.Conversations.FirstOrDefault(c => c.Id == currentId)); messages.Children.Clear();
        if (c is null) { currentId = null; messages.Children.Add(Text("Choose or create a conversation.", 20, Muted)); return; }
        foreach (var m in c.Messages) Bubble(m.Role, m.Text, m.Evidence);
        if (c.Messages.Count == 0)
        {
            messages.Children.Add(Text("Less friction. More room to think.", 24)); messages.Children.Add(Text("Ask a question, untangle an idea, or bring the app youâ€™re using into the conversation.", 15, Muted));
            foreach (var hint in new[] { "Help me plan a focused day", "Explain a concept with a simple example", "Turn my rough idea into a clear plan" }) messages.Children.Add(Btn(hint + "  â†—", () => { input.Text = hint; input.Focus(); }));
            messages.Children.Add(Text("Your model runs on this PC. Start with PC setup & models.", 13, Accent));
        }
        scroller.ScrollToEnd();
    }
    private TextBox Bubble(string role, string value, MessageEvidence? evidence = null)
    {
        var content = new StackPanel(); content.Children.Add(Text(role == "user" ? "YOU" : "BUDDY", 11, role == "user" ? Muted : Accent));
        var text = new TextBox { Text = value, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new(0), Background = System.Windows.Media.Brushes.Transparent, Foreground = Ink, FontSize = 16, Padding = new(0) }; content.Children.Add(text);
        var sources = new StackPanel(); SourceLinks.Fill(sources, evidence); content.Children.Add(sources); text.Tag = sources;
        messages.Children.Add(new Border { Child = content, Padding = new(18), Margin = new(0, 0, 8, 12), Background = role == "user" ? Panel : Bg, CornerRadius = new(12) }); return text;
    }
    private void SetBusy(bool value) { busy = value; input.IsReadOnly = value; send.IsEnabled = !value; stop.IsEnabled = value || homeSpeaking; conversations.IsEnabled = !value; companionState.Set("home", value ? CompanionMood.Thinking : CompanionMood.Idle); }
    private async Task Send()
    {
        if (host is null || busy || string.IsNullOrWhiteSpace(input.Text)) return; if (currentId is null) await NewChat();
        PrepareDesktopActivity("home"); var draft = input.Text.Trim(); const string selectedMode = "type";
        var route = AssistantIntent.Mode(draft); if (route is "agent" or "guide" or "knowledge") { StartWorkflow(route, draft); return; }
        var payload = new ChatRequest(currentId!, draft, Guid.NewGuid().ToString(), selectedMode, context, image is null ? null : Convert.ToBase64String(image), desktop.AllowWebResearch, screenApp);
        request = new(); SetBusy(true); tts?.Cancel(); input.Clear(); messages.Children.Clear();
        var old = await host.Service.Store.Read(s => s.Conversations.First(c => c.Id == currentId)); foreach (var m in old.Messages) Bubble(m.Role, m.Text, m.Evidence);
        Bubble("user", draft); var answer = Bubble("assistant", "");
        try
        {
            await foreach (var item in host.Service.Chat(payload, request.Token)) { request.Token.ThrowIfCancellationRequested(); if (item.Type == "delta") { answer.Text += item.Text; scroller.ScrollToEnd(); } if (item.Type == "status") status.Text = item.Text; if (item.Type == "evidence") SourceLinks.Fill((StackPanel)answer.Tag, item.Evidence); }
            request.Token.ThrowIfCancellationRequested();
            status.Text = "Answered on your PC Â· " + DateTime.Now.ToShortTimeString(); ClearContext();
            if (speak.IsChecked == true) ReadAloud(answer.Text);
        }
        catch (OperationCanceledException) { status.Text = "Stopped. Your draft has been restored."; input.Text = draft; answer.Text += "\n[Stopped â€” partial answer not saved]"; }
        catch (Exception ex) { status.Text = ex.Message; input.Text = draft; answer.Text += "\n[Answer failed â€” not saved]"; }
        finally { request.Dispose(); request = null; SetBusy(false); await RefreshList(); }
    }
    private void Cancel() { summonCapture?.Cancel();summonedField=null;voiceHeld=false; promptWatcher?.Suspend(); tasks?.Cancel(); regionPicker?.Cancel(); request?.Cancel(); captureRequest?.Cancel(); fieldCapture?.Cancel(); refineWindow?.Cancel(); dictationWindow?.Cancel(); onboarding?.Cancel(); host?.Service.StopAll(); StopMainDictation(); tts?.Cancel(); quick?.Cancel(); voiceOverlay?.Cancel(); assistant?.Cancel(); }
    private void PrepareDesktopActivity(string source)
    {
        if(source!="voice")promptWatcher?.Suspend();
        // Surface-local entry points (including restarting an open panel) share this handoff.
        // Cancel desktop operations without interrupting an unrelated Android request.
        if (source != "jobs") tasks?.Cancel();
        if (source != "region") regionPicker?.Cancel();
        if (source != "home") request?.Cancel();
        if (source != "talk") quick?.Cancel();
        if (source != "voice") voiceOverlay?.Cancel();
        if (source != "assistant") assistant?.Cancel();
        if (source != "refine") refineWindow?.Cancel();
        if (source != "dictation") dictationWindow?.Cancel();
        if (source is not "field" and not "refine" and not "dictation") fieldCapture?.Cancel();
        captureRequest?.Cancel(); onboarding?.Cancel(); StopMainDictation(); tts?.Cancel();
    }
    private void StopMainDictation()
    {
        mainMicrophone?.Cancel(); mainMicrophone?.Dispose(); mainMicrophone = null;
        var engine = recognizer; recognizer = null;
        LocalSpeechInput.Stop(engine); companionState.Set("home-dictation", CompanionMood.Idle);
        stop.IsEnabled = busy;
    }
    private async Task ReadLocalVoice(string text)
    {
        int token = ++homeSpeechGeneration; homeSpeaking = true; stop.IsEnabled = true;
        try { await tts!.SpeakAsync(ConversationalReply.PlainText(text), desktop); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { status.Text = "Answer shown â€” " + ex.Message; }
        finally { if (token == homeSpeechGeneration) { homeSpeaking = false; stop.IsEnabled = busy || mainMicrophone is not null; } }
    }
    private void ReadAloud(string text)
    {
        if (voiceOverlay?.IsListening == true) return;
        try { tts ??= new LocalVoiceOutput(); _ = ReadLocalVoice(text); }
        catch (Exception ex)
        {
            Diagnostics.Write("Optional read-aloud unavailable", ex);
            status.Text = "Answer saved. Read-aloud is unavailable: " + ex.Message;
        }
    }
    private void Dictate() => _ = DictateDraft();
    private async Task DictateDraft()
    {
        if (busy) return; Cancel();
        var cts = new CancellationTokenSource(); mainMicrophone = cts;
        var original = input.Text; var edit = new GuardedEdit(new HomeDraftField(input), original);
        var insertion = new DictationInsertion(input.SelectionStart, input.SelectionLength);
        status.Text = "Preparing microphoneâ€¦"; stop.IsEnabled = true; companionState.Set("home-dictation", CompanionMood.Listening);
        try
        {
            var engine = await LocalSpeechInput.Create(cts.Token, desktop);
            if (!ReferenceEquals(mainMicrophone, cts)) { LocalSpeechInput.Stop(engine); return; }
            recognizer = engine;
            engine.SpeechHypothesized += (_, e) => Dispatcher.BeginInvoke(new Action(() => { if (ReferenceEquals(recognizer, engine)) status.Text = "Listening Â· " + e.Result.Text; }));
            engine.RecognizeCompleted += (_, e) => Dispatcher.BeginInvoke(new Action(() => {
                if (!ReferenceEquals(recognizer, engine)) return;
                StopMainDictation();
                if (e.Error is not null || e.Result is null || e.Cancelled) { status.Text = e.Error?.Message ?? "No speech recognized."; return; }
                lastDictation = e.Result.Text; copyDictation.Visibility = Visibility.Visible;
                if (SpeechReview.Required(e.Result.Confidence, e.Result.Alternates.Select(a => (a.Text, a.Confidence)))) { status.Text = "Uncertain transcript: " + lastDictation + ". Use Copy dictation and review before sending."; return; }
                try {
                    if (input.SelectionStart != insertion.Start || input.SelectionLength != insertion.Length) throw new InvalidOperationException("The caret changed. Use Copy dictation.");
                    edit.Apply(insertion.Replace(original, lastDictation), DateTimeOffset.UtcNow, default);
                    mainDictationEdit = edit; undoDictation.Visibility = Visibility.Visible; status.Text = "Dictation inserted into your draft. Review before Send. Undo for 30 seconds.";
                } catch (Exception error) { status.Text = error.Message; }
            }));
            engine.RecognizeAsync(RecognizeMode.Single); status.Text = "Listeningâ€¦ Speak, then pause. Stop ends listening.";
        }
        catch (Exception ex) { if (ReferenceEquals(mainMicrophone, cts)) { StopMainDictation(); status.Text = ex is OperationCanceledException ? "Microphone unavailable or stopped." : ex.Message; input.Focus(); } }
    }
    private Window Dialog(string title, UIElement content, int width = 580, int height = 650)
    {
        var w = new Window { Title = title, Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(24) }, Width = Math.Min(width, SystemParameters.WorkArea.Width), Height = Math.Min(height, SystemParameters.WorkArea.Height), Background = Bg, Foreground = Ink, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        w.SourceInitialized += (_, _) => CaptureProtection.Apply(new WindowInteropHelper(w).Handle);
        w.Show(); return w;
    }
    private string? screenApp;
    private CancellationTokenSource? captureRequest;
    private void ClearContext() { context = null; screenApp = null; if (image is not null) Array.Clear(image); image = null; contextLabel.Text = ""; }
    private async Task ReadContext()
    {
        if (busy || captureRequest is not null) return;
        ClearContext();using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)); captureRequest = timeout;
        try
        {
            status.Text = "Reading visible screen textâ€¦"; var target = previousWindow;
            var perception = assistant?.Perception ?? new ScreenPerception(() => desktop);
            var snapshot = await perception.Capture(target, timeout.Token); timeout.Token.ThrowIfCancellationRequested();
            var text = snapshot.PromptText;
            if (host is not null) await host.Service.Audit("capture", snapshot.Context.App, "Explicit reviewed UIA text; no image stored");
            var p = new StackPanel(); p.Children.Add(Text("Review screen text before sharing", 23)); p.Children.Add(Text("Password fields and common sensitive tokens are omitted. Review the remaining text.", 13, Muted));
            var box = new TextBox { Text = text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; StyleBox(box); p.Children.Add(box); Window? w = null;
            p.Children.Add(Btn("Use this context", () => { ClearContext(); context = box.Text; screenApp = snapshot.Context.App; contextLabel.Text = "Screen text attached Â· sent with your next message"; w?.Close(); }, true)); w = Dialog("Buddy Â· Screen text", p); status.Text = "Review the context, then ask your question.";
        }
        catch (Exception ex) { status.Text = ex is OperationCanceledException ? "Screen reading stopped." : ex.Message; }
        finally { if (ReferenceEquals(captureRequest, timeout)) captureRequest = null; }
    }
    private async Task Capture()
    {
        if (busy || host is null || captureRequest is not null) return;
        ClearContext();using var captureTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)); captureRequest = captureTimeout;
        try
        {
            var s = await host.Service.Store.Read(s => s); var available = await host.Service.Engine.Status(s.Model, s.VisionModel, captureTimeout.Token);
            if (!available.Installed.Contains(s.VisionModel)) { status.Text = "Download the optional vision model in PC setup first."; return; }
            var target = previousWindow; Native.CheckWindow(target); Hide(); byte[] bytes; string capturedApp;
            try {
                if (!InputNative.SetForegroundWindow(target)) throw new InvalidOperationException("Activate the window you want to capture first.");
                await Task.Delay(150, captureTimeout.Token);
                var perception = assistant?.Perception ?? new ScreenPerception(() => desktop);
                var snapshot = await perception.Capture(target, captureTimeout.Token); capturedApp = snapshot.Context.App;
                bytes = await perception.Image(snapshot, captureTimeout.Token) ?? throw new InvalidOperationException("Buddy could not establish a private capture scope for this window. Use reviewed screen text instead.");
            } finally { Summon(); }
            var p = new StackPanel(); p.Children.Add(Text("Check this image before sharing", 23)); p.Children.Add(Text("Detected private fields are masked. Review the image and cancel if anything private remains. This capture stays on this PC and is not saved in chat history.", 13, Muted));
            p.Children.Add(new System.Windows.Controls.Image { Source = Bitmap(bytes), MaxHeight = 390 }); Window? w = null; bool retained = false;
            p.Children.Add(Btn("Attach image", () => { ClearContext(); image = bytes; screenApp = capturedApp; retained = true; contextLabel.Text = "Window image attached Â· sent with your next message"; w?.Close(); }, true)); p.Children.Add(Btn("Discard", () => w?.Close()));
            w = Dialog("Buddy Â· Capture preview", p, 720); w.Closed += (_, _) => { if (!retained) Array.Clear(bytes); };
        }
        catch (Exception ex) { status.Text = ex.Message; }
        finally { if (ReferenceEquals(captureRequest, captureTimeout)) captureRequest = null; }
    }
    private static BitmapImage Bitmap(byte[] bytes) { using var stream = new MemoryStream(bytes); var b = new BitmapImage(); b.BeginInit(); b.CacheOption = BitmapCacheOption.OnLoad; b.StreamSource = stream; b.EndInit(); b.Freeze(); return b; }
    private async Task Setup(bool embedded = false, int revision = 0)
    {
        if (host is null) return; var p = new StackPanel(); p.Children.Add(Text("Make Buddy yours", 27)); p.Children.Add(Text("1. Install or start Ollama\n2. Download the chat model\n3. Ask Buddy a question\n4. Pair your phone on the same Wi-Fi", 15, Muted));
        var info = Text("Checkingâ€¦", 14, Accent); p.Children.Add(info);
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
        if (embedded) { if (homeSection != "Settings" || settingsSection != "AI" || settingsRevision != revision) return; settingsBody.Content = p; p.Unloaded += (_, _) => download?.Cancel(); }
        else { var w = Dialog("Buddy Â· PC setup", p); w.Closed += (_, _) => download?.Cancel(); }
        await Check();
    }
    private void Pair()
    {
        if (host is null) return; var p = new StackPanel(); p.Children.Add(Text("Take Buddy with you", 26)); p.Children.Add(Text("Connect your phone and PC to the same Wi-Fi. In the Android app, tap Scan PC code. Each code works once and expires in 5 minutes.", 14, Muted));
        var ips = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up).SelectMany(n => n.GetIPProperties().UnicastAddresses).Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address)).Select(a => a.Address.ToString()).Distinct().ToList();
        if (ips.Count == 0) { p.Children.Add(Text("Connect the PC to Wi-Fi first.")); Dialog("Buddy Â· Pair phone", p); return; }
        var addresses = new ComboBox { ItemsSource = ips, SelectedIndex = 0 }; p.Children.Add(addresses);
        var qr = new System.Windows.Controls.Image { Height = 260, Margin = new(0, 12, 0, 12) }; var codeText = Text("", 22, Accent); var manual = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap }; StyleBox(manual); p.Children.Add(qr); p.Children.Add(codeText); p.Children.Add(manual);
        void Renew()
        {
            var pairing = host.Service.Pairing.Open(); var url = $"buddy://pair?host={addresses.SelectedItem}&port={host.Port}&pin={host.Fingerprint}&code={pairing.Code}";
            using var data = QRCodeGenerator.GenerateQrCode(url, QRCodeGenerator.ECCLevel.M); using var png = new PngByteQRCode(data); qr.Source = Bitmap(png.GetGraphic(5)); codeText.Text = "Pairing code  " + pairing.Code; manual.Text = url;
        }
        addresses.SelectionChanged += (_, _) => Renew(); Renew(); p.Children.Add(Btn("Refresh pairing code", Renew)); p.Children.Add(Text("If connection times out, allow Buddy through Windows Firewall on Private networks. The installation folder contains Enable-Phone-Access.ps1. Do not expose this port on your router.", 13, Muted));
        p.Children.Add(Btn("Manage paired phones", () => _ = Devices())); Dialog("Buddy Â· Pair phone", p, 600, 760);
    }
    private async Task Devices()
    {
        if (host is null) return; var p = new StackPanel(); p.Children.Add(Text("Paired phones", 24));
        foreach (var device in await host.Service.Store.Read(s => s.Devices)) { var row = new StackPanel(); row.Children.Add(Text(device.Name)); row.Children.Add(Btn("Revoke access", async () => { await host.Service.Store.Update(s => s.Devices.RemoveAll(d => d.Id == device.Id)); row.IsEnabled = false; })); p.Children.Add(row); } Dialog("Buddy Â· Devices", p);
    }
    private async Task Notes(bool memory)
    {
        if (host is null) return; var p = new StackPanel(); p.Children.Add(Text(memory ? "What Buddy remembers" : "Saved prompts", 25)); p.Children.Add(Text(memory ? "Only facts you explicitly save are remembered across chats." : "Keep useful prompts and reuse them in your draft.", 14, Muted));
        var items = await host.Service.Store.Read(s => memory ? s.Memories : s.Prompts);
        foreach (var n in items) { var card = new StackPanel(); card.Children.Add(Text(n.Title, 17, Accent)); card.Children.Add(Text(n.Text)); if (!memory) card.Children.Add(Btn("Use prompt", () => input.Text = n.Text)); card.Children.Add(Btn("Delete", async () => { await host.Service.Store.Update(s => (memory ? s.Memories : s.Prompts).RemoveAll(x => x.Id == n.Id)); card.IsEnabled = false; })); p.Children.Add(card); }
        var title = new TextBox { Text = "New item", Margin = new(0, 14, 0, 8) }; StyleBox(title); var body = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 100 }; StyleBox(body); p.Children.Add(title); p.Children.Add(body); var notice = Text("", 12, Accent); p.Children.Add(notice);
        p.Children.Add(Btn("Save", async () => {
            try { var note = new Note(Guid.NewGuid().ToString(), Security.Text(title.Text, 100, "Title"), Security.Text(body.Text, 2000, "Text")); await host.Service.Store.Update(s => { var list = memory ? s.Memories : s.Prompts; if (list.Count >= 50) throw new BuddyException("LIMIT", "Remove an old item first."); list.Add(note); return true; }); notice.Text = "Saved. Reopen this panel to view it."; body.Clear(); } catch (Exception ex) { notice.Text = ex.Message; }
        }, true)); Dialog(memory ? "Buddy Â· Memory" : "Buddy Â· Prompts", p);
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
        speechModelDownload?.Cancel();WhisperInference.Stop();
        if (shuttingDown) return; shuttingDown = true; Cancel(); promptWatcher?.Dispose(); refineWindow?.Close(); dictationWindow?.Close(); fieldBadge?.Dispose(); ptt?.Dispose(); regionHook?.Dispose(); regionShortcut?.Dispose(); voiceOverlay?.Dispose(); assistant?.Dispose(); quick?.Dispose(); companion?.Dispose(); refresh.Stop(); foreground.Stop(); tray.Dispose(); tts?.Dispose(); shortcut?.Dispose(); voiceShortcut?.Dispose(); Native.UnregisterHotKey(hwnd, 2); Native.UnregisterHotKey(hwnd, 5); Native.UnregisterHotKey(hwnd, 6);
        trayArtwork?.Dispose(); trayArtwork = null;
        try { if(tasks is not null){await tasks.Flush();tasks.Dispose();} if (host is not null) await host.DisposeAsync(); }
        finally { System.Windows.Application.Current.Shutdown(); }
    }
}
