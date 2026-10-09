using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Buddy.Server;

namespace Buddy.Windows;

internal enum ContextSourceKind { SelectedText, LocalTextFile, LinkReference, LocalImage, ImageText }
internal sealed record ContextImageInfo(string MimeType, int Width, int Height);
internal sealed record ContextSourceReadResult(string Id, string Name, ContextSourceKind Kind, string MimeType,
    string Text, string OriginalSha256, string TextSha256, int ByteCount, string ExtractionMethod,
    string? SuppliedLocator = null, ContextImageSnapshot? Image = null, ContextOriginalAsset? OriginalAsset = null) : IDisposable
{
    // Each result owns its leases. A workspace retaining an asset must take its
    // own lease before this result is disposed; do not copy this owning record.
    public void Dispose() { Image?.Dispose(); OriginalAsset?.Dispose(); }
}

// A memory-only selected snapshot. Never returns its owned array or local path.
// Dispose invalidates this lease; the last lease clears the shared encoded bytes.
// Callers must release separate preview/working copies, which cannot be erased here.
internal sealed class ContextImageSnapshot : IDisposable
{
    private readonly object gate = new();
    private ContextOriginalAsset? asset;
    internal ContextImageInfo Info { get; }
    internal string Sha256 { get; }
    internal int ByteCount { get; }
    internal ContextImageSnapshot(byte[] ownedBytes, ContextImageInfo info)
    {
        try { asset = ContextOriginalAsset.CreateCopy(ownedBytes, info.MimeType == "image/png" ? "selected.png" : "selected.jpg", info.MimeType); }
        finally { CryptographicOperations.ZeroMemory(ownedBytes); }
        Info = info; ByteCount = checked((int)asset.ByteCount); Sha256 = asset.Sha256;
    }
    internal ContextImageSnapshot(ContextOriginalAsset original, ContextImageInfo info)
    {
        asset = original.Retain(); Info = info; ByteCount = checked((int)asset.ByteCount); Sha256 = asset.Sha256;
    }
    internal byte[] CopyEncodedBytes() { lock (gate) return asset?.CopyBytes() ?? throw new ObjectDisposedException(nameof(ContextImageSnapshot)); }
    internal ContextOriginalAsset RetainOriginalAsset() { lock (gate) return asset?.Retain() ?? throw new ObjectDisposedException(nameof(ContextImageSnapshot)); }
    internal void RequireAvailable() { lock (gate) { ObjectDisposedException.ThrowIf(asset is null, this); asset.EnsureAvailable(); } }
    public void Dispose() { lock (gate) { asset?.Dispose(); asset = null; } }
}

// Stage only: no attachment, persistence, model call, network or destination write.
internal static class ContextSourceReader
{
    internal const int MaximumImageBytes = 2_000_000;
    internal const int MaximumImageDimension = 4096;
    internal const int MaximumImagePixels = 8_000_000;
    internal const int MaximumTextCharacters = 20_000;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static async Task<ContextSourceReadResult> ReadSelectedAsync(string path, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path)) throw Refused("Select a local file first.");
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".txt" or ".md" or ".png" or ".jpg" or ".jpeg"))
            throw Refused("Choose a local TXT, Markdown, PNG or JPEG file. Other formats and virtual browser files are not supported.");
        var selected = await ContextSourceFileReader.ReadSelectedBytesAsync(path, ct).ConfigureAwait(false);
        ContextImageSnapshot? image = null; ContextOriginalAsset? asset = null; bool transferred = false;
        try {
            ct.ThrowIfCancellationRequested();
            if (extension is ".txt" or ".md") {
                var textResult = FromSelectedTextBytes(selected.Name, selected.Bytes, extension == ".md" ? "text/markdown" : "text/plain");
                if (ct.IsCancellationRequested) { textResult.Dispose(); ct.ThrowIfCancellationRequested(); }
                return textResult;
            }
            var info = ParseImage(selected.Bytes);
            if (extension == ".png" != (info.MimeType == "image/png")) throw Refused("The selected image format does not match its filename.");
            asset = ContextOriginalAsset.CreateCopy(selected.Bytes, selected.Name, info.MimeType);
            image = new(asset, info);
            // Decode locally only after encoded size and declared dimensions pass.
            // Full decoding verifies the snapshot before it enters the review queue.
            await Task.Run(() => ContextSourceImagePreview.Validate(image, ct), ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var result = new ContextSourceReadResult("image_" + Guid.NewGuid().ToString("N"), selected.Name,
                ContextSourceKind.LocalImage, info.MimeType, "", image.Sha256, DigestText(""), selected.Bytes.Length,
                "selected-image-v1; no text extracted", Image: image, OriginalAsset: asset);
            transferred = true; return result;
        } finally {
            if (!transferred) { image?.Dispose(); asset?.Dispose(); }
            CryptographicOperations.ZeroMemory(selected.Bytes);
        }
    }

    // Pure projection of the single held-handle snapshot. Keeps original bytes
    // (including a BOM) distinct from extracted text and never reopens its path.
    internal static ContextSourceReadResult FromSelectedTextBytes(string name, byte[] bytes, string mimeType)
    {
        RequireName(name);
        if (mimeType is not ("text/plain" or "text/markdown") || bytes.Length is < 1 or > 65536)
            throw Refused("Choose a nonempty UTF-8 TXT or Markdown file no larger than 64 KiB.");
        int offset = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
        string text;
        try { text = StrictUtf8.GetString(bytes, offset, bytes.Length - offset); }
        catch (DecoderFallbackException) { throw Refused("The selected file is not valid UTF-8. Choose a UTF-8 text copy."); }
        RequireText(text);
        var asset = ContextOriginalAsset.CreateCopy(bytes, name, mimeType);
        return new("file_" + Guid.NewGuid().ToString("N"), name, ContextSourceKind.LocalTextFile, mimeType,
            text, asset.Sha256, DigestText(text), bytes.Length, "strict-utf8-v1", OriginalAsset: asset);
    }

    internal static ContextSourceReadResult StageText(string text, string name = "Selected text")
    {
        RequireText(text); RequireName(name);
        var bytes = StrictUtf8.GetBytes(text);
        try {
            string digest = Digest(bytes);
            return new("text_" + Guid.NewGuid().ToString("N"), name, ContextSourceKind.SelectedText,
                "text/plain", text, digest, digest, bytes.Length, "selected-text-v1");
        } finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    // Call only for an explicitly offered text/URI drop format. Do not pass HTML,
    // virtual-file descriptors, serialized objects or a downloaded URL's content.
    internal static ContextSourceReadResult ClassifyDropText(string text)
    {
        RequireText(text);
        string candidate = text.Trim();
        if (candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) return StageLink(candidate);
        if ((!candidate.Any(char.IsWhiteSpace) && Uri.TryCreate(candidate, UriKind.Absolute, out _)) ||
            new[] { "file:", "data:", "javascript:", "shell:", "ms-appx:" }.Any(prefix => candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ||
            (candidate.Length > 2 && char.IsAsciiLetter(candidate[0]) && candidate[1] == ':' && candidate[2] is '\\' or '/') ||
            candidate.StartsWith("\\\\", StringComparison.Ordinal) ||
            candidate.StartsWith("//", StringComparison.Ordinal) || candidate.StartsWith('<'))
            throw Refused("Drop plain text or a public HTTPS link. HTML, local-path links and other URI schemes are not imported.");
        return StageText(text);
    }

    internal static ContextSourceReadResult StageLink(string link)
    {
        RequireText(link);
        if (link.Length > 2048 || link != link.Trim() || link.Any(char.IsWhiteSpace) || link.Contains('\\') ||
            !link.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            !Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0 ||
            uri.Port != 443 || uri.IsLoopback || !uri.Host.Contains('.') ||
            uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            System.Net.IPAddress.TryParse(uri.Host.Trim('[', ']'), out _))
            throw Refused("Use a public HTTPS link without embedded credentials, an IP address or a custom port. The link will not be fetched.");
        // Preserve the explicitly supplied locator, including fragment/query; review
        // must display it exactly. A URL may contain private values even over HTTPS.
        var digest = DigestText(link);
        return new("link_" + Guid.NewGuid().ToString("N"), "Reviewed HTTPS link", ContextSourceKind.LinkReference,
            "text/uri-list", link, digest, digest, StrictUtf8.GetByteCount(link), "user-provided-link-v1; not fetched", link);
    }

    internal static ContextSourceReadResult FromOcr(ContextSourceReadResult original, string text, string method)
    {
        if (original.Kind != ContextSourceKind.LocalImage || original.Image is null) throw Refused("Select and review a local image before extracting text.");
        original.Image.RequireAvailable(); RequireText(text); RequireName(method);
        var asset = original.OriginalAsset?.Retain() ?? original.Image.RetainOriginalAsset();
        return new("ocr_" + Guid.NewGuid().ToString("N"), original.Name, ContextSourceKind.ImageText, "text/plain",
            text, original.OriginalSha256, DigestText(text), original.ByteCount, method, OriginalAsset: asset);
    }

    internal static ContextImageInfo ParseImage(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is < 12 or > MaximumImageBytes) throw Refused("Images must be nonempty PNG or JPEG files no larger than 2 MB.");
        if (bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return Png(bytes);
        if (bytes[0] == 0xff && bytes[1] == 0xd8) return Jpeg(bytes);
        throw Refused("Only PNG and JPEG image content is supported.");
    }

    private static ContextImageInfo Png(ReadOnlySpan<byte> data)
    {
        int at = 8, chunks = 0; bool header = false, pixels = false, ended = false; int width = 0, height = 0;
        while (at < data.Length) {
            if (++chunks > 4096 || data.Length - at < 12) throw Refused("The PNG structure is incomplete or exceeds the chunk limit.");
            uint rawLength = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(at, 4));
            if (rawLength > (uint)(data.Length - at - 12)) throw Refused("The PNG contains an invalid chunk length.");
            int length = (int)rawLength; var type = data.Slice(at + 4, 4); var content = data.Slice(at + 8, length);
            if (Crc(data.Slice(at + 4, length + 4)) != BinaryPrimitives.ReadUInt32BigEndian(data.Slice(at + 8 + length, 4)))
                throw Refused("The PNG checksum is invalid.");
            if (!header) {
                if (!type.SequenceEqual("IHDR"u8) || length != 13) throw Refused("The PNG has no valid leading image header.");
                uint w = BinaryPrimitives.ReadUInt32BigEndian(content[..4]), h = BinaryPrimitives.ReadUInt32BigEndian(content.Slice(4, 4));
                if (w > int.MaxValue || h > int.MaxValue) throw Refused("The image dimensions exceed the preview limit.");
                width = (int)w; height = (int)h; Dimensions(width, height);
                byte depth = content[8], color = content[9];
                bool depthValid = color switch { 0 => depth is 1 or 2 or 4 or 8 or 16, 2 or 4 or 6 => depth is 8 or 16, 3 => depth is 1 or 2 or 4 or 8, _ => false };
                if (!depthValid || content[10] != 0 || content[11] != 0 || content[12] > 1) throw Refused("The PNG encoding is unsupported.");
                header = true;
            } else if (type.SequenceEqual("IHDR"u8)) throw Refused("The PNG repeats its image header.");
            if (type.SequenceEqual("acTL"u8) || type.SequenceEqual("fcTL"u8) || type.SequenceEqual("fdAT"u8)) throw Refused("Animated PNG files are not supported. Choose a single image.");
            if (type.SequenceEqual("IDAT"u8)) pixels |= length > 0;
            if (type.SequenceEqual("IEND"u8)) { if (length != 0 || !pixels || at + 12 != data.Length) throw Refused("The PNG ending is invalid."); ended = true; }
            at += length + 12;
        }
        if (!header || !pixels || !ended) throw Refused("The PNG image is incomplete.");
        return new("image/png", width, height);
    }

    private static ContextImageInfo Jpeg(ReadOnlySpan<byte> data)
    {
        if (data[^2] != 0xff || data[^1] != 0xd9) throw Refused("The JPEG image is incomplete.");
        int at = 2, segments = 0; ContextImageInfo? info = null;
        while (at < data.Length - 2) {
            if (++segments > 4096 || data[at++] != 0xff) throw Refused("The JPEG segment structure is invalid.");
            while (at < data.Length && data[at] == 0xff) at++;
            if (at >= data.Length) throw Refused("The JPEG image is incomplete.");
            byte marker = data[at++];
            if (marker is 0 or 0xd8 or 0xd9 or >= 0xd0 and <= 0xd7) throw Refused("The JPEG marker is invalid.");
            if (at + 2 > data.Length) throw Refused("The JPEG image is incomplete.");
            int length = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(at, 2));
            if (length < 2 || length > data.Length - at - 2) throw Refused("The JPEG segment length is invalid.");
            if (marker is >= 0xc0 and <= 0xcf && marker is not (0xc4 or 0xc8 or 0xcc)) {
                if (marker is not (0xc0 or 0xc1 or 0xc2) || info is not null || length < 8 || data[at + 2] != 8)
                    throw Refused("Only a single standard 8-bit JPEG image is supported.");
                int height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(at + 3, 2)), width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(at + 5, 2));
                byte components = data[at + 7];
                if (components is not (1 or 3 or 4) || length != 8 + 3 * components) throw Refused("The JPEG frame is invalid.");
                Dimensions(width, height); info = new("image/jpeg", width, height);
            }
            if (marker == 0xda) {
                if (info is null || length < 6 || at + length >= data.Length - 2) throw Refused("The JPEG has no complete image scan.");
                // The platform decoder verifies entropy data before staging succeeds.
                return info;
            }
            at += length;
        }
        throw Refused("The JPEG has no supported image scan.");
    }

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = uint.MaxValue;
        foreach (byte value in bytes) { crc ^= value; for (int bit = 0; bit < 8; bit++) crc = (crc & 1) == 0 ? crc >> 1 : (crc >> 1) ^ 0xedb88320; }
        return ~crc;
    }
    private static void Dimensions(int width, int height)
    {
        if (width is < 1 or > MaximumImageDimension || height is < 1 or > MaximumImageDimension || (long)width * height > MaximumImagePixels)
            throw Refused("Choose an image at most 4096 pixels per side and 8 million pixels total.");
    }
    internal static void RequireText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaximumTextCharacters || text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            throw Refused("Use nonempty text up to 20,000 characters, without binary control data.");
        try { _ = StrictUtf8.GetByteCount(text); } catch (EncoderFallbackException) { throw Refused("The selected text has invalid Unicode."); }
    }
    internal static void RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 160 || name.IndexOfAny(['\\', '/', ':']) >= 0 ||
            name.Any(c => char.IsControl(c) || c is '\u200e' or '\u200f' or >= '\u202a' and <= '\u202e' or >= '\u2066' and <= '\u2069'))
            throw Refused("The selected source name is invalid.");
    }
    internal static string Digest(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    internal static string DigestText(string text) => Digest(StrictUtf8.GetBytes(text));
    private static InvalidOperationException Refused(string message) => new(message);
}
