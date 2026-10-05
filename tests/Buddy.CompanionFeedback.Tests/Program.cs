using Buddy.Windows;

int count = 0;
void Check(bool pass, string message) { if (!pass) throw new Exception(message); count++; Console.WriteLine("PASS: " + message); }
foreach (var mode in new[] { "Hidden", "Compact", "Expanded" })
foreach (bool owner in new[] { false, true })
foreach (bool fullscreen in new[] { false, true })
foreach (bool hide in new[] { false, true }) {
    var actual = CompanionPresentation.Visibility(mode, owner, fullscreen, hide);
    var expected = mode == "Hidden" ? IslandVisibility.HiddenByChoice : owner ? IslandVisibility.OtherInstance : fullscreen && hide ? IslandVisibility.Fullscreen : IslandVisibility.Visible;
    Check(actual == expected, $"{mode}, other instance {owner}, fullscreen {fullscreen}, opt-in hide {hide}: {expected}");
}
foreach (var value in new string?[] { null, "", "expanded", "unexpected" })
    Check(CompanionPresentation.Mode(value) == "Hidden", "Unknown mode cannot silently enable an overlay");
foreach (double scale in new[] { 1d, 1.25, 1.5, 2d })
foreach (bool compact in new[] { false, true }) {
    double face = compact ? CompanionPresentation.PointerSize : CompanionPresentation.FaceSize;
    double host = CompanionPresentation.FaceButtonSize(compact);
    Check((host - 12) * scale >= face * scale + 4 * scale, $"Face/pointer leaves complete focus ring/gap and margin at {scale * 100}%");
}
foreach (var work in new[] { (1920d,1080d), (800d,600d), (320d,240d), (250d,180d) }) {
    var fit = CompanionPresentation.FitPanel(440, 560, work.Item1, work.Item2);
    Check(fit.Width <= work.Item1 - 16 && fit.Height <= work.Item2 - 16 && fit.Width > 0 && fit.Height > 0,
        $"Panel bounds fit work area {work.Item1}x{work.Item2}");
}
foreach (double bad in new[] { double.NaN, double.PositiveInfinity, 0d, -1d }) {
    try { CompanionPresentation.FitPanel(440, 560, bad, 600); Check(false, "Invalid work area must fail"); }
    catch (ArgumentOutOfRangeException) { Check(true, "Invalid work area fails explicitly"); }
}
Console.WriteLine($"ALL {count} COMPANION FEEDBACK PURE CHECKS PASSED; no WPF, native window, model, profile or transport");
