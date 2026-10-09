using Buddy.Windows;
using Buddy.Server;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

if (args.Length != 0) {
    if (args.Length == 2 && args[0] == "--owned-native-assets") {
        await OwnedNativeChecks.Run(args[1], withOcr: false, assetsOnly: true); return;
    }
    if (args.Length is 2 or 3 && args[0] == "--owned-native" && (args.Length == 2 || args[2] == "--with-ocr")) {
        await OwnedNativeChecks.Run(args[1], args.Length == 3); return;
    }
    throw new InvalidOperationException("Use no arguments for pure checks, --owned-native-assets <fixed fixture path>, or --owned-native <fixed fixture path> [--with-ocr].");
}

// Deterministic in-memory data only. This executable does not open selected files,
// instantiate a decoder/OCR engine, create windows, read the clipboard or use network.
int checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS: " + name); }
void Reject(Action action, string name) { try { action(); } catch (InvalidOperationException) { Check(true, name); return; } throw new Exception("FAIL: " + name); }

var text = ContextSourceReader.StageText("  User text\r\nUnicode café e\u0301 😀\t  ", "Selected passage");
Check(text.Kind == ContextSourceKind.SelectedText && text.Text == "  User text\r\nUnicode café e\u0301 😀\t  ", "Selected text is exact, including whitespace and Unicode");
Check(text.ByteCount == Encoding.UTF8.GetByteCount(text.Text) && text.TextSha256 == Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Text))) && text.OriginalSha256 == text.TextSha256, "Selected-text digests describe exact bytes");
Check(ContextSourceReader.StageText(text.Text).Id != text.Id, "Separate selections receive separate identifiers");
Check(ContextSourceReader.StageText(new string('x', 20000)).Text.Length == 20000, "Text at the character bound is accepted");
byte[] exactFile = [0xef, 0xbb, 0xbf, .. Encoding.UTF8.GetBytes("# Exact source\r\nALPHA 123\n")];
using var fileText = ContextSourceReader.FromSelectedTextBytes("owned.md", exactFile, "text/markdown");
Check(fileText.Text == "# Exact source\r\nALPHA 123\n" && fileText.Kind == ContextSourceKind.LocalTextFile, "Text extraction removes only the UTF-8 BOM and preserves line endings");
Check(fileText.OriginalAsset is not null && fileText.OriginalAsset.ByteCount == exactFile.Length && fileText.OriginalAsset.Name == "owned.md", "Selected file owns a basename-only original asset");
Check(fileText.OriginalSha256.Equals(ContextSourceReader.Digest(exactFile), StringComparison.OrdinalIgnoreCase) && !fileText.OriginalSha256.Equals(fileText.TextSha256, StringComparison.OrdinalIgnoreCase), "Original BOM bytes and extracted text have distinct correct digests");
Check(fileText.OriginalAsset!.CopyBytes().SequenceEqual(exactFile), "Retained original includes exact BOM and line-ending bytes");
byte[] callerCopy = fileText.OriginalAsset.CopyBytes(); callerCopy[0] = 0;
Check(fileText.OriginalAsset.CopyBytes()[0] == 0xef, "Original asset never exposes a writable alias");
exactFile[0] = 0;
Check(fileText.OriginalAsset.CopyBytes()[0] == 0xef, "Input array mutation cannot change the retained original");
using var workspaceLease = fileText.OriginalAsset.Retain();
fileText.Dispose();
Reject(() => fileText.OriginalAsset.CopyBytes(), "Disposed reader result cannot yield new original copies");
Check(workspaceLease.CopyBytes()[0] == 0xef, "Independent workspace lease survives reader-result disposal");
workspaceLease.Dispose();
Reject(() => workspaceLease.CopyBytes(), "Released workspace lease cannot produce original bytes");
Reject(() => workspaceLease.Retain(), "Released workspace lease cannot revive the original");
foreach (byte[] invalid in new[] { Array.Empty<byte>(), new byte[] { 0xef, 0xbb, 0xbf }, new byte[] { 0xff }, new byte[] { 0x61, 0 }, new byte[65537] })
    Reject(() => ContextSourceReader.FromSelectedTextBytes("owned.txt", invalid, "text/plain"), "Invalid, empty, binary or oversized UTF-8 file is refused");
Reject(() => ContextSourceReader.FromSelectedTextBytes("owned.pdf", "text"u8.ToArray(), "application/pdf"), "Original file retention does not imply PDF support");
Check(ContextSourceReader.StageText("Only selected text").OriginalAsset is null && ContextSourceReader.StageLink("https://example.com/").OriginalAsset is null, "Pasted text and a bare link do not masquerade as original files");
foreach (string invalid in new[] { "", "  \r\n", "a\0b", "a\u0001b", "a\u007fb", "\ud800", new string('x', 20001) }) Reject(() => ContextSourceReader.StageText(invalid), "Invalid or oversized selected text is refused");
foreach (string invalid in new[] { "", "C:\\secret.txt", "name/path", "\u202ename", new string('x', 161) }) Reject(() => ContextSourceReader.StageText("text", invalid), "Invalid display name is refused");
Reject(() => ContextSourceReader.ClassifyDropText("<not HTML? no rendering>"), "Ambiguous markup drop requires explicit plain-text selection");

const string url = "https://example.com/resource?x=1#part";
var link = ContextSourceReader.ClassifyDropText(url);
Check(link.Kind == ContextSourceKind.LinkReference && link.Text == url && link.SuppliedLocator == url && link.ExtractionMethod.Contains("not fetched"), "Reviewed URL is preserved without a retrieval claim");
Check(link.Image is null && link.OriginalSha256 == link.TextSha256, "Link does not become an image or fetched source");
Check(ContextSourceReader.ClassifyDropText("ordinary browser selection").Kind == ContextSourceKind.SelectedText, "Browser text stages as plain selected text");
Check(ContextSourceReader.ClassifyDropText("Note: preserve this paragraph").Kind == ContextSourceKind.SelectedText, "Ordinary labeled browser text is not mistaken for a URI");
Check(ContextSourceReader.StageText("<script>source data only</script>").Text.Contains("<script>"), "Explicit plain text remains data without HTML rendering");
foreach (string invalid in new[] { "http://example.com/a", "https://user:password@example.com", "https://127.0.0.1/a", "https://[::1]/", "https://example.local/a", "https://example.localhost/a", "https://localhost/a", "https://example.com:444/a", "https://example.com/a b", "https://example.com\\a", "file:///C:/x.txt", "ftp://example.com/a", "https://example.com/" + new string('a', 2100) })
    Reject(() => ContextSourceReader.StageLink(invalid), "Unsafe or unsupported link is refused without fetching");
foreach (string invalid in new[] { "file:///C:/secret.txt", "data:image/png;base64,AAAA", "javascript:alert(1)", "\\\\server\\share", "//server/share", "<img src='https://example.com/a.png'>" })
    Reject(() => ContextSourceReader.ClassifyDropText(invalid), "Other schemes, paths and offered markup do not auto-import");

byte[] png = Png(32, 24);
var pngInfo = ContextSourceReader.ParseImage(png);
Check(pngInfo == new ContextImageInfo("image/png", 32, 24), "Synthetic PNG header reports correct dimensions");
Check(ContextSourceReader.ParseImage(Png(4000, 2000)).Width == 4000, "Image at eight-million-pixel bound passes header policy");
Reject(() => ContextSourceReader.ParseImage(Png(4001, 2000)), "Pixel budget refuses compressed dimension bombs");
Reject(() => ContextSourceReader.ParseImage(Png(4097, 1)), "Width dimension limit applies independently");
Reject(() => ContextSourceReader.ParseImage(Png(1, 4097)), "Height dimension limit applies independently");
Reject(() => ContextSourceReader.ParseImage(Png(0, 1)), "Zero image dimensions refused");
Reject(() => ContextSourceReader.ParseImage(Png(32, 24, animated: true)), "Animated PNG refused explicitly");
Reject(() => ContextSourceReader.ParseImage(Png(32, 24, depth: 3)), "Invalid PNG bit-depth refused");
byte[] broken = png.ToArray(); broken[20] ^= 1;
Reject(() => ContextSourceReader.ParseImage(broken), "PNG checksum corruption refused");
Reject(() => ContextSourceReader.ParseImage(png[..^1]), "Truncated PNG refused");
Reject(() => ContextSourceReader.ParseImage([.. png, 0]), "PNG trailing payload refused");
Reject(() => ContextSourceReader.ParseImage(png[..33]), "Header-only PNG cannot become a staged image");
foreach (byte[] invalid in new[] { new byte[12], "GIF89a0000000000"u8.ToArray(), Encoding.UTF8.GetBytes("<svg>not an image</svg>"), new byte[2_000_001] })
    Reject(() => ContextSourceReader.ParseImage(invalid), "Unsupported or oversized image bytes refused");

byte[] jpeg = Jpeg(40, 30);
Check(ContextSourceReader.ParseImage(jpeg) == new ContextImageInfo("image/jpeg", 40, 30), "Synthetic JPEG header parsed without decoding its scan");
Check(ContextSourceReader.ParseImage(Jpeg(40, 30, marker: 0xc2)).Width == 40, "Progressive JPEG header supported");
Reject(() => ContextSourceReader.ParseImage(Jpeg(40, 30, precision: 12)), "Nonstandard JPEG precision refused");
Reject(() => ContextSourceReader.ParseImage(Jpeg(40, 30, marker: 0xc3)), "Unsupported JPEG lossless frame refused");
Reject(() => ContextSourceReader.ParseImage(Jpeg(40, 0)), "JPEG zero dimensions refused");
Reject(() => ContextSourceReader.ParseImage(Jpeg(4097, 30)), "JPEG dimensions bounded before decoder use");
Reject(() => ContextSourceReader.ParseImage(jpeg[..^1]), "JPEG truncated end refused");
Reject(() => ContextSourceReader.ParseImage([.. jpeg, 0]), "JPEG trailing payload refused");
for (int size = 0; size < png.Length; size++) Reject(() => ContextSourceReader.ParseImage(png[..size]), "Every truncated PNG prefix is refused");

byte[] owned = png.ToArray();
using var image = new ContextImageSnapshot(owned, pngInfo);
byte[] copy = image.CopyEncodedBytes(); copy[0] = 0;
Check(image.CopyEncodedBytes()[0] == 137, "Image consumers cannot mutate owned snapshot through returned copies");
using var selectedImage = new ContextSourceReadResult("image_fixture", "fixture.png", ContextSourceKind.LocalImage, "image/png", "", image.Sha256,
    ContextSourceReader.DigestText(""), png.Length, "selected-image-v1", Image: image);
using var converted = ContextSourceReader.FromOcr(selectedImage, "Reviewed extracted text", "fixture-local-ocr-v1");
Check(converted.Kind == ContextSourceKind.ImageText && converted.Image is null && converted.OriginalSha256 == image.Sha256 && converted.TextSha256 == ContextSourceReader.DigestText(converted.Text), "OCR representation binds original image bytes and exact extracted text separately");
Check(converted.ByteCount == image.ByteCount && converted.Name == "fixture.png", "OCR metadata retains source basename and original size");
Check(!JsonSerializer.Serialize(selectedImage).Contains(Convert.ToBase64String(png)), "Metadata serialization does not expose encoded image bytes");
Reject(() => ContextSourceReader.FromOcr(text, "not valid", "fixture"), "Text source cannot claim image OCR provenance");
Reject(() => ContextSourceReader.FromOcr(selectedImage, "", "fixture"), "Empty OCR result is refused");
image.Dispose(); Check(owned.All(value => value == 0), "Transferred encoded input buffer is cleared");
Check(converted.OriginalAsset is not null && converted.OriginalAsset.CopyBytes().SequenceEqual(png), "OCR representation retains original image bytes after preview disposal");
Check(converted.OriginalAsset!.MimeType == "image/png" && converted.MimeType == "text/plain", "Original image media type is distinct from extracted text representation");
converted.Dispose();
Reject(() => converted.OriginalAsset.CopyBytes(), "Disposing OCR result releases its original-asset lease");
bool disposed = false; try { image.CopyEncodedBytes(); } catch (ObjectDisposedException) { disposed = true; }
Check(disposed, "Disposed image cannot yield new byte copies");
image.Dispose(); Check(true, "Image disposal is idempotent");

using var original = ContextOriginalAsset.CreateCopy(png, "actual.png", "image/png");
using var previewLease = new ContextImageSnapshot(original, pngInfo);
using var sourceResult = new ContextSourceReadResult("image_asset", "actual.png", ContextSourceKind.LocalImage, "image/png", "", original.Sha256,
    ContextSourceReader.DigestText(""), png.Length, "selected-image-v1", Image: previewLease, OriginalAsset: original);
using var ocrResult = ContextSourceReader.FromOcr(sourceResult, "ALPHA 125", "fixture-local-ocr-v1");
using var queueLease = ocrResult.OriginalAsset!.Retain();
sourceResult.Dispose(); ocrResult.Dispose();
Check(queueLease.Name == "actual.png" && queueLease.CopyBytes().SequenceEqual(png), "Staged original survives source-preview and OCR-result disposal");
Reject(() => previewLease.CopyEncodedBytes(), "Disposed source result also releases its preview lease");
queueLease.Dispose(); Reject(() => queueLease.CopyBytes(), "Queue clear releases final retained original lease");

using var lateCancel = new CancellationTokenSource();
var lateWorker = new TaskCompletionSource<ContextSourceReadResult>();
var waiting = SelectedImageOcr.WaitForWorkerAsync(lateWorker.Task, lateCancel.Token);
lateCancel.Cancel(); bool abandoned = false;
try { await waiting; } catch (OperationCanceledException) { abandoned = true; }
Check(abandoned, "OCR wait can be cancelled without waiting for a native worker");
using var lateResult = ContextSourceReader.FromSelectedTextBytes("late.txt", "synthetic late result"u8.ToArray(), "text/plain");
lateWorker.SetResult(lateResult);
Reject(() => lateResult.OriginalAsset!.CopyBytes(), "Successful abandoned OCR result releases its retained original asset");
using var readyResult = ContextSourceReader.FromSelectedTextBytes("ready.txt", "synthetic ready result"u8.ToArray(), "text/plain");
using var acceptedResult = await SelectedImageOcr.WaitForWorkerAsync(Task.FromResult(readyResult), CancellationToken.None);
Check(acceptedResult.OriginalAsset!.CopyBytes().SequenceEqual("synthetic ready result"u8.ToArray()), "Completed OCR wait transfers a usable result to its caller");

using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); bool stopped = false;
try { await ContextSourceReader.ReadSelectedAsync("\\\\never-contact\\share\\private.png", cancelled.Token); } catch (OperationCanceledException) { stopped = true; }
Check(stopped, "Pre-cancelled read returns before inspecting or accessing a path");
Console.WriteLine($"RESULT: {checks} pure checks passed; file reads, image decode, OCR, desktop and network NOT RUN.");

static byte[] Png(int width, int height, bool animated = false, byte depth = 8)
{
    byte[] header = new byte[13]; BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), (uint)width); BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), (uint)height);
    header[8] = depth; header[9] = 6;
    return [137, 80, 78, 71, 13, 10, 26, 10, .. Chunk("IHDR", header), .. (animated ? Chunk("acTL", new byte[8]) : []), .. Chunk("IDAT", [0]), .. Chunk("IEND", [])];
}
static byte[] Chunk(string name, byte[] content)
{
    byte[] result = new byte[content.Length + 12]; BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(0, 4), (uint)content.Length);
    Encoding.ASCII.GetBytes(name).CopyTo(result, 4); content.CopyTo(result, 8); uint crc = uint.MaxValue;
    foreach (byte item in result.AsSpan(4, content.Length + 4)) { crc ^= item; for (int bit = 0; bit < 8; bit++) crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320; }
    BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(result.Length - 4), ~crc); return result;
}
static byte[] Jpeg(int width, int height, byte precision = 8, byte marker = 0xc0)
{
    return [0xff, 0xd8, 0xff, marker, 0, 11, precision, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 1, 1, 0x11, 0,
        0xff, 0xda, 0, 8, 1, 1, 0, 0, 63, 0, 1, 2, 3, 0xff, 0xd9];
}
