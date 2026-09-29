using System.Windows;

namespace Buddy.Windows;

internal static class DesktopApplication
{
    internal static void Run(LaunchDestination destination)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) =>
        {
            Diagnostics.Write("Desktop error", e.Exception);
            Program.ShowMessage("Buddy encountered an error. Your saved conversations are retained.\n" +
                e.Exception.Message + "\n\nError log: " + Diagnostics.LogPath);
            e.Handled = true;
        };
        var window = new MainWindow(); window.ConfigureLaunch(destination);
        using var activation = new DesktopActivation(target => app.Dispatcher.InvokeAsync(() => window.OpenFromLaunch(target)).Task,
            report: error => Diagnostics.Write("Desktop activation failed", error));
        if (destination == LaunchDestination.Settings) window.Loaded += (_, _) => window.OpenFromLaunch(destination);
        app.Run(window);
    }
}
