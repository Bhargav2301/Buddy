using Buddy.Server;
using Buddy.Windows;
using Microsoft.AspNetCore.DataProtection;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

internal static class GuideLessonChecks
{
    static object Field(object value,string name)=>value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(value)!;
    static Task Call(object value,string name,params object[] args)=>(Task)value.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(value,args)!;
    internal static int Run(){
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var practice=new PracticeWindow();int count=0,exit=1;
        void Check(bool ok,string text){if(!ok)throw new Exception("FAIL: "+text);count++;Console.WriteLine("PASS: "+text);}
        var root=Path.Combine(Path.GetTempPath(),"Buddy-guide37-"+Guid.NewGuid());Directory.CreateDirectory(root);
        practice.Loaded+=async(_,_)=>{
            DesktopAssistant? assistant=null;
            try {
                var hwnd=new WindowInteropHelper(practice).Handle;ScreenPerception.PracticeHandle=hwnd;
                practice.Activate();InputNative.SetForegroundWindow(hwnd);await Task.Delay(180);
                var perception=new ScreenPerception(()=>new());var snapshot=await perception.Capture(hwnd,default);
                var editor=snapshot.Context.Elements.Single(e=>e.Name=="Example text"&&e.Role=="Edit");
                using var mock=new Mock();using var http=new HttpClient(mock){BaseAddress=new("http://127.0.0.1:11434")};
                var store=new StateStore(root,DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(root,"keys"))));await store.EnsureSaved();var service=new BuddyService(store,new(http));
                mock.Plan=new("Use the editor, then inspect the later dialog.",[new("Look at the editor",editor.Ref,editor.Name,editor.Role),new("Review Format in the later dialog","future","Format","ComboBox")]);
                assistant=new(()=>service,()=>new(),()=>hwnd,_=>{});
                await assistant.Open("guide","teach me to use the fixture");
                Check(((GuidePlan)Field(assistant,"guide")).Lessons?.Count==2&&mock.Calls==1,"Native Guide retains both lesson sections after a single mixed current/future model response");
                var ownedPanel=(Window)Field(assistant,"panel");ownedPanel.Activate();InputNative.SetForegroundWindow(new WindowInteropHelper(ownedPanel).Handle);await Task.Delay(120);
                Console.WriteLine($"FOREGROUND DIAGNOSTIC: source={(IntPtr)Field(assistant,"sourceWindow")}; expected={hwnd}; foreground={Native.GetForegroundWindow()}; panel={new WindowInteropHelper(ownedPanel).Handle}; own={Native.IsOwnWindow(Native.GetForegroundWindow())}; state={((TextBlock)Field(assistant,"state")).Text}");
                Check((IntPtr)Field(assistant,"sourceWindow")==hwnd&&Native.IsOwnWindow(Native.GetForegroundWindow()),"Source stays pinned while Buddy's Guide is in front");
                Check(((GuidanceOverlay)Field(assistant,"overlay")).IsVisible&&((TextBlock)Field(assistant,"state")).Text.Contains("pointer verified"),"Real UIA editor receives native guidance ink with Buddy in front");
                await Call(assistant,"GuideStep",1);
                Check(!((GuidanceOverlay)Field(assistant,"overlay")).IsVisible&&((Button)Field(assistant,"next")).IsEnabled&&((TextBlock)Field(assistant,"stepText")).Text.Contains("Format"),"Unseen later control keeps readable text and navigation without guessed ink");
                await Call(assistant,"GuideStep",-1);
                Check(((GuidanceOverlay)Field(assistant,"overlay")).IsVisible,"Back independently recaptures the original target and restores verified ink");
                await assistant.Open("guide","teach me how to use notepad");
                Check(((GuidePlan)Field(assistant,"guide")).Lessons?.Count==3&&mock.Calls==1,"Exact reported Notepad request produces three sections without depending on model schema compliance");
                Check(((Button)Field(assistant,"next")).IsEnabled&&((Button)Field(assistant,"skip")).IsEnabled&&!((GuidanceOverlay)Field(assistant,"overlay")).IsVisible,"Exact request remains navigable but refuses a Notepad pointer in the different fixture process");
                Check(((TextBox)Field(assistant,"planText")).Text!=((TextBlock)Field(assistant,"stepText")).Text,"Summary and current section are separate, without the reported duplicate fallback");
                await Call(assistant,"GuideStep",1);await Call(assistant,"GuideStep",1);await Call(assistant,"FinishGuide");
                Check((await store.Read(s=>s.Guides)).All(g=>!g.Completed),"Reading the full lesson never writes task completion");
                assistant.Cancel();await assistant.Open("guide","teach me how to use notepad");
                Check(((Button)Field(assistant,"next")).IsEnabled,"Stop and reopen restores a fresh navigable lesson");
                assistant.Cancel();practice.Activate();InputNative.SetForegroundWindow(hwnd);practice.Editor.Focus();await Task.Delay(120);
                Check(!new DesktopPreferences().LocalPromptSuggestions,"Automatic prompt suggestions remain off for existing and new profiles");
                var fieldEditor=new FocusedFieldEditor(perception);
                Check(await fieldEditor.Probe(hwnd,default)==null,"Real unsupported practice field fails the AI-chat allowlist before any passive text read");
                practice.Editor.Height=240;practice.UpdateLayout();snapshot=await perception.Capture(hwnd,default);editor=snapshot.Context.Elements.Single(e=>e.Name=="Example text"&&e.Role=="Edit");
                practice.Editor.Text="Please draft a meeting invitation.";var field=new FixtureField(practice.Editor);var draft=new FocusedDraft(new GuardedEdit(field,practice.Editor.Text),"owned fixture","Example text",new(hwnd,"fixture",new(editor.X,editor.Y,editor.Width,editor.Height)));
                var card=new InlinePromptWindow(service,fieldEditor,draft,()=>{});card.Show();await Task.Delay(120);
                Check(card.AwaitingConsent&&field.Writes==0&&Native.GetForegroundWindow()==hwnd,"Native inline permission bubble leaves the original field focused and untouched");
                typeof(InlinePromptWindow).GetMethod("ShowProposal",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(card,["Please draft a polite meeting invitation."]);
                Check(field.Writes==0&&((TextBlock)Field(card,"diff")).Inlines.Count==4,"Highlighted inline proposal renders without applying or copying anything");
                var previewBounds=WindowCapture.Bounds(new WindowInteropHelper(card).Handle);
                Check(card.IsInFieldPreview&&Math.Abs(previewBounds.X-editor.X)<=1&&Math.Abs(previewBounds.Y-editor.Y)<=1&&previewBounds.Width<=editor.Width+1&&previewBounds.Height<=editor.Height+1,"Native highlighted preview occupies the actual owned source field bounds without changing its contents");
                card.Reanchor(new(editor.X,editor.Y,280*OverlayNative.Scale(new WindowInteropHelper(card).Handle),180*OverlayNative.Scale(new WindowInteropHelper(card).Handle)));card.UpdateLayout();
                var viewports=((DockPanel)((Border)card.Content).Child).Children.OfType<ScrollViewer>().ToArray();
                Check(viewports.Length==2&&viewports.All(viewport=>viewport.ViewportHeight>0&&(viewport.ExtentHeight<=viewport.ViewportHeight||viewport.ScrollableHeight>0)),"Small in-field previews keep every review and approval control reachable by scrolling");card.Reanchor(draft.Anchor.Bounds);
                await Call(card,"Apply");Check(field.Writes==1&&practice.Editor.Text.Contains("polite"),"Accept applies exactly one guarded edit to the original owned field");
                await Call(card,"Undo");Check(field.Writes==2&&practice.Editor.Text==draft.Edit.Original,"Inline Undo restores the exact original text");card.Close();
                var stale=new InlinePromptWindow(service,fieldEditor,draft,()=>{});stale.Show();typeof(InlinePromptWindow).GetMethod("ShowProposal",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(stale,["A different proposal."]);practice.Editor.Text="User's newer draft";
                Check(!await stale.IsCurrent(default),"The watcher's current-field check detects resumed typing before Accept");
                await Call(stale,"Apply");Check(field.Writes==2&&practice.Editor.Text=="User's newer draft","Typing after the proposal blocks a stale inline replacement");stale.Close();
                bool refused=false;var unfocused=draft with{VerifyFocus=()=>throw new InvalidOperationException("Focus changed")};
                try{await fieldEditor.Apply(unfocused,"No write",default);}catch(InvalidOperationException){refused=true;}
                Check(refused&&field.Writes==2,"Inline focus/context invalidation aborts before the edit dispatcher writes");
                int voiceRequests=0;var voiceCard=new InlinePromptWindow(service,fieldEditor,draft,()=>voiceRequests++,"Ctrl+Alt+Space");voiceCard.Show();
                var voiceButtons=((StackPanel)((ScrollViewer)Field(voiceCard,"footerScroll")).Content).Children.OfType<WrapPanel>().Single();
                voiceButtons.Children.OfType<Button>().Single(b=>b.Content.ToString()=="Voice reply").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(voiceRequests==1&&field.Writes==2,"Permission bubble's Voice reply button requests the existing voice surface without editing");
                using(var voice=new VoiceOverlayWindow(()=>service,()=>Task.FromResult<string?>(null),()=>new(),_=>{},()=>hwnd,perception,(_,_)=>{},()=>{})){
                    voice.RefinementReply=voiceCard.Reply;await Call(voice,"Send","no thanks");
                    Check(!voiceCard.IsVisible&&field.Writes==2,"Actual voice-send route consumes a declined refinement reply and dismisses the bubble without model chat or editing");
                }
                var escapeCard=new InlinePromptWindow(service,fieldEditor,draft,()=>{});escapeCard.Show();practice.Activate();InputNative.SetForegroundWindow(hwnd);await Task.Delay(100);
                Check(Native.GetForegroundWindow()==hwnd,"Owned source is foreground before the bounded Esc dismissal test");
                InputNative.Keys("Escape",default);await Task.Delay(150);
                Check(!escapeCard.IsVisible&&field.Writes==2,"A quick native Esc press closes the nonactivating bubble without touching the original field");
                Check(Native.RegisterHotKey(hwnd,0x4250,0x4000,27),"Closing the bubble releases its temporary Escape registration");Native.UnregisterHotKey(hwnd,0x4250);
                Console.WriteLine($"ALL {count} NATIVE GUIDE LESSON CHECKS PASSED; owned fixture only, actual Notepad/Amoeba acceptance remains manual");exit=0;
            } catch(Exception ex){Console.Error.WriteLine(ex);} finally{assistant?.Dispose();practice.Close();Directory.Delete(root,true);app.Shutdown();}
        };
        app.Run(practice);return exit;
    }
    sealed class FixtureField(TextBox text):IVerifiedTextField {public string Identity=>"fixture";public int Writes;public string Read()=>text.Dispatcher.Invoke(()=>text.Text);public void Write(string expected,string value,CancellationToken ct)=>text.Dispatcher.Invoke(()=>{ct.ThrowIfCancellationRequested();if(text.Text!=expected)throw new InvalidOperationException("Changed");Writes++;text.Text=value;});}
    sealed class Mock:HttpMessageHandler {internal GuidePlan? Plan;internal int Calls;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){Calls++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{message=new{content=JsonSerializer.Serialize(Plan,StateStore.Json)},done=true}))});}}
}
