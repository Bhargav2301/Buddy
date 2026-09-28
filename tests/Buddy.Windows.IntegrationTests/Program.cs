using Buddy.Server;
using Buddy.Windows;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;

internal static class Program
{
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);
    [STAThread] private static int Main()
    {
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
}
