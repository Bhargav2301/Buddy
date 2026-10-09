using Buddy.Windows;

// Read-only installed identity checks: no Process.Start, app launch, foreground,
// microphone, camera activation, screenshot, or private application content.
internal static class InstalledAppResolutionChecks
{
    internal static async Task<int> Run()
    {
        try {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var comet = InstalledAppResolver.CometStartInfo();
            if (comet.UseShellExecute || comet.Arguments.Length != 0 || comet.ArgumentList.Count != 0) throw new Exception("Comet launcher was not argument-free.");
            using (WindowsRoutineAppBackend.ExecutableLease.Acquire(comet.FileName, deadline.Token)) {
                if (InstalledAppResolver.ResolveComet() != comet.FileName) throw new Exception("Comet identity changed while pinned.");
            }
            Console.WriteLine("PASS: Comet fixed-path publisher validation and executable lease; no launch.");
            int unavailable = 0;
            foreach (string alias in new[] { "camera", "spotify" }) {
                try {
                    var app = await InstalledAppResolver.ResolveAdditionalAsync(alias, deadline.Token).WaitAsync(deadline.Token);
                    using var lease = app.IsPackaged ? null : WindowsRoutineAppBackend.ExecutableLease.Acquire(app.Executable, deadline.Token);
                    var fresh = await InstalledAppResolver.ResolveAdditionalAsync(alias, deadline.Token).WaitAsync(deadline.Token);
                    if (!fresh.SameIdentity(app)) throw new Exception("Installed identity changed during repeated verification.");
                    if (app.IsPackaged && (app.Registration is null || app.PackageRegistration is null || app.MainExecutable.Length == 0 || app.Start is not null)) throw new Exception("Incomplete package identity.");
                    Console.WriteLine("AVAILABLE: " + alias + (app.IsPackaged ? " signed OS registration with fixed main executable" : " fixed installed executable with lease") + " passed repeated identity checks; no launch.");
                } catch (InvalidOperationException ex) { unavailable++; Console.WriteLine("UNAVAILABLE: " + alias + " refused by resolver: " + ex.Message); }
            }
            Console.WriteLine("RESOLUTION ONLY: no desktop-launch or action-completion acceptance."); return unavailable == 0 ? 0 : 1;
        } catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex.GetType().Name + ": " + ex.Message); return 1; }
    }
}
