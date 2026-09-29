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
        }
        faces.Measure(new Size(900, 100)); faces.Arrange(new Rect(0, 0, 900, 100)); faces.UpdateLayout();
        var bitmap = Render(faces, Path.Combine(output, "Companion-states.png"));
        var pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        int teal = 0; for (int i = 0; i < pixels.Length; i += 4) if (pixels[i + 1] > 100 && pixels[i + 1] > pixels[i + 2] * 1.4 && pixels[i + 3] > 0) teal++;
        check(teal > 300, "Original Figma SVG companion assets render visible teal pixels");
        check(Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Assets", "Figma"), "*.svg").All(p => new FileInfo(p).Length > 0), "Bundled Figma vectors are nonempty local files");
        BuddyTheme.Apply("Light", true);
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
