using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Buddy.Windows;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if(args.Length==1&&args[0]=="--whisper-worker")return WhisperWorker.Run().GetAwaiter().GetResult();
        if (args.Length > 0 && args[0] == "--delete-local-data-after") return LocalDataDeletion.Run(args);
        PreviewEnvironment.Configure(args);
        if (PreviewEnvironment.Enabled && args.Any(a => a is "--install" or "--rollback" or "--delete-local-data-after")) return 1;
        bool logErrors = false;
        // No desktop types here: catch load failures before WPF is JIT-compiled.
        try
        {
            if (args.Contains("--check-package"))
            {
                PackageVerifier.Verify(AppContext.BaseDirectory);
                ProbeOcrDependencies();
                Console.WriteLine("PASS: Windows package integrity, assembly dependencies and native OCR");
                return 0;
            }
            if (args.Contains("--install")) return Installer.Run(args.Contains("--quiet"), !args.Contains("--no-launch"));
            if (args.Contains("--check-voice")) { LocalPackageChecks.Voice().GetAwaiter().GetResult(); return 0; }
            if(args.Length>=2&&args[0]=="--check-whisper-model"){LocalPackageChecks.Whisper(args[1]).GetAwaiter().GetResult();return 0;}
            if(args.Length>=2&&args[0]=="--check-whisper-model"){LocalPackageChecks.Whisper(args[1]).GetAwaiter().GetResult();return 0;}
            if (args.Length>=3&&args[0]=="--compare-saved-data") { LocalPackageChecks.CompareSavedData(args[1],args[2]);return 0; }
            if (args.Contains("--check-branding")) { LocalPackageChecks.Branding(); return 0; }
            if (args.Contains("--inspect-running-branding")) { BrandingDiagnostics.InspectRunning(); return 0; }
            if (args.Contains("--rollback")) return Installer.Run(args.Contains("--quiet"), !args.Contains("--no-launch"), rollback: true);
            using var single = new Mutex(true, "Local\\Buddy.Desktop.v1" + PreviewEnvironment.Suffix, out bool first);
            if (!first)
            {
                if (DesktopActivation.Redirect(DesktopLaunch.Parse(args)).GetAwaiter().GetResult()) return 0;
                throw new InvalidOperationException("The running Buddy did not respond. If it is an older version, quit it from the tray and install this update, then open Buddy again.");
            }
            // Only the process holding the data-owning mutex may create diagnostics.
            // A second launch must not recreate data during post-exit deletion.
            logErrors = true; Diagnostics.Start();
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Diagnostics.Write("Unhandled exception", e.ExceptionObject as Exception);
            PackageVerifier.Verify(AppContext.BaseDirectory);
            RunDesktop(DesktopLaunch.Parse(args));
            return 0;
        }
        catch (Exception ex)
        {
            if (logErrors) Diagnostics.Write("Startup failed", ex);
            var message = "Buddy could not start.\n\n" + ex.Message + (logErrors ? "\n\nError log: " + Diagnostics.LogPath : "");
            if (OperatingSystem.IsWindows() && !args.Contains("--quiet") && !args.Contains("--check-package")) ShowMessage(message);
            else Console.Error.WriteLine(message);
            return 1;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunDesktop(LaunchDestination destination) => DesktopApplication.Run(destination);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ProbeOcrDependencies() => LocalOcr.ProbeDependencies();

    internal static void ShowMessage(string message) => MessageBoxW(IntPtr.Zero, message, "Buddy", 0x00000040);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);
}
