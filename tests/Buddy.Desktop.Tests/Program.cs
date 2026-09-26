using Buddy.Windows;

int assertions = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); assertions++; Console.WriteLine("PASS: " + name); }
var primary = new PixelBounds(0, 0, 1920, 1040);
var normal = OverlayPlacement.NearPointer(100, 100, 480, 354, primary, 1);
Check(normal == new PixelPosition(118, 118), "Chat opens beside the pointer with a gap");
var corner = OverlayPlacement.NearPointer(1918, 1038, 480, 354, primary, 1);
Check(corner.X < 1918 && corner.Y < 1038 && corner.X + 480 <= 1912 && corner.Y + 354 <= 1032,
    "Bottom-right activation flips left/up and stays above the taskbar");
var leftScreen = new PixelBounds(-2560, -300, 2560, 1400);
var left = OverlayPlacement.NearPointer(-2555, -296, 600, 442.5, leftScreen, 1.25);
Check(left.X >= -2550 && left.Y >= -290 && left.X < 0, "Negative monitor origins remain negative and correctly clamped");
var hidpi = OverlayPlacement.NearPointer(3700, 2050, 960, 708, new(1920, 0, 2560, 2080), 2);
Check(hidpi.X >= 1936 && hidpi.X + 960 <= 4464 && hidpi.Y + 708 <= 2064, "200 percent scale uses physical pixels and the target work area");
var tiny = OverlayPlacement.NearPointer(40, 40, 480, 354, new(0, 0, 320, 240), 1);
Check(double.IsFinite(tiny.X) && double.IsFinite(tiny.Y), "A smaller work area never throws from an inverted clamp");

foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0, 3.0 })
{
    foreach (var work in new[] { primary, leftScreen, new PixelBounds(1920, -1080, 3840, 2080) })
    {
        double width = Math.Min(480 * scale, work.Width - 16 * scale), height = Math.Min(354 * scale, work.Height - 16 * scale);
        for (int ix = 0; ix <= 10; ix++) for (int iy = 0; iy <= 10; iy++)
        {
            var point = OverlayPlacement.NearPointer(work.Left + work.Width * ix / 10, work.Top + work.Height * iy / 10, width, height, work, scale);
            if (point.X < work.Left || point.Y < work.Top || point.X + width > work.Left + work.Width + .001 || point.Y + height > work.Top + work.Height + .001)
                throw new Exception("Placement escaped work area");
        }
    }
}
Check(true, "1,815 activation positions across negative origins and 100–300 percent scales stay inside the work area");

var registered = new Dictionary<int, uint>(); bool conflict = false; int nativeCalls = 0;
using var shortcut = new ShortcutRegistration((id, mods) => { nativeCalls++; if (conflict) return false; registered.Add(id, mods); return true; }, id => registered.Remove(id));
Check(shortcut.TrySet(ShortcutChoice.Choices[0]) && registered.Count == 1, "Initial shortcut is registered");
int oldId = shortcut.ActiveId;
conflict = true;
Check(!shortcut.TrySet(ShortcutChoice.Choices[3]) && shortcut.ActiveId == oldId && registered.ContainsKey(oldId), "Reserved Windows shortcut failure preserves the working shortcut");
conflict = false;
Check(shortcut.TrySet(ShortcutChoice.Choices[1]) && shortcut.ActiveId != oldId && !registered.ContainsKey(oldId) && registered.Count == 1,
    "Successful replacement removes only the previous shortcut");
Check((registered[shortcut.ActiveId] & 0x4000) != 0, "Holding the shortcut does not generate repeated activations");
int calls = nativeCalls;
Check(shortcut.TrySet(ShortcutChoice.Choices[1]) && nativeCalls == calls, "Saving an unchanged shortcut does not re-register it");
shortcut.Dispose();
Check(registered.Count == 0 && shortcut.ActiveId == 0, "Quit releases the shortcut");
Check(!new DesktopPreferences().ShortcutStartsVoice, "Default shortcut opens chat without activating the microphone");
Console.WriteLine($"{assertions} desktop logic assertions passed. Native Windows interaction is a separate acceptance check.");
