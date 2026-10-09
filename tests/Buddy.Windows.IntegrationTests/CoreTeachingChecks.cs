using Buddy.Server;
using Buddy.Windows;
using Microsoft.AspNetCore.DataProtection;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

internal static class CoreTeachingChecks
{
    internal static async Task Policy(Action<bool,string> check)
    {
        var now=DateTimeOffset.UtcNow;var memory=new TeachingConversation();
        memory.For((IntPtr)1,"fixture","A",now);memory.Add("first question","first answer",now);
        check(memory.For((IntPtr)1,"fixture","A",now).Count==1,"Same-window follow-up retains only deliberate Q&A");
        check(memory.For((IntPtr)2,"fixture","A",now).Count==0,"A different window clears follow-up history");
        memory.Add("q","a",now);check(memory.For((IntPtr)2,"fixture","B",now).Count==0,"Title change clears follow-up history");
        memory.Add("q","a",now);check(memory.For((IntPtr)2,"other","B",now).Count==0,"Application change clears follow-up history");
        memory.Add("q","a",now);check(memory.For((IntPtr)2,"other","B",now.AddMinutes(11)).Count==0,"Ten-minute expiry clears follow-up history");
        for(int i=0;i<15;i++)memory.Add("question "+i,"answer",now);check(memory.For((IntPtr)2,"other","B",now).Count==10,"History keeps at most ten exchanges");
        memory.Clear();check(memory.For((IntPtr)2,"other","B",now).Count==0,"New lesson removes prior context");
        var arrays=new List<byte[]>();var order=new List<int>();int renders=0;
        await SentencePlayback.Run(["First.","Second.","Third."],(text,ct)=>{int n=++renders;var b=new[]{(byte)n};arrays.Add(b);return Task.FromResult(b);},async(b,ct)=>{int n=b[0];order.Add(n);check(renders==Math.Min(3,n+1),"Playback prefetches only one later sentence");await Task.Delay(5,ct);},default);
        check(order.SequenceEqual(new[]{1,2,3})&&arrays.All(a=>a.All(x=>x==0)),"Sentence playback preserves order and clears played/prefetched audio buffers");
        bool pendingCancelled=false;renders=0;using var cancel=new CancellationTokenSource();bool stopped=false;
        try{await SentencePlayback.Run(["First.","Second."],async(text,ct)=>{if(++renders==1)return new byte[]{1};try{await Task.Delay(30000,ct);}catch(OperationCanceledException){pendingCancelled=true;throw;}return new byte[]{2};},(b,ct)=>{cancel.Cancel();ct.ThrowIfCancellationRequested();return Task.CompletedTask;},cancel.Token);}catch(OperationCanceledException){stopped=true;}
        check(stopped&&pendingCancelled,"Stop cancels both current playback and in-flight sentence prefetch");
        check(SentencePlayback.Parts("Keep the original. Check the destination. Ask before saving.").Length==3,"All safety-qualified answer sentences remain in the playback queue");
    }
    internal static int Run(bool realVision=false)
    {
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int count=0,exit=1;
        void Check(bool ok,string note){if(!ok)throw new Exception("FAIL: "+note);count++;Console.WriteLine("PASS: "+note);}
        var root=Path.Combine(Path.GetTempPath(),"Buddy-core42-"+Guid.NewGuid());Directory.CreateDirectory(root);
        var fixture=new Window{Title="Buddy scoped visual fixture",Width=600,Height=390};
        var export=new Button{Content="Export",Height=45};var format=new Button{Content="Format",Height=45};var secret=new PasswordBox{Password="fixture-only-private",Height=40};
        AutomationProperties.SetName(export,"Export");AutomationProperties.SetName(format,"Format");AutomationProperties.SetName(secret,"Secret fixture");
        var stack=new StackPanel{Margin=new(20)};stack.Children.Add(new ChartCanvas{Height=90});stack.Children.Add(export);stack.Children.Add(format);stack.Children.Add(secret);fixture.Content=stack;int clicks=0;export.Click+=(_,_)=>clicks++;format.Click+=(_,_)=>clicks++;
        fixture.Loaded+=async(_,_)=>{
            VoiceTeaching? teacher=null;
            try {
                await Policy(Check);var hwnd=new WindowInteropHelper(fixture).Handle;ScreenPerception.PracticeHandle=hwnd;fixture.Activate();InputNative.SetForegroundWindow(hwnd);await Task.Delay(180);
                var perception=new ScreenPerception(()=>new());var store=new StateStore(root,DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(root,"keys"))));await store.EnsureSaved();
                using var mock=new Model();using var client=new HttpClient(mock){BaseAddress=new("http://127.0.0.1:11434")};var service=new BuddyService(store,new(client));
                bool allowed=false;teacher=new(perception,()=>service,contextAllowed:()=>allowed);
                teacher.Begin(hwnd,"Explain the chart.",includeWindowImage:true);var result=await teacher.Next(default);
                Check(mock.ImageSupplied&&result.Speech.Contains("two bars")&&!teacher.IsPointing,"Explicit selected-window image reaches local vision and permits a canvas explanation with no invented target");
                Check(!mock.Input.Contains("fixture-only-private")&&!mock.Input.Contains("Secret fixture"),"Selected-window teaching omits password controls from textual evidence");
                allowed=true;teacher.Pause();teacher.Begin(hwnd,"What about Beta?");await teacher.Next(default);
                Check(!mock.ImageSupplied&&mock.Input.Contains("Explain the chart.")&&mock.Input.Contains("two bars"),"Follow-up survives a microphone pause but no image is reused or silently attached");
                teacher.Pause();fixture.Title="Buddy new document fixture";teacher.Begin(hwnd,"Explain the new document.");await teacher.Next(default);
                Check(!mock.Input.Contains("What about Beta?"),"Changing the document title resets the actual teaching request history");
                mock.Multiple=true;teacher.Begin(hwnd,"Compare Export and Format.");result=await teacher.Next(default);
                Check(result.Targets.Count==2&&app.Windows.Cast<Window>().Count(w=>w.Title=="Buddy guidance ink")==2,"Two independently verified native targets render together");
                teacher.Pause();mock.During=()=>{format.Content="Changed";AutomationProperties.SetName(format,"Changed");};teacher.Begin(hwnd,"Compare the controls.");result=await teacher.Next(default);mock.During=null;
                Check(result.Speech==TeachingPolicy.Unverified&&!teacher.IsPointing,"One target changing during inference suppresses the entire stale annotation packet");
                Check(clicks==0&&secret.Password=="fixture-only-private","Visual teaching never invokes controls or edits the fixture");
                teacher.Cancel();mock.Multiple=false;teacher.Begin(hwnd,"Fresh question.");await teacher.Next(default);Check(!mock.Input.Contains("Compare the controls."),"Stop starts the next lesson without old Q&A");
                Check(!Directory.GetFiles(root,"*",SearchOption.AllDirectories).Any(p=>new[]{".png",".jpg",".wav"}.Contains(Path.GetExtension(p))),"Scoped visual questions persist no image or audio files");
                if(realVision){
                    teacher.Cancel();fixture.Activate();InputNative.SetForegroundWindow(hwnd);await Task.Delay(120);
                    var observed=await perception.Capture(hwnd,default);Check(!observed.Context.Elements.Any(e=>e.Name.Contains("Alpha")||e.Name.Contains("Beta")),"Synthetic chart labels are pixels only, absent from UIA context");
                    using var local=new HttpClient{BaseAddress=new("http://127.0.0.1:11434"),Timeout=TimeSpan.FromMinutes(2)};
                    var localService=new BuddyService(store,new(local));using var liveTeacher=new VoiceTeaching(perception,()=>localService);
                    liveTeacher.Begin(hwnd,"Describe the two bars in the selected image. Which named bar is larger? Do not give clicking instructions.",includeWindowImage:true);
                    using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(2));var live=await liveTeacher.Next(deadline.Token);Console.WriteLine("LOCAL SYNTHETIC VISION: "+live.Speech);
                    Check(live.Targets.Count==0&&ConversationalReply.IsConcise(live.Speech)&&live.Speech.Contains("Alpha",StringComparison.OrdinalIgnoreCase)&&live.Speech!=TeachingPolicy.Unverified,"Installed local vision model explains the pixel-only chart through the production selected-window path");
                }
                Console.WriteLine($"ALL {count} CORE TEACHING CHECKS PASSED (owned window and mock inference; no Blender/Grok/physical audio claim)");exit=0;
            }catch(Exception e){Console.Error.WriteLine(e);}finally{teacher?.Dispose();ScreenPerception.PracticeHandle=IntPtr.Zero;fixture.Close();app.Shutdown();}
        };app.Run(fixture);return exit;
    }
    private sealed class ChartCanvas:FrameworkElement
    {
        protected override void OnRender(DrawingContext drawing){
            drawing.DrawRectangle(Brushes.White,null,new Rect(0,0,500,90));
            void Bar(string name,double y,double width,Brush fill){drawing.DrawRectangle(fill,null,new Rect(110,y,width,24));drawing.DrawText(new FormattedText(name,System.Globalization.CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI"),18,Brushes.Black,1),new Point(5,y));}
            Bar("Alpha 12",8,300,Brushes.SteelBlue);Bar("Beta 7",48,175,Brushes.Goldenrod);
        }
    }
    private sealed class Model:HttpMessageHandler
    {
        internal bool ImageSupplied,Multiple;internal string Input="";internal Action? During;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));var message=body.RootElement.GetProperty("messages")[1];Input=message.GetProperty("content").GetString()!;ImageSupplied=message.TryGetProperty("images",out var images)&&images.GetArrayLength()==1;
            using var first=First(Input);var elements=first.RootElement.GetProperty("untrustedScreen").GetProperty("elements");
            var steps=Multiple?elements.EnumerateArray().Where(e=>e.GetProperty("role").GetString()=="Button").Take(2).Select(e=>new GuideStep("Look at "+e.GetProperty("name").GetString()+".",e.GetProperty("ref").GetString()!,e.GetProperty("name").GetString()!,"Button","circle")).ToList():[];
            During?.Invoke();var plan=new GuidePlan(Multiple?string.Join(" ",steps.Select(s=>s.Instruction)):"The visible chart has two bars.",steps);
            return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{message=new{content=JsonSerializer.Serialize(plan,StateStore.Json)},done=true}),Encoding.UTF8,"application/json")};
        }
        private static JsonDocument First(string value){var reader=new Utf8JsonReader(Encoding.UTF8.GetBytes(value));return JsonDocument.ParseValue(ref reader);}
    }
}
