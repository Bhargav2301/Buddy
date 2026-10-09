using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Buddy.Windows;

// Independent pixel evidence, never execution coordinates. Unbordered/unsupported
// controls abstain rather than treating a readable document label as a button.
internal static class VisualControlBoundary
{
    internal static bool HasBoundary(CapturedWindow frame, OcrText text, CancellationToken ct)
    {
        using var stream = new MemoryStream(frame.Image);
        using var source = new Bitmap(stream);
        using var image = source.Clone(new Rectangle(0, 0, source.Width, source.Height), PixelFormat.Format32bppArgb);
        double sx = image.Width / frame.Bounds.Width, sy = image.Height / frame.Bounds.Height;
        int left = (int)((text.Bounds.Left - frame.Bounds.Left) * sx), right = (int)((text.Bounds.Right - frame.Bounds.Left) * sx);
        int top = (int)((text.Bounds.Top - frame.Bounds.Top) * sy), bottom = (int)((text.Bounds.Bottom - frame.Bounds.Top) * sy);
        int horizontalGap = Math.Min(240, Math.Max(50, right - left)), verticalGap = Math.Min(100, Math.Max(24, (bottom - top) * 2));
        int minX = Math.Max(2, left - horizontalGap), maxX = Math.Min(image.Width - 3, right + horizontalGap);
        if (left < 3 || right >= image.Width - 3 || top < 3 || bottom >= image.Height - 3 || left >= right || top >= bottom) return false;
        var bits = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var pixels = new byte[Math.Abs(bits.Stride) * image.Height];
        try {
            if (bits.Stride <= 0) return false;
            Marshal.Copy(bits.Scan0, pixels, 0, pixels.Length);
            int Luminance(int x, int y) { int i = y * bits.Stride + x * 4; return (pixels[i] + pixels[i + 1] + pixels[i + 2]) / 3; }
            bool Edge(int x, int y, bool horizontal) {
                int center = Luminance(x, y);
                return horizontal
                    ? Math.Max(Math.Abs(center - Luminance(x, y - 2)), Math.Abs(center - Luminance(x, y + 2))) >= 25
                    : Math.Max(Math.Abs(center - Luminance(x - 2, y)), Math.Abs(center - Luminance(x + 2, y))) >= 25;
            }
            var edges = new List<(int Y, int Left, int Right)>();
            for (int y = Math.Max(2, top - verticalGap); y <= Math.Min(image.Height - 3, bottom + verticalGap); y++) {
                ct.ThrowIfCancellationRequested();
                if (y >= top - 2 && y <= bottom + 2) continue;
                int start = -1;
                for (int x = minX; x <= maxX + 1; x++) {
                    bool edge = x <= maxX && Edge(x, y, true);
                    if (edge && start < 0) start = x;
                    if (!edge && start >= 0) {
                        if (start <= left - 3 && x - 1 >= right + 3) edges.Add((y, start, x - 1));
                        start = -1;
                    }
                }
            }
            foreach (var upper in edges.Where(e => e.Y < top).Reverse()) foreach (var lower in edges.Where(e => e.Y > bottom)) {
                ct.ThrowIfCancellationRequested();
                if (Math.Abs(upper.Left - lower.Left) > 2 || Math.Abs(upper.Right - lower.Right) > 2) continue;
                int coverage = 0, total = lower.Y - upper.Y + 1;
                for (int y = upper.Y; y <= lower.Y; y++)
                    if (Edge(upper.Left, y, false) && Edge(upper.Right, y, false)) coverage++;
                if (coverage >= total * .85) return true;
            }
            return false;
        } finally { CryptographicOperations.ZeroMemory(pixels); image.UnlockBits(bits); }
    }
}
