namespace Buddy.Windows;

internal static class LocalDataTree
{
    // The caller supplies the fixed OS data parent; tests supply an isolated fixture parent.
    internal static void Delete(string parent)
    {
        parent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);
        string root = Path.GetFullPath(Path.Combine(parent, "Buddy"));
        if (!string.Equals(Path.GetDirectoryName(root), parent, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The local data directory is outside the expected location.");
        if (!Directory.Exists(root)) return;
        Check(root);
        var files = new List<string>(); var folders = new List<string>();
        Scan(root); // Refuse links before deleting any file.
        foreach (var file in files) { ValidateAncestors(file); File.Delete(file); }
        foreach (var directory in folders.AsEnumerable().Reverse()) { ValidateAncestors(directory); Directory.Delete(directory, false); }
        Check(root); Directory.Delete(root, false);

        void Check(string path) {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("A link was found in local data. Nothing outside Buddy will be followed.");
        }
        void ValidateAncestors(string path) {
            for (string? current = path; current is not null && current.Length >= root.Length; current = Path.GetDirectoryName(current))
                Check(current);
        }
        void Scan(string directory) {
            foreach (var item in Directory.EnumerateFileSystemEntries(directory)) {
                Check(item);
                if (Directory.Exists(item)) { folders.Add(item); Scan(item); }
                else files.Add(item);
            }
        }
    }
}
