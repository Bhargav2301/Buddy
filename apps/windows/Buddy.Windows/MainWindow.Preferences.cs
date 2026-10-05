using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;

namespace Buddy.Windows;

public sealed partial class MainWindow
{
    internal static readonly string[] SettingsSections = ["General", "Shortcuts", "Voice", "Add-ons", "Connectors", "Screen & Privacy", "Internet", "AI", "Brains", "Skills", "Memory", "Channels", "Guide & Agent", "Prompts", "Devices"];
    private readonly Dictionary<string, System.Windows.Controls.Button> settingsButtons = [];
    private int settingsRevision;
    private CancellationTokenSource? speechModelDownload;
    private static bool AdvancedSettings(string section) => section is "Connectors" or "Internet" or "Skills" or "Channels" or "Devices";
    internal void OpenSettingsSection(string section)
    {
        if (!FlushPendingPreferences()) return;
        settingsSection = SettingsSections.Contains(section) ? section : "General"; Summon(); NavigateHome("Settings");
    }
    private void BuildSettings()
    {
        if (desktop.CoreControlsOnly && AdvancedSettings(settingsSection)) settingsSection = "General";
        var layout = new Grid(); layout.ColumnDefinitions.Add(new() { Width = new(172) }); layout.ColumnDefinitions.Add(new());
        var rail = new StackPanel { Margin = new(0, 0, 20, 0) }; rail.Children.Add(Text("Settings", 24)); settingsButtons.Clear();
        foreach (var section in SettingsSections) {
            if (desktop.CoreControlsOnly && AdvancedSettings(section)) continue;
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
        if (!FlushPendingPreferences()) return;
        ClearPreferenceEditor();
        settingsSection = section; settingsRevision++;
        foreach (var item in settingsButtons) { item.Value.Background = item.Key == section ? BuddyTheme.Soft : Panel; item.Value.Foreground = item.Key == section ? Accent : Muted; }
        var p = new StackPanel(); settingsBody.Content = p; p.Children.Add(Text(section, 24));
        var notice = Text("", 12, Accent); AutomationProperties.SetLiveSetting(notice, AutomationLiveSetting.Polite);
        CheckBox Toggle(string label, bool value) { var box = new CheckBox { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, IsChecked = value, Margin = new(0, 4, 0, 8) }; AutomationProperties.SetName(box, label); p.Children.Add(box); return box; }
        void Save(Func<DesktopPreferences> next) { p.Children.Add(notice); p.Children.Add(Btn("Save changes", () => { MarkPreferencesPending(); FlushPendingPreferences(); }, true)); BindPreferenceEditor(p, notice, next); }
        if (section == "General") {
            if (!companionPresenceAvailable) p.Children.Add(Text("Installed Buddy is still running. This preview keeps its companion hidden to prevent duplicates. Quit the installed copy from its tray menu; the preview will take over automatically.", 14, Muted));
            p.Children.Add(Text("Companion name", 14)); var name = new TextBox { Text = desktop.CompanionName, MaxLength = 40 }; StyleBox(name); AutomationProperties.SetName(name, "Companion name"); p.Children.Add(name);
            p.Children.Add(Text("Appearance", 14)); var theme = new ComboBox { ItemsSource = new[] { "Black", "Night Mint", "System", "Light", "Dark" }, SelectedItem = desktop.Appearance }; AutomationProperties.SetName(theme, "Appearance"); p.Children.Add(theme);
            var motion = Toggle("Reduce motion", desktop.ReduceMotion); var companionEnabled = Toggle("Show companion", desktop.ShowCompanion);
            var compactPointer = Toggle("Use compact pointer and activity", desktop.CompactPointerMode);
            p.Children.Add(Text("Off uses the official Buddy mascot. Compact mode shows a small pointer and listening, thinking or speaking activity. Its bars are decorative, not a microphone waveform; reduced motion keeps them still.", 14, Muted));
            p.Children.Add(Text("Top-edge Buddy bar", 14));
            var islandMode = new ComboBox { ItemsSource = new[] { "Hidden", "Compact", "Expanded" }, SelectedItem = desktop.IslandMode };
            AutomationProperties.SetName(islandMode, "Top-edge Buddy bar"); p.Children.Add(islandMode);
            p.Children.Add(Text("Optional quick access to Type, Voice, Guide and Refine, with the current task and Stop. The original companion remains available. Changing the bar never starts listening.", 14, Muted));
            var hideFullscreen = Toggle("Hide Buddy bar in fullscreen apps", desktop.IslandHideInFullscreen);
            var coreOnly = Toggle("Show core controls first", desktop.CoreControlsOnly);
            p.Children.Add(Text("Simplify Home and Settings by hiding advanced entries. Saved data, existing permissions and tools are retained. Turn this off to restore all entries.", 14, Muted));
            var background = Toggle("Hide Home after an explicit background launch", desktop.StartInCompanionMode);
            p.Children.Add(Text("Closing Home keeps Buddy in the tray. Quit stops Buddy and phone access. Windows high contrast and reduced animation settings are respected.", 14, Muted));
            Save(() => desktop with { CompanionName = string.IsNullOrWhiteSpace(name.Text) ? "Buddy" : name.Text.Trim(), Appearance = theme.SelectedItem?.ToString() ?? desktop.Appearance, ReduceMotion = motion.IsChecked == true, ShowCompanion = companionEnabled.IsChecked == true, CompactPointerMode = compactPointer.IsChecked == true, IslandMode = islandMode.SelectedItem?.ToString() ?? desktop.IslandMode, IslandHideInFullscreen = hideFullscreen.IsChecked == true, CoreControlsOnly = coreOnly.IsChecked == true, StartInCompanionMode = background.IsChecked == true });
            p.Children.Add(Btn("Try pointing tutorial", ShowPractice));
        } else if (section == "Add-ons") {
            p.Children.Add(Text("The core teaches one step at a time. Turn on extra interactions here when you want them.",14,Muted));
            var region=Toggle("Enable circle selection",desktop.RegionSelectionEnabled);
            p.Children.Add(Text("Hold the area shortcut, draw around part of the active window, then release. Escape cancels. Screen-changing input requires selecting again.",14,Muted));
            var regionVoice=Toggle("Listen after I finish drawing an area",desktop.RegionVoiceAfterSelection);
            p.Children.Add(Text("Off: type or choose Talk after drawing. On: the microphone starts only after a valid selection, for one utterance up to 30 seconds. Stop, Escape and focus changes cancel it. Audio stays local; uncertain words still need review.",14,Muted));
            var chord=new ComboBox { ItemsSource=ShortcutChoice.RegionChoices, SelectedItem=ShortcutChoice.FindRegion(desktop.RegionShortcut) }; AutomationProperties.SetName(chord,"Area-selection shortcut");p.Children.Add(chord);
            p.Children.Add(Text("Ctrl+Shift+R is commonly used for browser reload. Windows can report registered conflicts; other apps' keyboard hooks may still compete.",12,Muted));
            var triangle=Toggle("Turn Buddy into a triangle while pointing",desktop.TrianglePointerEnabled);
            p.Children.Add(Text("A visual pointer only. Your real mouse stays under your control; reduced motion is respected.",14,Muted));
            Save(()=>desktop with { RegionSelectionEnabled=region.IsChecked==true,RegionVoiceAfterSelection=regionVoice.IsChecked==true,RegionShortcut=((ShortcutChoice)chord.SelectedItem).Label,TrianglePointerEnabled=triangle.IsChecked==true });
            p.Children.Add(Btn(desktop.CoreControlsOnly ? "App notes" : "Local jobs and app knowledge",OpenTasks));
            if (!desktop.CoreControlsOnly) {
                p.Children.Add(Btn("Connector setup and permissions",()=>ShowSettingsCategory("Connectors")));
            }
            p.Children.Add(Btn("Local voice options",()=>ShowSettingsCategory("Voice")));
            p.Children.Add(Btn(desktop.CoreControlsOnly ? "Guidance settings" : "Action approval settings",()=>ShowSettingsCategory("Guide & Agent")));
            p.Children.Add(Btn("Prompt refinement",()=>ShowSettingsCategory("Prompts")));
            if (!desktop.CoreControlsOnly) p.Children.Add(Text("Say 'Start an agent to ...' for a reviewed action plan, or 'Search my app notes ...' for sourced local excerpts. One job runs at a time; results and cancellations are visible in Local jobs. No terminal is required.",14,Muted));
        } else if(section=="Connectors") {
            p.Children.Add(Text("Not connected - account grants disabled",18));
            p.Children.Add(Text("This build cannot open an account-consent flow or read your accounts. Provider registration and your specific permission are needed first. Existing ChatGPT connections do not automatically grant Buddy access.",14,Muted));
            foreach(var capability in Buddy.Server.ConnectorCatalog.Capabilities){
                p.Children.Add(Text(capability.Provider=="gmail"?"Gmail":"Notion",18));
                p.Children.Add(Text(capability.Name+"\n"+capability.Setup,14,Muted));
                p.Children.Add(Text("Proposed scope: "+capability.Scope,12,Muted));
                var connect=Btn("Connect - awaiting specific approval",()=>{});connect.IsEnabled=false;p.Children.Add(connect);
            }
            p.Children.Add(Text("No send or write capability is exposed. Other providers need their own implementation and consent; they are not represented as connected tools.",14,Muted));
        } else if (section == "Shortcuts") {
            p.Children.Add(Text("Chat shortcut", 14)); var keys = new ComboBox { ItemsSource = ShortcutChoice.Choices, SelectedItem = ShortcutChoice.Find(desktop.Shortcut) }; AutomationProperties.SetName(keys, "Chat shortcut"); p.Children.Add(keys);
            p.Children.Add(Text("Voice shortcut", 14)); var voiceKeys = new ComboBox { ItemsSource = ShortcutChoice.Choices, SelectedItem = ShortcutChoice.Find(desktop.VoiceShortcut) }; AutomationProperties.SetName(voiceKeys, "Voice shortcut"); p.Children.Add(voiceKeys);
            var warning = Text("", 14, Muted); p.Children.Add(warning);
            void WarnShortcuts() => warning.Text = ShortcutChoice.Warnings(((ShortcutChoice)keys.SelectedItem).Label, ((ShortcutChoice)voiceKeys.SelectedItem).Label);
            WarnShortcuts(); keys.SelectionChanged += (_, _) => WarnShortcuts(); voiceKeys.SelectionChanged += (_, _) => WarnShortcuts();
            var hold = Toggle("Hold the voice shortcut to talk; release to finish", desktop.HoldToTalk);
            p.Children.Add(Text("Chat always opens typing. Voice opens listening. Ctrl+Alt+Esc stops Buddy. A shortcut conflict keeps your previous bindings active.", 14, Muted));
            p.Children.Add(Text(shortcutHint.Text, 12, Muted));
            Save(() => desktop with { Shortcut = ((ShortcutChoice)keys.SelectedItem).Label, VoiceShortcut = ((ShortcutChoice)voiceKeys.SelectedItem).Label, ShortcutStartsVoice = false, HoldToTalk = hold.IsChecked == true });
            p.Children.Add(Btn("Test typed shortcut surface", () => OpenQuick(false))); p.Children.Add(Btn("Test voice shortcut surface", () => OpenQuick(true)));
        } else if (section == "Voice") {
            var read = Toggle("Read voice answers aloud", desktop.ReadVoiceAnswers);
            var stream = Toggle("Start speaking complete sentences while Buddy prepares the rest", desktop.StreamVoiceSentences);
            p.Children.Add(Text("Optional for selected simple conversations: greetings, short jokes or poems, and selected neutral topics, with up to three sentences. Buddy generates sentences in separate stages; memory, screen context, advice, web research and actions use the full response. Stop cancels the remaining speech.", 14, Muted));
            ComboBox Select(string label, object[] items, object? selected) { p.Children.Add(Text(label, 14)); var box = new ComboBox { ItemsSource = items, SelectedItem = selected }; AutomationProperties.SetName(box, label); p.Children.Add(box); return box; }
            var recognitionEngines = new[]{new AudioChoice("whisper","Whisper - local neural recognition"),new AudioChoice("windows","Windows speech - legacy")};
            var recognitionEngine = Select("Speech recognition engine", recognitionEngines, recognitionEngines.FirstOrDefault(e=>e.Id==desktop.RecognitionEngine)??recognitionEngines[0]);
            var whisperModel = Select("Whisper model",WhisperModels.Choices,WhisperModels.Choices.FirstOrDefault(m=>m.Id==desktop.WhisperModel)??WhisperModels.Choices[0]);
            p.Children.Add(Text("Whisper processes up to 30 seconds on this PC and shows its words for review before a voice request. Audio stays in memory. English models need about 148-191 MB of local storage; use the multilingual model for other languages. Token probabilities cannot guarantee accuracy.",14,Muted));
            var download = Btn("Download selected Whisper model",async()=>{
                speechModelDownload?.Cancel();using var downloadRequest=new CancellationTokenSource();speechModelDownload=downloadRequest;
                try{notice.Text="Downloading the pinned model from Hugging Face (no audio is sent).";await WhisperModels.Download((WhisperModel)whisperModel.SelectedItem,downloadRequest.Token);notice.Text="Local Whisper model verified and ready.";}
                catch(OperationCanceledException){notice.Text="Model download stopped.";}catch(Exception ex){notice.Text=ex.Message;}
                finally{if(ReferenceEquals(speechModelDownload,downloadRequest))speechModelDownload=null;}
            });p.Children.Add(download);p.Children.Add(Btn("Stop model download",()=>speechModelDownload?.Cancel()));
            p.Children.Add(Text("Model storage: "+WhisperModels.DirectoryPath,12,Muted));
            var microphones = new[] { new AudioChoice("", "Windows default microphone") }.Concat(MicrophoneStream.Devices()).ToArray();
            if (desktop.MicrophoneId.Length > 0 && !microphones.Any(m => m.Id == desktop.MicrophoneId)) microphones = microphones.Append(new AudioChoice(desktop.MicrophoneId, "Unavailable: " + desktop.MicrophoneId)).ToArray();
            var mic = Select("Microphone", microphones, microphones.First(m => m.Id == desktop.MicrophoneId));
            var languages = LocalSpeechInput.Languages(); var language = Select("Recognition language", languages, languages.FirstOrDefault(l => l == desktop.RecognitionLanguage) ?? languages.FirstOrDefault(l => l == System.Globalization.CultureInfo.CurrentUICulture.Name));
            var engines = new[] { new AudioChoice("windows", "Windows local voice"), new AudioChoice("piper", "Piper · British voices") };
            var engine = Select("Voice engine", engines, engines.FirstOrDefault(e => e.Id == desktop.VoiceEngine) ?? engines[0]);
            var voices = new[] { "Windows default voice" }.Concat(LocalVoiceOutput.Voices()).ToArray(); var selectedVoice = Select("Windows voice", voices, voices.FirstOrDefault(v => v == desktop.VoiceName) ?? voices[0]);
            var neuralVoice = Select("Piper voice", NeuralSpeechSynthesizer.Voices, NeuralSpeechSynthesizer.Voices.FirstOrDefault(v => v.Id == desktop.NeuralSpeakerId && v.Preset == desktop.NeuralPreset) ?? NeuralSpeechSynthesizer.DefaultVoice);
            var paces = new[] { new NeuralVoiceChoice(-3, "Slowest"), new NeuralVoiceChoice(-2, "Slower"), new NeuralVoiceChoice(-1, "Calm"), new NeuralVoiceChoice(0, "Normal"), new NeuralVoiceChoice(1, "Brisk"), new NeuralVoiceChoice(2, "Fast") };
            var rate = Select("Speaking pace", paces, paces.FirstOrDefault(v => v.Id == desktop.VoiceRate) ?? paces[2]);
            var presetNote = Text("F3 uses its audition pace and softer level. Other voices use the speaking pace selected above.", 14, Muted); p.Children.Add(presetNote);
            void VoiceControls() {
                bool piper = ((AudioChoice)engine.SelectedItem).Id == "piper";
                bool fixedPreset = piper && ((NeuralVoiceChoice)neuralVoice.SelectedItem).Preset == "f3";
                selectedVoice.IsEnabled = !piper; neuralVoice.IsEnabled = piper; rate.IsEnabled = !fixedPreset; presetNote.Visibility = fixedPreset ? Visibility.Visible : Visibility.Collapsed;
            }
            engine.SelectionChanged += (_, _) => VoiceControls(); neuralVoice.SelectionChanged += (_, _) => VoiceControls(); VoiceControls();
            var voiceActions = new WrapPanel(); p.Children.Add(voiceActions);
            var headphonesOnly = Toggle("Speak only through selected headphones", desktop.HeadphonesOnly);
            var outputs = new[] { new AudioChoice("", "Select connected headphones") }.Concat(LocalVoiceOutput.Headphones()).ToArray();
            if (desktop.HeadphoneDeviceId.Length > 0 && !outputs.Any(o => o.Id == desktop.HeadphoneDeviceId)) outputs = outputs.Append(new AudioChoice(desktop.HeadphoneDeviceId, "Selected headphones are disconnected")).ToArray();
            var output = Select("Headphone output", outputs, outputs.First(o => o.Id == desktop.HeadphoneDeviceId));
            p.Children.Add(Text("Audio stays on this PC. Headphones-only speech stops on disconnect or default-output change and never switches to speakers. Windows must identify the endpoint as headphones or a headset; unverified devices stay muted.", 14, Muted));
            p.Children.Add(Text(NeuralSpeechSynthesizer.Available ? "Piper runs locally on this PC. Preview each voice to choose your preferred sound; the first phrase may take longer while the model loads. Stop releases the voice worker. Voice quality and pronunciation vary." : "Windows voices run locally. The optional Piper voice files are unavailable in this build; selecting Piper keeps speech muted until the approved files are restored.", 14, Muted));
            DesktopPreferences VoiceSelection() => desktop with { RecognitionEngine = ((AudioChoice)recognitionEngine.SelectedItem).Id, WhisperModel = ((WhisperModel)whisperModel.SelectedItem).Id, ReadVoiceAnswers = read.IsChecked == true, StreamVoiceSentences = stream.IsChecked == true, MicrophoneId = ((AudioChoice)mic.SelectedItem).Id, RecognitionLanguage = language.SelectedItem?.ToString() ?? "", VoiceEngine = ((AudioChoice)engine.SelectedItem).Id, NeuralSpeakerId = ((NeuralVoiceChoice)neuralVoice.SelectedItem).Id, NeuralPreset = ((NeuralVoiceChoice)neuralVoice.SelectedItem).Preset, VoiceName = selectedVoice.SelectedIndex <= 0 ? "" : selectedVoice.SelectedItem.ToString()!, VoiceRate = ((NeuralVoiceChoice)rate.SelectedItem).Id, HeadphonesOnly = headphonesOnly.IsChecked == true, HeadphoneDeviceId = ((AudioChoice)output.SelectedItem).Id };
            Save(VoiceSelection);
            voiceActions.Children.Add(Btn("Preview voice", async () => { try { tts ??= new LocalVoiceOutput(); await tts.SpeakAsync("I'm here. Tell me what you need, and we'll take it one step at a time.", VoiceSelection()); } catch (OperationCanceledException) { notice.Text = "Preview stopped."; } catch (Exception ex) { notice.Text = ex.Message; } }));
            voiceActions.Children.Add(Btn("Stop preview", () => tts?.Cancel())); p.Children.Add(Btn("Try voice", () => OpenQuick(true)));
        } else if (section == "Screen & Privacy") {
            var screen = Toggle("Use active-window context during voice", desktop.CaptureOnVoice);
            var protection = Toggle("Hide Buddy windows from screenshots and screen sharing", desktop.ProtectScreenshots);
            p.Children.Add(Text("Turn protection off when you want Buddy visible in a screenshot. This Windows feature cannot block a camera or every capture method.", 14, Muted));
            p.Children.Add(Text("Screenshots stay in memory on this PC. Detected private fields are masked. If Buddy cannot verify the capture scope, it skips the image. Screen contents never silently become search queries.", 14, Muted));
            p.Children.Add(Text("Blocked process names (comma separated)", 14)); var blocked = new TextBox { Text = desktop.BlockedApps }; StyleBox(blocked); AutomationProperties.SetName(blocked, "Blocked applications"); p.Children.Add(blocked);
            Save(() => desktop with { CaptureOnVoice = screen.IsChecked == true, ProtectScreenshots = protection.IsChecked == true, BlockedApps = blocked.Text.Trim() });
            p.Children.Add(Btn("Activity history", () => _ = ShowPrivacyHistory(p)));
            p.Children.Add(Btn("Delete all local Buddy data…", () => ConfirmLocalDeletion(p)));
        } else if (section == "Internet") {
            var web = Toggle("Allow internet research", desktop.AllowWebResearch);
            p.Children.Add(Text("Search queries go to DuckDuckGo. Public HTTPS pages are fetched without your browser cookies. Sources appear beside answers; local chat remains available with research off.", 14, Muted));
            Save(() => desktop with { AllowWebResearch = web.IsChecked == true });
        } else if (section == "AI") {
            if (host is null) p.Children.Add(Text("The local service is starting…", 14, Muted));
            else _ = Setup(true, settingsRevision);
        } else if (section == "Brains") {
            p.Children.Add(Text("On this PC - active", 18, Accent));
            p.Children.Add(Text("Local chat and voice use Ollama. A separately configured cloud text session requires review before each request; consumer subscriptions do not automatically include API access.", 14, Muted));
            p.Children.Add(Text(providerSession?.Status.Detail ?? "Cloud text is disconnected.", 14, Muted));
            p.Children.Add(Btn("Configure reviewed cloud text", OpenProviderSetup));
            p.Children.Add(Btn("Open reviewed cloud text", OpenCloudText));
            p.Children.Add(Btn("Local agent sessions", OpenLocalAgentSessions));
        } else if (section == "Skills") {
            p.Children.Add(Text("Available actions: Point, Guide, Refine and Dictate", 18));
            p.Children.Add(Text("Editable skill packs and Save as skill are planned for 0.7. No external skill runs in this preview.", 14, Muted));
        } else if (section == "Memory") {
            p.Children.Add(Text("Memories stay encrypted on this PC", 18));
            var remember=Toggle("Remember my screen-teaching questions and answers on this PC",desktop.RememberTeaching);
            p.Children.Add(Text("Off by default. When enabled, Buddy saves up to ten Q&A pairs per app, with one hundred total. Answers can include text you ask Buddy to read. Screenshots, microphone audio and unrelated field contents are not saved. Turning this off stops saving and using these notes; Clear removes them.",14,Muted));
            Save(()=>desktop with{RememberTeaching=remember.IsChecked==true});
            p.Children.Add(Btn("View saved teaching Q&A",async()=>{try{var rows=await TeachingMemory.Open().Read();var box=new TextBox{Text=rows.Count==0?"No teaching Q&A saved.":string.Join("\n\n",rows.Select(r=>r.App+" | "+r.At.ToLocalTime()+"\nYou: "+r.Question+"\nBuddy: "+r.Answer)),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Dialog("Saved teaching Q&A",box).Show();}catch(Exception ex){notice.Text=ex.Message;}}));
            p.Children.Add(Btn("Clear saved teaching Q&A",async()=>{try{await TeachingMemory.Open().Clear();voiceOverlay?.Cancel();notice.Text="Saved teaching Q&A cleared.";}catch(Exception ex){notice.Text=ex.Message;}}));
            p.Children.Add(Btn("Inspect saved facts", () => NavigateHome("Memories")));
            p.Children.Add(Text("The USER.md editor is planned for 0.7. Optional Honcho memory is off and unconnected; it requires separate consent before any conversation text leaves this PC.", 14, Muted));
        } else if (section == "Channels") {
            p.Children.Add(Text("This PC and paired Android", 18));
            p.Children.Add(Btn("Paired devices", () => ShowSettingsCategory("Devices")));
            p.Children.Add(Text("Telegram is planned for 0.8 and is not connected. Slack is later; iMessage is unavailable on Windows. Provider keys stay on the PC.", 14, Muted));
        } else if (section == "Guide & Agent") {
            var enabled = Toggle("Enable Agent on this PC", desktop.AgentEnabled); var strict = Toggle("Ask before every Agent change", desktop.StrictAgentConfirmations); var advance = Toggle("Advance Guide after a verified expected result", desktop.GuideAutoAdvance);
            if (desktop.CoreControlsOnly) enabled.Visibility = strict.Visibility = Visibility.Collapsed;
            p.Children.Add(Text("Approve the plan first. Every click, edit and app launch requires confirmation. Pixel-only targets provide directions. Esc, Stop and physical pointer movement halt further actions.", 14, Muted));
            p.Children.Add(Text("Guidance ink lifetime", 14));
            var inkLifetime = new ComboBox { ItemsSource = new[] { 5, 15, 30 }, SelectedItem = desktop.InkLifetimeSeconds };
            AutomationProperties.SetName(inkLifetime, "Guidance ink lifetime in seconds"); p.Children.Add(inkLifetime);
            p.Children.Add(Text("Seconds to show verified teaching and Guide marks. Input, focus loss or a changed target still clears them early; a longer lifetime does not keep stale pointers.", 14, Muted));
            Save(() => desktop with { AgentEnabled = enabled.IsChecked == true, StrictAgentConfirmations = strict.IsChecked == true, GuideAutoAdvance = advance.IsChecked == true, InkLifetimeSeconds = inkLifetime.SelectedItem is int seconds ? seconds : 15 });
            p.Children.Add(Btn("Try pointing tutorial", ShowPractice));
        } else if (section == "Prompts") {
            var badge = Toggle("Show the Refine badge beside supported AI-chat fields", desktop.ShowFieldBadge);
            var suggestions=Toggle("Offer local prompt suggestions while I type (opt in)",desktop.LocalPromptSuggestions);
            p.Children.Add(Text("Quick, Guided and Council refine locally. Facts and constraints are checked before a rewrite can replace your draft. Automatic suggestions are off.", 14, Muted));
            p.Children.Add(Text("The badge checks field names and roles without reading your text. Ctrl+Alt+R reads a prompt only when invoked. Ctrl+Alt+D dictates into a field with a verified caret; review the transcript before Insert.", 14, Muted));
            p.Children.Add(Text("Suggestions use text-change events in one supported focused AI-chat field, after a pause. Private windows and sensitive fields are skipped; no screenshots or global typed-text history. Buddy asks before refining, shows a highlighted change beside the original field, and never sends it. Use Yes or the voice shortcut to reply; Esc dismisses.",14,Muted));
            Save(() => desktop with { ShowFieldBadge = badge.IsChecked == true,LocalPromptSuggestions=suggestions.IsChecked==true });
            p.Children.Add(Btn("Open saved prompts", () => NavigateHome("Prompts")));
        } else if (section == "Devices") {
            p.Children.Add(Text("Paired phones use an encrypted connection to this PC.", 14, Muted));
            if (host is null) p.Children.Add(Text("The local service is starting…", 14, Muted)); else _ = LoadLibrary(p, "Devices", pageRevision);
        }
    }
    private void SavePreferences(DesktopPreferences next)
    {
        var old = desktop; var voiceBinding = voiceShortcut?.Active;
        using (var bindings = ShortcutBindingUpdate.Prepare(shortcut, voiceShortcut, regionShortcut, next)) {
            try { CaptureProtection.Set(next.ProtectScreenshots); next = persistPreferences(next); }
            catch { CaptureProtection.Set(old.ProtectScreenshots); throw; }
            bindings.Commit(); shortcut = bindings.Chat; voiceShortcut = bindings.Voice;
        }
        desktop = next;
        // Persistence can merge unrelated newer changes from another Buddy writer.
        // Reconcile the returned effective values, never the stale proposed object.
        var runtimeIssues = new List<string>();
        try {
            using var bindings = ShortcutBindingUpdate.Prepare(shortcut, voiceShortcut, regionShortcut, next);
            bindings.Commit(); shortcut = bindings.Chat; voiceShortcut = bindings.Voice;
        } catch (InvalidOperationException ex) { runtimeIssues.Add("Saved shortcut settings could not become active. " + ex.Message + " Use the displayed active shortcuts or tray."); }
        CaptureProtection.Set(next.ProtectScreenshots);
        BuddyTheme.Apply(next.Appearance, next.ReduceMotion); companion?.SetEnabled(companionPresenceAvailable && next.ShowCompanion);
        island?.SetMode(next.IslandMode); ApplyCoreNavigation();
        if (old.CoreControlsOnly != next.CoreControlsOnly) NavigateHome(next.CoreControlsOnly && homeSection is "Devices" or "Memories" or "Prompts" ? "Conversations" : homeSection);
        ApplyDisplayName(); companion?.SetTriangleEnabled(next.TrianglePointerEnabled); companion?.SetCompactPointerMode(next.CompactPointerMode); ConfigureRegionHook();
        fieldBadge?.SetEnabled(next.ShowFieldBadge);
        promptWatcher?.SetEnabled(next.LocalPromptSuggestions);
        if (voiceBinding != voiceShortcut?.Active || old.HoldToTalk != next.HoldToTalk) ConfigurePtt();
        if (old.VoiceEngine != next.VoiceEngine || old.VoiceName != next.VoiceName || old.NeuralSpeakerId != next.NeuralSpeakerId || old.NeuralPreset != next.NeuralPreset || old.VoiceRate != next.VoiceRate || old.HeadphonesOnly != next.HeadphonesOnly || old.HeadphoneDeviceId != next.HeadphoneDeviceId || old.ReadVoiceAnswers != next.ReadVoiceAnswers || old.StreamVoiceSentences != next.StreamVoiceSentences) { tts?.Cancel(); voiceOverlay?.Cancel(); }
        if (old.RecognitionEngine != next.RecognitionEngine || old.RecognitionLanguage != next.RecognitionLanguage || old.MicrophoneId != next.MicrophoneId || old.WhisperModel != next.WhisperModel) { voiceOverlay?.Cancel(); StopMainDictation(); }
        if (old.CaptureOnVoice != next.CaptureOnVoice || old.AgentEnabled != next.AgentEnabled || old.AllowWebResearch != next.AllowWebResearch || old.BlockedApps != next.BlockedApps) Cancel();
        if (host is not null) { host.Service.WebEnabled = next.AllowWebResearch; host.Service.AgentEnabled = next.AgentEnabled; }
        UpdateShortcutHint();
        // The disk commit succeeded. A merged shortcut can still be occupied by
        // another app; retain the working registration and let Settings navigate
        // to Shortcuts to repair it instead of misreporting an unsaved edit.
        preferenceRuntimeWarning = string.Join(" ", runtimeIssues);
        if (preferenceRuntimeWarning.Length > 0) status.Text = "Settings saved. " + preferenceRuntimeWarning;
    }
    private void ApplyDisplayName()
    {
        var name = string.IsNullOrWhiteSpace(desktop.CompanionName) ? "Buddy" : desktop.CompanionName.Trim();
        name = name[..Math.Min(40, name.Length)];
        companionBrand.Text = name; talkHeading.Text = "Talk with " + name; Title = name + " · Buddy Home";
        tray.Text = name + " · Buddy local AI";
        if (companion is not null) { companion.Title = name + " · Buddy companion"; AutomationProperties.SetName(companion, name + " companion menu"); }
        if (host is not null) host.Service.DisplayName = name;
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
        if (PreviewEnvironment.Enabled) { parent.Children.Add(Text("Local-data deletion is disabled in the isolated preview. The installed Buddy profile is untouched.", 14)); return; }
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

// A capability-free view: only bounded dropdown choices and a local preferences callback.
// No credentials, provider transport, account flow or active-routing control exists here.
internal sealed class ProviderDraftSetup : StackPanel
{
    private sealed record Choice(string Id, string Label, string[] Models)
    {
        public override string ToString() => Label;
    }
    private static readonly Choice[] choices = [
        new("", "Choose a provider draft", []),
        new("openai", "OpenAI API", ["gpt-4.1-mini", "gpt-4.1"]),
        new("anthropic", "Anthropic API", ["claude-sonnet-4-20250514"]),
        new("gemini", "Google Gemini API", ["gemini-2.0-flash"]),
        new("openrouter", "OpenRouter API", ["openai/gpt-4.1-mini"])
    ];
    internal ProviderDraftSetup(Func<DesktopPreferences> preferences, Action<DesktopPreferences> save)
    {
        void Label(string text, double size = 14)
            => Children.Add(new TextBlock { Text = text, FontSize = size, Foreground = BuddyTheme.Ink, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) });
        Label("Not connected - live activation unavailable", 18);
        Label("Save a provider and model draft on this PC. This sends no requests, accepts no keys or passwords, and grants no account access. Text and realtime transports are disconnected; microphone audio stays local.");
        var current = preferences();
        var provider = new ComboBox { ItemsSource = choices, SelectedItem = choices.FirstOrDefault(c => c.Id == current.ProviderDraft) ?? choices[0], Margin = new(0, 0, 0, 8) };
        var model = new ComboBox { Margin = new(0, 0, 0, 8) };
        AutomationProperties.SetName(provider, "Provider draft"); AutomationProperties.SetName(model, "Model draft");
        Label("Provider draft"); Children.Add(provider); Label("Model draft"); Children.Add(model);
        void Models(string selected = "")
        {
            var values = ((Choice)provider.SelectedItem).Models;
            model.ItemsSource = values; model.SelectedItem = values.Contains(selected) ? selected : values.FirstOrDefault(); model.IsEnabled = values.Length > 0;
        }
        Models(current.ProviderModelDraft); provider.SelectionChanged += (_, _) => Models();
        Label("Model identifiers are draft examples. Availability and account access have not been checked. Saving this draft keeps the local brain active.");
        var status = new TextBlock { Text = "Disconnected. No provider session or audio connection.", TextWrapping = TextWrapping.Wrap, Foreground = BuddyTheme.Deep, Margin = new(0, 0, 0, 8) };
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite); Children.Add(status);
        Children.Add(BuddyTheme.Button("Save disconnected draft", () => {
            try {
                var selected = (Choice)provider.SelectedItem;
                var selectedModel = model.SelectedItem as string ?? "";
                if (selected.Id.Length > 0 && !selected.Models.Contains(selectedModel)) throw new InvalidOperationException("Choose a model draft for this provider.");
                save(preferences() with { ProviderDraft = selected.Id, ProviderModelDraft = selectedModel });
                status.Text = "Draft saved on this PC. Still disconnected; your local brain is unchanged.";
            } catch (Exception ex) { status.Text = ex.Message; }
        }));
        var connect = new System.Windows.Controls.Button { Content = "Connect - unavailable in this build", IsEnabled = false, Margin = new(0, 0, 0, 8) };
        AutomationProperties.SetName(connect, "Connect - unavailable in this build"); Children.Add(connect);
    }
}
