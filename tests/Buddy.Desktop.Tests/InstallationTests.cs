using Buddy.Windows;
using System.Text.Json;

internal static class InstallationTests
{
    internal static void Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "Buddy.Install.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try {
            var programs = Path.Combine(root, "Programs"); var source = Path.Combine(root, "Package");
            Package(source, "new");
            int probes = 0;
            void Verify(string path) { probes++; if (Version(path) == "broken") throw new InvalidDataException("Broken fixture dependency"); }
            var install = new InstallationTransaction(programs, Verify);
            install.Install(source);
            check(Version(install.Target) == "new" && probes == 2, "Clean installation verifies source and staged files before promotion");
            check(!File.Exists(Path.Combine(programs, ".Buddy-update.json")), "Completed installation leaves no recovery journal");
            Refuses(() => install.Rollback(Verify), "First installation reports no available rollback", check);
            var data = Path.Combine(root, "Buddy"); Directory.CreateDirectory(data); File.WriteAllText(Path.Combine(data, "state.enc"), "private encrypted bytes");
            Package(source, "upgrade"); install.Install(source);
            check(Version(install.Target) == "upgrade" && Directory.GetDirectories(programs, ".Buddy-backup-*").Any(p => Version(p) == "new"), "Upgrade retains the complete previous installation");
            install.Rollback(Verify);
            check(Version(install.Target) == "new", "Rollback restores the previous binary set");
            install.Rollback(Verify);
            check(Version(install.Target) == "upgrade", "Rollback retains the displaced version for recovery");
            check(File.ReadAllText(Path.Combine(data, "state.enc")) == "private encrypted bytes", "Upgrade and rollback preserve separate user data byte-for-byte");
            Package(source, "broken");
            Refuses(() => install.Install(source), "Failed source dependency probe rejects the upgrade", check);
            check(Version(install.Target) == "upgrade", "Dependency failure preserves the working installation");
            Package(source, "next"); int calls = 0;
            var failingStage = new InstallationTransaction(programs, _ => { if (++calls == 2) throw new InvalidDataException("Staged fixture probe failed"); });
            Refuses(() => failingStage.Install(source), "Staged dependency failure rejects promotion", check);
            check(Version(install.Target) == "upgrade" && !Directory.GetDirectories(programs, ".Buddy-stage-*").Any(), "Failed staging leaves no partial replacement or abandoned stage");
            // Force a failure after both renames, when the receipt must replace a directory.
            Directory.CreateDirectory(Path.Combine(source, ".buddy-rollback.json"));
            Refuses(() => install.Install(source), "Receipt-write failure after promotion triggers recovery", check);
            check(Version(install.Target) == "upgrade" && Directory.GetDirectories(programs, ".Buddy-failed-*").Length == 1, "Post-promotion failure restores old binaries and preserves failed files");
            Refuses(() => install.Install(install.Target), "Installing over the executing package is rejected", check);
            Refuses(() => install.Install(Path.Combine(install.Target, "nested")), "A source nested in the target is rejected", check);
            Refuses(() => install.Install(root), "A source containing the target is rejected", check);
            Recovery(root, check, "before-swap", (target, stage, backup) => { Package(target, "old"); Package(stage, "new"); }, true, "old");
            Recovery(root, check, "old-moved", (target, stage, backup) => { Package(backup, "old"); Package(stage, "new"); }, true, "old");
            Recovery(root, check, "new-moved", (target, stage, backup) => { Package(backup, "old"); Package(target, "new"); }, true, "new");
            Recovery(root, check, "clean-before-swap", (target, stage, backup) => Package(stage, "new"), false, null);
            Recovery(root, check, "clean-promoted", (target, stage, backup) => Package(target, "new"), false, "new");
            Recovery(root, check, "corrupt-promoted", (target, stage, backup) => { Package(target, "broken"); Package(backup, "old"); }, true, "old", true);
            var badParent = Path.Combine(root, "InvalidJournal"); Directory.CreateDirectory(badParent);
            File.WriteAllText(Path.Combine(badParent, ".Buddy-update.json"), JsonSerializer.Serialize(new InstallJournal("../../Buddy", ".Buddy-backup-" + Guid.NewGuid().ToString("N"), true)));
            Refuses(() => new InstallationTransaction(badParent, Verify).Recover(), "Recovery rejects journal path traversal", check);
            check(File.ReadAllText(Path.Combine(data, "state.enc")) == "private encrypted bytes", "Invalid recovery paths cannot touch user data");
        } finally {
            var full = Path.GetFullPath(root);
            if (Path.GetDirectoryName(full) != Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) || !Path.GetFileName(full).StartsWith("Buddy.Install.Tests.", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe fixture cleanup");
            Directory.Delete(full, true);
        }
    }
    private static void Recovery(string root, Action<bool, string> check, string name, Action<string, string, string> setup, bool hadPrevious, string? expected, bool failure = false)
    {
        var parent = Path.Combine(root, name); Directory.CreateDirectory(parent);
        var target = Path.Combine(parent, "Buddy"); var stage = Path.Combine(parent, ".Buddy-stage-" + Guid.NewGuid().ToString("N")); var backup = Path.Combine(parent, ".Buddy-backup-" + Guid.NewGuid().ToString("N"));
        setup(target, stage, backup);
        var journal = Path.Combine(parent, ".Buddy-update.json");
        File.WriteAllText(journal, JsonSerializer.Serialize(new InstallJournal(Path.GetFileName(stage), Path.GetFileName(backup), hadPrevious)));
        var install = new InstallationTransaction(parent, path => { if (Version(path) == "broken") throw new InvalidDataException("Broken dependency"); });
        if (failure) Refuses(install.Recover, "Recovery detects a corrupt promoted dependency", check); else install.Recover();
        check((expected == null ? !Directory.Exists(target) : Version(target) == expected) && !File.Exists(journal) && !Directory.Exists(stage), "Interrupted installation recovers safely: " + name);
    }
    private static void Package(string path, string version)
    {
        Directory.CreateDirectory(path);
        foreach (var name in new[] { "Buddy.exe", "Buddy.dll", "Buddy.runtimeconfig.json" }) File.WriteAllText(Path.Combine(path, name), version);
    }
    private static string Version(string path) => File.ReadAllText(Path.Combine(path, "Buddy.dll"));
    private static void Refuses(Action action, string name, Action<bool, string> check)
    {
        bool failed = false; try { action(); } catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException) { failed = true; }
        check(failed, name);
    }
}
