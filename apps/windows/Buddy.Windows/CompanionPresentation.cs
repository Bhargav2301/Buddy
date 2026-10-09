namespace Buddy.Windows;

internal enum IslandVisibility { Visible, HiddenByChoice, OtherInstance, Fullscreen }

// Pure presentation decisions shared by the native surfaces and headless boundary checks.
internal static class CompanionPresentation
{
    internal const double FaceSize = 72;
    internal const double PointerSize = 44;
    internal const double ButtonChrome = 16; // 3px ring + 3px gap on each side, plus breathing room.
    internal static double FaceButtonSize(bool compact) => (compact ? PointerSize : FaceSize) + ButtonChrome;
    internal static string Mode(string? value) => value is "Compact" or "Expanded" ? value : "Hidden";
    internal static Guid? FocusTask(IReadOnlyList<Guid> ids, Guid? selected, Guid? active)
        => selected is { } keep && ids.Contains(keep) ? keep
            : active is { } current && ids.Contains(current) ? current : ids.Count > 0 ? ids[0] : null;
    internal static string TaskSource(string source) => source switch {
        "home" => "Home chat", "talk" or "chat" => "Quick chat", "notch" => "Notch chat", "voice" => "Voice",
        "assistant" or "guide" => "Guide", "field" => "Source-field refinement", "refine" => "Buddy-draft refinement",
        "region" => "Selected area", "routine" => "App launch", "jobs" => "Local task",
        "dictation" => "Dictation", "cloud-text" => "Reviewed cloud text", _ when source.StartsWith("agent-", StringComparison.Ordinal) => "Paired client status", _ => source
    };
    internal static IslandVisibility Visibility(string? mode, bool otherInstance, bool fullscreen, bool hideInFullscreen)
        => Mode(mode) == "Hidden" ? IslandVisibility.HiddenByChoice
            : otherInstance ? IslandVisibility.OtherInstance
            : fullscreen && hideInFullscreen ? IslandVisibility.Fullscreen : IslandVisibility.Visible;

    internal static (double Width, double Height) FitPanel(double width, double height, double workWidth, double workHeight)
    {
        if (!double.IsFinite(workWidth) || !double.IsFinite(workHeight) || workWidth <= 0 || workHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(workWidth));
        return (Math.Min(width, Math.Max(1, workWidth - 16)), Math.Min(height, Math.Max(1, workHeight - 16)));
    }
}
