using Buddy.Windows;
using System.Security.Cryptography;
using System.Text;

internal static class ResourceCases
{
    internal static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("explicit local UTF8 read preserves whitespace code and immutable reviewed bytes", async () => {
            using var f = new Fixture(); Directory.CreateDirectory(f.Folder);
            string path = Path.Combine(f.Folder, "owned-reference.MD");
            const string text = "  # Owned reference\r\nKeep `x = 7;` and 👩🏽‍💻 e\u0301.\r\nDo not send.  \r\n";
            byte[] bytes = Encoding.UTF8.GetBytes(text); await File.WriteAllBytesAsync(path, bytes);
            var read = await RefinementResourceReader.ReadAsync(path);
            Assert.That(read.Source.Text == text && read.ByteCount == bytes.Length && read.Sha256.Equals(Convert.ToHexString(SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase), "Snapshot must retain exact decoded text plus raw-byte provenance.");
            Assert.That(read.Source.Provenance == "document" && read.Source.Disposition == "reference" && !read.Source.Required && read.Source.Url is null && !read.Source.Title.Contains(f.Folder), "Selected local file is reference data, not a verified URL, decision or exposed absolute path.");
            await File.WriteAllTextAsync(path, "Changed after review");
            Assert.That(read.Source.Text == text && read.Sha256.Equals(Convert.ToHexString(SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase), "Review must not re-read a changed source or silently change the reviewed hash.");
        });
        await test("UTF8 BOM is only encoding metadata and not part of source text", async () => {
            using var f = new Fixture(); Directory.CreateDirectory(f.Folder);
            string path = Path.Combine(f.Folder, "bom.txt"); const string text = "  Owned note\n";
            byte[] bytes = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(text)]; await File.WriteAllBytesAsync(path, bytes);
            var read = await RefinementResourceReader.ReadAsync(path);
            Assert.That(read.Source.Text == text && read.ByteCount == bytes.Length && read.Sha256.Equals(Convert.ToHexString(SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase), "Only BOM may be removed from decoded text; raw receipt still includes it.");
        });
        await test("invalid UTF8 binary data empty documents and unsupported formats are refused", async () => {
            using var f = new Fixture(); Directory.CreateDirectory(f.Folder);
            foreach (var (name, bytes) in new[] {
                ("invalid.txt", new byte[] { 0xc3, 0x28 }),
                ("utf16.txt", new byte[] { 0xff, 0xfe, 0x41, 0 }),
                ("empty.md", Encoding.UTF8.GetBytes(" \r\n\t")),
                ("executable.html", Encoding.UTF8.GetBytes("Owned harmless fixture")),
                ("binary.txt", new byte[] { 65, 0, 66 })
            }) {
                string path = Path.Combine(f.Folder, name); await File.WriteAllBytesAsync(path, bytes);
                await Refused(path);
            }
            await Refused(f.Folder);
        });
        await test("oversize local input is refused without generating an output file", async () => {
            using var f = new Fixture(); Directory.CreateDirectory(f.Folder);
            string path = Path.Combine(f.Folder, "oversize.txt");
            using (var stream = File.Create(path)) stream.SetLength(2 * 1024 * 1024);
            await Refused(path);
            Assert.That(Directory.GetFiles(f.Folder).SequenceEqual(new[] { path }), "The reader must not create cached copies, receipts or output files.");
        });
        await test("network device alternate stream and relative paths reject before content access", async () => {
            foreach (string path in new[] { @"\\example.invalid\share\owned.txt", @"\\?\C:\owned.txt", @"\\.\pipe\owned.txt", @"C:\owned.txt:hidden", "https://example.invalid/owned.txt", "owned.txt", @"C:owned.txt" }) await Refused(path);
        });
        await test("pre-cancelled resource read performs no file access or snapshot", async () => {
            using var stop = new CancellationTokenSource(); stop.Cancel();
            await Assert.Cancelled(async () => { await RefinementResourceReader.ReadAsync(@"C:\does-not-exist-owned-boundary\cancelled.txt", stop.Token); });
        });
    }
    private static async Task Refused(string path)
    {
        try { await RefinementResourceReader.ReadAsync(path); }
        catch (InvalidOperationException) { return; }
        throw new Exception("Expected bounded resource refusal for owned or syntactically invalid path.");
    }
}
