using Buddy.Server;
using Buddy.Windows;
using Microsoft.AspNetCore.DataProtection;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;

internal static class JobChecks
{
    private static object Field(object value,string name)=>value.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(value)!;
    private static Task Call(object value,string name)=>(Task)value.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(value,[])!;
    internal static int Run()
    {
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};int exit=1,count=0;
        void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);count++;Console.WriteLine("PASS: "+name);}
        var root=Path.Combine(Path.GetTempPath(),"Buddy-native-jobs-"+Guid.NewGuid());Directory.CreateDirectory(root);
        app.Startup+=async(_,_)=>{
            TaskCenter? tasks=null;DesktopAssistant? assistant=null;
            try {
                var store=new StateStore(Path.Combine(root,"state"),DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(root,"keys"))));await store.EnsureSaved();
                using var transport=new NoNetwork();using var http=new HttpClient(transport){BaseAddress=new("http://127.0.0.1:11434")};var service=new BuddyService(store,new(http)){AgentEnabled=true};
                tasks=new(service,()=>assistant?.Cancel(),_=>{});await tasks.Initialize();
                var notes=Path.Combine(root,"notes");Directory.CreateDirectory(notes);await File.WriteAllTextAsync(Path.Combine(notes,"fixture.md"),"Export saves a separate copy.\nCheck the filename first.");
                await tasks.Import(notes,"notepad","fixture version");Check(tasks.Ledger.Snapshot.Last().State==JobState.Completed,"Real native job manager completes an actual bounded local import");
                await tasks.Search("notepad","Export");var read=tasks.Ledger.Snapshot.Last();
                Check(read.State==JobState.Completed&&read.Receipts.Single().Detail.Contains("fixture.md:1"),"Read-only job returns a real source excerpt with line provenance");
                var window=app.Windows.Cast<Window>().Single(w=>w.Title=="Buddy - Local jobs and knowledge");
                Check(window.IsVisible&&((StackPanel)Field(tasks,"entries")).Children.Count==2,"Actual job window renders the two observed lifecycle results");
                await tasks.Flush();var saved=await store.Read(s=>s.Jobs);Check(saved.Count==2&&saved.All(j=>j.State==JobState.Completed),"Completed native jobs are persisted in encrypted state");
                var preferences=new DesktopPreferences{AgentEnabled=true};
                assistant=new(()=>service,()=>preferences,()=>IntPtr.Zero,_=>{},jobs:tasks.Ledger);
                await assistant.Open("agent","open comet browser");
                Check(tasks.Ledger.Snapshot.Last().State==JobState.AwaitingApproval&&tasks.Ledger.Snapshot.Last().Receipts.Count==0,"Actual Comet plan becomes a visible approval job without launching an app");
                var execution=Call(assistant,"Execute");await Task.Delay(80);
                Check(tasks.Ledger.Snapshot.Last().State==JobState.AwaitingApproval&&!execution.IsCompleted,"Approved full plan still pauses at the actual consequential launch approval");
                assistant.Cancel();await execution.WaitAsync(TimeSpan.FromSeconds(3));
                Check(tasks.Ledger.Snapshot.Last().State==JobState.Cancelled&&tasks.Ledger.Snapshot.Last().Receipts.Count==0,"Stop releases live approval without an execution receipt or launch");
                await Call(assistant,"Execute");Check(tasks.Ledger.Snapshot.Last().State==JobState.Cancelled,"Run cannot revive a cancelled plan");
                await assistant.Open("agent","open comet browser");execution=Call(assistant,"Execute");await Task.Delay(80);
                ((Button)Field(assistant,"decline")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await execution.WaitAsync(TimeSpan.FromSeconds(3));
                Check(tasks.Ledger.Snapshot.Last().State==JobState.Cancelled&&tasks.Ledger.Snapshot.Last().Status.Contains("declined")&&tasks.Ledger.Snapshot.Last().Receipts.Count==0,"Explicit Decline at actual step approval prevents launch and preserves a truthful refusal result");
                preferences=preferences with{AgentEnabled=false};await assistant.Open("agent","open comet browser");
                Check(tasks.Ledger.Snapshot.Last().State==JobState.Failed,"Disabled Agent mode produces a failed job instead of fake progress");
                await tasks.Search("notepad","filename");Check(tasks.Ledger.Snapshot.Last().State==JobState.Completed,"A fresh read-only job works after cancellation/failure");
                Check(transport.Calls==0,"Native checks made no external/model HTTP request and granted no account");
                Check(AssistantIntent.Mode("Start an agent to open Comet browser")=="agent"&&AssistantIntent.ActionQuery("Start an agent to open Comet browser")=="open Comet browser","Spoken agent phrase preserves the concrete task for real plan review");
                Check(AssistantIntent.Mode("Search my app notes Export")=="knowledge"&&AssistantIntent.KnowledgeQuery("Search my app notes Export")=="Export","Explicit spoken note query routes to local sourced search");
                Check(!new DesktopPreferences().RegionVoiceAfterSelection,"Region speech handoff is opt-in and remains off by default");
                RegionLifetimeChecks.Run(Check);
                await tasks.Flush();Console.WriteLine($"ALL {count} NATIVE JOB CHECKS PASSED (own UI/local files; launch approval cancelled; no microphone or third-party action)");exit=0;
            }catch(Exception e){Console.Error.WriteLine(e);}
            finally{assistant?.Dispose();if(tasks is not null){await tasks.Flush();tasks.Dispose();}Directory.Delete(root,true);app.Shutdown();}
        };
        app.Run();return exit;
    }
    private sealed class NoNetwork:HttpMessageHandler
    {
        internal int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Calls++;throw new InvalidOperationException("No network is allowed in this fixture.");}
    }
}
