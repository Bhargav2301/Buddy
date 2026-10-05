namespace Buddy.Windows;

// JSON-safe profile preference. It describes position only, never a selected
// window, screenshot, attachment, task permission or input target.
internal sealed record NotchPlacementPreference
{
    public string Mode { get; init; } = "Top";
    public string? MonitorId { get; init; }
    public double XFraction { get; init; } = .5;
    public double YFraction { get; init; }
    internal static NotchPlacementPreference Normalize(NotchPlacementPreference? value)
    {
        if (value is null || value.Mode is not ("Top" or "Detached") || !double.IsFinite(value.XFraction) || !double.IsFinite(value.YFraction)) return new();
        string? monitor = value.MonitorId;
        if (monitor is { Length: > 128 } || monitor?.Any(char.IsControl) == true) return new();
        if (string.IsNullOrWhiteSpace(monitor)) monitor = null;
        return value.Mode == "Top" ? new() { MonitorId = monitor }
            : value with { MonitorId = monitor, XFraction = Math.Clamp(value.XFraction, 0, 1), YFraction = Math.Clamp(value.YFraction, 0, 1) };
    }
}

internal sealed record NotchPlacementMonitor(string Id, PixelBounds WorkArea);
internal sealed record NotchPlacementResult(NotchPlacementPreference Preference, NotchPlacementMonitor Monitor, PixelBounds Bounds, bool RecoveredMissingMonitor);

internal static class NotchPlacementPolicy
{
    internal static bool HoverExpands(NotchPlacementPreference? preference) => NotchPlacementPreference.Normalize(preference).Mode == "Top";
    internal static double Scale(double scale) => double.IsFinite(scale) && scale > 0 ? Math.Clamp(scale, .5, 8) : 1;
    private static bool Valid(NotchPlacementMonitor monitor)
    {
        var box = monitor.WorkArea;
        return !string.IsNullOrWhiteSpace(monitor.Id) && monitor.Id.Length <= 128 && !monitor.Id.Any(char.IsControl)
            && double.IsFinite(box.Left) && double.IsFinite(box.Top) && double.IsFinite(box.Width) && double.IsFinite(box.Height)
            && box.Width > 0 && box.Height > 0 && box.Left >= int.MinValue && box.Top >= int.MinValue
            && box.Left + box.Width <= int.MaxValue && box.Top + box.Height <= int.MaxValue;
    }
    internal static IReadOnlyList<NotchPlacementMonitor> ValidMonitors(IReadOnlyList<NotchPlacementMonitor> monitors)
        => monitors.Where(Valid).GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).ToArray();
    internal static NotchPlacementMonitor Select(IReadOnlyList<NotchPlacementMonitor> monitors, string? preferred, PixelPosition fallback)
    {
        var valid = ValidMonitors(monitors);
        if (valid.Count == 0) throw new InvalidOperationException("No usable display work area is available.");
        if (preferred is not null && valid.FirstOrDefault(m => m.Id.Equals(preferred, StringComparison.OrdinalIgnoreCase)) is { } selected) return selected;
        double x = double.IsFinite(fallback.X) ? fallback.X : 0, y = double.IsFinite(fallback.Y) ? fallback.Y : 0;
        double Distance(NotchPlacementMonitor monitor) {
            var box = monitor.WorkArea;
            double dx = x - Math.Clamp(x, box.Left, box.Left + box.Width), dy = y - Math.Clamp(y, box.Top, box.Top + box.Height);
            return dx * dx + dy * dy;
        }
        return valid.OrderBy(Distance).First();
    }
    private static (double Width, double Height, double Margin) Fit(PixelBounds work, double width, double height, double scale)
    {
        double margin = Math.Min(8 * Scale(scale), Math.Max(0, (Math.Min(work.Width, work.Height) - 1) / 2));
        width = double.IsFinite(width) && width > 0 ? width : 1;
        height = double.IsFinite(height) && height > 0 ? height : 1;
        return (Math.Min(width, work.Width - 2 * margin), Math.Min(height, work.Height - 2 * margin), margin);
    }
    internal static NotchPlacementResult Resolve(NotchPlacementPreference? preference, IReadOnlyList<NotchPlacementMonitor> monitors,
        PixelPosition fallback, double width, double height, double scale)
    {
        var saved = NotchPlacementPreference.Normalize(preference);
        var monitor = Select(monitors, saved.MonitorId, fallback); var work = monitor.WorkArea;
        var fit = Fit(work, width, height, scale);
        bool missing = saved.MonitorId is not null && !saved.MonitorId.Equals(monitor.Id, StringComparison.OrdinalIgnoreCase);
        double x = saved.Mode == "Top" ? work.Left + (work.Width - fit.Width) / 2
            : work.Left + fit.Margin + (work.Width - 2 * fit.Margin - fit.Width) * saved.XFraction;
        double y = saved.Mode == "Top" ? work.Top : work.Top + fit.Margin + (work.Height - 2 * fit.Margin - fit.Height) * saved.YFraction;
        return new(saved with { MonitorId = monitor.Id }, monitor, new(x, y, fit.Width, fit.Height), missing);
    }
    internal static NotchPlacementResult At(PixelPosition position, PixelPosition monitorAnchor, IReadOnlyList<NotchPlacementMonitor> monitors,
        double width, double height, double scale)
    {
        var monitor = Select(monitors, null, monitorAnchor); var work = monitor.WorkArea; var fit = Fit(work, width, height, scale);
        double availableX = work.Width - 2 * fit.Margin - fit.Width, availableY = work.Height - 2 * fit.Margin - fit.Height;
        double x = double.IsFinite(position.X) ? position.X : work.Left + fit.Margin;
        double y = double.IsFinite(position.Y) ? position.Y : work.Top + fit.Margin;
        x = Math.Clamp(x, work.Left + fit.Margin, work.Left + fit.Margin + availableX);
        y = Math.Clamp(y, work.Top + fit.Margin, work.Top + fit.Margin + availableY);
        var saved = new NotchPlacementPreference { Mode = "Detached", MonitorId = monitor.Id,
            XFraction = availableX > 0 ? (x - work.Left - fit.Margin) / availableX : .5,
            YFraction = availableY > 0 ? (y - work.Top - fit.Margin) / availableY : .5 };
        return new(saved, monitor, new(x, y, fit.Width, fit.Height), false);
    }
}
