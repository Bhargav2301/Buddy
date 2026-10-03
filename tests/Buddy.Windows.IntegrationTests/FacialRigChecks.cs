using Buddy.Windows;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class FacialRigChecks
{
    internal static int Run(){var app=new Application();int count=0;void Check(bool ok,string text){if(!ok)throw new Exception("FAIL: "+text);count++;Console.WriteLine("PASS: "+text);}
        try{
            BuddyTheme.Apply("Light",false);var output=Path.GetFullPath("validation/features38-faces");Directory.CreateDirectory(output);
            byte[] Render(string name,CompanionMood mood,double x,long time,bool still=false){BuddyTheme.Apply("Light",still);var rig=new FacialRig{Width=485,Height=387};rig.Set(mood,x,0,time);rig.Measure(new Size(485,387));rig.Arrange(new Rect(0,0,485,387));rig.UpdateLayout();var bitmap=new RenderTargetBitmap(485,387,96,96,PixelFormats.Pbgra32);bitmap.Render(rig);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(output,name+".png")))encoder.Save(stream);var pixels=new byte[485*387*4];bitmap.CopyPixels(pixels,485*4,0);return pixels;}
            (int Count,double X) Dark(byte[] pixels,int x0,int y0,int width,int height){int n=0;double total=0;for(int y=y0;y<y0+height;y++)for(int x=x0;x<x0+width;x++){int p=(y*485+x)*4;if(pixels[p+3]>200&&pixels[p]<85&&pixels[p+1]<85&&pixels[p+2]<85){n++;total+=x;}}return(n,n==0?0:total/n);}
            var left=Render("look-left",CompanionMood.Idle,-1,0);var right=Render("look-right",CompanionMood.Idle,1,0);var blink=Render("blink",CompanionMood.Idle,0,5050);var normal=Render("neutral",CompanionMood.Idle,0,0);
            var dl=Dark(left,75,175,105,105);var dr=Dark(right,75,175,105,105);
            Check(dr.X-dl.X>9&&Math.Abs(dl.Count-dr.Count)<80,"Supplied eye moves independently toward the pointer without leaving a second eye behind");
            Check(Dark(blink,75,175,105,105).Count<Dark(normal,75,175,105,105).Count*.15&&Dark(blink,308,175,105,105).Count<Dark(normal,308,175,105,105).Count*.15,"Both baked eyes are fully masked when the supplied eyes blink");
            var talkA=Render("speaking-wide",CompanionMood.Speaking,0,85);var talkB=Render("speaking-narrow",CompanionMood.Speaking,0,255);
            Check(Math.Abs(Dark(talkA,200,238,85,50).Count-Dark(talkB,200,238,85,50).Count)>100,"Mouth animation changes independently during speech");
            var reduced=Render("reduced-motion-original",CompanionMood.Speaking,1,5050,true);var baseline=Render("reduced-motion-still",CompanionMood.Idle,0,0,true);
            Check(reduced.SequenceEqual(baseline),"Reduced motion renders the unmodified original character for every pose/time");
            bool outside=true;for(int y=0;y<387;y++)for(int x=0;x<485;x++){if((x>=78&&x<=174&&y>=180&&y<=275)||(x>=310&&x<=406&&y>=180&&y<=275)||(x>=201&&x<=284&&y>=237&&y<=284))continue;int p=(y*485+x)*4;for(int c=0;c<4;c++)if(normal[p+c]!=baseline[p+c])outside=false;}
            Check(outside,"Silhouette and all pixels outside the face regions remain the supplied artwork");
            Render("unsure",CompanionMood.Unsure,0,0);Render("sleeping",CompanionMood.Sleeping,0,0);
            Console.WriteLine($"ALL {count} NATIVE FACIAL RIG CHECKS PASSED; rendered previews: {output}");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}finally{app.Shutdown();}
    }
}
