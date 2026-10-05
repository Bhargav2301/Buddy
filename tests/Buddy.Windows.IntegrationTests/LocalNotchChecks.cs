using Buddy.Windows;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

// Compile in workers; execution belongs to the root's serialized owned-window acceptance slot.
internal static class LocalNotchChecks
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    private static IEnumerable<DependencyObject> Tree(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Tree(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static T Named<T>(DependencyObject root, string name) where T : DependencyObject => Tree(root).OfType<T>().Single(t => AutomationProperties.GetName(t) == name);
    private static async Task Layout(FrameworkElement element) { element.UpdateLayout(); await element.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render); await element.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void Invoke(CompanionIsland island, string method, LocalTaskToken token) => typeof(CompanionIsland).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(island, [token]);
    private static void Save(FrameworkElement element, string path)
    {
        var dpi = VisualTreeHelper.GetDpi(element);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) dc.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        bitmap.Render(visual); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(path); png.Save(output);
    }
    internal static int Run()
    {
        int count = 0, exit = 1; var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        string parent = Environment.GetEnvironmentVariable("BUDDY_LOCAL_NOTCH_EVIDENCE") ?? Path.GetTempPath();
        string folder = Path.Combine(parent, "Buddy-local-notch50-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        void Check(bool ok, string note) { if (!ok) throw new Exception("FAIL: " + note); count++; Console.WriteLine("PASS: " + note); }
        var editor = new TextBox { Text = "Owned original field. Nothing may change this.", Margin = new(24) };
        var source = new Window { Title = "Owned local notch fixture", Width = 540, Height = 300, Content = editor };
        source.Loaded += async (_, _) => {
            CompanionIsland? island = null;
            try {
                BuddyTheme.Apply("Black", true); source.Activate(); editor.Focus(); await Layout(source);
                var initialWindow = Native.GetForegroundWindow(); var initialFocus = Keyboard.FocusedElement;
                Check(initialWindow == new WindowInteropHelper(source).Handle, "Owned source window has focus before passive notch opens");
                var now = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
                var journal = new LocalTaskJournal(() => now);
                var running = journal.Begin("talk", "Owned answer", "Preparing response");
                now = now.AddSeconds(3); journal.Update(running, "Observed response stage", observedStep: true);
                int routeCalls = 0, openCalls = 0, dismissCalls = 0; bool failRead = false;
                var mood = CompanionMood.Thinking;
                island = new CompanionIsland(() => new("Owned active task", "Local status", mood), _ => routeCalls++,
                    localTasks: () => failRead ? throw new InvalidOperationException("owned failure") : journal.Snapshot,
                    openTaskSource: _ => { openCalls++; return false; }, dismissTask: token => { dismissCalls++; return journal.Dismiss(token); },
                    taskCoverage: "Home, typed chat, Buddy-draft refinement and app launches in this session");
                island.SetMode("Expanded"); await Layout(island);
                Check((GetWindowLongPtr(new WindowInteropHelper(island).Handle, -20).ToInt64() & 0x08000000) != 0 && !island.ShowActivated,
                    "Actual native task-card window retains no-activate style");
                Check(Named<TextBlock>(island, "Local task title").Text == "Owned answer" && Named<TextBlock>(island, "Local task state").Text == "Running", "Focused card reflects actual running journal record");
                Check(Named<TextBlock>(island, "Local task source").Text == "Source: Quick chat", "Card names the real local operation source");
                Check(Named<TextBlock>(island, "Local task coverage").Text.Contains("Buddy-draft refinement"), "Explicit coverage label distinguishes integrated local routes from missing integrations");
                string updated = Named<TextBlock>(island, "Local task updated").Text;
                Check(updated.Contains(now.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz")), "Updated timestamp is the recorded event time, not timer time");
                Check(Tree(island).OfType<TextBlock>().Any(t => AutomationProperties.GetName(t) == "Observed local task stage" && t.Text.Contains("Observed response stage")), "Observed journal stages appear in the card");
                Check(Native.GetForegroundWindow() == initialWindow && ReferenceEquals(Keyboard.FocusedElement, initialFocus), "Passive bar opening preserves original source focus");
                var sameTitleControl = Named<TextBlock>(island, "Local task title");
                now = now.AddHours(1); mood = CompanionMood.Idle; island.Refresh(); await Layout(island);
                Check(ReferenceEquals(sameTitleControl, Named<TextBlock>(island, "Local task title")) && Named<TextBlock>(island, "Local task updated").Text == updated && Named<TextBlock>(island, "Local task state").Text == "Running", "Mood, elapsed time and repeated refresh never rebuild/complete the recorded task");
                Check(!Tree(island).OfType<Button>().Any(b => AutomationProperties.GetName(b) == "Dismiss finished local task"), "Running task exposes no terminal Dismiss control");
                Invoke(island, "DismissTask", running); Check(dismissCalls == 0 && journal.Snapshot.Tasks.Any(t => t.Token == running), "Forced dismissal cannot bypass live terminal-state guard");
                journal.Update(running, "Review needed", LocalTaskPhase.WaitingForReview, observedStep: true); island.Refresh(); await Layout(island);
                Check(Named<TextBlock>(island, "Local task state").Text == "Review" && Equals(Named<Button>(island, "Open local task source").Content, "Open source to review"), "Review remains explicit and directs decisions to the original view");
                Click(Named<Button>(island, "Open local task source")); Check(openCalls == 1 && Named<TextBlock>(island, "Local task notice").Text.Contains("unavailable") && routeCalls == 0, "Unavailable source is reported without task replay or automatic approval");
                foreach (var phase in new[] { LocalTaskPhase.Completed, LocalTaskPhase.Failed, LocalTaskPhase.Cancelled }) {
                    var token = journal.Begin("fixture-" + phase.ToString().ToLowerInvariant(), "Owned " + phase, "Started"); journal.Finish(token, phase, "Observed " + phase);
                    island.Refresh(); await Layout(island);
                    Click(Named<Button>(island, $"Show task: Owned {phase}. {phase}")); await Layout(island);
                    Check(Named<TextBlock>(island, "Local task state").Text == phase.ToString(), "Exact recorded terminal phase shown: " + phase);
                    var button = Named<Button>(island, "Dismiss finished local task"); Click(button); await Layout(island);
                    Check(!journal.Snapshot.Tasks.Any(t => t.Token == token), "Explicit Dismiss removes only the selected terminal record: " + phase);
                    int before = openCalls; Invoke(island, "OpenTask", token); Check(openCalls == before, "Dismissed/stale task cannot reach source navigation callback");
                }
                failRead = true; island.Refresh(); Check(Named<TextBlock>(island, "Local task notice").Text.Contains("could not refresh"), "Refresh failure is visible without changing task outcome");
                failRead = false; island.Refresh(); Check(!Named<TextBlock>(island, "Local task notice").Text.Contains("could not refresh"), "Recovery clears stale refresh error even when journal revision stayed the same");
                Check(Tree(island).OfType<Button>().Any(b => AutomationProperties.GetName(b) == "Stop Buddy task"), "Global Stop remains available outside scrolling detail");
                Check(editor.Text == "Owned original field. Nothing may change this." && Native.GetForegroundWindow() == initialWindow && routeCalls == 0,
                    "Card selection, review, dismissal and refreshing preserve source text/focus and do not start tasks");
                var anchor = (OverlayNative.Point)typeof(CompanionIsland).GetField("monitorAnchor", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(island)!;
                var work = OverlayNative.WorkArea(anchor); double scale = OverlayNative.Scale(new WindowInteropHelper(island).Handle);
                Check(island.Mode == "Expanded" && island.IsVisible && island.ActualHeight <= work.Height / scale + 1, "Expanded cards remain persistent and bounded by the selected monitor work area");
                Save((FrameworkElement)island.Content, Path.Combine(folder,"local-task-review.png"));
                Click(Named<Button>(island, "Stop Buddy task")); Check(routeCalls == 1, "Explicit Stop invokes only the existing Stop route");
                int opened = openCalls, dismissed = dismissCalls; island.SetMode("Hidden");
                Invoke(island, "OpenTask", running); Invoke(island, "DismissTask", running);
                Check(openCalls == opened && dismissCalls == dismissed, "A stale control from a hidden bar cannot navigate or dismiss tasks");
                Console.WriteLine($"ALL {count} LOCAL NOTCH NATIVE CHECKS PASSED; owned fixtures and injected local journal only. Evidence: {folder}"); exit = 0;
            } catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { island?.Dispose(); source.Close(); app.Shutdown(); }
        };
        app.Run(source); return exit;
    }
}
