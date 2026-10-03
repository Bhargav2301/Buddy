using Buddy.Server;
using System.Windows;
using System.Security.Cryptography;

namespace Buddy.Windows;

// One explicit summon/Next request owns each observation. No timers capture content.
// This class has no action runner, input dispatch, foreground activation or shell capability.
internal sealed class VoiceTeaching : IDisposable
{
    private readonly ScreenPerception perception;
    private readonly Func<BuddyService?> service;
    private readonly VisualGrounding visual;
    private readonly Func<bool> contextAllowed;
    private readonly Func<bool> regionAllowed;
    private RegionLease? region;
    private readonly GuidanceOverlay overlay = new();
    private readonly List<string> suggestions = [];
    private IntPtr window;
    private string query = "";
    private string app = "";
    private int revision;
    internal bool CanContinue { get; private set; }
    internal bool IsPointing => overlay.IsVisible;
    internal bool HasExplicitRegion => region?.ExplicitOnce == true;
    internal event Action? Invalidated;
    internal VoiceTeaching(ScreenPerception perception, Func<BuddyService?> service, Action<ScreenElement?>? pointing = null, Func<bool>? contextAllowed = null, Func<bool>? regionAllowed = null)
    {
        this.perception = perception; this.service = service; visual = new(perception, service); this.contextAllowed = contextAllowed ?? (() => true);
        this.regionAllowed = regionAllowed ?? (() => false);
        if (pointing is not null) overlay.TargetChanged += pointing;
        overlay.Invalidated += () => Invalidated?.Invoke();
    }
    internal void Begin(IntPtr selectedWindow, string task, RegionLease? selectedRegion = null) { Cancel(); window = selectedWindow; query = task; region = selectedRegion; if(region is not null) region.Invalidated += RegionInvalidated; }
    private void RegionInvalidated() { overlay.Clear(); CanContinue=false; Invalidated?.Invoke(); }
    private void CheckScope()
    {
        if (!(region is null ? contextAllowed() : regionAllowed() || region.ExplicitOnce)) throw new InvalidOperationException("Screen context was turned off. Summon Buddy for a text-only answer.");
        region?.Check();
        perception.Check(window);
        var foreground = Native.GetForegroundWindow();
        if (foreground != window && !Native.IsOwnWindow(foreground))
            throw new InvalidOperationException("Return to the selected app, then choose Next step. Summon Buddy again to choose another app.");
    }
    internal async Task<TeachingTurn> Next(CancellationToken ct)
    {
        int current = revision; overlay.Clear();
        if (window == IntPtr.Zero || query.Length == 0) throw new InvalidOperationException("Summon Buddy to begin a lesson.");
        if (suggestions.Count >= 8) return new("Let's start a fresh lesson before continuing. Focus the app and tell me the next goal.");
        CheckScope();
        var before = await perception.Capture(window, ct, region is null?GuideSafety.RequestedText(query):null); ct.ThrowIfCancellationRequested();
        if (app.Length > 0 && app != before.Context.App) throw new InvalidOperationException("The selected application changed. Summon Buddy again.");
        app = before.Context.App;
        var host = service() ?? throw new InvalidOperationException("Buddy is still starting.");
        await host.Audit("teaching", app, "Explicit one-step observation; no screenshot stored");
        byte[]? regionImage = null;
        byte[]? regionFingerprint = null;
        if (region is not null) {
            using var full = await perception.Frame(before, ct) ?? throw new InvalidOperationException("Buddy cannot safely capture that area. Focus the source app and select again.");
            using var cropped=region.Crop(full); before=region.Filter(before); regionFingerprint=SHA256.HashData(cropped.Image); regionImage=cropped.ForVision();
        }
        TeachingTurn turn;
        try { turn = await host.Teach(new(query, before.Context, suggestions.ToArray(), regionImage is null ? null : Convert.ToBase64String(regionImage)), ct); }
        finally { if(regionImage is not null) Array.Clear(regionImage); }
        ct.ThrowIfCancellationRequested(); if (revision != current) throw new OperationCanceledException();
        CheckScope();
        if(region is not null && regionFingerprint is not null) {
            var currentRegionSnapshot=await perception.Capture(window,ct);
            using var currentFull=await perception.Frame(currentRegionSnapshot,ct) ?? throw new InvalidOperationException("The selected area is no longer available. Circle it again.");
            using var currentCrop=region.Crop(currentFull);
            if(!CryptographicOperations.FixedTimeEquals(regionFingerprint,SHA256.HashData(currentCrop.Image))) { CanContinue=false; return new(TeachingPolicy.Unverified); }
        }
        if (turn.Step is not { } step) { CanContinue = false; return turn; }
        var fresh = await perception.Capture(window, ct, region is null?GuideSafety.RequestedText(query):null); ct.ThrowIfCancellationRequested();
        if(region is not null) fresh=region.Filter(fresh);
        if (revision != current) throw new OperationCanceledException();
        if (fresh.Context.App != before.Context.App || fresh.Context.Title != before.Context.Title)
            return new(TeachingPolicy.Unverified);
        var element = GroundingResolver.Resolve(fresh.Context.Elements, step.Ref, step.Target, step.Role);
        if(element is not null&&fresh.WordTargets?.TryGetValue(element.Ref,out var word)==true){overlay.Draw(window,element,step.Primitive,step.Target,()=>word.IsCurrent(element));}
        else if (element is not null && (step.Target.Length == 0 || element.Name.Equals(step.Target, StringComparison.OrdinalIgnoreCase)) && fresh.Nodes.TryGetValue(element.Ref, out var node)) {
            var original = new Rect(element.X, element.Y, element.Width, element.Height);
            var bounds = WindowCapture.Bounds(window); var title = fresh.Context.Title;
            overlay.Draw(window, element, step.Primitive, step.Target, () => {
                try { return WindowCapture.Bounds(window) == bounds && Security.Redact(Native.Label(window)) == title &&
                    !node.Current.IsOffscreen && node.Current.BoundingRectangle == original && Security.Redact(node.Current.Name ?? "") == element.Name; }
                catch { return false; }
            });
        } else if (step.Ref.Length == 0 && await visual.Resolve(fresh, step, ct, region) is { CanExecute: false } grounded) {
            ct.ThrowIfCancellationRequested(); if (revision != current) throw new OperationCanceledException(); CheckScope();
            var bounds = WindowCapture.Bounds(window); var title = fresh.Context.Title;
            overlay.Draw(window, grounded.Element, step.Primitive, step.Target, () => {
                try { return WindowCapture.Bounds(window) == bounds && Security.Redact(Native.Label(window)) == title; } catch { return false; }
            });
        } else return new(TeachingPolicy.Unverified);
        ct.ThrowIfCancellationRequested();
        suggestions.Add(step.Instruction); CanContinue = true;
        return turn;
    }
    internal void Cancel() { revision++; overlay.Clear(); region?.Dispose(); region=null; suggestions.Clear(); query = app = ""; window = IntPtr.Zero; CanContinue = false; }
    public void Dispose() { Cancel(); overlay.Dispose(); }
}
