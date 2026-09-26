using System.Runtime.InteropServices;

namespace Buddy.Windows;

internal static class Diagnostics
{
    private static readonly object Gate = new();
    internal static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Buddy", "Logs", "startup.log");

    internal static void Start()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1024 * 1024)
                File.Move(LogPath, LogPath + ".previous", true);
        }
        catch { /* Logging must never prevent startup. */ }
        Write("Buddy 0.1.0 Windows cursor companion 1 | " + RuntimeInformation.OSDescription +
            " | " + RuntimeInformation.ProcessArchitecture + " | .NET " + Environment.Version +
            " | " + AppContext.BaseDirectory);
    }

    internal static void Write(string message, Exception? exception = null)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, DateTimeOffset.Now.ToString("O") + " " + message +
                    (exception is null ? "" : Environment.NewLine + exception) + Environment.NewLine);
            }
        }
        catch { /* Disk errors must not mask the original failure. */ }
    }
}
