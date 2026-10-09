using Buddy.Server;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Buddy.Windows;

internal sealed class RegionSelectionWindow : Window
{
    private readonly RegionSelection initial;
    private readonly PixelBounds surface;
    private readonly Action<RegionSelection?> completed;
    private readonly List<PixelPosition> points=[];
    private readonly Canvas canvas=new(){Background=new SolidColorBrush(Color.FromArgb(30,0,0,0))};
    private readonly Polyline line=new(){Stroke=BuddyTheme.Deep,StrokeThickness=3,IsHitTestVisible=false};
    private readonly TextBlock hint=new(){Text="Draw around one area. Release the shortcut or press Enter to use it. Esc cancels.",Foreground=BuddyTheme.Ink,TextWrapping=TextWrapping.Wrap,MaxWidth=500,Margin=new(14),IsHitTestVisible=false};
    private bool drawing,finished,ready;
    internal RegionSelectionWindow(IntPtr window,Action<RegionSelection?> completed)
    {
        this.completed=completed;
        var bounds=WindowCapture.Bounds(window);if(!OverlayNative.GetCursorPos(out var cursor))throw new InvalidOperationException("Pointer unavailable.");
        var monitor=System.Windows.Forms.Screen.FromPoint(new(cursor.X,cursor.Y));var mb=monitor.Bounds;
        var scope=Rect.Intersect(bounds,new Rect(mb.X,mb.Y,mb.Width,mb.Height));if(scope.IsEmpty||scope.Width<24||scope.Height<24)throw new InvalidOperationException("Place the pointer over the window you want to select.");
        surface=new(scope.X,scope.Y,scope.Width,scope.Height);
        initial=new(window,InputNative.ProcessName(window),Security.Redact(Native.Label(window)),monitor.DeviceName,new(mb.X,mb.Y,mb.Width,mb.Height),bounds,1,DateTimeOffset.UtcNow,null!);
        Title="Buddy - select a region";WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;ShowInTaskbar=false;
        var root=new Grid();canvas.Children.Add(line);root.Children.Add(canvas);
        root.Children.Add(new Border{Background=BuddyTheme.Surface,BorderBrush=BuddyTheme.ControlBorder,BorderThickness=new(1),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top,Child=hint,IsHitTestVisible=false});Content=root;
        SourceInitialized+=(_,_)=>{OverlayNative.Configure(new WindowInteropHelper(this).Handle,false);OverlayNative.SetBounds(this,(int)scope.X,(int)scope.Y,(int)scope.Width,(int)scope.Height);};
        Loaded+=(_,_)=>{ready=true;Activate();Focus();};
        DpiChanged+=(_,_)=>{if(ready)Cancel();};
        Deactivated+=(_,_)=>{if(ready&&!finished)Cancel();};
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){e.Handled=true;Cancel();}else if(e.Key==Key.Enter){e.Handled=true;Finish();}};
        canvas.MouseLeftButtonDown+=(_,e)=>{points.Clear();line.Points.Clear();drawing=true;canvas.CaptureMouse();Add(e.GetPosition(canvas));e.Handled=true;};
        canvas.MouseMove+=(_,e)=>{if(drawing)Add(e.GetPosition(canvas));};
        canvas.MouseLeftButtonUp+=(_,e)=>{if(!drawing)return;Add(e.GetPosition(canvas));drawing=false;canvas.ReleaseMouseCapture();if(line.Points.Count>2)line.Points.Add(line.Points[0]);e.Handled=true;};
        Closing+=(_,_)=>{if(!finished){finished=true;completed(null);}};
    }
    private void Add(Point local)
    {
        if(points.Count>=512)return;var screen=PointToScreen(local);
        var p=new PixelPosition(Math.Clamp(screen.X,surface.Left,surface.Left+surface.Width),Math.Clamp(screen.Y,surface.Top,surface.Top+surface.Height));
        if(points.Count>0&&Math.Abs(points[^1].X-p.X)+Math.Abs(points[^1].Y-p.Y)<3)return;
        points.Add(p);var point=PointFromScreen(new(p.X,p.Y));line.Points.Add(point);
    }
    internal void Finish()
    {
        if(finished)return;
        try{var shape=new RegionShape(points,surface);if(WindowCapture.Bounds(initial.Window)!=initial.WindowBounds)throw new InvalidOperationException("The window moved. Cancel and select again.");
            var selection=initial with{Shape=shape,Scale=VisualTreeHelper.GetDpi(this).DpiScaleX,SelectedAt=DateTimeOffset.UtcNow};finished=true;canvas.ReleaseMouseCapture();Hide();Close();completed(selection);
        }catch(Exception ex){hint.Text=ex.Message+" Draw again, then press Enter; Esc cancels.";}
    }
    internal void Cancel(){if(finished)return;finished=true;canvas.ReleaseMouseCapture();Hide();Close();completed(null);}
}
