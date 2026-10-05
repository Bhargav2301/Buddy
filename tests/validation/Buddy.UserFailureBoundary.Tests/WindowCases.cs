using Buddy.Windows;

internal static class WindowCases
{
    private static readonly IntPtr Target = new(1001), Other = new(2002), Own = new(3003);
    private static readonly WindowProbe Valid = new(true, 11, 22, true, 0, 123456789, "owned-fixture", true, false);
    internal static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("window selection pins HWND thread PID creation time and process name", () => {
            var selected = WindowSelection.Capture(Target, _ => Valid);
            Require.True(selected.Window == Target && selected.ThreadId == 11 && selected.ProcessId == 22 && selected.ProcessStarted == 123456789 && selected.App == "owned-fixture", "Selection must retain process lifetime, not just an HWND.");
            selected.Validate(_ => Valid); return Task.CompletedTask;
        });
        foreach (var (name, probe, expected) in new[] {
            ("destroyed handle", Valid with { Exists = false }, "window-gone"),
            ("missing PID", Valid with { ProcessId = 0 }, "window-gone"),
            ("OpenProcess access denied", Valid with { ProcessOpened = false, NativeError = 5 }, "process-query"),
            ("unverified process creation time", Valid with { ProcessStarted = 0, NativeError = 6 }, "process-identity"),
            ("unknown elevation", Valid with { ElevationKnown = false, NativeError = 5 }, "elevation-unknown"),
            ("elevated application", Valid with { Elevated = true }, "elevated")
        }) await test("inspection refusal: " + name, () => {
            var error = Refused(() => WindowSelection.Capture(Target, _ => probe));
            Require.True(error.Code == expected && error.NativeError == probe.NativeError && error.Window == Target, "Preserve the specific failure and native error; never bypass inspection.");
            return Task.CompletedTask;
        });
        foreach (var (name, replacement) in new[] {
            ("same HWND new PID", Valid with { ProcessId = 23 }),
            ("same PID reused lifetime", Valid with { ProcessStarted = 123456790 }),
            ("same process different window thread", Valid with { ThreadId = 12 }),
            ("changed process identity", Valid with { App = "different-app" })
        }) await test("stale selection refuses " + name, () => {
            var selected = WindowSelection.Capture(Target, _ => Valid);
            Require.True(Refused(() => selected.Validate(_ => replacement)).Code == "window-replaced", "An old selection must never silently become a newly reused window/process.");
            return Task.CompletedTask;
        });
        await test("selection cache keeps failure until an explicit fresh valid observation", () => {
            long now = 100; WindowProbe current = Valid;
            var tracker = new WindowSelectionTracker(_ => current, () => now, h => h == Own, TimeSpan.FromSeconds(1), _ => true, _ => { });
            tracker.Observe(Target); Require.True(tracker.RequireCurrent().Window == Target, "Valid external observation must be usable.");
            current = Valid with { ProcessOpened = false, NativeError = 5 };
            Require.True(Refused(() => tracker.RequireCurrent()).Code == "process-query", "A fresh denial invalidates the cached identity.");
            current = Valid; tracker.Observe(Own);
            Require.True(Refused(() => tracker.RequireCurrent()).Code == "process-query", "An owned-window observation must not clear a prior access error or fall back.");
            tracker.Observe(Other);
            Require.True(tracker.RequireCurrent().Window == Other, "A new explicit valid external observation can establish a fresh selection.");
            now = 1101;
            Require.True(Refused(() => tracker.RequireCurrent()).Code == "selection-expired", "Expired selections require fresh observation.");
            tracker.Clear(); Require.True(Refused(() => tracker.RequireCurrent()).Code == "no-selection", "Clear removes the entire remembered selection.");
            return Task.CompletedTask;
        });
        await test("identity is rechecked after privacy/access policy work", () => {
            WindowProbe current = Valid;
            var tracker = new WindowSelectionTracker(_ => current, () => 100, _ => false, TimeSpan.FromSeconds(1), _ => true,
                _ => current = current with { ProcessStarted = current.ProcessStarted + 1 });
            tracker.Observe(Target);
            Require.True(Refused(() => tracker.RequireCurrent()).Code == "window-replaced", "A replaced process during policy checks cannot enter the cache.");
            return Task.CompletedTask;
        });
        await test("policy denial cannot retain the previous accessible selection", () => {
            bool denied = false;
            var tracker = new WindowSelectionTracker(_ => Valid, () => 100, _ => false, TimeSpan.FromSeconds(1), _ => true,
                _ => { if (denied) throw new InvalidOperationException("Owned fixture privacy denial"); });
            tracker.Observe(Target); denied = true; tracker.Observe(Other);
            Require.True(Refused(() => tracker.RequireCurrent()).Code == "window-unavailable", "A denied newly observed target must not fall back to the old one.");
            return Task.CompletedTask;
        });
    }
    private static WindowSelectionException Refused(Action action)
    {
        try { action(); } catch (WindowSelectionException e) { return e; }
        throw new Exception("Expected window-selection refusal.");
    }
}
