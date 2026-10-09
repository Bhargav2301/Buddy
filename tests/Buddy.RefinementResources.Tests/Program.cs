using Buddy.Server;
using Buddy.Windows;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

int checks = 0, skipped = 0;
void Check(bool value, string label) { if (!value) throw new Exception("FAIL: " + label); checks++; Console.WriteLine("PASS: " + label); }
var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "Buddy-refine-resources45-" + Guid.NewGuid().ToString("N")));
var junctions = new List<string>(); var symlinks = new List<(string Path, bool Directory)>();
Directory.CreateDirectory(root);
string PathFor(string name) => Path.Combine(root, name);
async Task<string> Write(string name, string text) { string path = PathFor(name); await File.WriteAllTextAsync(path, text, new UTF8Encoding(false, true)); return path; }
async Task Refuse(string path, string label, string? reason = null) {
    try { await RefinementResourceReader.ReadAsync(path); }
    catch (InvalidOperationException ex) {
        Check(!ex.Message.Contains(root, StringComparison.OrdinalIgnoreCase) && (reason is null || ex.Message.Contains(reason, StringComparison.OrdinalIgnoreCase)), label);
        return;
    }
    throw new Exception("FAIL: " + label);
}
try {
    const string original = "  # Selected notes\r\n\r\nKeep spaces, e\u0301, 日本語, and 😀.\n\tLast line  ";
    string selected = await Write("chosen.MD", original);
    var result = await RefinementResourceReader.ReadAsync(selected);
    Check(result.Source.Text == original, "Text, whitespace, mixed line endings, combining characters and emoji are preserved exactly");
    Check(result.Source is { Provenance: "document", Disposition: "reference", Required: false, Url: null, Title: "chosen.MD" }, "Returned resource has explicit document/reference provenance and filename-only title");
    var encoded = Encoding.UTF8.GetBytes(original);
    Check(result.ByteCount == encoded.Length && result.Sha256 == Convert.ToHexString(SHA256.HashData(encoded)), "Review byte count and SHA256 describe exact selected file bytes");
    Check(result.Source.Id.StartsWith("file_") && Guid.TryParseExact(result.Source.Id[5..], "N", out _), "Resource ID is opaque and independent of the local path");
    Check(!JsonSerializer.Serialize(result).Contains(root, StringComparison.OrdinalIgnoreCase), "Returned review metadata carries no absolute local path");
    var second = await RefinementResourceReader.ReadAsync(selected);
    Check(second.Source.Id != result.Source.Id && second.Sha256 == result.Sha256, "Separate selections get unique IDs while identical file bytes have the same digest");
    string sourceId = result.Source.Id;
    await File.WriteAllTextAsync(selected, "Later file edit", new UTF8Encoding(false));
    Check(result.Source.Text == original && result.Source.Id == sourceId && result.Sha256 == second.Sha256, "Reviewed snapshot and ID remain immutable after the original file changes");
    Check(RefinementContext.Build([result.Source]).Ready, "Returned source passes the server's context-source contract");
    await Write("unselected.txt", "NEIGHBOR MUST NOT BE READ");
    var selectedAgain = await RefinementResourceReader.ReadAsync(selected);
    Check(selectedAgain.Source.Text == "Later file edit" && !selectedAgain.Source.Text.Contains("NEIGHBOR"), "Only the selected file contributes content");

    var bomPath = PathFor("bom.txt");
    byte[] bomBytes = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes("\r\nLeading line\n")];
    await File.WriteAllBytesAsync(bomPath, bomBytes);
    var bom = await RefinementResourceReader.ReadAsync(bomPath);
    Check(bom.Source.Text == "\r\nLeading line\n" && bom.ByteCount == bomBytes.Length && bom.Sha256 == Convert.ToHexString(SHA256.HashData(bomBytes)), "Optional UTF8 BOM is an encoding marker; byte metadata includes it");
    Check((await RefinementResourceReader.ReadAsync(await Write("embedded.txt", "a\ufeffb"))).Source.Text == "a\ufeffb", "Embedded BOM character is retained, never globally stripped");
    Check((await RefinementResourceReader.ReadAsync(await Write("limit.txt", new string('x', 20_000)))).Source.Text.Length == 20_000, "Exactly 20,000 UTF16 characters are accepted");
    await Refuse(await Write("chars-over.txt", new string('x', 20_001)), "20,001 characters are refused without clipping", "20,000");
    var unicode = string.Concat(Enumerable.Repeat("😀", 10_000));
    Check((await RefinementResourceReader.ReadAsync(await Write("emoji.txt", unicode))).Source.Text == unicode, "Surrogate pairs count as two UTF16 units and remain intact at the limit");
    await Refuse(await Write("emoji-over.txt", unicode + "x"), "Unicode content over the UTF16 limit is rejected, not split", "20,000");
    Check((await RefinementResourceReader.ReadAsync(await Write("unicode.Md", new string('界', 20_000)))).ByteCount == 60_000, "Multibyte UTF8 within both bounds is accepted");
    await Refuse(await Write("bytes-at-limit.txt", new string('x', 65_536)), "Exactly 64KiB passes byte bound but still fails the independent text bound", "20,000");
    await Refuse(await Write("bytes-over.txt", new string('x', 65_537)), "64KiB plus one is rejected before content allocation/read", "64 KiB");
    await Refuse(await Write("empty.txt", ""), "Empty files are refused");
    await Refuse(await Write("blank.txt", " \r\n\t"), "Whitespace-only files are refused");
    await Refuse(await Write("nul.txt", "text\0binary"), "NUL-containing binary content is refused");
    foreach (var bytes in new byte[][] { [0xff, 0xfe, 0x41, 0], [0xfe, 0xff, 0, 0x41], [0xc0, 0xaf], [0xe2, 0x82], [0x80], [0xed, 0xa0, 0x80] }) {
        var invalid = PathFor("invalid.txt"); await File.WriteAllBytesAsync(invalid, bytes);
        await Refuse(invalid, "Invalid UTF8 is refused: " + Convert.ToHexString(bytes), "valid UTF-8");
    }
    await Refuse(await Write("wrong.json", "{}"), "Non-TXT/MD extension is refused");
    await Refuse(await Write("double.md.exe", "not run"), "Misleading double extension is refused");
    string directory = PathFor("folder.txt"); Directory.CreateDirectory(directory);
    await Refuse(directory, "A directory with a TXT suffix is refused");
    await Refuse(PathFor("absent.txt"), "Missing selected file fails without exposing its local path");
    string longName = new string('n', 156) + ".txt";
    Check((await RefinementResourceReader.ReadAsync(await Write(longName, "text"))).Source.Title.Length == 160, "Filename title at 160 characters is accepted");
    await Refuse(await Write(new string('n', 157) + ".txt", "text"), "Filename above title bound is refused without clipping");
    await Refuse(await Write("hidden\u202e.txt", "text"), "Misleading direction controls in filename are refused");

    foreach (string unsafePath in new[] { "relative.txt", "C:relative.txt", "\\rooted.txt", "https://example.com/notes.txt", "file:///C:/notes.txt", @"\\server\share\notes.txt", "//server/share/notes.txt", @"\\?\C:\notes.txt", @"\\.\pipe\notes.txt", @"\??\C:\notes.txt", PathFor("chosen.MD:secret.txt"), PathFor("..\\chosen.txt"), PathFor(".\\chosen.txt"), PathFor("chosen.txt "), PathFor("chosen.txt."), @"C:\NUL.txt", @"C:\CONIN$.md", @"C:\COM1.txt", @"C:\LPT².md" })
        await Refuse(unsafePath, "Unsafe path form is refused without following it: " + (unsafePath.StartsWith(root) ? unsafePath[root.Length..] : unsafePath));
    using (var cancelled = new CancellationTokenSource()) {
        cancelled.Cancel(); bool stopped = false;
        try { await RefinementResourceReader.ReadAsync(@"\\never-contact\share\notes.txt", cancelled.Token); } catch (OperationCanceledException) { stopped = true; }
        Check(stopped, "Pre-cancelled selection returns cancellation before path inspection or access");
    }
    using (var writer = new FileStream(selected, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        await Refuse(selected, "File already open for mutation is refused rather than reading an unstable snapshot");
    var unlocked = await RefinementResourceReader.ReadAsync(selected);
    File.Move(selected, PathFor("renamed.md"));
    Check(unlocked.Source.Text == "Later file edit", "All handles are released after success, allowing a later rename");

    string linkedTarget = await Write("link-target.txt", "owned link target");
    string hardLink = PathFor("hard.txt");
    if (!Native.CreateHardLinkW(hardLink, linkedTarget, IntPtr.Zero)) throw new Exception("Owned hard-link fixture creation failed: " + Marshal.GetLastWin32Error());
    await Refuse(hardLink, "Hard links are refused, even with an allowed extension");
    await Refuse(linkedTarget, "Multiply linked source file is refused as well");
    File.Delete(hardLink);
    Check((await RefinementResourceReader.ReadAsync(linkedTarget)).Source.Text == "owned link target", "Ordinary file is readable after owned hard link removal");

    string actualDirectory = PathFor("actual"); Directory.CreateDirectory(actualDirectory);
    await File.WriteAllTextAsync(Path.Combine(actualDirectory, "nested.txt"), "owned nested target");
    string junction = PathFor("junction"); Directory.CreateDirectory(junction);
    Native.MakeJunction(junction, actualDirectory); junctions.Add(junction);
    await Refuse(Path.Combine(junction, "nested.txt"), "Ancestor junction is refused before reading its target");
    await Refuse(Path.Combine(junction, "missing.txt"), "Linked ancestor fails closed even when the named leaf is absent");

    try {
        string fileLink = PathFor("symbolic.txt"); File.CreateSymbolicLink(fileLink, linkedTarget); symlinks.Add((fileLink, false));
        await Refuse(fileLink, "Leaf symbolic link is refused");
        string directoryLink = PathFor("directory-link"); Directory.CreateSymbolicLink(directoryLink, actualDirectory); symlinks.Add((directoryLink, true));
        await Refuse(Path.Combine(directoryLink, "nested.txt"), "Ancestor symbolic link is refused");
    } catch (UnauthorizedAccessException) { skipped++; Console.WriteLine("SKIP: Creating owned symbolic links requires OS privilege; junction and hard-link checks still ran."); }
    catch (IOException ex) when ((ex.HResult & 0xffff) == 1314) { skipped++; Console.WriteLine("SKIP: Creating owned symbolic links requires OS privilege; junction and hard-link checks still ran."); }

    Check(Directory.GetFiles(root).All(p => !p.EndsWith(".cache") && !p.EndsWith(".json.tmp")), "Reader creates no persistence artifacts in the owned fixture directory");
    Console.WriteLine($"REFINEMENT RESOURCE CHECKS PASSED: {checks}; SKIPPED: {skipped}");
} finally {
    foreach (var link in symlinks) { if (link.Directory) Directory.Delete(link.Path); else File.Delete(link.Path); }
    foreach (string junction in junctions) Directory.Delete(junction);
    string temporary = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!root.StartsWith(temporary, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(root).StartsWith("Buddy-refine-resources45-", StringComparison.Ordinal))
        throw new Exception("Owned fixture cleanup escaped its dedicated temporary directory.");
    if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0) throw new Exception("Owned fixture root unexpectedly became a link.");
    Directory.Delete(root, recursive: true);
}

internal static class Native
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CreateHardLinkW(string newName, string existingName, IntPtr security);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint sharing, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, byte[] input, int inputSize, IntPtr output, int outputSize, out int returned, IntPtr overlapped);
    internal static void MakeJunction(string junction, string target)
    {
        string substitute = @"\??\" + target;
        byte[] sub = Encoding.Unicode.GetBytes(substitute), print = Encoding.Unicode.GetBytes(target);
        byte[] data = new byte[16 + sub.Length + 2 + print.Length + 2];
        BitConverter.GetBytes(0xA0000003u).CopyTo(data, 0);
        BitConverter.GetBytes(checked((ushort)(data.Length - 8))).CopyTo(data, 4);
        BitConverter.GetBytes(checked((ushort)sub.Length)).CopyTo(data, 10);
        BitConverter.GetBytes(checked((ushort)(sub.Length + 2))).CopyTo(data, 12);
        BitConverter.GetBytes(checked((ushort)print.Length)).CopyTo(data, 14);
        sub.CopyTo(data, 16); print.CopyTo(data, 16 + sub.Length + 2);
        using var handle = CreateFileW(junction, 0x40000000, 0, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (handle.IsInvalid || !DeviceIoControl(handle, 0x000900A4, data, data.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new Exception("Owned junction fixture creation failed: " + Marshal.GetLastWin32Error());
    }
}
