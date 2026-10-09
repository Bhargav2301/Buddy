using Buddy.Windows;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// NEVER runs by default. Root must explicitly invoke this mode after reviewing its
// exact fixture path. Generates only synthetic media; no desktop/camera/clipboard,
// windows, UIA, network, user-file enumeration, deletion or installed-app mutation.
internal static class OwnedNativeChecks
{
    private static string Source([CallerFilePath] string source = "") => source;
    internal static string FixedFixturePath => Path.Combine(new FileInfo(Source()).Directory!.Parent!.Parent!.Parent!.FullName,
        "validation", "refinement60", "ingestion-owned-fixtures-v2");
    internal static string FixedAssetsFixturePath => Path.Combine(new FileInfo(Source()).Directory!.Parent!.Parent!.Parent!.FullName,
        "validation", "refinement60", "ingestion-owned-assets-v3");

    internal static async Task Run(string requestedPath, bool withOcr, bool assetsOnly = false)
    {
        if (assetsOnly && withOcr) throw new InvalidOperationException("Original-asset fixture mode never runs OCR.");
        string expected = Path.GetFullPath(assetsOnly ? FixedAssetsFixturePath : FixedFixturePath), actual = Path.GetFullPath(requestedPath);
        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The only permitted fixture path is: " + expected);
        for (var ancestor = new DirectoryInfo(expected).Parent; ancestor is not null; ancestor = ancestor.Parent)
            if (ancestor.Exists && (ancestor.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("The owned fixture path must have no linked ancestors.");
        if (Directory.Exists(expected) || File.Exists(expected))
            throw new InvalidOperationException("This one-use fixture path already exists. Preserve its receipts; do not rerun or overwrite it.");
        string workspace = new DirectoryInfo(expected).Parent!.Parent!.Parent!.FullName + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(AppContext.BaseDirectory).StartsWith(workspace, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Run the reviewed fixture executable from its output directory inside this workspace.");
        string tessdata = Path.Combine(AppContext.BaseDirectory, "tessdata", "eng.traineddata");
        if (withOcr && !File.Exists(tessdata))
            throw new InvalidOperationException("Optional OCR was requested, but root has not placed the reviewed English OCR fixture in this test output's tessdata directory. No automatic copying occurs.");
        Directory.CreateDirectory(expected);
        using (var started = new FileStream(Path.Combine(expected, "started.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            JsonSerializer.Serialize(started, new { Mode = assetsOnly ? "owned-native-assets-v3" : "owned-native-v2", WithOcr = withOcr, StartedAt = DateTimeOffset.UtcNow });
        int checks = 0; var labels = new List<string>();
        void Check(bool value, string label) { if (!value) throw new InvalidOperationException("FAIL: " + label); checks++; labels.Add(label); Console.WriteLine("PASS: " + label); }
        async Task RejectRead(string path, string label) {
            try { using var unexpected = await ContextSourceReader.ReadSelectedAsync(path); }
            catch (InvalidOperationException error) { Check(!error.Message.Contains(expected, StringComparison.OrdinalIgnoreCase), label); return; }
            throw new InvalidOperationException("FAIL: " + label);
        }
        object? ocrEvidence = null; var assets = new List<object>();
        try {
            const string sample = "# Owned synthetic context\r\nALPHA 123\nPreserve this exact text.\n";
            string textPath = Path.Combine(expected, "synthetic.md");
            await WriteNew(textPath, new UTF8Encoding(false, true).GetBytes(sample));
            using var text = await ContextSourceReader.ReadSelectedAsync(textPath);
            Check(text.Kind == ContextSourceKind.LocalTextFile && text.Text == sample && text.Name == "synthetic.md", "Actual held-handle text read returns exact owned UTF-8 source");
            Check(text.OriginalSha256.Equals(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sample))), StringComparison.OrdinalIgnoreCase), "Actual text read digest matches exact owned bytes");
            if (assetsOnly) {
                Check(text.OriginalAsset is not null && text.OriginalAsset.CopyBytes().SequenceEqual(Encoding.UTF8.GetBytes(sample)), "Actual Markdown read retains exact original bytes");
                assets.Add(new { text.Name, text.MimeType, text.ByteCount, text.OriginalSha256, text.TextSha256, text.ExtractionMethod });
                byte[] bom = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes(sample)];
                string bomPath = Path.Combine(expected, "synthetic-bom.txt"); await WriteNew(bomPath, bom);
                using var bomResult = await ContextSourceReader.ReadSelectedAsync(bomPath);
                Check(bomResult.Text == sample && bomResult.ByteCount == bom.Length, "Actual UTF-8 BOM text extraction preserves text and original byte count");
                Check(bomResult.OriginalSha256.Equals(ContextSourceReader.Digest(bom), StringComparison.OrdinalIgnoreCase) && !bomResult.OriginalSha256.Equals(bomResult.TextSha256, StringComparison.OrdinalIgnoreCase), "Actual BOM original and extracted text have separate verified digests");
                using var retained = bomResult.OriginalAsset!.Retain(); bomResult.Dispose();
                Check(retained.CopyBytes().SequenceEqual(bom), "Actual text original survives reader-result disposal");
                await File.WriteAllTextAsync(bomPath, "Replacement owned fixture text");
                Check(retained.CopyBytes().SequenceEqual(bom), "Changing the original path cannot change held snapshot bytes");
                using (var writer = new FileStream(textPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
                    await RejectRead(textPath, "Actual text read refuses an already open writer");
                assets.Add(new { bomResult.Name, bomResult.MimeType, bomResult.ByteCount, bomResult.OriginalSha256, bomResult.TextSha256, bomResult.ExtractionMethod });
                CryptographicOperations.ZeroMemory(bom);
            }

            var frame = Frame(48, 32, (x, y) => (byte)((x / 8 + y / 8) % 2 == 0 ? 255 : 32));
            foreach (bool jpeg in new[] { false, true }) {
                string name = jpeg ? "synthetic.jpg" : "synthetic.png";
                byte[] encoded = Encode(frame, jpeg);
                string path = Path.Combine(expected, name); await WriteNew(path, encoded);
                using var result = await ContextSourceReader.ReadSelectedAsync(path);
                using var image = result.Image ?? throw new InvalidOperationException("Owned image did not stage.");
                Check(result.Kind == ContextSourceKind.LocalImage && result.Name == name && result.ByteCount == encoded.Length && result.Text.Length == 0, "Actual selected image has basename-only metadata and no invented extracted text");
                Check(image.Info.Width == 48 && image.Info.Height == 32 && image.Sha256.Equals(Convert.ToHexString(SHA256.HashData(encoded)), StringComparison.OrdinalIgnoreCase), "Actual image dimensions and digest match genuine generated encoding");
                var preview = ContextSourceImagePreview.Decode(image);
                Check(preview.IsFrozen && preview.PixelWidth == 48 && preview.PixelHeight == 32, "Actual decoder produces a frozen complete-size preview");
                byte[] decoded = new byte[48 * 32 * 4]; preview.CopyPixels(decoded, 48 * 4, 0);
                Check(decoded.Any(value => value != 0), "Actual pixel decode has generated image content");
                CryptographicOperations.ZeroMemory(decoded);
                using (var writer = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
                    await RejectRead(path, "Actual image read refuses an already open writer");
                string renamed = Path.Combine(expected, jpeg ? "renamed.jpg" : "renamed.png");
                File.Move(path, renamed);
                Check(image.Sha256 == result.OriginalSha256, "Reader releases file handles while the immutable snapshot remains usable");
                if (assetsOnly) {
                    using var retained = result.OriginalAsset!.Retain();
                    Check(retained.CopyBytes().SequenceEqual(encoded), "Actual image original matches selected encoded bytes");
                    result.Dispose();
                    Check(retained.CopyBytes().SequenceEqual(encoded), "Actual original image survives result and preview disposal");
                    assets.Add(new { result.Name, result.MimeType, result.ByteCount, result.OriginalSha256, result.TextSha256, result.ExtractionMethod });
                }
                CryptographicOperations.ZeroMemory(encoded);
            }
            string badPath = Path.Combine(expected, "malformed.png");
            await WriteNew(badPath, "not a PNG file and not executable"u8.ToArray());
            await RejectRead(badPath, "Actual owned malformed image is refused without path disclosure");
            string mismatch = Path.Combine(expected, "wrong-extension.png");
            await WriteNew(mismatch, Encode(frame, jpeg: true));
            await RejectRead(mismatch, "Actual encoded signature and extension mismatch is refused");
            using (var cancelled = new CancellationTokenSource()) {
                cancelled.Cancel(); bool stopped = false;
                try { await ContextSourceReader.ReadSelectedAsync(Path.Combine(expected, "absent.png"), cancelled.Token); }
                catch (OperationCanceledException) { stopped = true; }
                Check(stopped, "Actual mode still cancels before opening a path");
            }

            if (!assetsOnly) {
            // Generate the original OCR image for review; optional OCR execution is
            // separately named on the command line and uses only root-copied data.
            byte[] ocrPng = Encode(AlphaImage(), jpeg: false);
            string ocrPath = Path.Combine(expected, "generated-alpha-123.png"); await WriteNew(ocrPath, ocrPng);
            Check(ContextSourceReader.ParseImage(ocrPng).Width == 480, "Owned OCR fixture is generated raster text, not a screenshot");
            CryptographicOperations.ZeroMemory(ocrPng);
            if (withOcr) {
                using var result = await ContextSourceReader.ReadSelectedAsync(ocrPath);
                using var image = result.Image ?? throw new InvalidOperationException("OCR fixture did not stage.");
                using var extracted = await new SelectedImageOcr().ExtractAsync(result, CancellationToken.None);
                // Preserve actual synthetic recognition before any acceptance
                // assertion can throw. No OCR threshold/model/fixture text change.
                ocrEvidence = new { ExpectedSyntheticText = "ALPHA 123", ActualText = extracted.Text,
                    Kind = extracted.Kind.ToString(), extracted.MimeType, extracted.Name, extracted.ByteCount,
                    extracted.ExtractionMethod, extracted.OriginalSha256, extracted.TextSha256,
                    InputSha256 = result.OriginalSha256,
                    Fixture = "embedded-5x7-glyphs-v1; 8x scale; 96 dpi; same as native-v1",
                    ContainsAlpha = extracted.Text.Contains("ALPHA", StringComparison.OrdinalIgnoreCase),
                    Contains123 = extracted.Text.Contains("123", StringComparison.Ordinal),
                    RecordedAt = DateTimeOffset.UtcNow };
                await using (var observed = new FileStream(Path.Combine(expected, "ocr-result.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                    await JsonSerializer.SerializeAsync(observed, ocrEvidence, new JsonSerializerOptions { WriteIndented = true });
                Check(extracted.Kind == ContextSourceKind.ImageText && extracted.Image is null &&
                    extracted.Text.Contains("ALPHA", StringComparison.OrdinalIgnoreCase) && extracted.Text.Contains("123", StringComparison.Ordinal),
                    "Actual local English OCR recognizes generated ALPHA 123 as text-only context");
                Check(extracted.OriginalSha256 == result.OriginalSha256 && extracted.TextSha256 == ContextSourceReader.DigestText(extracted.Text),
                    "Actual OCR representation binds original image and extracted text hashes");
            }
            }
            await WriteReceipt(new { Status = "passed", Checks = checks, Labels = labels, Assets = assets, WithOcr = withOcr, Ocr = ocrEvidence,
                NotRun = withOcr ? "desktop, user files, external destinations" : "OCR, desktop, user files, external destinations", EndedAt = DateTimeOffset.UtcNow });
            Console.WriteLine($"RESULT: {checks} owned-native checks passed; OCR {(withOcr ? "executed on generated fixture" : "NOT RUN")}; no desktop, user files or external delivery.");
        } catch (Exception error) {
            await WriteReceipt(new { Status = "failed", Checks = checks, Labels = labels, Assets = assets, WithOcr = withOcr, Ocr = ocrEvidence, ErrorType = error.GetType().Name,
                Message = error.Message.Replace(expected, "[owned fixture]", StringComparison.OrdinalIgnoreCase), EndedAt = DateTimeOffset.UtcNow });
            throw;
        }
        async Task WriteReceipt(object value) {
            await using var file = new FileStream(Path.Combine(expected, "receipt.json"), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            await JsonSerializer.SerializeAsync(file, value, new JsonSerializerOptions { WriteIndented = true });
        }
    }
    private static async Task WriteNew(string path, byte[] bytes)
    {
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await file.WriteAsync(bytes);
    }
    private static BitmapSource Frame(int width, int height, Func<int, int, byte> value)
    {
        byte[] pixels = new byte[width * height * 4];
        try {
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) {
                int at = (y * width + x) * 4; byte shade = value(x, y);
                pixels[at] = pixels[at + 1] = pixels[at + 2] = shade; pixels[at + 3] = 255;
            }
            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); bitmap.Freeze(); return bitmap;
        } finally { CryptographicOperations.ZeroMemory(pixels); }
    }
    private static byte[] Encode(BitmapSource image, bool jpeg)
    {
        BitmapEncoder encoder = jpeg ? new JpegBitmapEncoder { QualityLevel = 95 } : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image)); using var stream = new MemoryStream(); encoder.Save(stream); return stream.ToArray();
    }
    private static BitmapSource AlphaImage()
    {
        var glyphs = new Dictionary<char, string[]> {
            ['A'] = ["01110","10001","10001","11111","10001","10001","10001"],
            ['L'] = ["10000","10000","10000","10000","10000","10000","11111"],
            ['P'] = ["11110","10001","10001","11110","10000","10000","10000"],
            ['H'] = ["10001","10001","10001","11111","10001","10001","10001"],
            ['1'] = ["00100","01100","00100","00100","00100","00100","01110"],
            ['2'] = ["01110","10001","00001","00010","00100","01000","11111"],
            ['3'] = ["11110","00001","00001","01110","00001","00001","11110"]
        };
        const string text = "ALPHA 123";
        return Frame(480, 112, (x, y) => {
            int dx = x - 24, dy = y - 28; if (dx < 0 || dy < 0 || dy >= 56) return 255;
            int letter = dx / 48, column = dx % 48 / 8;
            if (letter >= text.Length || column >= 5 || !glyphs.TryGetValue(text[letter], out var glyph)) return 255;
            return glyph[dy / 8][column] == '1' ? (byte)0 : (byte)255;
        });
    }
}
