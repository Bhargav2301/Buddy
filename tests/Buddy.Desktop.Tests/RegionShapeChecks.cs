using Buddy.Windows;

internal static class RegionShapeChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var bounds=new PixelBounds(-1600,-200,1400,900);
        var shape=new RegionShape([new(-1400,0),new(-1000,0),new(-1000,300),new(-1400,300)],bounds);
        check(shape.Contains(new PixelPosition(-1200,150))&&!shape.Contains(new PixelPosition(-900,150)),"Region polygon includes only its interior on a negative-origin display");
        check(shape.Contains(new PixelBounds(-1300,50,120,80))&&!shape.Contains(new PixelBounds(-1020,50,120,80)),"A partially enclosed control is excluded from regional context");
        var notch=new RegionShape([new(0,0),new(200,0),new(200,200),new(120,200),new(120,70),new(80,70),new(80,200),new(0,200)],new(0,0,300,300));
        check(!notch.Contains(new PixelBounds(50,50,100,100)),"A concave cut through a control is rejected even when all four corners are inside");
        foreach(double scale in new[]{1.0,1.5,2.0})foreach(var surface in new[]{bounds,new PixelBounds(1920,-1080,2560,1440)}){
            var p=new PixelPosition(84.5,121.25);var physical=RegionShape.FromLocal(p,surface,scale);
            check(RegionShape.ToLocal(physical,surface,scale)==p,$"Region coordinate round-trip preserves 100/150/200 percent scale ({scale}, origin {surface.Left})");
        }
        void Reject(IEnumerable<PixelPosition> points,PixelBounds allowed,string name){bool rejected=false;try{_ = new RegionShape(points,allowed);}catch(InvalidOperationException){rejected=true;}check(rejected,name);}
        Reject([new(0,0),new(10,0),new(10,10)],new(0,0,100,100),"Tiny gestures cannot become region context");
        Reject([new(0,0),new(100,0),new(100,100)],new(0,0,90,90),"A polygon outside the selected monitor/window is rejected");
        Reject([new(double.NaN,0),new(100,0),new(100,100)],new(0,0,500,500),"Nonfinite region coordinates are rejected");
        Reject([new(0,0),new(5000,0),new(5000,5000)],new(0,0,6000,6000),"Oversize region allocations are rejected");
        Reject(Enumerable.Repeat(new PixelPosition(40,40),513),new(0,0,100,100),"Region input is bounded to 512 points");
        var prefs=new DesktopPreferences();check(!prefs.RegionSelectionEnabled&&!prefs.TrianglePointerEnabled,"Region and triangle add-ons are off by default");
        bool conflict=false;var registered=new HashSet<int>();using var chord=new ShortcutRegistration((id,_)=>!conflict&&registered.Add(id),id=>registered.Remove(id),8,9);
        check(chord.TrySet(ShortcutChoice.RegionChoices[0]),"Region chord reserves its separate registration IDs");
        var old=chord.Active;conflict=true;
        check(!chord.TrySet(ShortcutChoice.RegionChoices[1])&&chord.Active==old&&registered.Count==1,"Conflicting region chord preserves the previous registration");
        chord.Dispose();check(registered.Count==0,"Disabling the region add-on releases its reserved chord");
    }
}
