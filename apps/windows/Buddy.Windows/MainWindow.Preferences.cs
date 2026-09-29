using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;

namespace Buddy.Windows;

public sealed partial class MainWindow
{
    internal static readonly string[] SettingsSections = ["General", "Shortcuts", "Voice", "Screen & Privacy", "Internet", "AI", "Guide & Agent", "Prompts", "Devices"];
    private readonly Dictionary<string, System.Windows.Controls.Button> settingsButtons = [];
    private int settingsRevision;
    internal void OpenSettingsSection(string section)
    {
        settingsSection = SettingsSections.Contains(section) ? section : "General"; Summon(); NavigateHome("Settings");
    }
    private void BuildSettings()
    {
        var layout = new Grid(); layout.ColumnDefinitions.Add(new() { Width = new(172) }); layout.ColumnDefinitions.Add(new());
        var rail = new StackPanel { Margin = new(0, 0, 20, 0) }; rail.Children.Add(Text("Settings", 24)); settingsButtons.Clear();
        foreach (var section in SettingsSections) {
            var b = Btn(section, () => ShowSettingsCategory(section)); b.HorizontalContentAlignment = HorizontalAlignment.Left;
            b.Margin = new(0, 0, 0, 4); b.Padding = new(8); settingsButtons[section] = b; rail.Children.Add(b);
        }
        layout.Children.Add(new ScrollViewer { Content = rail, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        // Detach before reusing the content host on subsequent Settings activations.
        if (settingsBody.Parent is ScrollViewer old) old.Content = null;
        var scroll = new ScrollViewer { Content = settingsBody, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(scroll, 1); layout.Children.Add(scroll); homeBody.Content = layout;
        ShowSettingsCategory(settingsSection);
    }
    private void ShowSettingsCategory(string section)
    {
        settingsSection = section; settingsRevision++;
        foreach (var item in settingsButtons) { item.Value.Background = item.Key == section ? BuddyTheme.Soft : Panel; item.Value.Foreground = item.Key == section ? Accent : Muted; }
        var p = new StackPanel(); settingsBody.Content = p; p.Children.Add(Text(section, 24));
        var notice = Text("", 12, Accent); AutomationProperties.SetLiveSetting(notice, AutomationLiveSetting.Polite);
        CheckBox Toggle(string label, bool value) { var box = new CheckBox { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, IsChecked = value, Margin = new(0, 4, 0, 8) }; AutomationProperties.SetName(box, label); p.Children.Add(box); return box; }
        void Save(Func<DesktopPreferences> next) { p.Children.Add(notice); p.Children.Add(Btn("Save changes", () => { try { SavePreferences(next()); notice.Text = "Saved on this PC."; } catch (Exception e) { notice.Text = e.Message; } }, true)); }
        if (section == "General") {
            p.Children.Add(Text("Appearance", 14)); var theme = new ComboBox { ItemsSource = new[] { "System", "Light", "Dark" }, SelectedItem = desktop.Appearance }; AutomationProperties.SetName(theme, "Appearance"); p.Children.Add(theme);
            var motion = Toggle("Reduce motion", desktop.ReduceMotion); var companionEnabled = Toggle("Show companion", desktop.ShowCompanion);
            var background = Toggle("Hide Home after an explicit background launch", desktop.StartInCompanionMode);
            p.Children.Add(Text("Closing Home keeps Buddy in the tray. Quit stops Buddy and phone access. Windows high contrast and reduced animation settings are respected.", 14, Muted));
            Save(() => desktop with { Appearance = theme.SelectedItem?.ToString() ?? "System", ReduceMotion = motion.IsChecked == true, ShowCompanion = companionEnabled.IsChecked == true, StartInCompanionMode = background.IsChecked == true });
            p.Children.Add(Btn("Try pointing tutorial", ShowPractice));
        } else if (section == "Shortcuts") {
            p.Children.Add(Text("Main shortcut", 14)); var keys = new ComboBox { ItemsSource = ShortcutChoice.Choices, SelectedItem = ShortcutChoice.Choices.FirstOrDefault(c => c.Label == desktop.Shortcut) ?? ShortcutChoice.Choices[0] }; AutomationProperties.SetName(keys, "Main shortcut"); p.Children.Add(keys);
            var voice = Toggle("Main shortcut opens voice", desktop.ShortcutStartsVoice); var hold = Toggle("Tap for chat; hold for voice", desktop.HoldToTalk);
            p.Children.Add(Text("Ctrl+Shift+Space opens voice. Ctrl+Alt+Esc stops Buddy. Hold-to-talk listens after 250 ms and sends on release. Conflicting shortcuts leave the previous binding active.", 14, Muted));
            Save(() => desktop with { Shortcut = ((ShortcutChoice)keys.SelectedItem).Label, ShortcutStartsVoice = voice.IsChecked == true, HoldToTalk = hold.IsChecked == true });
            p.Children.Add(Btn("Test typed shortcut surface", () => OpenQuick(false))); p.Children.Add(Btn("Test voice shortcut surface", () => OpenQuick(true)));
        } else if (section == "Voice") {
            var read = Toggle("Read voice answers aloud", desktop.ReadVoiceAnswers); var hold = Toggle("Enable hold-to-talk", desktop.HoldToTalk);
            p.Children.Add(Text("Buddy uses your Windows default microphone and installed speech language. The microphone starts only when you invoke voice; Stop interrupts recognition, inference and speech.", 14, Muted));
            Save(() => desktop with { ReadVoiceAnswers = read.IsChecked == true, HoldToTalk = hold.IsChecked == true });
            p.Children.Add(Btn("Try voice", () => OpenQuick(true)));
        } else if (section == "Screen & Privacy") {
            var screen = Toggle("Use active-window context during voice", desktop.CaptureOnVoice);
            p.Children.Add(Text("Screenshots stay in memory on this PC. Detected private fields are masked. If Buddy cannot verify the capture scope, it skips the image. Screen contents never silently become search queries.", 14, Muted));
            p.Children.Add(Text("Blocked process names (comma separated)", 14)); var blocked = new TextBox { Text = desktop.BlockedApps }; StyleBox(blocked); AutomationProperties.SetName(blocked, "Blocked applications"); p.Children.Add(blocked);
            Save(() => desktop with { CaptureOnVoice = screen.IsChecked == true, BlockedApps = blocked.Text.Trim() });
            p.Children.Add(Btn("Activity history", () => _ = ShowPrivacyHistory(p)));
            p.Children.Add(Btn("Delete all local Buddy data…", () => ConfirmLocalDeletion(p)));
        } else if (section == "Internet") {
            var web = Toggle("Allow internet research", desktop.AllowWebResearch);
            p.Children.Add(Text("Search queries go to DuckDuckGo. Public HTTPS pages are fetched without your browser cookies. Sources appear beside answers; local chat remains available with research off.", 14, Muted));
            Save(() => desktop with { AllowWebResearch = web.IsChecked == true });
        } else if (section == "AI") {
            if (host is null) p.Children.Add(Text("The local service is starting…", 14, Muted));
            else _ = Setup(true, settingsRevision);
        } else if (section == "Guide & Agent") {
            var enabled = Toggle("Enable Agent on this PC", desktop.AgentEnabled); var strict = Toggle("Ask before every Agent change", desktop.StrictAgentConfirmations); var advance = Toggle("Advance Guide after a verified expected result", desktop.GuideAutoAdvance);
            p.Children.Add(Text("Approve the plan first. Verified reversible actions may continue; consequential or uncertain actions ask again. Pixel-only targets provide directions. Esc, Stop and physical pointer movement halt further actions.", 14, Muted));
            Save(() => desktop with { AgentEnabled = enabled.IsChecked == true, StrictAgentConfirmations = strict.IsChecked == true, GuideAutoAdvance = advance.IsChecked == true });
            p.Children.Add(Btn("Try pointing tutorial", ShowPractice));
        } else if (section == "Prompts") {
            p.Children.Add(Text("Quick, Guided and Council refine locally. Facts and constraints are checked before a rewrite can replace your draft. Automatic suggestions are off.", 14, Muted));
            p.Children.Add(Btn("Open saved prompts", () => NavigateHome("Prompts")));
        } else if (section == "Devices") {
            p.Children.Add(Text("Paired phones use an encrypted connection to this PC.", 14, Muted));
            if (host is null) p.Children.Add(Text("The local service is starting…", 14, Muted)); else _ = LoadLibrary(p, "Devices", pageRevision);
        }
    }
    private void SavePreferences(DesktopPreferences next)
    {
        var old = desktop; var binding = shortcut?.Active;
        if (next.Shortcut != desktop.Shortcut && shortcut is not null) {
            var selected = ShortcutChoice.Choices.First(c => c.Label == next.Shortcut);
            if (!shortcut.TrySet(selected)) throw new InvalidOperationException("That shortcut is in use. Your previous shortcut is unchanged.");
        }
        try { next.Save(); } catch { if (binding is not null) shortcut?.TrySet(binding); throw; }
        desktop = next; BuddyTheme.Apply(next.Appearance, next.ReduceMotion); companion?.SetEnabled(next.ShowCompanion);
        if (old.Shortcut != next.Shortcut || old.HoldToTalk != next.HoldToTalk) ConfigurePtt();
        if (old.CaptureOnVoice != next.CaptureOnVoice || old.AgentEnabled != next.AgentEnabled || old.AllowWebResearch != next.AllowWebResearch || old.BlockedApps != next.BlockedApps) Cancel();
        if (host is not null) { host.Service.WebEnabled = next.AllowWebResearch; host.Service.AgentEnabled = next.AgentEnabled; }
        UpdateShortcutHint();
    }
    private async Task ShowPrivacyHistory(StackPanel p)
    {
        if (host is null) return;
        var rows = await host.Service.Store.Read(s => s.Audit.TakeLast(20).Reverse().ToList());
        var history = new StackPanel(); history.Children.Add(Text("Recent activity", 20)); history.Children.Add(Text("Metadata only. No session screenshots are saved.", 12, Muted));
        foreach (var row in rows) history.Children.Add(Text($"{row.At.ToLocalTime():g} · {row.Kind} · {row.Target}\n{row.Result}", 14));
        if (rows.Count == 0) history.Children.Add(Text("No recorded activity.", 14, Muted));
        p.Children.Add(BuddyTheme.Card(history));
    }
    private void ConfirmLocalDeletion(StackPanel parent)
    {
        var p = new StackPanel();
        p.Children.Add(Text("Delete all local Buddy data", 20));
        p.Children.Add(Text("This permanently removes conversations, memories, prompts, walkthroughs, paired devices, settings, encryption keys and logs on this PC. Paired phones will need pairing again. Buddy will quit before deletion. Downloaded Ollama models and the installed app remain.", 14));
        p.Children.Add(Text("Type DELETE to confirm.", 14));
        var confirmation = new TextBox(); StyleBox(confirmation); AutomationProperties.SetName(confirmation, "Type DELETE to confirm local data deletion"); p.Children.Add(confirmation);
        var notice = Text("", 14, Muted); p.Children.Add(notice);
        var erase = Btn("Delete data and quit", () => { }); erase.IsEnabled = false;
        confirmation.TextChanged += (_, _) => erase.IsEnabled = confirmation.Text == "DELETE";
        erase.Click += async (_, _) => {
            if (confirmation.Text != "DELETE" || shuttingDown) return;
            erase.IsEnabled = confirmation.IsEnabled = false;
            try { await LocalDataDeletion.Prepare(); await Quit(); }
            catch (Exception e) { notice.Text = e.Message; confirmation.IsEnabled = true; erase.IsEnabled = true; }
        };
        p.Children.Add(erase);
        var card = BuddyTheme.Card(p); p.Children.Add(Btn("Keep my data", () => parent.Children.Remove(card)));
        parent.Children.Add(card); confirmation.Focus();
    }
}
