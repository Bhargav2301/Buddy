using Buddy.Server;
using Buddy.Windows;
using Microsoft.AspNetCore.DataProtection;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;

internal static class QaChecks
{
    private static object Field(object value,string name)=>value.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(value)!;
    private static Task Call(object value,string name,params object[] args)=>(Task)value.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(value,args)!;
    internal static int Run(bool presentationOnly=false){
        ShellIdentity.Initialize();var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int count=0,exit=1;
        void Check(bool good,string text){if(!good)throw new Exception("FAIL: "+text);count++;Console.WriteLine("PASS: "+text);}
        var root=Path.Combine(Path.GetTempPath(),"Buddy-native-QA-"+Guid.NewGuid());Directory.CreateDirectory(root);
        var practice=new PracticeWindow();
        practice.Loaded+=async(_,_)=>{
            DesktopAssistant? assistant=null;
            try{
                var hwnd=new WindowInteropHelper(practice).Handle;await Task.Delay(200);
                Check(ShellIdentity.ReadProcess()==ShellIdentity.CurrentId&&ShellIdentity.ReadWindow(hwnd)==ShellIdentity.CurrentId,"Actual owned process and native window expose the same explicit shell identity");
                var small=BrandingDiagnostics.IconInfo(hwnd,0);var large=BrandingDiagnostics.IconInfo(hwnd,1);
                Check(small.Handle!=large.Handle&&small.Width==16&&small.Height==16&&large.Width==48&&large.Height==48,"Actual native small/large icons have separate handles and 16/48 pixel sizes");
                Check(BrandingDiagnostics.HasMint(hwnd,0)&&BrandingDiagnostics.HasMint(hwnd,1),"Both native icon handles contain the supplied mint artwork");
                var shortcut=Path.Combine(root,"Buddy fixture.lnk");dynamic shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;dynamic link=shell.CreateShortcut(shortcut);link.TargetPath=Environment.ProcessPath;link.Save();ShellIdentity.Shortcut(shortcut);
                Check(ShellIdentity.ReadShortcut(shortcut)==ShellIdentity.InstalledId,"Shortcut AppUserModelID is persisted and read back through the Shell property store");
                practice.Export.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(practice.Export.Content.ToString()!.Contains("Simulate")&&practice.Result.Text.Contains("Simulation acknowledged")&&!practice.Result.Text.Contains("exported"),"Practice explicitly reports a simulation with no file or real task completion");
                if(presentationOnly){
                    var presentationStore=new StateStore(Path.Combine(root,"presentation"),DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(root,"presentation-keys"))));await presentationStore.EnsureSaved();
                    using var presentationModel=new MockModel();using var presentationHttp=new HttpClient(presentationModel){BaseAddress=new("http://127.0.0.1:11434")};var presentationService=new BuddyService(presentationStore,new(presentationHttp));
                    assistant=new(()=>presentationService,()=>new(),()=>IntPtr.Zero,_=>{});await assistant.Open("guide","");
                    var presentationGuideId=Guid.NewGuid().ToString();var guidePlan=new GuidePlan("Fixture guide",[new("Look at the fixture","fixture","Fixture","Text")]);await presentationStore.Update(s=>{s.Guides.Add(new(presentationGuideId,"fixture",guidePlan,0,DateTimeOffset.UtcNow));return true;});
                    void Set(string field,object value)=>typeof(DesktopAssistant).GetField(field,BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(assistant,value);
                    Set("guide",guidePlan);Set("guideId",presentationGuideId);
                    var review=new GuideReviewProgress();review.Advance(0,false,false);Set("guideProgress",review);await Call(assistant,"FinishGuide");
                    Check(!await presentationStore.Read(s=>s.Guides.Single().Completed)&&((TextBlock)Field(assistant,"state")).Text.Contains("not verified"),"Native Next/review finish does not persist or announce task completion");
                    review=new();review.Advance(0,false,true);Set("guideProgress",review);await Call(assistant,"FinishGuide");
                    Check(!await presentationStore.Read(s=>s.Guides.Single().Completed)&&((TextBlock)Field(assistant,"state")).Text.Contains("skipped"),"Native Skip finish records an unverified result");
                    review=new();review.Advance(0,true,false);Set("guideProgress",review);await Call(assistant,"FinishGuide");
                    Check(await presentationStore.Read(s=>s.Guides.Single().Completed)&&((TextBlock)Field(assistant,"state")).Text.Contains("overall task"),"Observed native step outcomes remain distinct from overall task completion");
                    await assistant.Resume(presentationGuideId);
                    Check(Field(assistant,"guide") is null,"Resume rejects reuse of a saved guide when fresh target observation fails");
                    Console.WriteLine($"ALL {count} NATIVE QA PRESENTATION CHECKS PASSED (no screen capture or input dispatch)");exit=0;return;
                }
                practice.Editor.Text="HI from the document";
                var perception=new ScreenPerception(()=>new());var snapshot=await perception.Capture(hwnd,default,"HI");
                var matches=snapshot.Context.Elements.Where(e=>e.Ref.StartsWith("word:")).ToArray();
                Check(matches.Length==1&&matches[0].Name=="HI"&&matches[0].Width<practice.Editor.ActualWidth,"Real UIA document range exposes just the requested word, not a window title or full editor");
                Check(await Task.Run(()=>snapshot.WordTargets![matches[0].Ref].IsCurrent(matches[0])),"Document word geometry is independently rechecked before pointing");
                practice.Editor.Text="Changed content";Check(!await Task.Run(()=>snapshot.WordTargets![matches[0].Ref].IsCurrent(matches[0])),"Changing document text invalidates the previous word target");
                var store=new StateStore(Path.Combine(root,"data"),DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(root,"keys"))));await store.EnsureSaved();
                using var model=new MockModel();using var client=new HttpClient(model){BaseAddress=new("http://127.0.0.1:11434")};var service=new BuddyService(store,new(client)){AgentEnabled=true};
                var ledger=new JobLedger();assistant=new(()=>service,()=>new(){AgentEnabled=true,StrictAgentConfirmations=true},()=>hwnd,_=>{},jobs:ledger);
                // The actual app window is the disposable owned fixture; no Notepad or other third-party app is driven.
                var field=snapshot.Context.Elements.Single(e=>e.Name=="Example text"&&e.Role=="Edit");
                var action=new AssistantAction("type",Ref:field.Ref,Target:field.Name,Role:field.Role,Value:"Fixture poem across a lonely sea.",Description:"Insert reviewed fixture draft into empty editor",RequireEmpty:true);
                model.Replies.Enqueue(JsonSerializer.Serialize(new AssistantPlan("Review fixture text",[action]),StateStore.Json));
                practice.Editor.Text="";await assistant.Open("agent","Insert the reviewed draft into the practice editor");
                Check(ledger.Snapshot.Last().State==JobState.AwaitingApproval&&practice.Editor.Text=="","Actual native plan waits for Run without changing the editor");
                var execution=Call(assistant,"Execute");await Task.Delay(250);
                if(execution.IsCompleted){await execution;throw new InvalidOperationException("NATIVE_FOREGROUND_BLOCKED: action fixture could not reach approval; "+((TextBlock)Field(assistant,"state")).Text);}
                Check(ledger.Snapshot.Last().State==JobState.AwaitingApproval&&practice.Editor.Text=="","Run still pauses at separate actual insertion approval");
                ((Button)Field(assistant,"decline")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await execution.WaitAsync(TimeSpan.FromSeconds(5));
                Check(ledger.Snapshot.Last().State==JobState.Cancelled&&practice.Editor.Text==""&&ledger.Snapshot.Last().Receipts.Count==0,"Declined native insertion changes no text and emits no success receipt");
                model.Replies.Enqueue(JsonSerializer.Serialize(new AssistantPlan("Review fixture text",[action]),StateStore.Json));
                await assistant.Open("agent","Insert the reviewed draft into the practice editor");
                model.Replies.Enqueue("{\"status\":\"done\",\"summary\":\"Verified the fixture text\",\"actions\":[]}");
                execution=Call(assistant,"Execute");await Task.Delay(250);((Button)Field(assistant,"approve")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await execution.WaitAsync(TimeSpan.FromSeconds(10));
                Check(practice.Editor.Text==action.Value&&ledger.Snapshot.Last().State==JobState.Completed&&ledger.Snapshot.Last().Receipts.Single().Detail.StartsWith("Updated and verified"),"Approved native job inserts and reads back text, then records a real completion receipt");
                // Exercise fresh target/read-only/nonempty/stale guards through the real Apply implementation.
                assistant.Cancel();practice.Activate();InputNative.SetForegroundWindow(hwnd);await Task.Delay(120);snapshot=await perception.Capture(hwnd,default);field=snapshot.Context.Elements.Single(e=>e.Name=="Example text"&&e.Role=="Edit");
                bool rejected=false;try{await Task.Run(()=>assistant.Apply(action,snapshot,field,default));}catch(InvalidOperationException){rejected=true;}Check(rejected&&practice.Editor.Text==action.Value,"Nonempty editor is never replaced by an empty-only draft");
                practice.Editor.Text="";practice.Editor.IsReadOnly=true;rejected=false;try{await Task.Run(()=>assistant.Apply(action,snapshot,field,default));}catch(InvalidOperationException){rejected=true;}Check(rejected&&practice.Editor.Text=="","Read-only editor rejects insertion");
                practice.Editor.IsReadOnly=false;var altered=field with{X=field.X+1};rejected=false;try{await Task.Run(()=>assistant.Apply(action,snapshot,altered,default));}catch(InvalidOperationException){rejected=true;}Check(rejected&&practice.Editor.Text=="","Moved or stale target geometry aborts before insertion");
                async Task AwaitState(Func<bool> ready,Task pending){
                    for(int i=0;i<160&&!ready();i++){if(pending.IsCompleted){await pending;throw new Exception("Execution ended before expected approval: "+((TextBlock)Field(assistant,"state")).Text);}await Task.Delay(50);}
                    if(!ready())throw new TimeoutException("Expected native approval did not appear.");
                }
                void Approve()=>((Button)Field(assistant,"approve")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                bool Approval(string text)=>((Button)Field(assistant,"approve")).Visibility==Visibility.Visible&&((TextBlock)Field(assistant,"state")).Text.Contains(text);
                var followup=action with{Value="Second reviewed draft, including every word of this replacement.",Description="Replace the fixture with the reviewed second draft",RequireEmpty=false};
                model.Replies.Enqueue(JsonSerializer.Serialize(new AssistantPlan("First reviewed batch",[action]),StateStore.Json));
                // Plan the final mixed request; edits after review deliberately invalidate approval.
                await assistant.Open("agent","Teach me and then write the reviewed fixture draft");
                model.Replies.Enqueue(JsonSerializer.Serialize(new AgentDecision("continue","Review a second fixture batch",[followup]),StateStore.Json));
                model.Replies.Enqueue("{\"status\":\"done\",\"summary\":\"Both fixture replacements were read back; nothing was sent or saved.\",\"actions\":[]}");
                execution=Call(assistant,"Execute");await AwaitState(()=>Approval(action.Description),execution);
                Check(practice.Editor.Text=="","Execution specialist retains separate initial insertion approval");Approve();
                await AwaitState(()=>Approval("Review the updated plan"),execution);
                Check(practice.Editor.Text==action.Value&&((TextBox)Field(assistant,"planText")).Text.Contains(followup.Value),"Actual continuation shows the COMPLETE next insertion text before revised-plan approval");Approve();
                await AwaitState(()=>Approval(followup.Description),execution);
                Check(practice.Editor.Text==action.Value,"Revised-plan approval still requires separate native replacement approval");Approve();await execution.WaitAsync(TimeSpan.FromSeconds(12));
                Check(practice.Editor.Text==followup.Value&&ledger.Snapshot.Last().State==JobState.Completed&&ledger.Snapshot.Last().Receipts.Count==2,"Actual dispatcher continues into a second approved batch and verifies both receipts");
                Check(new[]{"Writer","Observer","Result verifier","Teacher"}.All(role=>assistant.ExecutionSnapshot.Any(j=>j.Specialist==role))&&model.TeacherCalls==2&&((TextBlock)Field(assistant,"teachingUpdate")).Text.Contains("recorded"),"Writer, Observer, Result verifier and Teacher jobs run on actual native continuation with separate teacher responses");
                practice.Editor.Text="";model.Replies.Enqueue(JsonSerializer.Serialize(new AssistantPlan("Cancellation fixture",[action]),StateStore.Json));await assistant.Open("agent","Insert the reviewed fixture draft");
                execution=Call(assistant,"Execute");await AwaitState(()=>Approval(action.Description),execution);assistant.Cancel();await execution.WaitAsync(TimeSpan.FromSeconds(5));
                Check(practice.Editor.Text==""&&ledger.Snapshot.Last().State==JobState.Cancelled&&assistant.ExecutionSnapshot.Any(j=>j.State.Contains("cancelled")),"Stop cancels an actual awaiting-approval specialist without dispatching its edit");
                model.Replies.Enqueue(JsonSerializer.Serialize(new AssistantPlan("Fresh restart",[action]),StateStore.Json));await assistant.Open("agent","Insert the reviewed fixture draft");model.Replies.Enqueue("{\"status\":\"done\",\"summary\":\"Fixture text read back\",\"actions\":[]}");
                execution=Call(assistant,"Execute");await AwaitState(()=>Approval(action.Description),execution);Approve();await execution.WaitAsync(TimeSpan.FromSeconds(10));
                Check(practice.Editor.Text==action.Value&&ledger.Snapshot.Last().State==JobState.Completed&&ledger.Snapshot.Last().Receipts.Count==1,"A fresh reviewed job runs after Stop with no cancelled-job replay");
                var guideId=Guid.NewGuid().ToString();var plan=new GuidePlan("Fixture",[new("Look at text",field.Ref,field.Name,field.Role)]);await store.Update(s=>{s.Guides.Add(new(guideId,"fixture",plan,0,DateTimeOffset.UtcNow));return true;});
                typeof(DesktopAssistant).GetField("guide",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(assistant,plan);typeof(DesktopAssistant).GetField("guideId",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(assistant,guideId);
                ((GuideReviewProgress)Field(assistant,"guideProgress")).Advance(0,false,true);await Call(assistant,"FinishGuide");
                Check(!await store.Read(s=>s.Guides.Single().Completed)&&((TextBlock)Field(assistant,"state")).Text.Contains("skipped"),"Real guide end persists skipped status without a false completion flag");
                Console.WriteLine($"ALL {count} NATIVE QA CHECKS PASSED (owned fixture only; no third-party input)");exit=0;
            }catch(Exception e){Console.Error.WriteLine(e);Console.WriteLine($"Native QA reached {count} passing checks before failure/blocker.");}
            finally{assistant?.Dispose();practice.Close();Directory.Delete(root,true);app.Shutdown();}
        };
        app.Run(practice);return exit;
    }
    private sealed class MockModel:HttpMessageHandler{
        internal Queue<string> Replies=new();internal int TeacherCalls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){
            var input=await r.Content!.ReadAsStringAsync(ct);string reply;
            if(input.Contains("Explain the observed progress")){TeacherCalls++;reply="{\"summary\":\"The recorded fixture edit was read back. Nothing was sent or saved.\"}";}
            else if(input.Contains("Give a brief source-backed orientation")) reply="{\"summary\":\"Review each fixture draft before approving its insertion.\",\"steps\":[]}";
            else reply=Replies.Dequeue();
            return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{message=new{content=reply},done=true}))};
        }
    }
}
