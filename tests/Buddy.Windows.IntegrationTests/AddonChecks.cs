using Buddy.Server;
using Buddy.Windows;
using Microsoft.AspNetCore.DataProtection;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static class AddonChecks
{
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]private static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
    private static object Field(object value,string name)=>value.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(value)!;
    private static Task Call(object value,string name,params object[] args)=>(Task)value.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(value,args)!;
    internal static int Run(bool useRealModel=false)
    {
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int count=0,exit=1;
        void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);count++;Console.WriteLine("PASS: "+name);}
        var fixture=new Window{Title="Buddy optional add-on fixture",Width=640,Height=470};
        var chosen=new Button{Content="Selected control",Width=160,Height=48,HorizontalAlignment=HorizontalAlignment.Left};AutomationProperties.SetName(chosen,"Selected control");
        var outside=new Button{Content="Outside private marker",Height=45,Margin=new(0,125,0,0)};AutomationProperties.SetName(outside,"Outside private marker");
        var panel=new StackPanel{Margin=new(45)};panel.Children.Add(chosen);panel.Children.Add(outside);fixture.Content=panel;
        var folder=Path.Combine(Path.GetTempPath(),"Buddy-addon-tests-"+Guid.NewGuid());Directory.CreateDirectory(folder);
        fixture.Loaded+=async(_,_)=>{
            VoiceOverlayWindow? voice=null;CursorCompanionWindow? companion=null;
            try{
                var window=new WindowInteropHelper(fixture).Handle;ScreenPerception.PracticeHandle=window;
                OverlayNative.GetCursorPos(out var cursor);var screen=System.Windows.Forms.Screen.FromPoint(new(cursor.X,cursor.Y));var work=screen.WorkingArea;
                OverlayNative.SetBounds(fixture,work.X+40,work.Y+40,Math.Min(640,work.Width-80),Math.Min(470,work.Height-80));
                for(int attempt=0;attempt<3&&Native.GetForegroundWindow()!=window;attempt++){fixture.Activate();InputNative.SetForegroundWindow(window);await Task.Delay(250);}
                Check(Native.GetForegroundWindow()==window,"Region tests operate only on the owned foreground fixture");
                var preferences=new DesktopPreferences{RegionSelectionEnabled=true,CaptureOnVoice=false,ReadVoiceAnswers=false};var perception=new ScreenPerception(()=>preferences);
                var snapshot=await perception.Capture(window,default);var target=snapshot.Context.Elements.Single(e=>e.Name=="Selected control"&&e.Role=="Button");var bounds=WindowCapture.Bounds(window);var mb=screen.Bounds;
                RegionSelection Selection(RegionShape shape)=>new(window,snapshot.Context.App,snapshot.Context.Title,screen.DeviceName,new(mb.X,mb.Y,mb.Width,mb.Height),bounds,OverlayNative.Scale(window),DateTimeOffset.UtcNow,shape);
                var allowed=new PixelBounds(bounds.X,bounds.Y,bounds.Width,bounds.Height);
                var shape=new RegionShape([new(target.X-12,target.Y-12),new(target.X+target.Width+12,target.Y-12),new(target.X+target.Width+12,target.Y+target.Height+12),new(target.X-12,target.Y+target.Height+12)],allowed);
                using(var lease=new RegionLease(Selection(shape))){
                    var filtered=lease.Filter(snapshot);
                    Check(filtered.Context.Elements.Any(e=>e.Ref==target.Ref)&&filtered.Context.Elements.All(e=>e.Name!="Outside private marker"),"Regional UIA context excludes controls outside the selected polygon");
                    Check(filtered.Nodes.Count==filtered.Context.Elements.Count,"Only scoped native control references survive filtering");
                }
                double cx=bounds.Left+260,cy=bounds.Top+210;
                var diamond=new RegionShape([new(cx-90,cy),new(cx,cy-90),new(cx+90,cy),new(cx,cy+90)],allowed);
                using(var source=new System.Drawing.Bitmap((int)bounds.Width,(int)bounds.Height)){
                    using(var g=System.Drawing.Graphics.FromImage(source)){g.Clear(System.Drawing.Color.Lime);g.FillRectangle(System.Drawing.Brushes.Black,(float)(cx-bounds.X+15),(float)(cy-bounds.Y+15),12,12);}
                    using var stream=new MemoryStream();source.Save(stream,System.Drawing.Imaging.ImageFormat.Png);using var frame=new CapturedWindow(stream.ToArray(),bounds,source.Width,source.Height);
                    using var lease=new RegionLease(Selection(diamond));using var cropped=lease.Crop(frame);using var imageStream=new MemoryStream(cropped.Image);using var bitmap=new System.Drawing.Bitmap(imageStream);
                    Check(bitmap.Width==180&&bitmap.Height==180,"Region crop uses physical pixel bounds");
                    var corner=bitmap.GetPixel(2,2);var center=bitmap.GetPixel(90,90);var secret=bitmap.GetPixel(110,110);
                    Check(corner.R==0&&corner.G==0&&corner.B==0&&center.G>200,"Pixels outside the drawn polygon are black before model input");
                    Check(secret.R==0&&secret.G==0&&secret.B==0,"Existing private-pixel masks remain black after cropping");
                    cropped.Dispose();Check(cropped.Image.All(b=>b==0),"Disposing a regional frame clears its encoded buffer");
                }
                using(var moved=new RegionLease(Selection(shape))){fixture.Left+=15;await Task.Delay(100);bool rejected=false;try{moved.Check();}catch(InvalidOperationException){rejected=true;}Check(rejected,"Moving the selected source invalidates the region lease");fixture.Left-=15;await Task.Delay(100);}
                bounds=WindowCapture.Bounds(window);
                using(var expired=new RegionLease(Selection(shape) with {SelectedAt=DateTimeOffset.UtcNow.AddMinutes(-6)})){bool rejected=false;try{expired.Check();}catch(InvalidOperationException){rejected=true;}Check(rejected,"Expired selection cannot be reused as current context");}
                using(var changed=new RegionLease(Selection(shape))){typeof(RegionLease).GetMethod("DisplaysChanged",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(changed,[null,EventArgs.Empty]);await Task.Delay(60);bool rejected=false;try{changed.Check();}catch(InvalidOperationException){rejected=true;}Check(rejected,"Display-change notification invalidates the selected area");}
                RegionSelection? completed=null;int completions=0;
                var picker=new RegionSelectionWindow(window,s=>{completed=s;completions++;});picker.Show();await Task.Delay(100);
                foreach(var point in shape.Points){var local=picker.PointFromScreen(new(point.X,point.Y));typeof(RegionSelectionWindow).GetMethod("Add",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(picker,[local]);}
                picker.Finish();
                Check(completed is not null&&completions==1&&completed.Shape.Contains(new PixelPosition(target.X+target.Width/2,target.Y+target.Height/2)),"Native picker shares screen/local transforms with the completed polygon");
                var cancelPicker=new RegionSelectionWindow(window,s=>{if(s is null)completions++;});cancelPicker.Show();await Task.Delay(60);cancelPicker.Cancel();
                Check(completions==2&&!cancelPicker.IsVisible,"Cancel closes the interactive picker without submitting context");
                fixture.Activate();InputNative.SetForegroundWindow(window);await Task.Delay(150);
                var store=new StateStore(folder,DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(folder,"keys"))));await store.EnsureSaved();
                using var model=new RegionModel();using var http=new HttpClient(model){BaseAddress=new("http://127.0.0.1:11434")};var service=new BuddyService(store,new(http));
                voice=new(()=>service,()=>Task.FromResult<string?>(null),()=>preferences,_=>{},()=>window,perception,(_,_)=>throw new Exception("Region must not enter the action runner"),()=>{});
                voice.OpenRegion(new RegionLease(Selection(shape)));
                Check(!voice.IsListening&&((System.Windows.Controls.TextBox)Field(voice,"review")).IsVisible,"Selected-region question opens with the microphone off");
                await Call(voice,"Send","Explain this selected control");
                Check(model.Calls==1&&model.HadImage&&!model.LastInput.Contains("Outside private marker"),"Regional teaching sends only cropped image and scoped controls to local structured inference");
                Check(((TextBlock)Field(voice,"answer")).Text=="This is the selected control.","Region question uses the real teaching response path");
                voice.Cancel();Check(!voice.IsBusy&&!voice.IsListening,"Stop releases regional question state");
                if(useRealModel) {
                    fixture.Activate();InputNative.SetForegroundWindow(window);await Task.Delay(100);
                    using var localHttp=new HttpClient{BaseAddress=new("http://127.0.0.1:11434"),Timeout=TimeSpan.FromMinutes(3)};
                    var localService=new BuddyService(store,new(localHttp));using var lesson=new VoiceTeaching(perception,()=>localService,regionAllowed:()=>true);
                    lesson.Begin(window,"Read the exact label of the selected control. Do not click or change anything.",new RegionLease(Selection(shape)));
                    using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(3));var result=await lesson.Next(deadline.Token);
                    Console.WriteLine("REAL REGIONAL MODEL: "+result.Speech);
                    Check(result.Speech.Contains("Selected control",StringComparison.OrdinalIgnoreCase)&&ConversationalReply.IsConcise(result.Speech),"Installed local vision model answers from the deliberately cropped native fixture");
                }
                model.DuringInference=()=>{chosen.Content="Changed after observation";AutomationProperties.SetName(chosen,"Changed after observation");};
                voice.OpenRegion(new RegionLease(Selection(shape)));await Call(voice,"Send","Explain this area");
                Check(((TextBlock)Field(voice,"answer")).Text==TeachingPolicy.Unverified,"A region that changes during inference is rejected before the answer is presented");voice.Cancel();model.DuringInference=null;
                using(var inputLease=new RegionLease(Selection(shape))){
                    var input=Field(inputLease,"input");voice.Show();voice.Activate();await Task.Delay(60);
                    typeof(GuideInputInvalidation).GetMethod("Notify",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(input,[]);inputLease.Check();
                    fixture.Activate();InputNative.SetForegroundWindow(window);await Task.Delay(60);
                    typeof(GuideInputInvalidation).GetMethod("Notify",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(input,[]);await Task.Delay(60);
                    bool rejected=false;try{inputLease.Check();}catch(InvalidOperationException){rejected=true;}
                    Check(rejected,"Own prompt input is ignored without disabling later target-input invalidation");
                }
                BuddyTheme.Apply("Light",true);companion=new(()=>false);companion.Show();companion.SetTriangleEnabled(true);
                typeof(CursorCompanionWindow).GetMethod("Triangle",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(companion,[true]);
                var face=(CompanionFace)Field(companion,"glyph");var handle=new WindowInteropHelper(companion).Handle;
                Check(face.IsPointer&&(GetWindowLongPtr(handle,-20).ToInt64()&0x20)!=0,"Optional triangle is click-through and active with reduced motion");
                var triangle=(System.Windows.Shapes.Polygon)Field(face,"triangle");var body=(Image)Field(face,"body");
                Check(triangle.Opacity==1&&body.Opacity==0&&triangle.Points.Count==3,"Triangle state has three vertices and no duplicate character layer");
                typeof(CursorCompanionWindow).GetMethod("Triangle",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(companion,[false]);
                Check(!face.IsPointer&&(GetWindowLongPtr(handle,-20).ToInt64()&0x20)==0&&((Button)Field(companion,"menuButton")).IsHitTestVisible,"Ending triangle mode restores the real companion menu's interactivity");
                BuddyTheme.Apply("Light",false);face.SetPointer(true);await Task.Delay(240);Check(face.IsPointer&&triangle.Opacity>.99&&body.Opacity<.01,"Animated character-to-triangle transition reaches its final state");face.SetPointer(false);await Task.Delay(240);
                Check(body.Opacity>.99&&triangle.Opacity<.01,"Triangle transition reverses cleanly to the supplied character");
                Check(!Directory.GetFiles(folder,"*",SearchOption.AllDirectories).Any(f=>new[]{".png",".jpg",".wav"}.Contains(Path.GetExtension(f))),"Region interaction persists no screen or audio files");
                Console.WriteLine($"ALL {count} NATIVE REGION AND TRIANGLE CHECKS PASSED; physical gesture, mixed-monitor and third-party app acceptance remain separate");exit=0;
            }catch(Exception ex){Console.Error.WriteLine(ex);}finally{voice?.Dispose();companion?.Dispose();ScreenPerception.PracticeHandle=IntPtr.Zero;fixture.Close();Directory.Delete(folder,true);app.Shutdown();}
        };
        app.Run(fixture);return exit;
    }
    private sealed class RegionModel:HttpMessageHandler
    {
        internal int Calls;internal bool HadImage;internal string LastInput="";internal Action? DuringInference;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Calls++;using var data=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));var message=data.RootElement.GetProperty("messages")[1];HadImage=message.TryGetProperty("images",out var images)&&images.GetArrayLength()==1;LastInput=message.GetProperty("content").GetString()!;
            if(DuringInference is not null){DuringInference();await Task.Delay(150,ct);}
            return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{message=new{content=JsonSerializer.Serialize(new GuidePlan("This is the selected control.",[]),StateStore.Json)},done=true}),Encoding.UTF8,"application/json")};
        }
    }
}
