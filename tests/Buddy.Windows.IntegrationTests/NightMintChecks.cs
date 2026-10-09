using Buddy.Server;
using Buddy.Windows;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

// Owned native controls only. Root serializes execution; this never loads an installed profile or calls a model.
internal static class NightMintChecks
{
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint x, uint y, uint data, nuint extra);
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        yield return parent;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    private static T Part<T>(Control control, string name) where T : DependencyObject => (T)control.Template.FindName(name, control);
    private static Color ColorOf(Brush brush) => ((SolidColorBrush)brush).Color;
    private static double Contrast(Color a, Color b)
    {
        static double Channel(byte x) { double v = x / 255d; return v <= .04045 ? v / 12.92 : Math.Pow((v + .055) / 1.055, 2.4); }
        static double L(Color c) => .2126 * Channel(c.R) + .7152 * Channel(c.G) + .0722 * Channel(c.B);
        double x = L(a), y = L(b); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }
    private static RenderTargetBitmap Render(FrameworkElement element, bool nativeLayout = false)
    {
        element.UpdateLayout(); var dpi = VisualTreeHelper.GetDpi(element);
        var image = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX)), Math.Max(1, (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY)), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        // VisualBrush maps this connected, laid-out native visual into its own bounds, including popup roots.
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen()) {
            // A content-root render must include its actual window's opaque surface,
            // which lives above this visual and is otherwise absent from the bitmap.
            dc.DrawRectangle(Window.GetWindow(element)?.Background ?? BuddyTheme.Surface, null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
            var bounds = new Rect(0, 0, element.ActualWidth, element.ActualHeight);
            if (!nativeLayout) dc.DrawRectangle(new VisualBrush(element), null, bounds);
        }
        image.Render(drawing); if (nativeLayout) image.Render(element); return image;
    }
    private static Color Pixel(RenderTargetBitmap image, double x, double y)
    {
        int px = Math.Clamp((int)(x * image.DpiX / 96), 0, image.PixelWidth - 1), py = Math.Clamp((int)(y * image.DpiY / 96), 0, image.PixelHeight - 1);
        byte[] bytes = new byte[4]; image.CopyPixels(new Int32Rect(px, py, 1, 1), bytes, 4, 0); return Color.FromArgb(bytes[3], bytes[2], bytes[1], bytes[0]);
    }
    private static async Task RenderTurn(FrameworkElement root)
    {
        root.UpdateLayout(); await root.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        await root.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }
    internal static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        BuddyTheme.Apply("Night Mint", true);
        string parent = Environment.GetEnvironmentVariable("BUDDY_NIGHTMINT_EVIDENCE") ?? Path.GetTempPath();
        string evidence = Path.Combine(Path.GetFullPath(parent), "Buddy-nightmint44-" + Guid.NewGuid()); Directory.CreateDirectory(evidence);
        int count = 0, exit = 1; var receipts = new List<object>(); var images = new List<string>();
        GetCursorPos(out var originalPointer); bool pointerPressed = false;
        void Check(bool ok, string note) { if (!ok) throw new Exception("FAIL: " + note); count++; Console.WriteLine("PASS: " + note); }
        void Save(FrameworkElement visual, string name)
        {
            // Render entire owned windows directly to preserve their native layout and empty space.
            // Cropped control/popup probes use the visual brush without changing the control itself.
            var window = Window.GetWindow(visual); bool wholeWindow = window?.Content == visual;
            if (wholeWindow) visual = window!;
            var file = Path.Combine(evidence, name + ".png"); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Render(visual, wholeWindow))); using var stream = File.Create(file); encoder.Save(stream); images.Add(file);
        }
        void Pair(Brush foreground, Brush background, double minimum, string note)
        {
            var fg = ColorOf(foreground); var bg = ColorOf(background); double ratio = Contrast(fg, bg);
            receipts.Add(new { name = note, foreground = fg.ToString(), background = bg.ToString(), ratio, minimum });
            Check(fg.A == 255 && bg.A == 255 && ratio >= minimum, $"{note}: opaque native role contrast {ratio:F2}:1 >= {minimum:F1}:1");
        }
        void Surface(Control control, string name, Brush expected, string note)
        {
            var frame = Part<Border>(control, name); var sample = Pixel(Render(frame), frame.ActualWidth - 10, frame.ActualHeight / 2);
            Check(ColorOf(frame.Background) == ColorOf(expected) && sample == ColorOf(expected) && frame.Opacity == 1 && control.Opacity == 1, note + " uses its opaque rendered surface");
        }
        var panel = new StackPanel { Margin = new(20) };
        var heading = new DockPanel(); var mascot = AppBranding.Image(36); DockPanel.SetDock(mascot, Dock.Left); heading.Children.Add(mascot);
        heading.Children.Add(new TextBlock { Text = "Buddy · Night Mint native states", FontSize = 24, Margin = new(10, 0, 0, 12), Foreground = BuddyTheme.Ink }); panel.Children.Add(heading);
        var normal = BuddyTheme.Button("Ordinary action", () => { });
        var primary = BuddyTheme.Button("Primary action", () => { }, true);
        var disabled = BuddyTheme.Button("Disabled action", () => throw new Exception("Disabled action must not activate"), true); disabled.IsEnabled = false;
        var row = new WrapPanel(); row.Children.Add(normal); row.Children.Add(primary); row.Children.Add(disabled); panel.Children.Add(row);
        var input = new TextBox { Text = "Please draft a short note.", Margin = new(0, 6, 0, 6) }; panel.Children.Add(input);
        var unavailable = new TextBox { Text = "Unavailable input remains readable", IsEnabled = false, Margin = new(0, 0, 0, 6) }; panel.Children.Add(unavailable);
        var invalid = new TextBox { Margin = new(0, 0, 0, 6) }; invalid.SetBinding(TextBox.TextProperty, new Binding(nameof(TextSource.Text)) { Source = new TextSource() }); panel.Children.Add(invalid);
        Validation.MarkInvalid(invalid.GetBindingExpression(TextBox.TextProperty)!, new ValidationError(new ExceptionValidationRule(), invalid.GetBindingExpression(TextBox.TextProperty)!, "Choose a supported value", null));
        panel.Children.Add(new TextBlock { Text = "! Choose a supported value", Foreground = BuddyTheme.Risk, Background = BuddyTheme.RiskSoft });
        var choice = new ComboBox { ItemsSource = new[] { "Night Mint", "Light", "Dark", "System" }, SelectedIndex = 0, Margin = new(0, 6, 0, 6) }; panel.Children.Add(choice);
        var checkbox = new CheckBox { Content = "Checked, with a visible check mark", IsChecked = true }; panel.Children.Add(checkbox);
        var disabledCheck = new CheckBox { Content = "Disabled checkbox remains readable", IsChecked = true, IsEnabled = false }; panel.Children.Add(disabledCheck);
        var diff = new TextBlock { FontSize = 17, Margin = new(0, 8, 0, 8), Foreground = BuddyTheme.Ink }; diff.Inlines.Add(new Run("Refine: ")); diff.Inlines.Add(BuddyTheme.DiffRun("original words", false)); diff.Inlines.Add(new Run("  ")); diff.Inlines.Add(BuddyTheme.DiffRun("clearer words", true)); panel.Children.Add(diff);
        panel.Children.Add(new TextBlock { Text = "✓ Ready on this PC", Foreground = BuddyTheme.Positive, Background = BuddyTheme.Soft, Padding = new(8) });
        panel.Children.Add(new TextBlock { Text = "! Review before applying", Foreground = BuddyTheme.Warn, Background = BuddyTheme.WarningSurface, Padding = new(8) });
        panel.Children.Add(new TextBlock { Text = "Secondary information", Foreground = BuddyTheme.Secondary });
        panel.Children.Add(new TextBlock { Text = "Muted supporting text", Foreground = BuddyTheme.Muted });
        panel.Children.Add(new TextBlock { Text = "Placeholder role preview", Foreground = BuddyTheme.Placeholder, Background = BuddyTheme.Input });
        var window = new Window { Title = "Buddy Night Mint owned fixture", Width = 760, Height = 820, Background = BuddyTheme.Surface, Foreground = BuddyTheme.Ink, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        window.Loaded += async (_, _) => {
            InlinePromptWindow? inline = null; MainWindow? home = null; ContextMenu? menu = null;
            try {
                await RenderTurn(window); window.Activate(); var handle = new WindowInteropHelper(window).Handle; InputNative.SetForegroundWindow(handle);
                var parking = heading.PointToScreen(new Point(heading.ActualWidth - 10, 10));
                Check(Native.GetForegroundWindow() == handle && SetCursorPos((int)parking.X, (int)parking.Y), "Pointer probes are confined to the foreground owned fixture"); await RenderTurn(window);
                Check(!window.AllowsTransparency && ColorOf(window.Background).A == 255 && window.Foreground == BuddyTheme.Ink, "Native theme root has an explicit opaque panel and primary foreground");
                Check(ReferenceEquals(mascot.Source, AppBranding.Character), "Original Buddy artwork remains the rendered companion image");
                Check(new DesktopPreferences().Appearance == "Night Mint", "New preferences select Night Mint by default");
                if (!SystemParameters.HighContrast) Check(ColorOf(BuddyTheme.Surface).ToString() == "#FF172B23" && ColorOf(BuddyTheme.Ink).ToString() == "#FFF4FCF6" && ColorOf(BuddyTheme.Accent).ToString() == "#FF285B45", "Native panel, text and action roles match the approved tokens exactly");
                foreach (var bg in new[] { BuddyTheme.Canvas, BuddyTheme.Surface, BuddyTheme.Raised, BuddyTheme.Input, BuddyTheme.Hover, BuddyTheme.Pressed }) Pair(BuddyTheme.Ink, bg, 4.5, "Primary text on " + ColorOf(bg));
                foreach (var fg in new[] { BuddyTheme.Secondary, BuddyTheme.Muted, BuddyTheme.Placeholder }) Pair(fg, BuddyTheme.Surface, 4.5, "Supporting text " + ColorOf(fg));
                Pair(BuddyTheme.Deep, BuddyTheme.Surface, 4.5, "Functional mint/text"); Pair(BuddyTheme.DisabledText, BuddyTheme.DisabledSurface, 4.5, "Disabled text");
                foreach (var bg in new[] { BuddyTheme.Accent, BuddyTheme.ActionHover, BuddyTheme.ActionPressed }) Pair(BuddyTheme.OnAccent, bg, 4.5, "Action text on " + ColorOf(bg));
                Pair(BuddyTheme.ControlBorder, BuddyTheme.Surface, 3, "Essential edge"); Pair(BuddyTheme.ControlBorder, BuddyTheme.Pressed, 3, "Essential pressed edge"); Pair(BuddyTheme.FocusRing, BuddyTheme.FocusGap, 3, "Focus ring and gap");
                Pair(BuddyTheme.Positive, BuddyTheme.Soft, 4.5, "Positive label"); Pair(BuddyTheme.Warn, BuddyTheme.WarningSurface, 4.5, "Warning label"); Pair(BuddyTheme.Risk, BuddyTheme.RiskSoft, 4.5, "Error label");
                Pair(BuddyTheme.Added, BuddyTheme.AddedSurface, 4.5, "Diff insertion"); Pair(BuddyTheme.Removed, BuddyTheme.RemovedSurface, 4.5, "Diff deletion"); Pair(BuddyTheme.SelectionText, BuddyTheme.SelectionSurface, 4.5, "Selected option/text");
                Surface(normal, "frame", BuddyTheme.Surface, "Default button"); Surface(primary, "frame", BuddyTheme.Accent, "Primary button"); Surface(disabled, "frame", BuddyTheme.DisabledSurface, "Disabled button");
                Check(TextElement.GetForeground(Part<ContentPresenter>(disabled, "content")) == BuddyTheme.DisabledText, "Disabled primary text uses its own readable role without opacity");
                Surface(input, "frame", BuddyTheme.Input, "Input"); Surface(unavailable, "frame", BuddyTheme.DisabledSurface, "Disabled input"); Surface(invalid, "frame", BuddyTheme.RiskSoft, "Invalid input");
                Check(Part<Border>(invalid, "frame").BorderBrush == BuddyTheme.ErrorBorder && Validation.GetHasError(invalid), "Invalid input exposes validation state and explicit error border");
                Check(unavailable.Foreground == BuddyTheme.DisabledText && disabledCheck.Opacity == 1 && TextElement.GetForeground(Part<ContentPresenter>(disabledCheck, "content")) == BuddyTheme.DisabledText, "Disabled input and checkbox remain fully opaque and readable");
                Check(Part<System.Windows.Shapes.Path>(checkbox, "check").Visibility == Visibility.Visible, "Checked selection preserves its non-color check mark");
                Save((FrameworkElement)window.Content, "native-states");

                var buttonPoint = primary.PointToScreen(new Point(primary.ActualWidth / 2, primary.ActualHeight / 2));
                Check(Native.GetForegroundWindow() == handle && SetCursorPos((int)buttonPoint.X, (int)buttonPoint.Y), "Hover probe targets the owned primary button"); await RenderTurn(window);
                Check(primary.IsMouseOver, "Native pointer movement enters the primary action"); Surface(primary, "frame", BuddyTheme.ActionHover, "Hovered primary button"); Save(primary, "native-hovered-button");
                Check(Native.GetForegroundWindow() == handle, "Owned fixture retains foreground before the bounded press probe");
                try { mouse_event(0x0002, 0, 0, 0, 0); pointerPressed = true; await RenderTurn(window); Check(primary.IsPressed, "Native press enters the action's pressed state"); Surface(primary, "frame", BuddyTheme.ActionPressed, "Pressed primary button"); Save(primary, "native-pressed-button"); }
                finally { mouse_event(0x0004, 0, 0, 0, 0); pointerPressed = false; }
                SetCursorPos((int)parking.X, (int)parking.Y); await RenderTurn(window);

                primary.Focus(); await RenderTurn(window);
                var ring = Part<Border>(primary, "focusRing"); var gap = Part<Border>(primary, "focusGap");
                Check(primary.IsKeyboardFocused && ring.BorderThickness == new Thickness(3) && gap.BorderThickness == new Thickness(3) && ring.BorderBrush == BuddyTheme.FocusRing && gap.BorderBrush == BuddyTheme.FocusGap, "Actual keyboard focus renders a 3-DIP mint ring and separate 3-DIP dark gap");
                var focused = Render(primary); Check(Pixel(focused, primary.ActualWidth / 2, 1.5) == ColorOf(BuddyTheme.FocusRing) && Pixel(focused, primary.ActualWidth / 2, 4.5) == ColorOf(BuddyTheme.FocusGap), "Native focused-button pixels contain both the opaque ring and separating gap"); Save(primary, "native-focused-button");
                Check(Native.GetForegroundWindow() == handle, "Owned fixture alone receives the keyboard navigation probe"); InputNative.Keys("Tab", default); await RenderTurn(window);
                Check(input.IsKeyboardFocused && !disabled.IsKeyboardFocused, "Actual Tab navigation skips the disabled control and reaches the input"); input.Select(0, 6); await RenderTurn(window);
                Check(input.SelectionBrush == BuddyTheme.SelectionSurface && input.SelectionTextBrush == BuddyTheme.SelectionText && input.SelectionOpacity == 1, "Native selected input has explicit opaque foreground and selection fill"); Save(input, "native-selected-input");

                choice.Focus(); choice.IsDropDownOpen = true; await RenderTurn(window);
                var popup = Part<Popup>(choice, "PART_Popup"); var popupSurface = Part<Border>(choice, "popupSurface");
                Check(popup.IsOpen && PresentationSource.FromVisual(popupSurface) is not null && popupSurface.Background == BuddyTheme.Surface && TextElement.GetForeground(popupSurface) == BuddyTheme.Ink, "Actual native dropdown popup explicitly binds opaque Night Mint content outside the owner tree");
                var selected = (ComboBoxItem)choice.ItemContainerGenerator.ContainerFromIndex(0); var option = (ComboBoxItem)choice.ItemContainerGenerator.ContainerFromIndex(1);
                Surface(selected, "frame", BuddyTheme.SelectionSurface, "Selected dropdown option"); Surface(option, "frame", BuddyTheme.Surface, "Unselected dropdown option");
                Check(TextElement.GetForeground(Part<ContentPresenter>(selected, "content")) == BuddyTheme.SelectionText, "Selected dropdown text uses the dark-on-mint selection role");
                Save(popupSurface, "native-dropdown");
                Check(Native.GetForegroundWindow() == handle, "Owned fixture alone receives dropdown keyboard navigation"); InputNative.Keys("Down", default); await RenderTurn(window);
                var highlighted = choice.Items.Cast<object>().Select((_, i) => (ComboBoxItem)choice.ItemContainerGenerator.ContainerFromIndex(i)).Single(i => i.IsHighlighted);
                Check(!ReferenceEquals(highlighted, selected) && Part<Border>(highlighted, "focusRing").BorderBrush == BuddyTheme.FocusRing && Part<Border>(highlighted, "focusGap").BorderBrush == BuddyTheme.FocusGap,
                    "ArrowDown highlights the next actual dropdown option with a bright ring and dark separating gap");
                Save(popupSurface, "native-dropdown-keyboard-focus"); InputNative.Keys("Escape", default); await RenderTurn(window); Check(!choice.IsDropDownOpen, "Escape closes the native dropdown after keyboard navigation");

                menu = new ContextMenu { PlacementTarget = normal }; var menuItem = new MenuItem { Header = "Core action", IsCheckable = true, IsChecked = true }; var menuDisabled = new MenuItem { Header = "Unavailable action", IsEnabled = false }; var submenu = new MenuItem { Header = "Optional tools" }; submenu.Items.Add(new MenuItem { Header = "Refine source field" }); menu.Items.Add(menuItem); menu.Items.Add(menuDisabled); menu.Items.Add(submenu); menu.IsOpen = true; await RenderTurn(menu);
                Check(menu.Background == BuddyTheme.Surface && menu.Foreground == BuddyTheme.Ink && PresentationSource.FromVisual(menu) is not null, "Context menu popup has an opaque native theme root");
                Check(menuDisabled.Foreground == BuddyTheme.DisabledText && Part<TextBlock>(menuItem, "check").Visibility == Visibility.Visible, "Menu disabled and checked states preserve text and check symbol"); Save(menu, "native-menu");
                submenu.IsSubmenuOpen = true; await RenderTurn(menu); var subPopup = Part<Popup>(submenu, "PART_Popup");
                Check(subPopup.IsOpen && subPopup.Child is Border b && b.Background == BuddyTheme.Surface && TextElement.GetForeground(b) == BuddyTheme.Ink, "Nested native menu popup retains explicit readable roles"); Save((FrameworkElement)subPopup.Child, "native-submenu"); submenu.IsSubmenuOpen = false; menu.IsOpen = false;

                var field = new FixtureField(input); var corner = input.PointToScreen(new(0, 0)); var dpi = VisualTreeHelper.GetDpi(input); var draft = new FocusedDraft(new GuardedEdit(field, input.Text), "owned fixture", "Theme input", new(handle, "fixture", new(corner.X, corner.Y, input.ActualWidth * dpi.DpiScaleX, input.ActualHeight * dpi.DpiScaleY)));
                inline = new InlinePromptWindow(null!, new FocusedFieldEditor(new ScreenPerception(() => new DesktopPreferences())), draft, () => { }); inline.Show();
                typeof(InlinePromptWindow).GetMethod("ShowProposal", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(inline, ["Please draft a clear invitation."]); await RenderTurn(inline);
                var actualDiff = (TextBlock)typeof(InlinePromptWindow).GetField("diff", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(inline)!; var runs = actualDiff.Inlines.OfType<Run>().ToArray();
                Check(runs.Length == 4 && runs[1].TextDecorations.Contains(TextDecorations.Strikethrough[0]) && runs[2].TextDecorations.Contains(TextDecorations.Underline[0]) && runs[1].Foreground == BuddyTheme.Removed && runs[2].Foreground == BuddyTheme.Added, "Actual Refine proposal renders deletion strike-through and insertion underline with separate readable roles");
                Check(field.Writes == 0 && input.Text == draft.Edit.Original && inline.Foreground == BuddyTheme.Ink && inline.Background == BuddyTheme.Surface, "Native Refine rendering preserves the original field and an opaque explicit theme root"); Save((FrameworkElement)inline.Content, "native-refine-diff"); inline.Close(); inline = null;

                string prefPath = Path.Combine(evidence, "fixture-desktop.json"); var saved = new DesktopPreferences { Appearance = "Night Mint", ReduceMotion = true, NeuralSpeakerId = 85, AdditionalSettings = new() { ["FutureFixtureSetting"] = JsonSerializer.SerializeToElement("preserve") } };
                home = new MainWindow(false, saved, next => { saved = next; next.Save(prefPath); }); home.Show(); home.OpenSettingsSection("General"); await RenderTurn(home);
                var theme = Descendants(home).OfType<ComboBox>().Single(c => c.Items.Cast<object>().Contains("Night Mint"));
                Check(Equals(theme.SelectedItem, "Night Mint") && theme.Items.Count == 4, "Actual General settings select Night Mint and retain System, Light and Dark choices");
                Descendants(home).OfType<Button>().Single(b => Equals(b.Content, "Save changes")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                var loaded = DesktopPreferences.Load(prefPath); Check(loaded.Appearance == "Night Mint" && loaded.NeuralSpeakerId == 85 && loaded.AdditionalSettings!["FutureFixtureSetting"].GetString() == "preserve", "Theme persistence preserves selected voice and extension preferences in an isolated file");
                Save((FrameworkElement)home.Content, "native-home-general");
                foreach (var label in new[] { "Hide Home - keep Buddy running", "Exit Buddy (stops companion)" }) {
                    var footerButton = Descendants(home).OfType<Button>().Single(b => Equals(b.Content, label));
                    var footerLabel = Descendants(footerButton).OfType<TextBlock>().Single(t => t.Text == label);
                    Check(footerLabel.TextWrapping == TextWrapping.Wrap && footerLabel.ActualHeight > footerLabel.FontSize, "Home lifetime control keeps its complete meaning visible: " + label);
                }
                using (var guideSurface = new DesktopAssistant(() => null, () => saved, () => IntPtr.Zero, _ => { })) {
                    await guideSurface.Open("guide", ""); var guideWindow = (Window)typeof(DesktopAssistant).GetField("panel", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(guideSurface)!; await RenderTurn(guideWindow);
                    Save((FrameworkElement)guideWindow.Content, "native-guide"); Check(guideWindow.Foreground == BuddyTheme.Ink && guideWindow.Background == BuddyTheme.Surface, "Actual Guide panel has explicit Night Mint text and an opaque content surface");
                    using var voiceSurface = new VoiceOverlayWindow(() => null, () => Task.FromResult<string?>(null), () => saved, _ => { }, () => IntPtr.Zero, guideSurface.Perception, (_, _) => { }, () => { });
                    voiceSurface.Show(); await RenderTurn(voiceSurface); Save((FrameworkElement)voiceSurface.Content, "native-voice");
                    Check(!voiceSurface.IsListening && ((Border)voiceSurface.Content).Background == BuddyTheme.Surface && Descendants(voiceSurface).OfType<Button>().Any(b => Equals(b.Content, "Stop") && b.IsVisible), "Actual Voice overlay keeps its opaque content and visible Stop without opening the microphone");
                }
                var brush = BuddyTheme.Ink; BuddyTheme.Apply("Light", true); await RenderTurn(home); Check(ReferenceEquals(brush, BuddyTheme.Ink) && home.Foreground == BuddyTheme.Ink, "Existing open native surfaces update shared theme brushes without rebuilding state");
                BuddyTheme.Apply("Night Mint", true); Check(!BuddyTheme.Animate, "Night Mint retains reduced-motion policy");
                Check(typeof(MainWindow).GetField("host", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(home) is null, "Theme/settings rendering never starts service, network routing or account activation");
                Console.WriteLine($"ALL {count} NIGHT MINT NATIVE CHECKS PASSED; WPF control rendering and owned popup windows only. High contrast OS state: {SystemParameters.HighContrast}. No wallpaper, physical audio, installed-profile or browser acceptance claimed."); exit = 0;
            } catch (Exception ex) { Console.Error.WriteLine(ex); receipts.Add(new { failure = ex.ToString() }); }
            finally {
                if (pointerPressed) mouse_event(0x0004, 0, 0, 0, 0); SetCursorPos(originalPointer.X, originalPointer.Y);
                if (menu is not null) menu.IsOpen = false; inline?.Close();
                if (home is not null) { typeof(MainWindow).GetField("shuttingDown", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(home, true); home.Close(); }
                File.WriteAllText(Path.Combine(evidence, "native-theme-results.json"), JsonSerializer.Serialize(new { exitCode = exit, checks = count, highContrast = SystemParameters.HighContrast, scope = "Connected owned WPF windows rendered through RenderTargetBitmap; no desktop/wallpaper capture", receipts, images }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine("NIGHT MINT EVIDENCE: " + evidence); window.Close(); app.Shutdown();
            }
        };
        app.Run(window); return exit;
    }
    private sealed class TextSource { public string Text { get; set; } = "Unsupported value"; }
    private sealed class FixtureField(TextBox input) : IVerifiedTextField
    {
        public string Identity => "night-mint-owned-field"; internal int Writes;
        public string Read() => input.Text;
        public void Write(string expected, string value, CancellationToken ct) { ct.ThrowIfCancellationRequested(); if (input.Text != expected) throw new InvalidOperationException("Field changed"); Writes++; input.Text = value; }
    }
}
