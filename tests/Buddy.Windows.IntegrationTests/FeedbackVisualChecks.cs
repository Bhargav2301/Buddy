using Buddy.Server;
using Buddy.Windows;
using Microsoft.AspNetCore.DataProtection;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

// Owned WPF controls only; integration owner serializes this entrypoint. No installed profile, model or external UI.
internal static class FeedbackVisualChecks
{
    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;
    private static IEnumerable<DependencyObject> Tree(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Tree(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static async Task Layout(FrameworkElement item)
    {
        item.UpdateLayout(); await item.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
        await item.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    }
    private static double Contrast(SolidColorBrush a, SolidColorBrush b)
    {
        static double Channel(byte c) { double x = c / 255d; return x <= .04045 ? x / 12.92 : Math.Pow((x + .055) / 1.055, 2.4); }
        static double L(Color c) => .2126 * Channel(c.R) + .7152 * Channel(c.G) + .0722 * Channel(c.B);
        double x = L(a.Color), y = L(b.Color); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }
    private static void Save(FrameworkElement root, string path)
    {
        root.UpdateLayout(); var dpi = VisualTreeHelper.GetDpi(root);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        bitmap.Render(visual); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); png.Save(file);
    }
    internal static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; int count = 0, exit = 1;
        string parent = Environment.GetEnvironmentVariable("BUDDY_FEEDBACK_EVIDENCE") ?? Path.GetTempPath();
        string folder = Path.Combine(parent, "Buddy-feedback47-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        void Check(bool pass, string message) { if (!pass) throw new Exception("FAIL: " + message); count++; Console.WriteLine("PASS: " + message); }
        var host = new Window { Title = "Owned Buddy feedback fixture", Width = 980, Height = 760 };
        host.Loaded += async (_, _) => {
            CompanionIsland? island = null; CursorCompanionWindow? cursor = null; QuickChatWindow? quick = null;
            try {
                BuddyTheme.Apply("Black", true); host.Background = BuddyTheme.Canvas;
                Check(BuddyTheme.IsBlack && (SystemParameters.HighContrast || BuddyTheme.Canvas.Color.R == BuddyTheme.Canvas.Color.G && BuddyTheme.Surface.Color.G == BuddyTheme.Surface.Color.B), "Black uses neutral opaque base surfaces");
                foreach (var surface in new[] { BuddyTheme.Canvas, BuddyTheme.Surface, BuddyTheme.Input, BuddyTheme.Raised, BuddyTheme.Hover }) {
                    Check(surface.Color.A == 255 && Contrast(BuddyTheme.Ink, surface) >= 7 && Contrast(BuddyTheme.Muted, surface) >= 4.5, "Primary and muted text remain readable on black surfaces");
                }
                Check(Contrast(BuddyTheme.DisabledText, BuddyTheme.DisabledSurface) >= 4.5 && Contrast(BuddyTheme.FocusRing, BuddyTheme.FocusGap) >= 3, "Disabled text and focus ring/gap retain contrast without fading");
                foreach (double scale in new[] { 1d, 1.25, 1.5, 2d }) {
                    var group = new StackPanel { Width = 360, LayoutTransform = new ScaleTransform(scale, scale), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
                    var line = new TextBox { Text = "Explain this area", Height = 76, FontSize = 14, Padding = new(12), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
                    var multiline = new TextBox { Text = "Explain this area\nKeep the second line readable", Height = 96, FontSize = 14, Padding = new(10,8,10,8), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                    group.Children.Add(line); group.Children.Add(multiline);
                    var face = new CompanionFace(); var button = new Button { Content = face, Width = CompanionPresentation.FaceButtonSize(false), Height = CompanionPresentation.FaceButtonSize(false), Padding = new(0), BorderThickness = new(0), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
                    group.Children.Add(button); host.Content = group; await Layout(host);
                    foreach (var box in new[] { line, multiline }) {
                        var contentHost = (ScrollViewer)box.Template.FindName("PART_ContentHost", box);
                        var presenter = Tree(contentHost).OfType<ScrollContentPresenter>().First();
                        var bounds = presenter.TransformToAncestor(box).TransformBounds(new Rect(presenter.RenderSize));
                        Check(contentHost.Margin == new Thickness(0), "Text padding is applied once, outside margin is not duplicated");
                        foreach (int index in new[] { 0, box.Text.Length - 1 }) {
                            var character = box.GetRectFromCharacterIndex(index);
                            Check(!character.IsEmpty && character.Top >= bounds.Top - 1 && character.Bottom <= bounds.Bottom + 1, $"Full first/last glyph height fits the actual text viewport at {scale * 100}% layout scale");
                        }
                    }
                    var faceBounds = face.TransformToAncestor(button).TransformBounds(new Rect(face.RenderSize));
                    Check(faceBounds.Left >= 6 && faceBounds.Top >= 6 && faceBounds.Right <= button.ActualWidth - 6 && faceBounds.Bottom <= button.ActualHeight - 6,
                        $"Complete mascot has room inside button focus chrome at {scale * 100}% layout scale");
                    var rig = Tree(face).OfType<FacialRig>().Single(); var inkBounds = VisualTreeHelper.GetDescendantBounds(rig);
                    Check(inkBounds.Left >= -.1 && inkBounds.Top >= -.1 && inkBounds.Right <= rig.ActualWidth + .1 && inkBounds.Bottom <= rig.ActualHeight + .1, "Original Buddy body drawing stays inside the rig bounds");
                    Save(group, Path.Combine(folder, $"text-face-scale-{scale * 100:0}.png"));
                }
                cursor = new CursorCompanionWindow(() => true); cursor.Show(); await Layout(cursor);
                var faceButton = Field<Button>(cursor, "menuButton");
                Check(faceButton.ActualWidth >= 88 && cursor.Height >= faceButton.ActualHeight + 24, "Production cursor companion reserves full face and badge height");
                Save((FrameworkElement)cursor.Content, Path.Combine(folder, "cursor-full-face.png")); cursor.Hide();

                bool otherInstance = false; string savedMode = "Hidden"; var routes = new List<string>();
                var current = new IslandActivity("Owned task", "Waiting for your review", CompanionMood.Thinking, "Local brain unavailable");
                island = new CompanionIsland(() => current, routes.Add, () => otherInstance, value => savedMode = value);
                island.SetMode("Expanded", true); await Layout(island);
                Check(savedMode == "Expanded" && island.IsVisible && island.VisibilityReason == IslandVisibility.Visible, "Expanded mode persists explicitly and remains visible");
                island.Refresh(); island.Refresh(); Check(island.Mode == "Expanded" && routes.Count == 0, "Refresh does not collapse the bar or start a task/microphone");
                Check(Tree(island).OfType<CompanionFace>().Any() && Tree(island).OfType<TextBlock>().Any(t => t.Text.Contains("Local brain unavailable")), "Expanded bar uses Buddy mood art and actual local availability");
                Check(Tree(island).OfType<Button>().Any(b => Equals(b.Content, "Stop")), "Stop remains reachable in expanded header");
                Save((FrameworkElement)island.Content, Path.Combine(folder, "bar-expanded.png"));
                otherInstance = true; island.Refresh(); Check(!island.IsVisible && island.VisibilityReason == IslandVisibility.OtherInstance && island.Mode == "Expanded", "Duplicate-instance suppression retains selected mode");
                otherInstance = false; island.Refresh(); Check(island.IsVisible && island.Mode == "Expanded", "Bar recovers automatically when ownership returns");
                island.SetMode("Compact", true); Check(savedMode == "Compact" && island.IsVisible, "Explicit collapse persists compact state");
                island.SetMode("Hidden", true); island.Refresh(); Check(savedMode == "Hidden" && !island.IsVisible, "Explicit hiding persists and refresh respects it");

                var first = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
                var second = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously); int conversationCalls = 0;
                using var handler = new NoTransport(); using var client = new HttpClient(handler) { BaseAddress = new("http://127.0.0.1:11434") };
                var store = new StateStore(Path.Combine(folder,"data"), DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(folder,"keys"))));
                var service = new BuddyService(store, new(client));
                quick = new QuickChatWindow(() => service, () => ++conversationCalls == 1 ? first.Task : second.Task, () => new(), _ => { }, () => { }, () => { }, () => { }, (_,_) => { }, _ => Task.FromResult<ScreenSnapshot?>(null));
                quick.Open("owned-conversation"); await Layout(quick);
                var draft = Field<TextBox>(quick, "draft"); var answer = Field<TextBox>(quick, "answer");
                draft.Text = "Explain this area"; answer.Text = "Owned retained answer"; quick.Dismiss(); quick.Open("owned-conversation");
                Check(draft.Text == "Explain this area" && answer.Text == "Owned retained answer", "Reopening the same quick conversation retains draft and reviewed answer");
                host.Activate(); await Layout(host); Check(quick.IsVisible, "Quick chat stays open after focus moves to another owned window");
                Task Send() => (Task)typeof(QuickChatWindow).GetMethod("Send", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(quick, null)!;
                var old = Send(); Check(quick.IsBusy, "A pending conversation request marks quick chat busy"); quick.Cancel();
                Check(!quick.IsBusy && !draft.IsReadOnly && draft.Text == "Explain this area", "Stop restores editable draft without waiting for a delayed operation");
                draft.Text = "Newer owned draft"; var newer = Send(); first.SetResult("late-old"); await old;
                Check(quick.IsBusy && draft.Text == "Newer owned draft", "Late old completion cannot clear a newer request or overwrite its draft");
                quick.Cancel(); second.SetResult("late-new"); await newer;
                Check(!quick.IsBusy && draft.Text == "Newer owned draft" && handler.Calls == 0, "Stopped requests never reach transport and preserve current draft");
                await Layout(quick); Save((FrameworkElement)quick.Content, Path.Combine(folder,"quick-chat-retained.png"));
                quick.Height = 300; await Layout(quick); Field<ScrollViewer>(quick, "bodyScroll").ScrollToEnd(); await Layout(quick);
                var send = Field<Button>(quick, "send"); var sendBounds = send.TransformToAncestor(quick).TransformBounds(new Rect(send.RenderSize));
                Check(sendBounds.Top >= 0 && sendBounds.Bottom <= quick.ActualHeight && Field<ScrollViewer>(quick, "bodyScroll").ScrollableHeight > 0,
                    "Short quick-chat pane scrolls to the complete Send/footer without clipping it");
                Save((FrameworkElement)quick.Content, Path.Combine(folder,"quick-chat-short-footer.png"));
                Console.WriteLine($"ALL {count} FEEDBACK VISUAL CHECKS PASSED; owned WPF only. Layout scaling is not a multi-monitor DPI acceptance claim. Evidence: {folder}"); exit = 0;
            } catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { quick?.Dispose(); cursor?.Dispose(); island?.Dispose(); host.Close(); app.Shutdown(); }
        };
        app.Run(host); return exit;
    }
    private sealed class NoTransport : HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { Calls++; throw new InvalidOperationException("No transport is permitted in this fixture."); }
    }
}
