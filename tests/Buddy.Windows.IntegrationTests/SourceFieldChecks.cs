using Buddy.Server;
using Buddy.Windows;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Interop;

internal static class SourceFieldChecks
{
    const string Original="Write a poem on a boat sailing on a lonely sea";
    const string Improved="Please draft a poem on a boat sailing on a lonely sea.";
    static object? Get(object o,string name)=>o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(o);
    static void Set(object o,string name,object? value)=>o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(o,value);
    static Task Call(object o,string name,params object[] args)=>(Task)o.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public)!.Invoke(o,args)!;
    internal static int Target(string ready)
    {
        var app=new Application();var window=new Window{Title="Buddy owned external prompt fixture",Width=680,Height=740};
        var first=new TextBox{Text=Original,Height=200,AcceptsReturn=true};AutomationProperties.SetName(first,"Message fixture");
        var second=new TextBox{Text="Other field must stay unchanged",Height=50};AutomationProperties.SetName(second,"Other fixture");
        var stack=new StackPanel{Margin=new(30)};stack.Children.Add(first);stack.Children.Add(second);
        var document=new DocumentField{Text="Contenteditable fixture draft",Height=60};AutomationProperties.SetName(document,"Grok-like writable document");stack.Children.Add(document);
        var rich=new RichTextBox{Height=60};rich.Document.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("Text pattern without writable value")));AutomationProperties.SetName(rich,"Text-only contenteditable");stack.Children.Add(rich);
        var readOnly=new TextBox{Text="Read-only draft",IsReadOnly=true,Height=40};AutomationProperties.SetName(readOnly,"Read-only fixture");stack.Children.Add(readOnly);
        var secret=new PasswordBox{Height=40,Password="fixture-only-secret"};AutomationProperties.SetName(secret,"Password fixture");stack.Children.Add(secret);window.Content=stack;
        window.Loaded+=(_,_)=>{first.Focus();File.WriteAllText(ready,new WindowInteropHelper(window).Handle.ToInt64().ToString());};app.Run(window);return 0;
    }
    internal static int Run()
    {
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int count=0,exit=1;
        void Check(bool pass,string text){if(!pass)throw new Exception("FAIL: "+text);count++;Console.WriteLine("PASS: "+text);}
        var root=Path.Combine(Path.GetTempPath(),"Buddy-source39-"+Guid.NewGuid());Directory.CreateDirectory(root);
        var home=new MainWindow(startService:false);var ready=Path.Combine(root,"fixture.hwnd");Process? child=null;BuddyHost? host=null;
        home.Loaded+=async(_,_)=>{
            try {
                using var model=new Model();host=await BuddyHost.Start(Path.Combine(root,"profile"),FreePort(),model.Address,loopbackOnly:true);
                Set(home,"host",host);
                var info=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};info.ArgumentList.Add("--source39-target");info.ArgumentList.Add(ready);child=Process.Start(info)!;
                for(int i=0;i<100&&!File.Exists(ready);i++){if(child.HasExited)throw new Exception("Owned target exited");await Task.Delay(50);}
                var hwnd=new IntPtr(long.Parse(File.ReadAllText(ready)));Native.GetWindowThreadProcessId(hwnd,out var owner);
                Check(owner==child.Id&&!Native.IsOwnWindow(hwnd),"Source is a separate owned fixture process, never Buddy Home or a third-party app");
                var perception=new ScreenPerception(()=>new());
                var editor=new FocusedFieldEditor(perception);
                var assistant=new DesktopAssistant(()=>host.Service,()=>new(),()=>hwnd,_=>{});Set(home,"assistant",assistant);Set(home,"fieldEditor",editor);
                var watcher=new LocalPromptWatcher(editor,()=>host.Service,()=>{},()=>"fixture shortcut");Set(home,"promptWatcher",watcher);
                var companion=new CursorCompanionWindow(()=>false);Set(home,"companion",companion);companion.SetEnabled(true);
                var input=(TextBox)Get(home,"input")!;input.Text=Original;
                var node=await Task.Run(()=>AutomationElement.FromHandle(hwnd).FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.NameProperty,"Message fixture")));
                async Task Focus(){InputNative.SetForegroundWindow(hwnd);await Task.Run(()=>node.SetFocus());await Task.Delay(120);if(Native.GetForegroundWindow()!=hwnd)throw new Exception("Owned target foreground not granted");}
                string Read()=>((ValuePattern)node.GetCurrentPattern(ValuePattern.Pattern)).Current.Value;
                async Task Wait(Func<bool> predicate){for(int i=0;i<160&&!predicate();i++)await Task.Delay(50);if(!predicate())throw new TimeoutException("Expected review state did not arrive: "+((TextBlock)Get(home,"status")!).Text);}
                await Focus();
                Check(await editor.Probe(hwnd,default) is null,"Passive eligibility still excludes an unknown application");
                Check(await editor.Probe(hwnd,default,explicitInvocation:true) is not null,"Production explicit eligibility accepts a verified writable field without an app/title/name allowlist");
                foreach(var label in new[]{"Grok-like writable document","Text-only contenteditable","Read-only fixture","Password fixture"}){
                    var candidate=await Task.Run(()=>AutomationElement.FromHandle(hwnd).FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.NameProperty,label)));
                    await Task.Run(()=>candidate.SetFocus());await Task.Delay(100);
                    if(label.StartsWith("Grok-like")){
                        var draft=await editor.Capture(hwnd,default,strictFocus:true,explicitInvocation:true);
                        Check(candidate.Current.ControlType==ControlType.Document,"Owned contenteditable capability fixture exposes Document role plus writable ValuePattern");
                        await editor.Apply(draft,"Reviewed document replacement",default);Check(((ValuePattern)candidate.GetCurrentPattern(ValuePattern.Pattern)).Current.Value=="Reviewed document replacement","Document-role replacement is verified in the exact source field");
                        await editor.Undo(draft,default);Check(((ValuePattern)candidate.GetCurrentPattern(ValuePattern.Pattern)).Current.Value=="Contenteditable fixture draft","Document-role Undo restores the exact original");
                    }else{bool refused=false;try{refused=await editor.Probe(hwnd,default,explicitInvocation:true) is null;}catch(InvalidOperationException){refused=true;}Check(refused,"Unsupported/private field refused without keyboard or clipboard fallback: "+label);}
                }
                await Focus();await Call(home,"RememberSourceField");var pinned=(SummonedField)Get(home,"summonedField")!;
                Check(pinned.Anchor.Window==hwnd&&pinned.Anchor.Identity.Length>0,"Summon captures the exact external HWND and field identity before Buddy takes focus");
                Check(typeof(SummonedField).GetProperties().All(p=>p.Name is "Anchor" or "Title" or "CapturedAt"),"The summon lease contains metadata only; no cached prompt text");
                home.Activate();input.Focus();await Task.Delay(100);await Call(home,"RefineFocusedField",null!,false,true);
                await Wait(()=>Get(watcher,"bubble") is InlinePromptWindow c&&Get(c,"proposal") is string);
                var card=(InlinePromptWindow)Get(watcher,"bubble")!;
                Check((string)Get(card,"proposal")! == Improved&&await Task.Run(Read)==Original&&input.Text==Original,"Actual MainWindow route refines the pinned external prompt with local mocked inference and changes neither identical draft before Accept");
                Check(card.IsInFieldPreview&&Get(home,"refineWindow") is null,"Explicit source refinement uses the original-field diff overlay, not the separate Buddy-draft window");
                await Call(card,"Apply");Check(await Task.Run(Read)==Improved&&input.Text==Original,"Accept updates only the original external fixture; the identical Buddy Home composer stays unchanged");
                await Call(card,"Undo");Check(await Task.Run(Read)==Original&&input.Text==Original,"Undo restores only the original source field");card.Close();
                await Focus();await Call(home,"RememberSourceField");Set(home,"summonedField",pinned with{CapturedAt=Environment.TickCount64-120001});home.Activate();await Call(home,"RefineFocusedField",null!,false,true);
                Check(Get(watcher,"bubble") is null&&await Task.Run(Read)==Original&&((TextBlock)Get(home,"status")!).Text.Contains("expired"),"Expired source lease refuses before refinement or edits");
                Set(home,"summonedField",pinned with{Title="Changed source context",CapturedAt=Environment.TickCount64});await Call(home,"RefineFocusedField",null!,false,true);
                Check(Get(watcher,"bubble") is null&&await Task.Run(Read)==Original&&((TextBlock)Get(home,"status")!).Text.Contains("changed or expired"),"Changed source title refuses the remembered-field route before inference or editing");
                Set(home,"summonedField",pinned with{Anchor=pinned.Anchor with{Identity="different-runtime-field"},CapturedAt=Environment.TickCount64});await Call(home,"RefineFocusedField",null!,false,true);
                Check(Get(watcher,"bubble") is null&&await Task.Run(Read)==Original&&((TextBlock)Get(home,"status")!).Text.Contains("focused field changed"),"A stale UIA field identity refuses replacement even when the original HWND is valid");
                Set(home,"summonedField",null);home.Activate();input.Focus();await Call(home,"RefineFocusedField",null!,false,true);
                Check(Get(watcher,"bubble") is null&&input.Text==Original&&((TextBlock)Get(home,"status")!).Text.Contains("No external"),"External-refine route refuses Buddy's own composer when no external lease exists");
                await Focus();await Call(home,"RememberSourceField");home.Activate();await Call(home,"RefineFocusedField",null!,false,true);await Wait(()=>Get(watcher,"bubble") is InlinePromptWindow c&&Get(c,"proposal") is string);
                await Task.Run(()=>((ValuePattern)node.GetCurrentPattern(ValuePattern.Pattern)).SetValue("The user's newer external draft"));
                await Wait(()=>Get(watcher,"bubble") is null);
                Check(await Task.Run(Read)=="The user's newer external draft"&&input.Text==Original,"Scoped UIA text-change events dismiss an actual explicit review when the user edits the original field");
                // Begin an independent focus-invalidation case with the faithful canned rewrite's original draft.
                await Task.Run(()=>((ValuePattern)node.GetCurrentPattern(ValuePattern.Pattern)).SetValue(Original));
                await Focus();await Call(home,"RememberSourceField");home.Activate();await Call(home,"RefineFocusedField",null!,false,true);await Wait(()=>Get(watcher,"bubble") is InlinePromptWindow c&&Get(c,"proposal") is string);
                var other=await Task.Run(()=>AutomationElement.FromHandle(hwnd).FindFirst(TreeScope.Descendants,new PropertyCondition(AutomationElement.NameProperty,"Other fixture")));await Task.Run(()=>other.SetFocus());await Wait(()=>Get(watcher,"bubble") is null);
                Check(await Task.Run(Read)==Original,"Changing focused field dismisses the source review without overwriting either field");
                home.Show();home.Activate();home.Close();await Task.Delay(150);
                Check(!home.IsVisible&&companion.IsVisible&&app.ShutdownMode==ShutdownMode.OnExplicitShutdown,"Closing Home hides it while the configured companion and explicit app lifetime remain active");
                Check(!FullscreenPolicy.Suppress("Progman",0,true)&&!FullscreenPolicy.Suppress("WorkerW",0,true)&&!FullscreenPolicy.Suppress("Chrome_WidgetWin_1",0x00C00000,true)&&FullscreenPolicy.Suppress("Owned borderless fullscreen",0,true),"Desktop shell and ordinary maximized apps preserve the companion; actual borderless fullscreen remains suppressible");
                Check(!ResearchIntent.UseWeb(Original,true)&&ResearchIntent.UseWeb("Research sources and write a poem about sea voyages",true)&&!ResearchIntent.UseWeb("latest news",false),"Creative prompt stays local unless research is requested; disabled research never runs");
                child.CloseMainWindow();await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));child.Dispose();child=null;
                await home.Quit();host=null;
                Check((bool)Get(home,"shuttingDown")!&&(bool)Get(companion,"disposed")!,"Exit Buddy actually disposes the companion and shuts down the application");
                Console.WriteLine($"ALL {count} SOURCE FIELD AND RESIDENT LIFETIME CHECKS PASSED; separate owned fixture, no third-party GUI driven");exit=0;
            }catch(Exception ex){Console.Error.WriteLine(ex);}
            finally{
                if(child is {HasExited:false}){child.CloseMainWindow();if(!child.WaitForExit(2000))child.Kill();}child?.Dispose();
                if(host is not null)await home.Quit();if(Directory.Exists(root))Directory.Delete(root,true);app.Shutdown();
            }
        };app.Run(home);return exit;
    }
    sealed class DocumentField:TextBox { protected override AutomationPeer OnCreateAutomationPeer()=>new DocumentPeer(this); }
    sealed class DocumentPeer(DocumentField owner):TextBoxAutomationPeer(owner) { protected override AutomationControlType GetAutomationControlTypeCore()=>AutomationControlType.Document; }
    static int FreePort(){var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;listener.Stop();return port;}
    sealed class Model:IDisposable
    {
        readonly HttpListener listener=new();readonly Task loop;internal Uri Address {get;}
        internal Model(){Address=new($"http://127.0.0.1:{FreePort()}/");listener.Prefixes.Add(Address.AbsoluteUri);listener.Start();loop=Task.Run(async()=>{try{while(listener.IsListening){var c=await listener.GetContextAsync();using var reader=new StreamReader(c.Request.InputStream);var body=await reader.ReadToEndAsync();var result=c.Request.RawUrl=="/api/embed"?"{\"embeddings\":[[1,0,0],[1,0,0]]}":JsonSerializer.Serialize(new{message=new{content=body.Contains("Compare the original user draft")?"{\"preserved\":true,\"scoreBefore\":60,\"scoreAfter\":80,\"changes\":[\"Clarified phrasing\"]}":Improved},done=true})+"\n";var bytes=Encoding.UTF8.GetBytes(result);c.Response.StatusCode=200;c.Response.ContentType="application/json";c.Response.ContentLength64=bytes.Length;await c.Response.OutputStream.WriteAsync(bytes);c.Response.Close();}}catch(Exception ex) when(ex is HttpListenerException or ObjectDisposedException){}});}
        public void Dispose(){listener.Stop();listener.Close();}
    }
}
