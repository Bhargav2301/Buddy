using System.Security.Cryptography;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Buddy.Windows;

internal static class ContextSourceImagePreview
{
    internal static void Validate(ContextImageSnapshot image, CancellationToken ct) => _ = Decode(image, ct);
    // The returned bitmap owns decoded pixels only. UI owners release it when the
    // source is removed/cleared. Encoded input never becomes a URI or external resource.
    internal static BitmapSource Decode(ContextImageSnapshot image, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); byte[] encoded = image.CopyEncodedBytes(); byte[]? pixels = null;
        try {
            using var stream = new MemoryStream(encoded, writable: false);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count != 1) throw new InvalidOperationException("Choose a single PNG or JPEG image.");
            var frame = decoder.Frames[0];
            if (frame.PixelWidth != image.Info.Width || frame.PixelHeight != image.Info.Height)
                throw new InvalidOperationException("The decoded image dimensions do not match its reviewed header.");
            ct.ThrowIfCancellationRequested();
            var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            int stride = checked(frame.PixelWidth * 4); pixels = new byte[checked(stride * frame.PixelHeight)];
            converted.CopyPixels(pixels, stride, 0); ct.ThrowIfCancellationRequested(); image.RequireAvailable();
            var preview = BitmapSource.Create(frame.PixelWidth, frame.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            preview.Freeze(); return preview;
        } catch (Exception error) when (error is System.IO.IOException or NotSupportedException or ArgumentException or System.Runtime.InteropServices.COMException) {
            throw new InvalidOperationException("The selected image could not be decoded safely. Choose another PNG or JPEG file.");
        } finally { CryptographicOperations.ZeroMemory(encoded); if (pixels is not null) CryptographicOperations.ZeroMemory(pixels); }
    }
}
