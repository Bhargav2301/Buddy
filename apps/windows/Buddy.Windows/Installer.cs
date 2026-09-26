using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Buddy.Windows;

internal static class Installer
{
    internal static int Run()
    {
        var source = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Buddy");
        PackageVerifier.Verify(source);
        using var single = new Mutex(true, "Local\\Buddy.Desktop.v1", out bool first);
        if (!first)
            throw new InvalidOperationException("Quit Buddy using its tray icon, then run Install-Buddy.cmd again.");
        var alreadyInstalled = string.Equals(source, Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase);
        if (!alreadyInstalled)
        {
            Directory.CreateDirectory(target);
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var destination = Path.Combine(target, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(file, destination, true);
            }
        }
        PackageVerifier.Verify(target);
        var shortcutWarning = "";
        try
        {
            CreateShortcut(Environment.GetFolderPath(Environment.SpecialFolder.Programs), target);
            CreateShortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), target);
        }
        catch (Exception ex)
        {
            Diagnostics.Write("Shortcut creation failed; application files are installed", ex);
            shortcutWarning = "\nShortcuts could not be created. Open Buddy.exe from that folder.";
        }
        Diagnostics.Write("Installed to " + target);
        Program.ShowMessage("Buddy is installed in:\n" + target + "\n\nClick OK to open Buddy." + shortcutWarning);
        single.ReleaseMutex();
        single.Dispose();
        Process.Start(new ProcessStartInfo(Path.Combine(target, "Buddy.exe")) { WorkingDirectory = target, UseShellExecute = true });
        return 0;
    }

    private static void CreateShortcut(string folder, string target)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)!)!;
        object? shortcut = null;
        try
        {
            shortcut = shell.CreateShortcut(Path.Combine(folder, "Buddy.lnk"));
            dynamic link = shortcut;
            link.TargetPath = Path.Combine(target, "Buddy.exe");
            link.WorkingDirectory = target;
            link.Description = "Buddy - your local AI companion";
            link.Save();
        }
        finally
        {
            if (shortcut is not null) Marshal.FinalReleaseComObject(shortcut);
            Marshal.FinalReleaseComObject(shell);
        }
    }
}
