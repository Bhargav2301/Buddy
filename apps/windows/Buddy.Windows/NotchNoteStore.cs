using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Buddy.Windows;

internal sealed record NotchNote(string Text, DateTimeOffset? SavedAt, string Revision);

// This file contains only the note explicitly saved in the local editor. Chat,
// attachment contents, model output and task payloads are never passed here.
internal sealed class NotchNoteStore
{
    internal const int MaximumCharacters = 20000;
    private readonly string path;
    private readonly Func<DateTimeOffset> clock;
    private sealed record Document(int Version, string Text, DateTimeOffset SavedAt);
    internal NotchNoteStore(string path, Func<DateTimeOffset>? clock = null)
    {
        this.path = Path.GetFullPath(path);
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }
    internal NotchNote Load()
    {
        if (!File.Exists(path)) return new("", null, "missing");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("The local note file cannot be a link.");
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > 200000) throw new IOException("The local note file is too large.");
        using var memory = new MemoryStream(); input.CopyTo(memory);
        byte[] bytes = memory.ToArray();
        var document = JsonSerializer.Deserialize<Document>(bytes) ?? throw new IOException("The local note file is invalid. It was not changed.");
        if (document.Version != 1 || document.Text is null || document.Text.Length > MaximumCharacters)
            throw new IOException("The local note file is unsupported. It was not changed.");
        return new(document.Text, document.SavedAt, Convert.ToHexString(SHA256.HashData(bytes)));
    }
    internal NotchNote Save(string text, string expectedRevision)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > MaximumCharacters) throw new ArgumentException("Keep the note within 20,000 characters.", nameof(text));
        string directory = Path.GetDirectoryName(path)!; Directory.CreateDirectory(directory);
        // Cooperating Buddy instances serialize the compare/write operation.
        using var lease = new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (Load().Revision != expectedRevision) throw new IOException("The saved note changed in another window. Reload it before saving; your draft is kept.");
        var document = new Document(1, text, clock());
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(document);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) {
                output.Write(bytes); output.Flush(true);
            }
            File.Move(temporary, path, true);
        } finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return new(text, document.SavedAt, Convert.ToHexString(SHA256.HashData(bytes)));
    }
}
