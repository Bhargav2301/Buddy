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
                Console.WriteLine("PASS: Windows package integrity and assembly dependencies");
                return 0;
            }
            Diagnostics.Start();
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Diagnostics.Write("Unhandled exception", e.ExceptionObject as Exception);
            if (args.Contains("--install")) return Installer.Run(args.Contains("--quiet"), !args.Contains("--no-launch"));
            using var single = new Mutex(true, "Local\\Buddy.Desktop.v1", out bool first);
            if (!first)
            {
                ShowMessage("Buddy is already running. Use your configured shortcut for quick chat, or its tray icon for Buddy Home.");
                return 0;
            }
            PackageVerifier.Verify(AppContext.BaseDirectory);
            RunDesktop();
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
    private static void RunDesktop() => DesktopApplication.Run();

    internal static void ShowMessage(string message) => MessageBoxW(IntPtr.Zero, message, "Buddy", 0x00000040);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);
}
