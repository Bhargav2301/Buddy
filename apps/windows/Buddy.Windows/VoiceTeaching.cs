using Buddy.Server;
using System.Windows;
using System.Security.Cryptography;
using System.Windows.Threading;

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
    private readonly Func<bool> rememberAllowed;
    private RegionLease? region;
    private readonly GuidanceOverlay overlay;
    private readonly List<string> suggestions = [];
    private readonly TeachingConversation conversation = new();
    private bool windowImage;
    private Task<bool>? packetValidation;
    private readonly DispatcherTimer historyMonitor=new(){Interval=TimeSpan.FromMilliseconds(300)};
    private IntPtr window;
    private string query = "";
    private string app = "";
    private int revision;
    internal bool CanContinue { get; private set; }
    internal bool IsPointing => overlay.IsVisible;
    internal bool HasRegion => region is not null;
    internal bool HasExplicitRegion => region?.ExplicitOnce == true;
    internal bool HasExplicitImage => HasExplicitRegion || windowImage;
    internal event Action? Invalidated;
    internal VoiceTeaching(ScreenPerception perception, Func<BuddyService?> service, Action<ScreenElement?>? pointing = null, Func<bool>? contextAllowed = null, Func<bool>? regionAllowed = null,Func<bool>? rememberAllowed=null, Func<int>? inkLifetimeSeconds = null)
    {
        this.perception = perception; this.service = service; visual = new(perception, service); this.contextAllowed = contextAllowed ?? (() => true);
        this.regionAllowed = regionAllowed ?? (() => false);
        this.rememberAllowed=rememberAllowed??(()=>false);
        overlay = new(inkLifetimeSeconds);
        if (pointing is not null) overlay.TargetChanged += pointing;
        overlay.Invalidated += () => Invalidated?.Invoke();
        historyMonitor.Tick+=(_,_)=>{
            try{var selected=conversation.Window;var foreground=Native.GetForegroundWindow();
                if(selected==IntPtr.Zero||foreground!=selected&&!Native.IsOwnWindow(foreground)||!conversation.IsCurrent(InputNative.ProcessName(selected),Security.Redact(Native.Label(selected)),DateTimeOffset.UtcNow)){conversation.Clear();historyMonitor.Stop();}}
            catch{conversation.Clear();historyMonitor.Stop();}
        };
    }
    internal void Begin(IntPtr selectedWindow, string task, RegionLease? selectedRegion = null, bool includeWindowImage=false) { Pause(); window = selectedWindow; query = task; region = selectedRegion; windowImage=includeWindowImage&&region is null; if(region is not null) region.Invalidated += RegionInvalidated; }
    internal void HideGuidance() { overlay.Clear(); CanContinue = false; }
    internal void ChangeQuestion(string task) { region?.Check(); query = task; suggestions.Clear(); overlay.Clear(); CanContinue = false; }
    private void RegionInvalidated() { overlay.Clear(); CanContinue=false; Invalidated?.Invoke(); }
    private void CheckScope()
    {
        if (!(region is null ? contextAllowed() || windowImage : regionAllowed() || region.ExplicitOnce)) throw new InvalidOperationException("Screen context was turned off. Summon Buddy for a text-only answer.");
        region?.Check();
        perception.Check(window);
        var foreground = Native.GetForegroundWindow();
        if (foreground != window && !Native.IsOwnWindow(foreground))
            throw new InvalidOperationException("Return to the selected app, then choose Next step. Summon Buddy again to choose another app.");
    }
    internal async Task<TeachingTurn> Next(CancellationToken ct)
    {
        int current = revision; overlay.Clear();CanContinue=false;
        if(packetValidation is {IsCompleted:false})throw new InvalidOperationException("The app's accessibility provider is still responding. Try again shortly.");
        if (window == IntPtr.Zero || query.Length == 0) throw new InvalidOperationException("Summon Buddy to begin a lesson.");
        if (suggestions.Count >= 8) return new("Let's start a fresh lesson before continuing. Focus the app and tell me the next goal.");
        CheckScope();
        var before = await perception.Capture(window, ct, region is null?GuideSafety.RequestedText(query):null); ct.ThrowIfCancellationRequested();
        if (app.Length > 0 && app != before.Context.App) throw new InvalidOperationException("The selected application changed. Summon Buddy again.");
        app = before.Context.App;
        var prior=conversation.For(window,app,before.Context.Title,DateTimeOffset.UtcNow);
        var saved=new List<TeachingExchange>();
        if(rememberAllowed()){
            foreach(var entry in (await TeachingMemory.Open().Read(app,ct)).Reverse()){
                if(saved.Sum(e=>e.Question.Length+e.Answer.Length)+entry.Question.Length+entry.Answer.Length>12000)break;
                saved.Insert(0,new(entry.Question,entry.Answer));
            }
        }
        var host = service() ?? throw new InvalidOperationException("Buddy is still starting.");
        await host.Audit("teaching", app, "Explicit one-step observation; no screenshot stored");
        byte[]? regionImage = null;
        byte[]? regionFingerprint = null;
        if (region is not null) {
            using var full = await perception.Frame(before, ct) ?? throw new InvalidOperationException("Buddy cannot safely capture that area. Focus the source app and select again.");
            using var cropped=region.Crop(full); before=region.Filter(before); regionFingerprint=SHA256.HashData(cropped.Image); regionImage=cropped.ForVision();
        }
        else if(windowImage){using var frame=await perception.Frame(before,ct)??throw new InvalidOperationException("Buddy cannot safely capture this window. Try an explicit smaller area or a text-only question.");regionImage=frame.ForVision();}
        TeachingTurn turn;
        try { turn = await host.Teach(new(query, before.Context, suggestions.ToArray(), regionImage is null ? null : Convert.ToBase64String(regionImage),prior,windowImage?"window":"region",saved), ct); }
        finally { if(regionImage is not null) Array.Clear(regionImage); }
        ct.ThrowIfCancellationRequested(); if (revision != current) throw new OperationCanceledException();
        CheckScope();
        if(region is not null && regionFingerprint is not null) {
            var currentRegionSnapshot=await perception.Capture(window,ct);
            using var currentFull=await perception.Frame(currentRegionSnapshot,ct) ?? throw new InvalidOperationException("The selected area is no longer available. Circle it again.");
            using var currentCrop=region.Crop(currentFull);
            if(!CryptographicOperations.FixedTimeEquals(regionFingerprint,SHA256.HashData(currentCrop.Image))) { CanContinue=false; return new(TeachingPolicy.Unverified); }
        }
        async Task<TeachingTurn> Remember(TeachingTurn value){ct.ThrowIfCancellationRequested();if(revision!=current)throw new OperationCanceledException();conversation.Add(query,value.Speech,DateTimeOffset.UtcNow);historyMonitor.Start();if(rememberAllowed())await TeachingMemory.Open().Save(app,query,value.Speech,ct);return value;}
        var fresh = await perception.Capture(window, ct, region is null?GuideSafety.RequestedText(query):null); ct.ThrowIfCancellationRequested();
        if(region is not null) fresh=region.Filter(fresh);
        if (revision != current) throw new OperationCanceledException();
        if (fresh.Context.App != before.Context.App || fresh.Context.Title != before.Context.Title)
            {conversation.Clear();return new(TeachingPolicy.Unverified);}
        if(turn.Targets.Count==0)return await Remember(turn);
        var marks=new List<GuidanceMark>();
        var visualTargets=new List<(ScreenElement Element,string Role)>();
        foreach(var step in turn.Targets){
        var element = GroundingResolver.Resolve(fresh.Context.Elements, step.Ref, step.Target, step.Role);
        if(element is not null&&fresh.WordTargets?.TryGetValue(element.Ref,out var word)==true){marks.Add(new(element,step.Primitive,step.Target,()=>word.IsCurrent(element)));}
        else if (element is not null && (step.Target.Length == 0 || element.Name.Equals(step.Target, StringComparison.OrdinalIgnoreCase)) && fresh.Nodes.TryGetValue(element.Ref, out var node)) {
            var original = new Rect(element.X, element.Y, element.Width, element.Height);
            var bounds = WindowCapture.Bounds(window); var title = fresh.Context.Title;
            marks.Add(new(element, step.Primitive, step.Target, () => {
                try { return WindowCapture.Bounds(window) == bounds && Security.Redact(Native.Label(window)) == title &&
                    !node.Current.IsOffscreen && node.Current.BoundingRectangle == original && Security.Redact(node.Current.Name ?? "") == element.Name; }
                catch { return false; }
            }));
        } else if (step.Ref.Length == 0 && await visual.Resolve(fresh, step, ct, region) is { CanExecute: false } grounded) {
            ct.ThrowIfCancellationRequested(); if (revision != current) throw new OperationCanceledException(); CheckScope();
            var bounds = WindowCapture.Bounds(window); var title = fresh.Context.Title;
            visualTargets.Add((grounded.Element,step.Role));
            marks.Add(new(grounded.Element, step.Primitive, step.Target, () => {
                try { return WindowCapture.Bounds(window) == bounds && Security.Redact(Native.Label(window)) == title; } catch { return false; }
            }));
        } else return await Remember(new(TeachingPolicy.Unverified));
        }
        ct.ThrowIfCancellationRequested();
        CheckScope();if(revision!=current)throw new OperationCanceledException();
        if(visualTargets.Count>0){
            var finalSnapshot=await perception.Capture(window,ct);
            if(finalSnapshot.Context.App!=before.Context.App||finalSnapshot.Context.Title!=before.Context.Title)return await Remember(new(TeachingPolicy.Unverified));
            foreach(var target in visualTargets)if(!await visual.StillVisible(finalSnapshot,target.Element,target.Role,ct))return await Remember(new(TeachingPolicy.Unverified));
        }
        packetValidation=Task.Run(()=>marks.All(m=>m.Valid?.Invoke()!=false),ct);
        try{if(!await packetValidation.WaitAsync(TimeSpan.FromMilliseconds(500),ct))return await Remember(new(TeachingPolicy.Unverified));}
        catch(TimeoutException){return await Remember(new(TeachingPolicy.Unverified));}
        ct.ThrowIfCancellationRequested();CheckScope();if(revision!=current)throw new OperationCanceledException();
        overlay.DrawMany(window,marks);
        suggestions.AddRange(turn.Targets.Select(s=>s.Instruction)); CanContinue = true;
        return await Remember(turn);
    }
    internal void Pause() { revision++; overlay.Clear(); region?.Dispose(); region=null; suggestions.Clear(); query = app = ""; window = IntPtr.Zero; CanContinue = false;windowImage=false; }
    internal void Cancel() { Pause();conversation.Clear();historyMonitor.Stop(); }
    public void Dispose() { Cancel(); overlay.Dispose(); }
}
