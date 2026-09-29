using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Buddy.Windows;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
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
            Diagnostics.Start();
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Diagnostics.Write("Unhandled exception", e.ExceptionObject as Exception);
            if (args.Contains("--install")) return Installer.Run(args.Contains("--quiet"), !args.Contains("--no-launch"));
            using var single = new Mutex(true, "Local\\Buddy.Desktop.v1", out bool first);
            if (!first)
            {
                if (DesktopActivation.Redirect(DesktopLaunch.Parse(args)).GetAwaiter().GetResult()) return 0;
                throw new InvalidOperationException("The running Buddy did not respond. If it is an older version, quit it from the tray and install this update, then open Buddy again.");
            }
            PackageVerifier.Verify(AppContext.BaseDirectory);
            RunDesktop(DesktopLaunch.Parse(args));
            return 0;
        }
        catch (Exception ex)
        {
            Diagnostics.Write("Startup failed", ex);
            var message = "Buddy could not start.\n\n" + ex.Message + "\n\nError log: " + Diagnostics.LogPath;
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
