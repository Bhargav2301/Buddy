using Buddy.Windows;
using System.Windows;
using System.Windows.Threading;

internal static class PresenceChecks
{
    [STAThread]
    internal static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        int count = 0, result = 1;
        void Check(bool value, string text) { if (!value) throw new Exception("FAIL: " + text); count++; Console.WriteLine("PASS: " + text); }
        var name = "Local\\Buddy.Presence.Fixture." + Guid.NewGuid();
        var channel = "Buddy.Presence.Fixture." + Guid.NewGuid();
        using var ready = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
        var older = new Thread(() => { using var gate = new Mutex(true, name); ready.Set(); release.Wait(); gate.ReleaseMutex(); });
        older.Start(); ready.Wait();
        app.Dispatcher.BeginInvoke(new Action(async () => {
            DesktopPresence? presence = null;
            try {
                bool visible = true; int activations = 0, opens = 0;
                presence = new DesktopPresence(() => { activations++; return new DesktopActivation(_ => { opens++; return Task.CompletedTask; }, channel); }, enabled => visible = enabled, name, false);
                Check(!visible && !presence.OwnsPresence && activations == 0, "An older Buddy owner suppresses the preview companion without closing that app");
                presence.Poll(); Check(!visible && activations == 0, "Repeated checks do not create a competing activation listener");
                release.Set(); older.Join(); presence.Poll();
                Check(visible && presence.OwnsPresence && activations == 1, "After the older owner exits, the preview acquires companion ownership automatically");
                presence.Poll(); Check(activations == 1, "The preview creates only one companion activation listener");
                Check(await DesktopActivation.Redirect(LaunchDestination.Settings, channel) && opens == 1, "An installed-style launch redirects to the running preview's Settings");
                bool refused = false;
                var contender = new Thread(() => { using var candidate = new Mutex(true, name, out bool first); refused = !first && !candidate.WaitOne(0); });
                contender.Start(); contender.Join();
                Check(refused, "A competing legacy-style launch cannot become a second desktop owner");
                presence.Dispose(); presence = null;
                bool reacquired = false;
                var afterExit = new Thread(() => { using var candidate = new Mutex(false, name); reacquired = candidate.WaitOne(0); if (reacquired) candidate.ReleaseMutex(); });
                afterExit.Start(); afterExit.Join();
                Check(reacquired, "Quitting the preview releases normal Buddy ownership");
                Check(DesktopActivation.InstalledChannelName != "" && !DesktopActivation.InstalledChannelName.EndsWith(".Preview"), "Installed activation alias retains the normal namespace");
                Console.WriteLine($"ALL {count} COMPANION OWNERSHIP CHECKS PASSED"); result = 0;
            } catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { release.Set(); older.Join(); presence?.Dispose(); app.Shutdown(); }
        }));
        app.Run(); return result;
    }
}
