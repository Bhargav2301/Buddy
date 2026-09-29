using System.Text.Json;
using System.Text.RegularExpressions;

namespace Buddy.Windows;

internal sealed record InstallJournal(string Stage, string Backup, bool HadPrevious);
internal sealed record InstallReceipt(string PreviousDirectory, DateTimeOffset InstalledAt);

// Swaps only application directories. User data, model stores and pairing keys live elsewhere.
internal sealed class InstallationTransaction
{
    private readonly string parent, target, journal;
    private readonly Action<string> verify, probeExisting;
    internal string Target => target;
    private const string ReceiptName = ".buddy-rollback.json";
    internal InstallationTransaction(string programsDirectory, Action<string> verifyPackage, Action<string>? probeExistingPackage = null)
    {
        parent = Path.GetFullPath(programsDirectory).TrimEnd(Path.DirectorySeparatorChar);
        if (parent == Path.GetPathRoot(parent)?.TrimEnd(Path.DirectorySeparatorChar)) throw new InvalidOperationException("An application parent directory is required.");
        target = Path.Combine(parent, "Buddy"); journal = Path.Combine(parent, ".Buddy-update.json"); verify = verifyPackage; probeExisting = probeExistingPackage ?? verifyPackage;
        RejectLinks(parent);
    }

    internal void Install(string source)
    {
        source = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar);
        if (source.Equals(target, StringComparison.OrdinalIgnoreCase) || IsWithin(target, source) || IsWithin(source, target)) throw new InvalidOperationException("Run the update from its extracted package folder.");
        RejectLinks(source); Directory.CreateDirectory(parent); Recover();
        CheckTree(source); verify(source);
        var stage = Child(".Buddy-stage-" + Guid.NewGuid().ToString("N"));
        try {
            CopyTree(source, stage); verify(stage);
            Promote(stage);
        } finally { if (Directory.Exists(stage) && !File.Exists(journal)) DeleteOwned(stage); }
    }

    internal void Rollback(Action<string> verifyPrevious)
    {
        Recover(); RejectLinks(target);
        var receiptFile = Path.Combine(target, ReceiptName); RejectLinks(receiptFile);
        if (!File.Exists(receiptFile)) throw new InvalidOperationException("There is no recorded previous installation to restore.");
        var receipt = JsonSerializer.Deserialize<InstallReceipt>(File.ReadAllText(receiptFile)) ?? throw new InvalidDataException("Invalid rollback record.");
        if (string.IsNullOrEmpty(receipt.PreviousDirectory)) throw new InvalidOperationException("There is no recorded previous installation to restore.");
        var previous = Backup(receipt.PreviousDirectory); RejectLinks(previous);
        if (!Directory.Exists(previous)) throw new InvalidOperationException("The previous installation is no longer available.");
        CheckTree(previous); verifyPrevious(previous);
        Promote(previous);
    }

    private void Promote(string stage)
    {
        RejectLinks(stage); RejectLinks(target);
        if (Directory.Exists(target)) ValidateExisting(target);
        string backup = Child(".Buddy-backup-" + Guid.NewGuid().ToString("N"));
        var pending = new InstallJournal(Path.GetFileName(stage), Path.GetFileName(backup), Directory.Exists(target));
        WriteJson(journal, pending);
        try {
            if (pending.HadPrevious) Directory.Move(target, backup);
            Directory.Move(stage, target);
            WriteJson(Path.Combine(target, ReceiptName), new InstallReceipt(pending.HadPrevious ? pending.Backup : "", DateTimeOffset.UtcNow));
            File.Delete(journal);
        } catch {
            // Restore the known previous binaries; never overwrite or delete them on failure.
            if (Directory.Exists(backup)) {
                if (Directory.Exists(target)) Directory.Move(target, Child(".Buddy-failed-" + Guid.NewGuid().ToString("N")));
                Directory.Move(backup, target);
                File.Delete(journal);
            }
            throw;
        }
    }

    internal void Recover()
    {
        RejectLinks(parent); RejectLinks(journal);
        if (!File.Exists(journal)) return;
        var pending = JsonSerializer.Deserialize<InstallJournal>(File.ReadAllText(journal)) ?? throw new InvalidDataException("Invalid update recovery record.");
        var stage = Child(pending.Stage); var backup = Backup(pending.Backup);
        if (pending.Stage.StartsWith(".Buddy-failed-", StringComparison.Ordinal)) throw new InvalidDataException("Invalid update staging path.");
        if (stage == backup) throw new InvalidDataException("Update recovery directories overlap.");
        RejectLinks(stage); RejectLinks(backup); RejectLinks(target);
        if (!Directory.Exists(target) && Directory.Exists(backup)) {
            ValidateExisting(backup); Directory.Move(backup, target); File.Delete(journal);
            if (pending.Stage.StartsWith(".Buddy-stage-", StringComparison.Ordinal) && Directory.Exists(stage)) DeleteOwned(stage);
            return;
        }
        if (Directory.Exists(target) && !Directory.Exists(stage)) {
            try { CheckTree(target); ValidateExisting(target); probeExisting(target); }
            catch {
                if (Directory.Exists(backup)) {
                    ValidateExisting(backup);
                    Directory.Move(target, Child(".Buddy-failed-" + Guid.NewGuid().ToString("N")));
                    Directory.Move(backup, target); File.Delete(journal);
                }
                throw;
            }
            WriteJson(Path.Combine(target, ReceiptName), new InstallReceipt(Directory.Exists(backup) ? pending.Backup : "", DateTimeOffset.UtcNow));
            File.Delete(journal); return;
        }
        if (Directory.Exists(stage) && !Directory.Exists(backup) && Directory.Exists(target) == pending.HadPrevious) {
            File.Delete(journal);
            if (pending.Stage.StartsWith(".Buddy-stage-", StringComparison.Ordinal)) DeleteOwned(stage);
            return;
        }
        throw new InvalidOperationException("The interrupted update needs review. Application backups were preserved in " + parent);
    }

    private string Child(string name)
    {
        if (name is null || !Regex.IsMatch(name, @"^\.Buddy-(stage|backup|failed)-[0-9a-f]{32}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw new InvalidDataException("Invalid application backup path.");
        var path = Path.GetFullPath(Path.Combine(parent, name));
        if (!IsWithin(path, parent)) throw new InvalidDataException("Application backup is outside the installation directory.");
        return path;
    }
    private string Backup(string name)
    {
        var path = Child(name);
        if (!name.StartsWith(".Buddy-backup-", StringComparison.Ordinal)) throw new InvalidDataException("Invalid previous installation path.");
        return path;
    }
    private static bool IsWithin(string path, string root) => path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static void ValidateExisting(string folder)
    {
        RejectLinks(folder);
        foreach (var name in new[] { "Buddy.exe", "Buddy.dll", "Buddy.runtimeconfig.json" }) {
            var file = Path.Combine(folder, name); RejectLinks(file);
            if (!File.Exists(file)) throw new InvalidDataException("The previous application folder is incomplete. It has been preserved for review.");
        }
    }
    private static void CopyTree(string source, string destination)
    {
        RejectLinks(source); Directory.CreateDirectory(destination);
        foreach (var entry in Directory.EnumerateFileSystemEntries(source)) {
            RejectLinks(entry);
            var output = Path.Combine(destination, Path.GetFileName(entry));
            if (Directory.Exists(entry)) CopyTree(entry, output);
            else File.Copy(entry, output, false);
        }
    }
    private void DeleteOwned(string path)
    {
        if (path != Child(Path.GetFileName(path))) throw new InvalidDataException("Unsafe staging cleanup path.");
        CheckTree(path); Directory.Delete(path, true);
    }
    private static void CheckTree(string path)
    {
        RejectLinks(path);
        foreach (var entry in Directory.EnumerateFileSystemEntries(path)) { RejectLinks(entry); if (Directory.Exists(entry)) CheckTree(entry); }
    }
    private static void RejectLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Installation cannot traverse a symbolic link or junction: " + current);
    }
    private static void WriteJson<T>(string path, T value)
    {
        RejectLinks(path); RejectLinks(path + ".new");
        File.WriteAllText(path + ".new", JsonSerializer.Serialize(value)); File.Move(path + ".new", path, true);
    }
}
