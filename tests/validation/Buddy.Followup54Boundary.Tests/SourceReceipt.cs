using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

internal static class SourceReceipt
{
    internal static void Verify()
    {
        var assembly = Assembly.GetExecutingAssembly();
        string root = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "BuddySourceRoot").Value!;
        string server = Path.GetFullPath(Path.Combine(root, "services", "Buddy.Server"));
        var inventory = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var resource = assembly.GetManifestResourceStream("Qa54ReviewedInputs") ?? throw new InvalidOperationException("Missing compiled-source receipt.");
        using var reader = new StreamReader(resource);
        while (reader.ReadLine() is { } line) {
            string[] fields = line.Split('|');
            if (fields.Length != 2 || !File.Exists(fields[0]) || !Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fields[0]))).Equals(fields[1], StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Compile input changed; rebuild before claiming a result: " + fields[0]);
            if (Path.GetDirectoryName(fields[0])!.Equals(server, StringComparison.OrdinalIgnoreCase) && Path.GetExtension(fields[0]) == ".cs") inventory.Add(Path.GetFullPath(fields[0]));
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "reviewed_source", path = fields[0], sha256 = fields[1] }));
        }
        if (!inventory.SetEquals(Directory.GetFiles(server, "*.cs").Select(Path.GetFullPath))) throw new InvalidOperationException("Production server inventory changed; rebuild.");
    }
}
