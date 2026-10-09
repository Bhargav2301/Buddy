using SharpVectors.Converters;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;
using System.Windows.Media.Animation;

namespace Buddy.Windows;

// The supplied character already includes eyes and a mouth: never layer a second face over it.
internal sealed class CompanionFace : Grid
{
    private readonly SvgViewbox halo;
    private readonly FrameworkElement body;
    private readonly FacialRig? facialRig;
    private readonly Grid pose = new();
    private readonly RotateTransform tilt = new();
    private readonly TranslateTransform gaze = new();
    private readonly ScaleTransform expression = new(1,1);
    private readonly bool compact;
    private readonly System.Windows.Threading.DispatcherTimer pulse = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private CompanionMood currentMood;
    private bool pointing;
    private bool compactPointer;
    private readonly System.Windows.Shapes.Polygon triangle = new() { Points=new PointCollection([new(8,36),new(61,9),new(61,63)]), Fill=BuddyTheme.Deep,Stroke=BuddyTheme.Surface,StrokeThickness=2,Opacity=0,Width=72,Height=72,IsHitTestVisible=false };
    private readonly RotateTransform triangleRotation=new(0,36,36);
    internal CompanionFace(bool compact = false)
    {
        this.compact = compact; Width = Height = compact ? 40 : CompanionPresentation.FaceSize; IsHitTestVisible = false;
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        halo = Asset("91dcc.svg", 72, 72); halo.Visibility = Visibility.Collapsed; Children.Add(halo);
        if(compact)body=AppBranding.Image(40);else body=facialRig=new FacialRig();
        body.HorizontalAlignment = HorizontalAlignment.Center; body.VerticalAlignment = VerticalAlignment.Center;
        pose.Children.Add(body); Children.Add(pose);
        pose.RenderTransformOrigin=new(.5,.5);pose.RenderTransform=new TransformGroup{Children={expression,tilt,gaze}};SetMood(CompanionMood.Idle);
        if(!compact){triangle.RenderTransform=triangleRotation;Children.Add(triangle);}
        pulse.Tick += (_, _) => halo.Opacity = BuddyTheme.Animate ? .6 + .4 * (.5 + .5 * Math.Sin(Environment.TickCount64 * Math.PI / 600)) : 1;
        Loaded += (_, _) => { if (currentMood == CompanionMood.Listening) pulse.Start(); };
        Unloaded += (_, _) => pulse.Stop();
    }
    internal static SvgViewbox Asset(string file, double width, double height) => new() {
        Source = new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Figma", file)),
        Width = width, Height = height, Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
    };
    internal void SetMood(CompanionMood mood)
    {
        currentMood = mood; pulse.Stop(); halo.Opacity = 1;
        facialRig?.Set(mood,0,0,Environment.TickCount64);
        if (mood == CompanionMood.Listening && IsLoaded && !IsPointer) pulse.Start();
        halo.Visibility = !compact && !IsPointer && mood == CompanionMood.Listening ? Visibility.Visible : Visibility.Collapsed;
        if(!IsPointer) body.Opacity = mood == CompanionMood.Sleeping ? .55 : 1;
        AutomationProperties.SetName(this, "Buddy - " + mood);
    }
    internal bool IsPointer => pointing || compactPointer;
    internal void SetCompactPointer(bool value)
    {
        if (compact || compactPointer == value) return;
        compactPointer = value;
        Width = Height = value ? CompanionPresentation.PointerSize : CompanionPresentation.FaceSize;
        triangle.LayoutTransform = value ? new ScaleTransform(44.0 / 72, 44.0 / 72) : Transform.Identity;
        body.Visibility = value ? Visibility.Collapsed : Visibility.Visible;
        ApplyPointer(false);
        if (!IsPointer) PointAngle(0);
        SetMood(currentMood);
    }
    internal void LookToward(double dx,double dy)
    {
        if(compact||IsPointer)return;
        if(!BuddyTheme.Animate){tilt.Angle=0;gaze.X=gaze.Y=0;expression.ScaleX=expression.ScaleY=1;facialRig?.Set(currentMood,0,0,Environment.TickCount64);return;}
        double x=Math.Clamp(dx/200,-1,1),y=Math.Clamp(dy/200,-1,1);
        facialRig?.Set(currentMood,x,y,Environment.TickCount64);
        tilt.Angle+=(x*7+(currentMood==CompanionMood.Unsure?-8:0)-tilt.Angle)*.15;
        gaze.X+=(x*2-gaze.X)*.15;gaze.Y+=(y*2-gaze.Y)*.15;
        double beat=Math.Sin(Environment.TickCount64*Math.PI/400);
        var sy=currentMood switch{CompanionMood.Listening=>1.04,CompanionMood.Speaking=>1+.025*beat,CompanionMood.Thinking=>.97,CompanionMood.Sleeping=>.90,_=>1.0};
        expression.ScaleY+=(sy-expression.ScaleY)*.15;expression.ScaleX+=(2-sy-expression.ScaleX)*.15;
    }
    internal void PointAngle(double angle) => triangleRotation.Angle=angle;
    internal void SetPointer(bool value)
    {
        if(compact || value==pointing)return;pointing=value;
        ApplyPointer(BuddyTheme.Animate && !compactPointer);
        SetMood(currentMood);
    }
    private void ApplyPointer(bool animate)
    {
        bool value = IsPointer;
        body.BeginAnimation(OpacityProperty,null);triangle.BeginAnimation(OpacityProperty,null);
        var scale=new ScaleTransform(value?1:.55,value?1:.55);body.RenderTransformOrigin=new(.5,.5);body.RenderTransform=scale;
        if(animate){
            var duration=TimeSpan.FromMilliseconds(180);
            body.BeginAnimation(OpacityProperty,new DoubleAnimation(value?1:0,value?0:1,duration));
            triangle.BeginAnimation(OpacityProperty,new DoubleAnimation(value?0:1,value?1:0,duration));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(value ? .55 : 1,duration));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(value ? .55 : 1,duration));
        }else{body.Opacity=value?0:1;triangle.Opacity=value?1:0;scale.ScaleX=scale.ScaleY=1;}
        halo.Visibility=Visibility.Collapsed;
    }
}
