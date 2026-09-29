using Buddy.Server;
using Buddy.Windows;
using System.Runtime.InteropServices;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;

internal static class Program
{
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);
    [STAThread] private static int Main(string[] args)
    {
        if (args.Contains("--settings-navigation")) return SettingsNavigation();
        if (args.Contains("--ocr")) return OcrChecks().GetAwaiter().GetResult();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; int exit = 1, count = 0;
        void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); count++; Console.WriteLine("PASS: " + message); }
        var fixture = new Window { Title = "Buddy isolated integration fixture", Width = 600, Height = 400 };
        var editor = new TextBox { Text = "Before", Height = 60 }; AutomationProperties.SetName(editor, "Example text");
        var password = new PasswordBox { Password = "private-test-marker", Height = 30 }; AutomationProperties.SetName(password,"Private test field");
        var button = new Button { Content = "Export example", Height = 50 }; int clicks = 0; button.Click += (_,_) => clicks++;
        var stack = new StackPanel { Margin = new(20) }; stack.Children.Add(editor); stack.Children.Add(button); stack.Children.Add(password); fixture.Content = stack;
        fixture.Loaded += async (_,_) => {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try {
                var window = new WindowInteropHelper(fixture).Handle; ScreenPerception.PracticeHandle = window;
                fixture.Activate(); InputNative.SetForegroundWindow(window); await Task.Delay(300); Check(Native.GetForegroundWindow() == window,"Fixture has focus before native tests");
                using var assistant = new DesktopAssistant(() => null, () => new() { AgentEnabled = true }, () => window, _ => { });
                var snapshot = await assistant.Perception.Capture(window, timeout.Token);
                var field = GroundingResolver.Resolve(snapshot.Context.Elements,"","Example text","Edit");
                var export = GroundingResolver.Resolve(snapshot.Context.Elements,"","Export example","Button");
                Check(field is not null && export is not null,"Production UIA capture finds real WPF controls");
                Check(snapshot.PrivateRects.Count > 0 && !snapshot.Context.Elements.Any(e => e.Name.Contains("private-test-marker") || e.Name == "Private test field"),"Password controls are omitted and their bounds are redacted");
                var captured = await assistant.Perception.Frame(snapshot, timeout.Token);
                Check(captured is not null, "Windows Graphics Capture returns a frame for a verified fixture scope");
                using (var capturedStream = new MemoryStream(captured!.Image)) using (var capturedBitmap = new System.Drawing.Bitmap(capturedStream)) {
                    var secret = snapshot.PrivateRects[0]; int x = (int)(secret.X + secret.Width / 2 - captured.Bounds.X), y = (int)(secret.Y + secret.Height / 2 - captured.Bounds.Y);
                    var pixel = capturedBitmap.GetPixel(x, y);
                    Check(pixel.R == 0 && pixel.G == 0 && pixel.B == 0, "Captured password pixels are masked at physical window coordinates");
                }
                captured.Dispose(); Check(captured.Image.All(b => b == 0), "Disposing the capture clears its encoded image buffer");
                var changed = await Task.Run(() => assistant.Apply(new("type", Ref:field!.Ref, Value:"Buddy test text"),snapshot,field,timeout.Token));
                Check(editor.Text == "Buddy test text" && changed.StartsWith("Updated"),"Production action runner edits a real field through ValuePattern");
                await Task.Run(() => assistant.Apply(new("invoke", Ref:export!.Ref),snapshot,export,timeout.Token)); await Task.Delay(100);
                Check(clicks == 1,"Production action runner invokes a real button");
                using var overlay = new GuidanceOverlay(); overlay.Draw(window,export!,"arrow","Export example"); await Task.Delay(100);
                var ink = app.Windows.Cast<Window>().Single(w => w.Title == "Buddy guidance ink"); var inkHandle = new WindowInteropHelper(ink).Handle;
                long styles = GetWindowLongPtr(inkHandle,-20).ToInt64();
                Check((styles & 0x20) != 0 && (styles & 0x08000000) != 0 && Native.GetForegroundWindow() == window,"Guidance ink is click-through, nonactivating, and preserves app focus");
                Check(GetWindowDisplayAffinity(inkHandle,out var affinity) && affinity == 0x11,"Guidance ink is excluded from capture"); overlay.Clear();
                fixture.Left += 80; await Task.Delay(100); bool stale = false;
                try { await Task.Run(() => assistant.Apply(new("invoke", Ref:export!.Ref),snapshot,export,timeout.Token)); } catch (InvalidOperationException) { stale = true; }
                Check(stale && clicks == 1,"Moving the target window invalidates a pending action");
                using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); bool stopped = false;
                try { await Task.Run(() => assistant.Apply(new("type", Value:"Must not appear"),snapshot,field,cancelled.Token)); } catch (OperationCanceledException) { stopped = true; }
                Check(stopped && editor.Text == "Buddy test text","Cancelled actions leave the field unchanged");
                using var voice = new VoiceOverlayWindow(() => null, () => Task.FromResult<string?>(null), () => new(), _ => {}, () => window, assistant.Perception, (_,_) => {}, () => {});
                Check(voice.Title.Contains("voice overlay") && voice.Width == 360 && !voice.IsListening,"The dedicated voice surface initializes separately with the microphone off");
                Console.WriteLine($"ALL {count} NATIVE FIXTURE CHECKS PASSED (microphone and multi-monitor acceptance remain manual)"); exit = 0;
            } catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { ScreenPerception.PracticeHandle = IntPtr.Zero; fixture.Close(); app.Shutdown(); }
        };
        app.Run(fixture); return exit;
    }
    private static int SettingsNavigation()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; int exit = 1, checks = 0;
        void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); checks++; Console.WriteLine("PASS: " + message); }
        var home = new MainWindow(startService: false); home.ConfigureLaunch(LaunchDestination.Background);
        home.Loaded += async (_, _) => {
            try {
                home.Hide(); var channel = "Buddy.Settings.Fixture." + Guid.NewGuid();
                using var activation = new DesktopActivation(target => app.Dispatcher.InvokeAsync(() => home.OpenFromLaunch(target)).Task, channel);
                Check(await DesktopActivation.Redirect(LaunchDestination.Home, channel), "Running app acknowledges a Home launch");
                Check(home.IsVisible && home.WindowState == WindowState.Normal, "A repeat launch restores hidden Home");
                home.WindowState = WindowState.Minimized;
                Check(await DesktopActivation.Redirect(LaunchDestination.Settings, channel), "Running app acknowledges a Settings launch");
                Check(home.WindowState == WindowState.Normal && home.VisibleHomeSection == "Settings", "Settings restores the minimized Home and navigates directly");
                Check(home.OwnedWindows.Count == 0, "Settings is embedded in Home without a separate dialog");
                await DesktopActivation.Redirect(LaunchDestination.Settings, channel);
                Check(app.Windows.Cast<Window>().Count() == 1, "Repeated Settings activation reuses Home");
                foreach (var section in MainWindow.SettingsSections) {
                    home.OpenSettingsSection(section);
                    Check(home.VisibleHomeSection == "Settings" && home.VisibleSettingsSection == section, "Unified Settings reaches " + section);
                }
                home.Hide();
                home.OpenFromLaunch(LaunchDestination.Settings);
                Check(home.IsVisible && home.VisibleHomeSection == "Settings", "Settings reopens after Home was hidden");
                VisualChecks.Run(home, Check);
                Console.WriteLine($"ALL {checks} SETTINGS WINDOW CHECKS PASSED"); exit = 0;
            } catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { await home.Quit(); }
        };
        app.Run(home); return exit;
    }
    private static async Task<int> OcrChecks()
    {
        try {
            using var fixture = new System.Drawing.Bitmap(1000, 380);
            using (var g = System.Drawing.Graphics.FromImage(fixture)) using (var font = new System.Drawing.Font("Arial", 38)) {
                g.Clear(System.Drawing.Color.White); g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.DrawString("Export Project", font, System.Drawing.Brushes.Black, 50, 35);
                g.DrawString("Save Project", font, System.Drawing.Brushes.Black, 50, 130);
                g.DrawString("password: secret-test", font, System.Drawing.Brushes.Black, 50, 230);
            }
            using var buffer = new MemoryStream(); fixture.Save(buffer, System.Drawing.Imaging.ImageFormat.Png);
            using var frame = new CapturedWindow(buffer.ToArray(), new Rect(-1200, 100, 1000, 380), 1000, 380);
            var text = await LocalOcr.Read(frame, default);
            if (!text.Text.Any(t => t.Text.Contains("Export Project") && t.Confidence > .8)) throw new Exception("Bundled OCR did not recognize the fixture label.");
            Console.WriteLine("PASS: Bundled native OCR recognizes fixture text offline");
            if (text.Text.Any(t => t.Text.Contains("secret")) || text.PrivateBounds.Count != 1) throw new Exception("Sensitive OCR line was not separated for redaction.");
            Console.WriteLine("PASS: Sensitive OCR text is omitted from grounding evidence");
            if (!text.Text.All(t => t.Bounds.X < 0 && t.Bounds.Y > 100)) throw new Exception("OCR coordinates lost the negative monitor origin.");
            Console.WriteLine("PASS: OCR coordinates preserve physical bounds and negative monitor origins");
            using var masked = frame.Mask(text); using var png = new MemoryStream(masked.Image); using var bitmap = new System.Drawing.Bitmap(png);
            var secret = text.PrivateBounds[0]; var pixel = bitmap.GetPixel((int)(secret.X + secret.Width / 2 - frame.Bounds.X), (int)(secret.Y + secret.Height / 2 - frame.Bounds.Y));
            if (pixel.R != 0 || pixel.G != 0 || pixel.B != 0) throw new Exception("OCR region was not masked.");
            Console.WriteLine("PASS: OCR-detected private regions are blacked out before vision");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); bool stopped = false;
            try { await LocalOcr.Read(frame, cancelled.Token); } catch (OperationCanceledException) { stopped = true; }
            if (!stopped) throw new Exception("Cancelled OCR was dispatched.");
            Console.WriteLine("PASS: Cancelled OCR requests are not dispatched");
            Console.WriteLine("ALL 5 NATIVE OCR CHECKS PASSED; this synthetic fixture does not measure grounding acceptance"); return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
