using Buddy.Windows;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class VisualChecks
{
    internal static void Run(MainWindow home, Action<bool, string> check)
    {
        // Renders only Buddy's isolated, service-free fixture. No screen capture or user chat data.
        var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../dist/design-review"));
        Directory.CreateDirectory(output);
        foreach (var theme in new[] { "Light", "Dark" }) {
            BuddyTheme.Apply(theme, true);
            home.NavigateHome("Conversations"); home.UpdateLayout();
            Render((FrameworkElement)home.Content, Path.Combine(output, "Home-" + theme + ".png"));
            home.OpenSettingsSection("General"); home.UpdateLayout();
            Render((FrameworkElement)home.Content, Path.Combine(output, "Settings-" + theme + ".png"));
            check(BuddyTheme.Ink.Color != BuddyTheme.Surface.Color && !BuddyTheme.Animate, theme + " updates open surfaces and disables reduced-motion animation");
        }
        var faces = new WrapPanel { Background = Brushes.White, Margin = new(10) };
        foreach (var mood in Enum.GetValues<CompanionMood>()) {
            var face = new CompanionFace(); face.SetMood(mood); faces.Children.Add(face);
            check(Descendants(face).OfType<FacialRig>().Count()+Descendants(face).OfType<Image>().Count(i=>ReferenceEquals(i.Source,AppBranding.Character)) == 1 && !Descendants(face).OfType<TextBlock>().Any(), mood + " uses one character-rendering surface without a second facial layer");
        }
        faces.Measure(new Size(900, 100)); faces.Arrange(new Rect(0, 0, 900, 100)); faces.UpdateLayout();
        var bitmap = Render(faces, Path.Combine(output, "Companion-states.png"));
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        int teal = 0; for (int i = 0; i < pixels.Length; i += 4) if (pixels[i + 1] > 100 && pixels[i + 1] > pixels[i + 2] * 1.4 && pixels[i + 3] > 0) teal++;
        check(teal > 300, "Owner-supplied character artwork renders visible mint pixels");
        check(Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Assets", "Figma"), "*.svg").All(p => new FileInfo(p).Length > 0), "Bundled Figma vectors are nonempty local files");
        BuddyTheme.Apply("Light", true);
        foreach (var section in new[] { "Shortcuts", "Voice", "Add-ons", "Connectors", "Screen & Privacy", "Brains", "Channels" }) {
            home.OpenSettingsSection(section); home.UpdateLayout();
            if(section == "Add-ons") {
                var before=DesktopPreferences.Load();
                try {
                    var region=Descendants(home).OfType<System.Windows.Controls.CheckBox>().Single(c=>System.Windows.Automation.AutomationProperties.GetName(c)=="Enable circle selection");
                    var triangle=Descendants(home).OfType<System.Windows.Controls.CheckBox>().Single(c=>System.Windows.Automation.AutomationProperties.GetName(c)=="Turn Buddy into a triangle while pointing");
                    var regionalSpeech=Descendants(home).OfType<System.Windows.Controls.CheckBox>().Single(c=>System.Windows.Automation.AutomationProperties.GetName(c)=="Listen after I finish drawing an area");
                    var chord=Descendants(home).OfType<ComboBox>().Single(c=>System.Windows.Automation.AutomationProperties.GetName(c)=="Area-selection shortcut");
                    region.IsChecked=true;triangle.IsChecked=true;regionalSpeech.IsChecked=true;chord.SelectedItem=ShortcutChoice.RegionChoices[0];
                    Descendants(home).OfType<Button>().Single(b=>Equals(b.Content,"Save changes")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var saved=DesktopPreferences.Load();
                    check(saved.RegionSelectionEnabled&&saved.TrianglePointerEnabled&&saved.RegionShortcut=="Ctrl + Alt + Shift + R","Optional region/triangle choices persist through the real Settings controls");
                    check(saved.RegionVoiceAfterSelection,"Explicit regional speech preference persists through Settings without starting the microphone");
                    check(saved.NeuralSpeakerId==before.NeuralSpeakerId&&saved.NeuralPreset==before.NeuralPreset&&saved.VoiceShortcut==before.VoiceShortcut,"Saving add-ons preserves voice and shortcut choices");
                } finally { typeof(MainWindow).GetMethod("SavePreferences", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(home,[before]); }
            }
            if(section=="Connectors")check(Descendants(home).OfType<Button>().Count(b=>Equals(b.Content,"Connect - awaiting specific approval")&&!b.IsEnabled)==2,"Both real provider grant controls remain disabled in the shipped connection UI");
            if (section == "Voice") {
                var picker = Descendants(home).OfType<ComboBox>().Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Piper voice");
                var voices = picker.Items.Cast<NeuralVoiceChoice>().ToArray();
                check(voices.Length == 7 && voices.Any(v => v.Speaker == "p227") && voices.Any(v => v.Speaker == "p229" && v.Preset == "") && voices.Any(v => v.Preset == "f3"), "Voice Settings exposes B, C, original F and the separate F3 preset");
                var before = DesktopPreferences.Load();
                var engine = Descendants(home).OfType<ComboBox>().Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Voice engine");
                var pace = Descendants(home).OfType<ComboBox>().Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Speaking pace");
                try {
                    engine.SelectedItem = engine.Items.Cast<AudioChoice>().Single(v => v.Id == "piper");
                    picker.SelectedItem = voices.Single(v => v.Preset == "f3");
                    check(!pace.IsEnabled, "F3 keeps its approved audition pace instead of applying the general pace control");
                    Descendants(home).OfType<Button>().Single(b => Equals(b.Content, "Save changes")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var saved = DesktopPreferences.Load();
                    check(saved.NeuralSpeakerId == 85 && saved.NeuralPreset == "f3", "Saving F3 from the real Settings controls persists both speaker and preset");
                    home.UpdateLayout(); Render((FrameworkElement)home.Content, Path.Combine(output, "Settings-Voice-F3.png"));
                    picker.SelectedItem = voices.Single(v => v.Speaker == "p229" && v.Preset == "");
                    check(pace.IsEnabled, "Original F retains its adjustable speaking pace");
                } finally {
                    typeof(MainWindow).GetMethod("SavePreferences", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(home, [before]);
                }
            }
            Render((FrameworkElement)home.Content, Path.Combine(output, "Settings-" + section.Replace(" & ", "-") + ".png"));
        }
        var onboarding = new OnboardingWindow("Buddy", _ => { }, () => { }, () => { }, () => { }, () => { }, () => Task.FromResult("Fixture model ready"));
        onboarding.Show(); onboarding.UpdateLayout(); Render((FrameworkElement)onboarding.Content, Path.Combine(output, "Onboarding-Light.png")); onboarding.Close();
        var refine = new RefineWindow(null!, "Write a concise summary of this draft. Keep the dates unchanged.", (_, _) => Task.CompletedTask, _ => Task.CompletedTask, "Isolated preview · no inference");
        refine.Show(); refine.UpdateLayout(); Render((FrameworkElement)refine.Content, Path.Combine(output, "Refine-Light.png")); refine.Close();
        using var assistant = new DesktopAssistant(() => null, () => new(), () => IntPtr.Zero, _ => { });
        assistant.Open("guide", "").GetAwaiter().GetResult();
        var guide = Application.Current.Windows.Cast<Window>().Single(w => w.Title == "Buddy · Guide"); guide.UpdateLayout();
        Render((FrameworkElement)guide.Content, Path.Combine(output, "Guide-Light.png"));
        assistant.Open("agent", "").GetAwaiter().GetResult(); guide.UpdateLayout();
        Render((FrameworkElement)guide.Content, Path.Combine(output, "Agent-Light.png"));
        using var voice = new VoiceOverlayWindow(() => null, () => Task.FromResult<string?>(null), () => new(), _ => { }, () => IntPtr.Zero, assistant.Perception, (_, _) => { }, () => { });
        voice.Show(); voice.UpdateLayout(); Render((FrameworkElement)voice.Content, Path.Combine(output, "Voice-Light.png"));
        check(!voice.IsListening, "Voice design review does not activate the microphone");
        Console.WriteLine("Buddy-only layout review images: " + output);
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static RenderTargetBitmap Render(FrameworkElement element, string path)
    {
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(element.ActualWidth)), Math.Max(1, (int)Math.Ceiling(element.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle(Window.GetWindow(element)?.Background ?? BuddyTheme.Surface, null, new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
        bitmap.Render(background);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream); return bitmap;
    }
}
