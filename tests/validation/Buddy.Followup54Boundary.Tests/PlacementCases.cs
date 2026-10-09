#if HAS_PLACEMENT54
using Buddy.Windows;

internal static class PlacementCases {
    internal static void Run(Checks c) {
        NotchPlacementMonitor[] monitors=[new("left",new(-1920,-200,1920,1080)),new("right",new(0,0,2560,1400))];
        var saved=new NotchPlacementPreference{Mode="Detached",MonitorId="left",XFraction=.25,YFraction=.75};
        var x=NotchPlacementPolicy.Resolve(saved,monitors,new(500,500),400,200,1);
        c.Check(x.Monitor.Id=="left"&&x.Bounds.Left<0&&x.Bounds.Top>0,"PLACE-NEGATIVE","Stored monitor selection survives negative desktop origins.");
        c.Check(x.Bounds.Left>=-1912&&x.Bounds.Left+x.Bounds.Width<=-8&&x.Bounds.Top+x.Bounds.Height<=872,"PLACE-FIT","Detached bounds remain inside work area margins.");
        var recovered=NotchPlacementPolicy.Resolve(saved,[monitors[1]],new(50,50),400,200,2);
        c.Check(recovered.RecoveredMissingMonitor&&recovered.Monitor.Id=="right"&&recovered.Bounds.Left>=16,"PLACE-MISSING","An unavailable monitor recovers onto current work space.");
        c.Check(saved.MonitorId=="left"&&saved.XFraction==.25,"PLACE-NO-AUTO-SAVE","Resolving a missing monitor does not mutate saved preferences.");
        foreach(double scale in new[]{.5,1,1.25,1.5,2,8}) {
            var r=NotchPlacementPolicy.Resolve(saved,monitors,new(0,0),8000,8000,scale);
            c.Check(double.IsFinite(r.Bounds.Left)&&r.Bounds.Width>0&&r.Bounds.Height>0&&r.Bounds.Left>=-1920&&r.Bounds.Top>=-200&&r.Bounds.Left+r.Bounds.Width<=0&&r.Bounds.Top+r.Bounds.Height<=880,
                "PLACE-SCALE-"+scale,"Oversized bar fits physical work area without crossing display edges.");
        }
        var tiny=NotchPlacementPolicy.Resolve(saved,[new("tiny",new(-2,-3,1,1))],new(0,0),900,900,2);
        c.Check(tiny.Bounds==new PixelBounds(-2,-3,1,1),"PLACE-TINY","Tiny work area yields a finite contained result.");
        var invalid=NotchPlacementPreference.Normalize(saved with{XFraction=double.NaN});
        c.Check(invalid.Mode=="Top","PLACE-BAD-PREF","Nonfinite stored coordinates reset to safe placement.");
        c.Rejects<InvalidOperationException>(()=>NotchPlacementPolicy.Resolve(saved,[new("bad",new(0,0,double.NaN,5))],new(0,0),10,10,1),"PLACE-NO-DISPLAY","Invalid work areas cannot become placement authority.");
        var state=new NotchPlacementState(saved); int saves=0;
        c.Check(state.Begin(new(120,120),new(100,100,300,80),1),"PLACE-BEGIN","A valid drag creates a transient owner.");
        state.Move(new(123,122)); state.Resolve(monitors,new(0,0),300,80,1); state.Finish(p=>{saves++;return p;});
        c.Check(saves==0&&state.Saved==saved,"PLACE-CLICK","Subthreshold motion does not save a drag.");
        state.Begin(new(120,120),new(100,100,300,80),1); state.Move(new(500,400));
        var preview=state.Resolve(monitors,new(0,0),300,80,2);
        c.Check(preview.Bounds.Left==460&&preview.Bounds.Top==360&&state.Saved==saved,"PLACE-DPI-GRAB","Preview keeps the DIP grab offset across DPI change and remains unsaved.");
        state.Cancel(); state.Finish(p=>{saves++;return p;});
        c.Check(saves==0&&state.Saved==saved&&!state.IsDragging,"PLACE-CANCEL","Lost capture/Stop cancellation cannot later commit a stale preview.");
        state.Begin(new(120,120),new(100,100,300,80),1); state.Move(new(500,400)); state.Resolve(monitors,new(0,0),300,80,1);
        c.Check(!state.Finish(_=>throw new IOException("owned injected failure"))&&state.Saved==saved&&state.Error is not null,"PLACE-SAVE-FAIL","Failed persistence retains prior saved placement and visible error.");
        state.Begin(new(120,120),new(100,100,300,80),1); state.Move(new(500,400)); var next=state.Resolve(monitors,new(0,0),300,80,1);
        c.Check(state.Finish(p=>{saves++;return p;})&&saves==1&&state.Saved==next.Preference&&state.Error is null,"PLACE-COMMIT","Only successful explicit release commits the current preview once.");
        state.Finish(p=>{saves++;return p;}); c.Check(saves==1,"PLACE-NO-REPLAY","A repeated release does not replay persistence.");
        c.Check(state.Commit(saved,p=>p with{YFraction=.125})&&state.Saved.YFraction==.125,"PLACE-MERGED-EFFECTIVE","Successful persistence adopts actual merged coordinates returned by the store.");
        var previous=state.Saved;
        c.Check(!state.Commit(saved,_=>null!)&&state.Saved==previous,"PLACE-NULL-STORE","Missing effective persistence result cannot silently reset placement.");
    }
}
#endif
