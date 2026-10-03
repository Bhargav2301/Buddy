using Buddy.Server;
using System.Windows;
using System.Windows.Threading;
using System.Drawing.Drawing2D;

namespace Buddy.Windows;

internal sealed record RegionSelection(IntPtr Window, string App, string Title, string MonitorId, PixelBounds MonitorBounds, Rect WindowBounds, double Scale, DateTimeOffset SelectedAt, RegionShape Shape);

internal sealed class RegionLease : IDisposable
{
    internal RegionSelection Selection { get; }
    internal bool ExplicitOnce { get; }
    private readonly GuideInputInvalidation input;
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private bool invalid, disposed;
    private readonly double sourceScale;
    internal event Action? Invalidated;
    internal RegionLease(RegionSelection selection, bool explicitOnce = false)
    {
        ExplicitOnce=explicitOnce;
        Selection=selection; sourceScale=OverlayNative.Scale(selection.Window);
        input=new GuideInputInvalidation(Invalidate, () => Native.GetForegroundWindow()==Selection.Window);
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplaysChanged;
    }
    private void DisplaysChanged(object? sender, EventArgs e) => dispatcher.BeginInvoke(new Action(Invalidate));
    private void Invalidate() { if(invalid || disposed)return;invalid=true;Invalidated?.Invoke(); }
    internal void Check()
    {
        if(disposed || invalid || DateTimeOffset.UtcNow-Selection.SelectedAt>TimeSpan.FromMinutes(5)) throw new InvalidOperationException("The selected area changed or expired. Circle it again.");
        var monitor=System.Windows.Forms.Screen.AllScreens.FirstOrDefault(s=>s.DeviceName==Selection.MonitorId);
        if(monitor is null || new PixelBounds(monitor.Bounds.X,monitor.Bounds.Y,monitor.Bounds.Width,monitor.Bounds.Height)!=Selection.MonitorBounds ||
            WindowCapture.Bounds(Selection.Window)!=Selection.WindowBounds || OverlayNative.Scale(Selection.Window)!=sourceScale || Security.Redact(Native.Label(Selection.Window))!=Selection.Title || InputNative.ProcessName(Selection.Window)!=Selection.App)
            throw new InvalidOperationException("The window or display changed. Circle the area again.");
    }
    internal ScreenSnapshot Filter(ScreenSnapshot snapshot)
    {
        Check(); if(snapshot.Window!=Selection.Window)throw new InvalidOperationException("The selected window changed.");
        var kept=snapshot.Context.Elements.Where(e=>Selection.Shape.Contains(new PixelBounds(e.X,e.Y,e.Width,e.Height))).ToList();
        return snapshot with { Context=snapshot.Context with { Elements=kept }, Nodes=snapshot.Nodes.Where(p=>kept.Any(e=>e.Ref==p.Key)).ToDictionary(p=>p.Key,p=>p.Value) };
    }
    internal CapturedWindow Crop(CapturedWindow frame)
    {
        Check(); if(frame.Bounds!=Selection.WindowBounds)throw new InvalidOperationException("The capture moved. Select again.");
        var area=Selection.Shape.Bounds; double sx=frame.PixelWidth/frame.Bounds.Width,sy=frame.PixelHeight/frame.Bounds.Height;
        int x=(int)Math.Floor((area.Left-frame.Bounds.Left)*sx),y=(int)Math.Floor((area.Top-frame.Bounds.Top)*sy);
        int right=(int)Math.Ceiling((area.Left+area.Width-frame.Bounds.Left)*sx),bottom=(int)Math.Ceiling((area.Top+area.Height-frame.Bounds.Top)*sy);
        if(x<0||y<0||right>frame.PixelWidth||bottom>frame.PixelHeight)throw new InvalidOperationException("Region pixels are outside the verified frame.");
        using var sourceStream=new MemoryStream(frame.Image);using var source=new System.Drawing.Bitmap(sourceStream);
        using var result=new System.Drawing.Bitmap(right-x,bottom-y,System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using(var graphics=System.Drawing.Graphics.FromImage(result)) {
            graphics.Clear(System.Drawing.Color.Black);
            using var path=new GraphicsPath(FillMode.Alternate);
            path.AddPolygon(Selection.Shape.Points.Select(p=>new System.Drawing.PointF((float)((p.X-frame.Bounds.Left)*sx-x),(float)((p.Y-frame.Bounds.Top)*sy-y))).ToArray());
            graphics.SetClip(path);graphics.DrawImage(source,new System.Drawing.Rectangle(0,0,result.Width,result.Height),new System.Drawing.Rectangle(x,y,result.Width,result.Height),System.Drawing.GraphicsUnit.Pixel);
        }
        using var output=new MemoryStream();result.Save(output,System.Drawing.Imaging.ImageFormat.Png);
        var bounds=new Rect(frame.Bounds.Left+x/sx,frame.Bounds.Top+y/sy,result.Width/sx,result.Height/sy);
        return new(output.ToArray(),bounds,result.Width,result.Height) { Text=frame.Text.Where(t=>Selection.Shape.Contains(new PixelBounds(t.Bounds.X,t.Bounds.Y,t.Bounds.Width,t.Bounds.Height))).ToList() };
    }
    public void Dispose(){if(disposed)return;disposed=true;input.Dispose();Microsoft.Win32.SystemEvents.DisplaySettingsChanged-=DisplaysChanged;}
}
