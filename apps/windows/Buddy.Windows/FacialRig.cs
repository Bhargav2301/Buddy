using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Buddy.Windows;

// Runtime companion rendering only. The original logo/ICO is never modified.
// Supplied body includes a face: opaque mint masks remove it before the supplied pieces draw once.
internal sealed class FacialRig:FrameworkElement
{
    private static readonly BitmapSource body=(BitmapSource)AppBranding.Character;
    private static readonly BitmapSource eyes=Load("eyes.png"),mouth=Load("mouth.png");
    private static readonly BitmapSource left=new CroppedBitmap(eyes,new Int32Rect(0,0,79,79)),right=new CroppedBitmap(eyes,new Int32Rect(eyes.PixelWidth-79,0,79,79));
    private static readonly Brush leftMint=Mint(126,177,278),rightMint=Mint(358,177,278),mouthMint=Mint(242,232,286);
    private CompanionMood mood;private double dx,dy;private long time;
    internal FacialRig(){Width=Height=64;IsHitTestVisible=false;}
    internal void Set(CompanionMood mood,double x,double y,long time){this.mood=mood;dx=Math.Clamp(x,-1,1);dy=Math.Clamp(y,-1,1);this.time=time;InvalidateVisual();}
    private static BitmapSource Load(string name){var b=new BitmapImage();b.BeginInit();b.CacheOption=BitmapCacheOption.OnLoad;b.UriSource=new Uri(Path.Combine(AppContext.BaseDirectory,"Assets","Branding","Expressions",name));b.EndInit();b.Freeze();return b;}
    private static Brush Mint(int x,int top,int bottom){
        var source=new FormatConvertedBitmap(body,PixelFormats.Bgra32,null,0);
        Color Pixel(int y){var data=new byte[4];source.CopyPixels(new Int32Rect(x,y,1,1),data,4,0);return Color.FromRgb(data[2],data[1],data[0]);}
        var brush=new LinearGradientBrush(Pixel(top),Pixel(bottom),90);brush.Freeze();return brush;
    }
    protected override void OnRender(DrawingContext dc)
    {
        double scale=Math.Min(ActualWidth/body.PixelWidth,ActualHeight/body.PixelHeight);
        if(scale<=0)return;
        dc.PushTransform(new TranslateTransform((ActualWidth-body.PixelWidth*scale)/2,(ActualHeight-body.PixelHeight*scale)/2));dc.PushTransform(new ScaleTransform(scale,scale));
        dc.DrawImage(body,new Rect(0,0,body.PixelWidth,body.PixelHeight));
        if(BuddyTheme.Animate&&body.PixelWidth==485&&body.PixelHeight==387){
            // These opaque masks cover the complete baked eyes/mouth, including their antialiasing.
            dc.DrawEllipse(leftMint,null,new Point(126,226),42,42);dc.DrawEllipse(rightMint,null,new Point(358,226),42,42);
            dc.DrawRoundedRectangle(mouthMint,null,new Rect(205,240,75,38),7,7);
            double phase=time%5200;double blink=phase<4900?1:Math.Max(.045,Math.Abs(phase-5050)/150);
            double openness=mood==CompanionMood.Sleeping?.055:blink*(mood==CompanionMood.Thinking?.77:mood==CompanionMood.Listening?1.08:1);
            double gx=dx*6,gy=dy*4;
            dc.DrawImage(left,new Rect(89+gx,226.5-39.5*openness+gy,79,79*openness));
            double second=mood==CompanionMood.Unsure?openness*.67:openness;
            dc.DrawImage(right,new Rect(317+gx,226.5-39.5*second+gy,79,79*second));
            double width=mood==CompanionMood.Thinking?46:64;
            double height=mood==CompanionMood.Speaking?20+16*(.5+.5*Math.Sin(time*Math.PI/170)):28;
            if(mood is CompanionMood.Unsure or CompanionMood.Error){dc.PushTransform(new ScaleTransform(1,-1,242,259));dc.DrawImage(mouth,new Rect(242-width/2,243,width,height));dc.Pop();}
            else dc.DrawImage(mouth,new Rect(242-width/2,243,width,height));
        }
        dc.Pop();dc.Pop();
    }
}
