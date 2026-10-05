using Buddy.Server;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Buddy.Windows;

public sealed partial class MainWindow
{
    private readonly Func<string, CancellationToken, Task<ComputerUseResult>>? runRoutine;
    private CancellationTokenSource? routineRequest;
    private string routineTask = "", routineStatus = "";

    private async Task OpenRoutineApp(string query, string alias)
    {
        // A second route cannot start another launch while one is being verified.
        if (routineRequest is not null || runRoutine is null) return;
        PrepareDesktopActivity("routine");
        quick?.Dismiss(); voiceOverlay?.Dismiss();
        using var request = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        routineRequest = request;
        var activity = localTasks.Begin("routine", "Open requested app", "Checking the exact supported app request.");
        routineTask = query; routineStatus = "Opening " + alias + ".";
        status.Text = routineStatus; stop.IsEnabled = true;
        companionState.Set("routine", CompanionMood.AgentWorking);
        using var indicator = new RoutineIndicator();
        bool completed = false;
        var escape = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        escape.Tick += (_, _) => { if ((InputNative.GetAsyncKeyState(27) & 0x8000) != 0) request.Cancel(); };
        try {
            indicator.Show(routineStatus); escape.Start();
            var result = await runRoutine(query, request.Token);
            request.Token.ThrowIfCancellationRequested();
            completed = true;
            localTasks.Finish(activity, result.Verified ? LocalTaskPhase.Completed : LocalTaskPhase.Failed,
                result.Verified ? "Requested app window verified." : result.ActionDispatched ? "Launch dispatched; requested window not verified. No retry." : "No launch completed; review the result.", observedStep: true);
            routineStatus = result.Message; status.Text = routineStatus;
            companionState.Set("routine", result.Verified ? CompanionMood.Idle : CompanionMood.Unsure);
            indicator.Show(routineStatus);
            // Only a verified result is represented as success. No relaunch on failure.
            if (host is not null) await host.Service.Audit("explicit-app-open", alias,
                result.Verified ? "Verified requested application window" : result.ActionDispatched ? "Launch dispatched; requested window not verified" : "Nothing launched");
            await Task.Delay(1800, request.Token);
        } catch (OperationCanceledException) {
            if (!completed) localTasks.Finish(activity, LocalTaskPhase.Cancelled, "Stopped. An already dispatched launch cannot be undone.");
            if (!completed && desktopActivity == "routine") {
                routineStatus = "Stopped. An already dispatched app launch cannot be undone; no further action will run.";
                status.Text = routineStatus;
            }
        } catch (Exception ex) {
            if (!completed) localTasks.Finish(activity, LocalTaskPhase.Failed, "App opening failed; review the result before retrying.");
            if (desktopActivity == "routine") {
                routineStatus = ex.Message; status.Text = routineStatus;
                companionState.Set("routine", CompanionMood.Unsure);
            }
        } finally {
            escape.Stop();
            if (ReferenceEquals(routineRequest, request)) routineRequest = null;
            stop.IsEnabled = busy || homeSpeaking || mainMicrophone is not null;
            if (desktopActivity == "routine") desktopActivity = "";
            companionState.Set("routine", CompanionMood.Idle);
        }
    }

    private sealed class RoutineIndicator : IDisposable
    {
        private readonly ExecutionBanner window = new();
        internal void Show(string message)
        {
            if (window.Content is TextBlock text) text.Text = message + "  Esc or Ctrl+Alt+Esc to stop";
            window.Open();
        }
        public void Dispose() => window.Close();
    }
}
