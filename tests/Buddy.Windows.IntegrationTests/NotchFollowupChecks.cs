using Buddy.Windows;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

// COMPILE ONLY in specialists. Run only in root's coordinated owned-window slot.
internal static class NotchFollowupChecks
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr handle, int index);
    private static IEnumerable<DependencyObject> Tree(DependencyObject item)
    {
        yield return item;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(item); i++) foreach (var child in Tree(VisualTreeHelper.GetChild(item, i))) yield return child;
    }
    private static T Named<T>(DependencyObject root, string name) where T : DependencyObject => Tree(root).OfType<T>().Single(t => AutomationProperties.GetName(t) == name);
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static bool Passive(Window window) => (GetWindowLongPtr(new WindowInteropHelper(window).Handle, -20).ToInt64() & 0x08000000) != 0;
    private static async Task Layout(FrameworkElement element) { element.UpdateLayout(); await element.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render); await element.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); }
    internal static int Run()
    {
        int count = 0, exit = 1; var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Buddy-notch53-owned-native-" + Guid.NewGuid().ToString("N")); System.IO.Directory.CreateDirectory(folder);
        var field = new TextBox { Text = "Owned source must remain unchanged.", Margin = new(24) };
        var source = new Window { Title = "Owned notch53 source", Width = 520, Height = 320, Content = field };
        void Check(bool value, string note) { if (!value) throw new Exception("FAIL: " + note); count++; Console.WriteLine("PASS: " + note); }
        source.Loaded += async (_, _) => {
            CompanionIsland? island = null; NotchChatSession? chat = null;
            try {
                BuddyTheme.Apply("Black", true); source.Activate(); field.Focus(); await Layout(source);
                IntPtr initial = Native.GetForegroundWindow(); var pending = new TaskCompletionSource<NotchChatReply>(TaskCreationOptions.RunContinuationsAsynchronously);
                int calls = 0, stops = 0; CancellationToken token = default;
                chat = new(() => [new("local", "Owned local model"), new("offline", "Unavailable local model", false, "Not installed")], (_, ct) => { calls++; token = ct; return pending.Task; });
                var notes = new NotchNoteStore(System.IO.Path.Combine(folder, "note.json"));
                var journal = new LocalTaskJournal(); var running = journal.Begin("talk", "Owned task", "Preparing"); journal.Update(running, "Observed stage", observedStep: true);
                island = new(() => new("Owned task", "Owned status", CompanionMood.Idle), action => { if (action == "Stop") stops++; }, localTasks: () => journal.Snapshot, workspace: new(chat, notes));
                island.SetMode("Expanded"); await Layout(island);
                Check(Passive(island) && Native.GetForegroundWindow() == initial && ReferenceEquals(Keyboard.FocusedElement, field), "Idle embedded workspace retains passive no-activate source focus");
                Check(Named<TextBox>(island, "Notch chat message").IsReadOnly && Named<TextBox>(island, "Local note").IsReadOnly, "Editors start read-only until explicit Edit");
                Check(Named<TextBlock>(island, "Observed local task ticker").Text == "Running: Observed stage", "Native ticker binds actual journal stage");
                Click(Named<Button>(island, "Edit local note and chat")); await Layout(island);
                Check(!Passive(island) && !Named<TextBox>(island, "Notch chat message").IsReadOnly && Native.GetForegroundWindow() == new WindowInteropHelper(island).Handle, "Explicit Edit alone enables activation and actual typing surface");
                var note = Named<TextBox>(island, "Local note"); note.Text = "An explicitly saved owned note."; Click(Named<Button>(island, "Save note"));
                Check(notes.Load().Text == note.Text, "Native Save note button writes exact explicit editor content");
                var draft = Named<TextBox>(island, "Notch chat message"); draft.Text = "Owned pending message"; Click(Named<Button>(island, "Send local message")); await Layout(island);
                Check(calls == 1 && chat.Snapshot.Busy && draft.IsReadOnly, "Native embedded Send reaches injected operation once and locks pending draft");
                Click(Named<Button>(island, "Stop Buddy task")); await Layout(island);
                Check(stops == 1 && token.IsCancellationRequested && !chat.Snapshot.Busy && draft.Text == "Owned pending message", "Header Stop cancels embedded request and existing root route while keeping draft");
                pending.SetResult(new("Late output")); await Layout(island);
                Check(chat.Snapshot.Messages.Count == 0 && !Named<TextBox>(island, "Notch chat history").Text.Contains("Late output"), "Stopped native surface ignores late callback output");
                Click(Named<Button>(island, "Edit local note and chat")); await Layout(island);
                Check(Passive(island) && draft.IsReadOnly, "Done editing restores no-activate native style");
                source.Activate(); field.Focus(); island.SetMode("Compact");
                island.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseEnterEvent }); await Layout(island);
                Check(island.Mode == "Compact" && Native.GetForegroundWindow() == initial, "Hover expands without saved mode or source focus change");
                Click(Named<Button>(island, "Pin Buddy bar open for this session"));
                island.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = UIElement.MouseLeaveEvent }); await Layout(island);
                Check(Named<Button>(island, "Unpin Buddy bar").IsVisible && island.Mode == "Compact", "Pin retains visible details after hover leaves without persisting a different mode");
                Click(Named<Button>(island, "Unpin Buddy bar")); await Layout(island);
                Check(!Named<TextBox>(island, "Local note").IsVisible && island.IsVisible, "Unpin returns to compact bar without hiding it");
                island.SetMode("Expanded"); await Layout(island);
                Check(Named<TextBox>(island, "Local note").Text == "An explicitly saved owned note.", "Collapsing and reopening retains the local editor text");
                Check(field.Text == "Owned source must remain unchanged.", "Embedded interaction never edits the original external source");
                var stop = Named<Button>(island, "Stop Buddy task"); Check(stop.IsVisible && stop.ActualHeight >= 40, "Stop stays visible above scrollable editor and task history");
                island.SetMode("Hidden"); Check(!island.IsVisible && Passive(island), "Explicit Hide restores passive style and remains hidden");
                Check(!island.OpenChatSession("stale-session") && !island.IsVisible && calls == 1, "Stale session cannot reopen hidden workspace or replay chat");
                Check(island.OpenChatSession(chat.Snapshot.SessionId) && calls == 1 && draft.Text == "Owned pending message", "Exact current session source jump reopens existing content without replay");
                Console.WriteLine($"ALL {count} NOTCH53 NATIVE CHECKS PASSED; owned windows, files and injected chat only; no actual model/third-party input/audio acceptance."); exit = 0;
            } catch (Exception error) { Console.Error.WriteLine(error); }
            finally { island?.Dispose(); chat?.Dispose(); source.Close(); System.IO.Directory.Delete(folder, true); app.Shutdown(); }
        };
        app.Run(source); return exit;
    }
}
