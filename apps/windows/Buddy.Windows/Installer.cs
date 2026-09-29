using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Buddy.Windows;

internal static class Installer
{
    internal static int Run(bool quiet = false, bool launch = true, bool rollback = false)
    {
        var source = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        var target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Buddy");
        PackageVerifier.Verify(source);
        using var single = new Mutex(true, "Local\\Buddy.Desktop.v1", out bool first);
        if (!first)
            throw new InvalidOperationException("Quit Buddy using its tray icon, then run Install-Buddy.cmd again.");
        var alreadyInstalled = string.Equals(source, Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase);
        if (rollback && alreadyInstalled) throw new InvalidOperationException("Quit Buddy, then run Rollback-Buddy.cmd from the extracted Windows package folder.");
        var transaction = new InstallationTransaction(Path.GetDirectoryName(target)!, folder => { PackageVerifier.Verify(folder); Probe(folder); }, Probe);
        if (rollback) transaction.Rollback(Probe);
        else if (!alreadyInstalled) transaction.Install(source);
        else { PackageVerifier.Verify(target); Probe(target); }
        var shortcutWarning = "";
        try
        {
            CreateShortcut(Environment.GetFolderPath(Environment.SpecialFolder.Programs), target);
            CreateShortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), target);
            CreateShortcut(Environment.GetFolderPath(Environment.SpecialFolder.Programs), target, "Buddy Settings", "--settings");
            CreateShortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), target, "Buddy Settings", "--settings");
        }
        catch (Exception ex)
        {
            Diagnostics.Write("Shortcut creation failed; application files are installed", ex);
            shortcutWarning = "\nShortcuts could not be created. Open Buddy.exe from that folder.";
        }
        Diagnostics.Write("Installed to " + target);
        if (!quiet) Program.ShowMessage((rollback ? "Buddy's previous version was restored in:\n" : "Buddy is installed in:\n") + target + "\n\nYour local data and model choices are preserved.\nClick OK to open Buddy." + shortcutWarning);
        single.ReleaseMutex();
        single.Dispose();
        if (launch) Process.Start(new ProcessStartInfo(Path.Combine(target, "Buddy.exe")) { WorkingDirectory = target, UseShellExecute = true });
        return 0;
    }
    private static void Probe(string folder)
    {
        using var process = Process.Start(new ProcessStartInfo(Path.Combine(folder, "Buddy.exe"), "--check-package") { WorkingDirectory = folder, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true })
            ?? throw new InvalidOperationException("Could not start the package dependency check.");
        var error = process.StandardError.ReadToEndAsync(); var output = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(30000)) { process.Kill(); throw new InvalidOperationException("The package dependency check timed out. Your previous installation is unchanged."); }
        if (process.ExitCode != 0) {
            var details = error.GetAwaiter().GetResult(); if (string.IsNullOrWhiteSpace(details)) details = output.GetAwaiter().GetResult();
            throw new InvalidOperationException("The package dependency check failed. Your previous installation is unchanged.\n\n" +
                details[..Math.Min(details.Length, 1600)] + "\nIf the Microsoft Visual C++ x64 Runtime is missing, run Install-Prerequisites.cmd from the extracted preview, then retry.");
        }
    }

    private static void CreateShortcut(string folder, string target, string name = "Buddy", string arguments = "--home")
    {
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", throwOnError: true)!)!;
        object? shortcut = null;
        try
        {
            shortcut = shell.CreateShortcut(Path.Combine(folder, name + ".lnk"));
            dynamic link = shortcut;
            link.TargetPath = Path.Combine(target, "Buddy.exe");
            link.WorkingDirectory = target;
            link.Arguments = arguments;
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
