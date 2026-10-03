using Buddy.Server;
using Buddy.Windows;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;

// Own-window lease checks require no foreground takeover, screen capture or physical input.
internal static class RegionLifetimeChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var fixture=new Window{Title="Buddy region lease fixture",Width=400,Height=300};fixture.Show();
        try{
            var handle=new WindowInteropHelper(fixture).Handle;var bounds=WindowCapture.Bounds(handle);var screen=System.Windows.Forms.Screen.FromHandle(handle);var mb=screen.Bounds;
            var allowed=new PixelBounds(bounds.X,bounds.Y,bounds.Width,bounds.Height);
            var shape=new RegionShape([new(bounds.X+30,bounds.Y+50),new(bounds.X+150,bounds.Y+50),new(bounds.X+150,bounds.Y+160),new(bounds.X+30,bounds.Y+160)],allowed);
            var selection=new RegionSelection(handle,InputNative.ProcessName(handle),Security.Redact(Native.Label(handle)),screen.DeviceName,new(mb.X,mb.Y,mb.Width,mb.Height),bounds,OverlayNative.Scale(handle),DateTimeOffset.UtcNow,shape);
            bool Rejected(RegionLease lease){try{lease.Check();return false;}catch(InvalidOperationException){return true;}}
            using(var lease=new RegionLease(selection)){lease.Check();check(true,"A real owned-window region lease validates without taking foreground or capturing pixels");}
            using(var expired=new RegionLease(selection with{SelectedAt=DateTimeOffset.UtcNow.AddMinutes(-6)}))check(Rejected(expired),"Expired selected regions are refused in the current native build");
            using(var moved=new RegionLease(selection)){fixture.Left+=20;check(Rejected(moved),"Moving only the owned fixture invalidates its stored region");fixture.Left-=20;}
            using(var retitled=new RegionLease(selection)){fixture.Title="Changed owned fixture";check(Rejected(retitled),"Changed source title invalidates the current region");fixture.Title=selection.Title;}
            using(var display=new RegionLease(selection)){typeof(RegionLease).GetMethod("Invalidate",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(display,[]);check(Rejected(display),"A region invalidated by the shared display/input callback cannot be reused");}
            var preferences=new DesktopPreferences{RegionSelectionEnabled=true,ReadVoiceAnswers=false};var perception=new ScreenPerception(()=>preferences);
            using var voice=new VoiceOverlayWindow(()=>null,()=>Task.FromResult<string?>(null),()=>preferences,_=>{},()=>handle,perception,(_,_)=>throw new Exception("No action routing allowed"),()=>{});
            using var pending=new RegionLease(selection);voice.OpenRegion(pending);check(!voice.IsListening,"Default region handoff uses the actual microphone-off question surface");voice.Cancel();check(Rejected(pending)&&!voice.IsBusy&&!voice.IsListening,"Actual Stop disposes pending region context and keeps microphone off");
        }finally{fixture.Close();}
    }
}
