using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

internal static class SourceReceipt
{
    internal static void Verify(string scope)
    {
        string root = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "BuddySourceRoot").Value!;
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("QaReviewedInputs") ?? throw new InvalidOperationException("Missing source receipt.");
        using var reader = new StreamReader(resource);
        var serverRoot = Path.GetFullPath(Path.Combine(root, "services", "Buddy.Server"));
        var inventory = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is { } line) {
            var parts = line.Split('|');
            if (parts.Length != 2 || !File.Exists(parts[0]) || !Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(parts[0]))).Equals(parts[1], StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Changed compile input; rebuild: " + parts[0]);
            if (Path.GetDirectoryName(parts[0])!.Equals(serverRoot, StringComparison.OrdinalIgnoreCase)) inventory.Add(Path.GetFullPath(parts[0]));
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "source", path = parts[0], sha256 = parts[1] }));
        }
        if (!inventory.SetEquals(Directory.GetFiles(serverRoot, "*.cs").Select(Path.GetFullPath))) throw new InvalidOperationException("Server source inventory changed; rebuild.");
        Console.WriteLine(JsonSerializer.Serialize(new { kind = "scope", scope, installedProfile = false, executedActions = false, physicalAudio = false }));
    }
    internal static void DeleteOwned(string path, string prefix)
    {
        string full = Path.GetFullPath(path), temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith(prefix, StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe cleanup path.");
        if (Directory.Exists(full)) Directory.Delete(full, true);
    }
}
