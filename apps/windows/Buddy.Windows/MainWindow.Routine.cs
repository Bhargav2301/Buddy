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
        var activity = localTasks.Begin("routine", RoutineLaunchReport.Title(alias), "Checking the exact supported app request.");
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
            var report = RoutineLaunchReport.Result(alias, result);
            bool verified = report.Code == "APP_VERIFIED";
            localTasks.Finish(activity, verified ? LocalTaskPhase.Completed : LocalTaskPhase.Failed, report.Detail, observedStep: true);
            routineStatus = report.Detail; status.Text = routineStatus;
            Diagnostics.Write("App launch " + alias + ": " + report.Detail);
            companionState.Set("routine", verified ? CompanionMood.Idle : CompanionMood.Unsure);
            indicator.Show(routineStatus);
            // Only a verified result is represented as success. No relaunch on failure.
            if (host is not null) await host.Service.Audit("explicit-app-open", alias,
                verified ? "Verified requested application window" : result.ActionDispatched ? "Launch dispatched; requested window not verified" : "Nothing launched");
            await Task.Delay(1800, request.Token);
        } catch (OperationCanceledException) {
            if (!completed) {
                var report = RoutineLaunchReport.Failure(alias, new OperationCanceledException());
                localTasks.Finish(activity, LocalTaskPhase.Cancelled, report.Detail, observedStep: true);
                Diagnostics.Write("App launch " + alias + ": " + report.Detail);
            }
            if (!completed && desktopActivity == "routine") {
                routineStatus = "Stopped. An already dispatched app launch cannot be undone; no further action will run.";
                status.Text = routineStatus;
            }
        } catch (Exception ex) {
            var report = RoutineLaunchReport.Failure(alias, ex);
            if (!completed) {
                localTasks.Finish(activity, LocalTaskPhase.Failed, report.Detail, observedStep: true);
                Diagnostics.Write("App launch " + alias + ": " + report.Detail);
            }
            if (desktopActivity == "routine") {
                routineStatus = report.Detail; status.Text = routineStatus;
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
