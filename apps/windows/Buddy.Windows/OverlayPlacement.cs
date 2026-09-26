namespace Buddy.Windows;

internal readonly record struct PixelBounds(double Left, double Top, double Width, double Height);
internal readonly record struct PixelPosition(double X, double Y);

internal static class OverlayPlacement
{
    internal static PixelPosition NearPointer(double x, double y, double width, double height, PixelBounds work, double scale)
    {
        double gap = 18 * scale, margin = 8 * scale;
        double right = work.Left + work.Width, bottom = work.Top + work.Height;
        double left = x + gap, top = y + gap;
        if (left + width > right - margin) left = x - gap - width;
        if (top + height > bottom - margin) top = y - gap - height;
        return new(Math.Clamp(left, work.Left + margin, Math.Max(work.Left + margin, right - width - margin)),
            Math.Clamp(top, work.Top + margin, Math.Max(work.Top + margin, bottom - height - margin)));
    }
}
