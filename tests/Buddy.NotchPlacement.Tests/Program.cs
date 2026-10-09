using Buddy.Windows;
using System.Text.Json;

int checks = 0;
void Check(bool value, string text) { if (!value) throw new Exception("FAIL: " + text); checks++; Console.WriteLine("PASS: " + text); }
bool Inside(NotchPlacementResult result) {
    var b = result.Bounds; var w = result.Monitor.WorkArea;
    return b.Width > 0 && b.Height > 0 && b.Left >= w.Left - .001 && b.Top >= w.Top - .001
        && b.Left + b.Width <= w.Left + w.Width + .001 && b.Top + b.Height <= w.Top + w.Height + .001;
}
bool Near(double a, double b) => Math.Abs(a - b) < .001;
var monitors = new[] {
    new NotchPlacementMonitor("left", new(-1920, -200, 1920, 1080)),
    new NotchPlacementMonitor("main", new(0, 0, 2560, 1440)),
    new NotchPlacementMonitor("above", new(100, -1600, 2160, 1400))
};
var fallback = new PixelPosition(1200, 200);
Check(NotchPlacementPreference.Normalize(null) == new NotchPlacementPreference(), "Missing preference defaults to top edge");
Check(NotchPlacementPreference.Normalize(new() { Mode = "window-attach", MonitorId = "main", XFraction = .2 }) == new NotchPlacementPreference(), "Unknown placement mode cannot invent a window attachment");
foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity }) {
    Check(NotchPlacementPreference.Normalize(new() { Mode = "Detached", XFraction = value, YFraction = .2 }).Mode == "Top", "Nonfinite X fails closed to top: " + value);
    Check(NotchPlacementPreference.Normalize(new() { Mode = "Detached", XFraction = .2, YFraction = value }).Mode == "Top", "Nonfinite Y fails closed to top: " + value);
}
var clamped = NotchPlacementPreference.Normalize(new() { Mode = "Detached", XFraction = -99, YFraction = 99 });
Check(clamped.XFraction == 0 && clamped.YFraction == 1, "Finite persisted coordinates clamp to available work-area fractions");
Check(NotchPlacementPreference.Normalize(new() { Mode = "Detached", MonitorId = "invalid\nmonitor" }).Mode == "Top", "Control characters in monitor identity fail closed");
var persisted = new NotchPlacementPreference { Mode = "Detached", MonitorId = "left", XFraction = .37, YFraction = .81 };
Check(JsonSerializer.Deserialize<NotchPlacementPreference>(JsonSerializer.Serialize(persisted)) == persisted, "Detached preference round trips through JSON using public properties");
Check(JsonSerializer.Deserialize<NotchPlacementPreference>("{}") == new NotchPlacementPreference(), "Older profiles with empty placement preserve top defaults");
Check(NotchPlacementPolicy.HoverExpands(null) && !NotchPlacementPolicy.HoverExpands(persisted), "Top keeps hover expansion while detached compact requires explicit Expand to keep its header stationary");

var top = NotchPlacementPolicy.Resolve(null, monitors, fallback, 350, 96, 1);
Check(top.Monitor.Id == "main" && top.Bounds.Top == 0 && Near(top.Bounds.Left, (2560 - 350) / 2d), "Default top placement retains cursor-monitor center and top edge");
var left = NotchPlacementPolicy.Resolve(persisted, monitors, fallback, 420, 650, 1);
Check(left.Monitor.Id == "left" && Inside(left), "Negative monitor coordinates produce visible detached placement");
var caseIdentity = NotchPlacementPolicy.Resolve(persisted with { MonitorId = "LEFT" }, monitors, fallback, 420, 650, 1);
Check(caseIdentity.Bounds == left.Bounds && !caseIdentity.RecoveredMissingMonitor, "Monitor identity matching tolerates Windows casing");
var missing = NotchPlacementPolicy.Resolve(persisted with { MonitorId = "removed-display" }, monitors, new(-400, 100), 420, 650, 1);
Check(missing.Monitor.Id == "left" && missing.RecoveredMissingMonitor && Inside(missing), "Removed display recovers to the nearest available work area");
Check(persisted.MonitorId == "left" && persisted.XFraction == .37, "Recovery math never mutates the saved caller preference");
var above = NotchPlacementPolicy.Resolve(new() { MonitorId = "above" }, monitors, fallback, 350, 96, 2);
Check(above.Bounds.Top == -1600 && Inside(above), "Top return can target a monitor above the primary display");
foreach (double scale in new[] { .75, 1d, 1.25, 1.5, 2d, 3d }) {
    var corner = new NotchPlacementPreference { Mode = "Detached", MonitorId = "main", XFraction = 1, YFraction = 1 };
    var actual = NotchPlacementPolicy.Resolve(corner, monitors, fallback, 420 * scale, 650 * scale, scale);
    Check(Inside(actual), "Physical bounds remain contained at scale " + scale);
    Check(Near(actual.Bounds.Left + actual.Bounds.Width, 2560 - 8 * scale) && Near(actual.Bounds.Top + actual.Bounds.Height, 1440 - 8 * scale), "Bottom/right anchoring includes scaled margin at " + scale);
}
var shrinking = new[] { new NotchPlacementMonitor("main", new(100, 50, 640, 380)) };
var afterWorkAreaChange = NotchPlacementPolicy.Resolve(new() { Mode = "Detached", MonitorId = "main", XFraction = .9, YFraction = .9 }, shrinking, fallback, 840, 1400, 2);
Check(Inside(afterWorkAreaChange) && afterWorkAreaChange.Bounds.Width <= 640 && afterWorkAreaChange.Bounds.Height <= 380, "Oversized window fits a reduced work area after taskbar/display changes");
var tiny = NotchPlacementPolicy.Resolve(persisted, [new("tiny", new(10, 20, 5, 3))], fallback, 800, 1400, 2);
Check(Inside(tiny), "Tiny valid work area remains numerically bounded without negative clamp ranges");
var invalids = new[] { new NotchPlacementMonitor("bad", new(double.NaN, 0, 100, 100)), new NotchPlacementMonitor("zero", new(0, 0, 0, 100)), monitors[1] };
Check(NotchPlacementPolicy.Resolve(null, invalids, fallback, double.NaN, double.PositiveInfinity, double.NaN).Monitor.Id == "main", "Invalid display/size/DPI values cannot create nonfinite placement");
try { NotchPlacementPolicy.Resolve(null, [], fallback, 350, 96, 1); throw new Exception("FAIL: absent display refusal"); }
catch (InvalidOperationException) { Check(true, "No valid display is a recoverable refusal, not invented geometry"); }
var far = NotchPlacementPolicy.At(new(-100000, 100000), new(-1000, 20), monitors, 420, 200, 1);
Check(far.Monitor.Id == "left" && Inside(far), "Dragging beyond desktop bounds clamps to the selected nearby monitor");
var roundTrip = NotchPlacementPolicy.Resolve(far.Preference, monitors, fallback, 420, 200, 1);
Check(roundTrip.Bounds == far.Bounds, "Committed drag position resolves exactly from normalized preference");

var state = new NotchPlacementState(); int writes = 0; NotchPlacementPreference? observed = null;
NotchPlacementPreference Save(NotchPlacementPreference preference) { writes++; observed = preference; return preference; }
Check(state.Begin(new(1200, 24), top.Bounds, 1), "Explicit drag begins from a known bar and pointer position");
Check(!state.Begin(new(1000, 24), top.Bounds, 1), "A second pointer-down cannot replace an active drag owner");
state.Move(new(1202, 25)); state.Resolve(monitors, fallback, 350, 96, 1); state.Finish(Save);
Check(writes == 0 && state.Saved.Mode == "Top", "Click jitter below threshold does not detach or persist placement");
state.Begin(new(1200, 24), top.Bounds, 1); state.Move(new(-1200, 200)); var preview = state.Resolve(monitors, fallback, 350, 96, 1);
Check(preview.Preference.Mode == "Detached" && preview.Monitor.Id == "left" && state.Saved.Mode == "Top" && writes == 0, "Cross-monitor drag is transient and does not write preferences mid-gesture");
state.Cancel(); Check(!state.IsDragging && state.Saved.Mode == "Top" && writes == 0, "Lost capture/Hide/Stop cancellation keeps original placement without a write");
state.Begin(new(1200, 24), top.Bounds, 1); state.Move(new(1800, 900)); var final = state.Resolve(monitors, fallback, 350, 96, 1);
Check(state.Finish(Save) && writes == 1 && observed == final.Preference && state.Saved == final.Preference, "Explicit release commits the exact last bounded preview once");
var committed = state.Saved;
Check(!state.Commit(new() { Mode = "Detached", MonitorId = "above", XFraction = .5, YFraction = .5 }, _ => throw new IOException("Owned save failure"))
    && state.Saved == committed && state.Error is not null && !state.IsDragging, "Failed persistence keeps previous placement and exposes a visible error");
state.Begin(new(final.Bounds.Left + 20, final.Bounds.Top + 24), final.Bounds, 1); state.Move(new(800, 600)); state.Resolve(monitors, fallback, 350, 96, 1);
Check(!state.Finish(_ => throw new IOException("Owned drag save failure")) && state.Saved == committed && !state.IsDragging, "Failed drag commit rolls back preview and always releases drag state");
Check(state.Commit(new() { MonitorId = "main" }, Save) && state.Saved.Mode == "Top" && state.Error is null, "Explicit Return to top clears detached mode and prior save error");
var requested = new NotchPlacementPreference { Mode = "Detached", MonitorId = "main", XFraction = .7, YFraction = .2 };
var merged = requested with { YFraction = .85 };
Check(state.Commit(requested, _ => merged) && state.Saved == merged && state.Saved != requested, "Placement adopts the effective merged preference returned by persistence");
Check(!state.Commit(requested, _ => null!) && state.Saved == merged && state.Error is not null, "Missing effective save result refuses replacement instead of assuming the draft was saved");

var dpiDrag = new NotchPlacementState(); dpiDrag.Begin(new(top.Bounds.Left + 20, 24), top.Bounds, 1); dpiDrag.Move(new(1600, 700));
var scaledDrag = dpiDrag.Resolve(monitors, fallback, 700, 192, 2);
Check(Near(scaledDrag.Bounds.Left, 1600 - 40) && Near(scaledDrag.Bounds.Top, 700 - 48) && Inside(scaledDrag), "A DPI change preserves the physical pointer's DIP grab offset and clamps the resized bar");
dpiDrag.Cancel(); Check(dpiDrag.Resolve(monitors, fallback, 700, 192, 2).Preference.Mode == "Top", "Cancelled DPI-crossing drag restores the saved top mode");
Check(!dpiDrag.Begin(new(double.NaN, 0), top.Bounds, 1), "Invalid pointer values cannot enter a drag session");
Check(new NotchPlacementState(new() { Mode = "Detached", XFraction = double.NaN }).Saved.Mode == "Top", "Loaded malformed detached state normalizes before any placement");
var refused = new NotchPlacementState(persisted); refused.Begin(new(left.Bounds.Left + 10, left.Bounds.Top + 20), left.Bounds, 1);
refused.Move(new(1000, 400)); refused.Resolve(monitors, fallback, 420, 650, 1); refused.Refuse("Display map unavailable at release.");
Check(!refused.IsDragging && refused.Saved == persisted && refused.Error == "Display map unavailable at release.", "Release-time geometry refusal discards preview and preserves a diagnosable prior placement");
bool contained = true;
foreach (var monitor in monitors.Concat(shrinking)) foreach (var scale in new[] { .75, 1.25, 2d, 4d })
    foreach (var fractions in new[] { (-1d, -1d), (0d, 0d), (.5d, .5d), (1d, 1d), (2d, 2d) })
        foreach (var size in new[] { (12d, 10d), (420d, 650d), (9000d, 9000d) }) {
            var candidate = new NotchPlacementPreference { Mode = "Detached", MonitorId = monitor.Id, XFraction = fractions.Item1, YFraction = fractions.Item2 };
            contained &= Inside(NotchPlacementPolicy.Resolve(candidate, [monitor], fallback, size.Item1 * scale, size.Item2 * scale, scale));
        }
Check(contained, "240 synthetic combinations of negative origins, scaling, fraction bounds and oversized windows remain inside work areas");

Console.WriteLine($"ALL {checks} NOTCH54 PLACEMENT CHECKS PASSED; pure geometry/state, JSON and injected persistence only. No desktop enumeration, windows, mouse input/capture, model, network or profile writes.");
