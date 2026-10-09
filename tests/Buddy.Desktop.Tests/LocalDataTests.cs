using Buddy.Windows;
using System.Text.Json;

internal static class LocalDataTests
{
    internal static void Run(Action<bool, string> check)
    {
        string fixture = Path.Combine(Path.GetTempPath(), "Buddy.Data.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        try {
            string preferences = Path.Combine(fixture, "preferences.json");
            File.WriteAllText(preferences, """{"Shortcut":"Ctrl + Alt + Space","AllowWebResearch":true,"AgentEnabled":true,"FutureOption":{"enabled":true}}""");
            var migrated = DesktopPreferences.Load(preferences);
            check(migrated.OnboardingCompleted && migrated.SchemaVersion == 2 && migrated.Shortcut == "Ctrl + Alt + Space" && migrated.AllowWebResearch && migrated.AgentEnabled,
                "Upgrade preserves bindings and opt-ins and does not repeat first-run onboarding");
            (migrated with { Appearance = "Dark" }).Save(preferences);
            using (var saved = JsonDocument.Parse(File.ReadAllText(preferences)))
                check(saved.RootElement.GetProperty("FutureOption").GetProperty("enabled").GetBoolean(), "Saving preferences preserves unknown extension data");
            File.WriteAllText(preferences, """{"SchemaVersion":999,"Shortcut":"Keep this"}""");
            bool rejected = false; try { new DesktopPreferences().Save(preferences); } catch (InvalidDataException) { rejected = true; }
            check(rejected && File.ReadAllText(preferences).Contains("Keep this"), "Newer preferences cannot be overwritten by this version");
            File.WriteAllText(preferences, "{broken");
            rejected = false; try { new DesktopPreferences().Save(preferences); } catch (JsonException) { rejected = true; }
            check(rejected && File.ReadAllText(preferences) == "{broken", "Corrupt preferences remain available for recovery");
            check(!DesktopPreferences.Load(Path.Combine(fixture, "missing.json")).OnboardingCompleted, "Only a new profile requires onboarding");

            string data = Path.Combine(fixture, "Buddy"); Directory.CreateDirectory(Path.Combine(data, "keys"));
            File.WriteAllText(Path.Combine(data, "keys", "secret"), "fixture"); File.WriteAllText(Path.Combine(data, "state.encrypted"), "fixture");
            string sibling = Path.Combine(fixture, "Buddy-models"); Directory.CreateDirectory(sibling); File.WriteAllText(Path.Combine(sibling, "keep"), "keep");
            LocalDataTree.Delete(fixture);
            check(!Directory.Exists(data) && File.ReadAllText(Path.Combine(sibling, "keep")) == "keep" && File.Exists(preferences),
                "Local deletion removes only the fixed Buddy subtree and leaves models and sibling files intact");
            LocalDataTree.Delete(fixture); check(true, "Deleting an already absent profile is safe");
            // A Windows junction is a reparse point and must be rejected before any deletion.
            if (OperatingSystem.IsWindows()) {
                Directory.CreateDirectory(data); File.WriteAllText(Path.Combine(data, "first"), "keep");
                var command = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
                command.ArgumentList.Add("/c"); command.ArgumentList.Add("mklink"); command.ArgumentList.Add("/J");
                command.ArgumentList.Add(Path.Combine(data, "link")); command.ArgumentList.Add(sibling);
                using var process = System.Diagnostics.Process.Start(command)!; process.WaitForExit();
                if (process.ExitCode != 0) throw new Exception("Cannot create isolated reparse-point test fixture.");
                rejected = false; try { LocalDataTree.Delete(fixture); } catch (InvalidOperationException) { rejected = true; }
                check(rejected && File.Exists(Path.Combine(data, "first")) && File.Exists(Path.Combine(sibling, "keep")), "Link detection rejects the whole deletion before removing any data");
                Directory.Delete(Path.Combine(data, "link")); // Remove only the test junction, never its target.
            }
        } finally {
            string resolved = Path.GetFullPath(fixture);
            if (Path.GetDirectoryName(resolved) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar) && Path.GetFileName(resolved).StartsWith("Buddy.Data.Tests.", StringComparison.Ordinal))
                Directory.Delete(resolved, true);
        }
    }
}
