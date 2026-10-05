using Buddy.Windows;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

// Root-only owned native fixture: observes an owned window but launches no app,
// sends no input, and reads no external accessibility text or profile files.
internal static class ComputerUseChecks
{
    internal static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var window = new Window { Title = "Buddy owned metadata fixture", Width = 440, Height = 180, Content = new TextBlock { Text = "Owned test window; no external action is dispatched." } };
        int checks = 0, exit = 1;
        void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); checks++; Console.WriteLine("PASS: " + label); }
        window.Loaded += async (_, _) => {
            try {
                var hwnd = new WindowInteropHelper(window).Handle;
                window.Activate(); InputNative.SetForegroundWindow(hwnd); await Task.Delay(150);
                var backend = new WindowsRoutineAppBackend(() => true, () => "");
                var request = Guid.NewGuid(); var observation = await backend.ObserveAsync(request, default);
                Check(observation.RequestId == request && observation.Id != Guid.Empty && observation.Complete, "Production backend issues a bounded metadata observation for the request");
                Check(observation.Window.Window == hwnd && observation.Window.ProcessId == Environment.ProcessId && observation.Window.ThreadId != 0 && observation.Window.ProcessStarted > 0, "Production observation identifies the actual owned HWND/PID/thread/process creation");
                Check(Native.GetForegroundWindow() == hwnd, "Metadata observation does not change foreground focus");
                bool refused = false;
                try { await backend.VerifyAsync(observation, new(request, observation.Id, "notepad"), default); } catch (InvalidOperationException) { refused = true; }
                Check(refused, "A forged launch receipt cannot authorize production verification");
                using var stop = new CancellationTokenSource(); stop.Cancel();
                bool cancelled = false; try { await backend.CheckpointAsync(observation, "notepad", stop.Token); } catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled, "Cancelled checkpoint performs no app preparation or dispatch");
                bool blocked = false;
                try { await new WindowsRoutineAppBackend(() => true, () => observation.Window.App).ObserveAsync(Guid.NewGuid(), default); } catch (InvalidOperationException) { blocked = true; }
                Check(blocked, "Production observation honors the configured privacy blocklist");
                window.Hide(); await Task.Delay(80);
                var scope = WindowSelection.Capture(hwnd); scope.Validate();
                Check(scope.Window == hwnd, "Identity can be checked without treating a hidden window as verified foreground completion");
                Console.WriteLine($"COMPUTER USE OWNED NATIVE CHECKS PASSED: {checks}"); exit = 0;
            } catch (Exception error) { Console.Error.WriteLine(error); }
            finally { window.Close(); app.Shutdown(exit); }
        };
        app.Run(window); return exit;
    }
}
