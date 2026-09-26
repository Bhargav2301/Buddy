using System.Windows;

namespace Buddy.Windows;

internal static class DesktopApplication
{
    internal static void Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.DispatcherUnhandledException += (_, e) =>
        {
            Diagnostics.Write("Desktop error", e.Exception);
            Program.ShowMessage("Buddy encountered an error. Your saved conversations are retained.\n" +
                e.Exception.Message + "\n\nError log: " + Diagnostics.LogPath);
            e.Handled = true;
        };
        app.Run(new MainWindow());
    }
}
