using System.Speech.Recognition;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Media;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;

namespace Buddy.Windows;

internal sealed class OnboardingWindow : Window
{
    private readonly Action<string> completed;
    private readonly Action setup, tutorial, testChat, testVoice;
    private readonly Func<Task<string>> modelStatus;
    private readonly TextBox name = new() { MaxLength = 40, Margin = new(0, 12, 0, 20) };
    private readonly TextBlock notice = Label("", 12);
    private SpeechRecognitionEngine? recognizer;
    private bool closed;
    private int namingGeneration;
    private bool namingActive;
    private Button? nameVoice;
    internal OnboardingWindow(string initialName, Action<string> completed, Action setup, Action tutorial, Action testChat, Action testVoice, Func<Task<string>> modelStatus)
    {
        BuddyTheme.Ensure(); this.completed = completed; this.setup = setup; this.tutorial = tutorial; this.testChat = testChat; this.testVoice = testVoice; this.modelStatus = modelStatus;
        Title = "Welcome to Buddy"; Width = 420; SizeToContent = SizeToContent.Height; MaxHeight = SystemParameters.WorkArea.Height; ResizeMode = ResizeMode.NoResize; Background = BuddyTheme.Surface; Foreground = BuddyTheme.Ink; FontFamily = BuddyTheme.Font; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        name.Text = initialName; AutomationProperties.SetName(name, "Companion name"); name.ToolTip = "Rename me, or keep Buddy";
        Closed += (_, _) => { closed = true; StopNaming(); };
        Hatch();
    }
    private StackPanel Page()
    {
        var p = new StackPanel { Margin = new(40, 20, 40, 28) };
        Content = new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        return p;
    }
    private void Hatch()
    {
        var p = Page(); p.Children.Add(new CompanionFace(compact: true) { HorizontalAlignment = HorizontalAlignment.Center, Margin = new(0, 0, 0, 16) });
        var hello = Label("Hello, my name is", 14); hello.TextAlignment = TextAlignment.Center; p.Children.Add(hello);
        var heading = Label("Buddy", 28); heading.Foreground = BuddyTheme.Deep; heading.FontWeight = FontWeights.Bold; heading.TextAlignment = TextAlignment.Center; p.Children.Add(heading); p.Children.Add(name);
        var actions = new Grid(); actions.ColumnDefinitions.Add(new()); actions.ColumnDefinitions.Add(new());
        var yes = BuddyTheme.Button("That is me", () => { StopNaming(); if (string.IsNullOrWhiteSpace(name.Text)) name.Text = "Buddy"; Privacy(); }, true); actions.Children.Add(yes);
        nameVoice = BuddyTheme.Button("Speak it", () => { if (namingActive) StopNaming(); else _ = SpeakName(); }); Grid.SetColumn(nameVoice, 1); actions.Children.Add(nameVoice); p.Children.Add(actions);
        p.Children.Add(notice);
        p.Children.Add(Label("I look only when you ask. Screenshots stay in memory.", 12));
    }
    private async Task SpeakName()
    {
        StopNaming(); int generation = namingGeneration; namingActive = true; if (nameVoice is not null) nameVoice.Content = "Stop"; notice.Text = "Preparing microphone…";
        SpeechRecognitionEngine? prepared = null;
        try {
            prepared = await Task.Run(() => {
                var installed = SpeechRecognitionEngine.InstalledRecognizers(); if (installed.Count == 0) throw new InvalidOperationException("No Windows speech language is installed. Type a name instead.");
                var r = new SpeechRecognitionEngine(installed[0]);
                try { r.LoadGrammar(new DictationGrammar()); r.SetInputToDefaultAudioDevice(); return r; } catch { r.Dispose(); throw; }
            });
            if (closed || generation != namingGeneration) { prepared.Dispose(); return; }
            recognizer = prepared; var active = prepared;
            active.SpeechRecognized += (_, e) => Dispatcher.BeginInvoke(new Action(() => { if (ReferenceEquals(recognizer, active)) name.Text = e.Result.Text.Trim()[..Math.Min(40, e.Result.Text.Trim().Length)]; }));
            active.RecognizeCompleted += (_, e) => Dispatcher.BeginInvoke(new Action(() => { if (ReferenceEquals(recognizer, active)) { notice.Text = e.Error?.Message ?? "Check the name, then choose That is me."; StopNaming(); } }));
            active.RecognizeAsync(RecognizeMode.Single); notice.Text = "Listening for a name…";
        } catch (Exception e) { prepared?.Dispose(); StopNaming(); notice.Text = e.Message; }
    }
    private void StopNaming()
    {
        namingGeneration++;
        namingActive = false; if (nameVoice is not null) nameVoice.Content = "Speak it";
        var current = recognizer; recognizer = null;
        if (current is not null) { try { current.RecognizeAsyncCancel(); } catch (InvalidOperationException) { } current.Dispose(); notice.Text = "Microphone stopped."; }
    }
    internal void Cancel() => StopNaming();
    private void Privacy()
    {
        var p = Page(); p.Children.Add(Label("Meet " + name.Text.Trim(), 28));
        p.Children.Add(Label("Your AI runs on this PC. Screenshots are never saved with your conversations. Sensitive fields are masked; uncertain capture scopes are skipped.", 14));
        p.Children.Add(Label("Internet research and Agent execution start off. Enable them in Settings when you want them. Every Agent task starts with a plan for you to approve.", 14));
        p.Children.Add(Label("You can try pointing before any model finishes downloading.", 14));
        p.Children.Add(BuddyTheme.Button("Try the sample point", tutorial, true));
        p.Children.Add(BuddyTheme.Button("Next · shortcuts and model", Ready));
    }
    private void Ready()
    {
        var p = Page(); p.Children.Add(Label("Ready when you are", 28)); var status = Label("Checking local model…", 14); p.Children.Add(status);
        async Task Check() { try { status.Text = await modelStatus(); } catch (Exception e) { status.Text = e.Message; } }
        _ = Check();
        p.Children.Add(BuddyTheme.Button("Set up or download a model", setup, true));
        p.Children.Add(BuddyTheme.Button("Check model readiness", () => _ = Check()));
        p.Children.Add(Label("Test your configured shortcut for chat. Ctrl+Shift+Space opens the separate voice bubble. Ctrl+Alt+Esc stops Buddy.", 14));
        var actions = new WrapPanel(); actions.Children.Add(BuddyTheme.Button("Try chat", testChat)); actions.Children.Add(BuddyTheme.Button("Try voice", testVoice)); p.Children.Add(actions);
        p.Children.Add(BuddyTheme.Button("Finish", () => { try { completed(name.Text.Trim()); Close(); } catch (Exception e) { status.Text = e.Message; } }, true));
    }
    private static TextBlock Label(string value, double size) => new() { Text = value, FontSize = size, Foreground = BuddyTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) };
}
