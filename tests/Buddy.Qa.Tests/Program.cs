using Buddy.Server;
using Microsoft.AspNetCore.DataProtection;
using System.Net;
using System.Text;
using System.Text.Json;

int count=0;void Check(bool yes,string text){if(!yes)throw new Exception("FAIL: "+text);count++;Console.WriteLine("PASS: "+text);}
void Reject(Action action,string code,string text){try{action();}catch(BuddyException e)when(e.Code==code){Check(true,text);return;}throw new Exception("FAIL: "+text);}
Check(ResearchCuration.ExplicitUrls(@"Help me set up Amoeba [official](https\://useamoeba.com/). | Video | Title only |").Single()=="https://useamoeba.com/","Escaped markdown URL in mixed prose/table is extracted and normalized");
Check(ResearchCuration.ExplicitUrls("https://example.com/a(b) and https://example.com/docs.").SequenceEqual(new[]{"https://example.com/a(b)","https://example.com/docs"}),"Balanced path parentheses retained; sentence punctuation omitted");
Reject(()=>ResearchCuration.ExplicitUrls("https://example.com https://127.0.0.1/private"),"WEB_URL_BLOCKED","All supplied URLs validated before any fetch");
Reject(()=>ResearchCuration.ExplicitUrls("https://user:secret@example.com"),"WEB_URL_BLOCKED","Credential-bearing URLs cannot enter research");
Check(!ResearchCuration.Needed("point to the word HI in Notepad"),"A local pointing request does not require web search");
Check(GuideSafety.RequestedText("point to the word HI in Notepad")=="HI"&&GuideSafety.RequestedText("underline 'hello world' in Notepad")=="hello world","Document target extracted without treating app name as text");
Check(GuideSafety.RequestedText("Show me where the \"Export\" button is")==null,"Quoted button labels are not misrouted into document-word lookup");
var controls=new List<ScreenElement>{new("min","Minimize","Button",0,0,20,20),new("win","HI - Notepad","Window",0,0,600,400),new("edit","Text editor","Edit",0,30,600,300),new("export","Export","Button",0,360,100,30)};
var context=new ScreenContext("notepad","HI - Notepad",controls);
var intro=GuideLessons.Notepad(new("teach me how to use notepad",context));
Check(intro.Lessons?.Count==3&&intro.Summary.Contains("plain-text")&&GuideLessons.Resolve(intro.Lessons[0],context,"teach me how to use notepad")?.Ref=="edit","Exact reported Notepad introduction has a useful navigable lesson and verified editor pointer");
var mixed=GuideLessons.Compose(new("A useful overview",[new("Choose Export","export","Export","Button"),new("In the later dialog choose Format","later","Format","ComboBox")]),new("Help with exporting",context));
Check(mixed.Lessons?.Count==2&&mixed.Steps?.Count==1,"Unseen later dialog no longer discards the valid first pointer or entire lesson");
Check(GuideLessons.Resolve(mixed.Lessons![1],context,"Help with exporting")==null,"A future lesson target cannot become a current pointer");
Check(GuideLessons.Resolve(mixed.Lessons[0],context with{Elements=[..controls,controls[^1] with{Ref="duplicate"}]},"Help with exporting")==null,"Ambiguous duplicate labels fail closed for teaching ink");
Check(GuideLessons.Resolve(intro.Lessons![0],context with{App="amoeba"},"teach me how to use notepad")==null,"Notepad lesson never draws into a different app");
Check(!PromptSuggestionPolicy.ShouldOffer("hi")&&PromptSuggestionPolicy.ShouldOffer("Please help me draft a polite meeting invitation."),"Local suggestion heuristic requires a bounded prompt-like field");
Check(!PromptSuggestionPolicy.ShouldOffer(new string('x',4001))&&PromptSuggestionPolicy.PrivateMetadata("ChatGPT - InPrivate", "Message")&&PromptSuggestionPolicy.PrivateMetadata("Bank account", "Ask"),"Suggestion policy excludes private/banking metadata and oversized text");
Check(PromptSuggestionPolicy.Reply("yes please")==true&&PromptSuggestionPolicy.Reply("no thanks")==false&&PromptSuggestionPolicy.Reply("send the email")==null,"Inline voice consent is narrowly scoped and cannot authorize external actions");
var delta=PromptSuggestionPolicy.Difference("Write a message.","Write a polite message.");Check(delta.Prefix+delta.Removed+delta.Suffix=="Write a message."&&delta.Prefix+delta.Added+delta.Suffix=="Write a polite message.","Highlighted diff reconstructs exact original and proposal");
Check(SpecialistWork.Requested("Teach me Notepad and then write a poem")&&!SpecialistWork.Requested("Teach me how to use Notepad"),"Teach-and-perform is decomposed while teaching alone requests no action");
var entered=0;var peak=0;var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
async Task ReadWork(CancellationToken ct){var n=Interlocked.Increment(ref entered);peak=Math.Max(peak,n);if(n==2)release.TrySetResult();await release.Task.WaitAsync(ct);await Task.Delay(20,ct);Interlocked.Decrement(ref entered);}
await SpecialistWork.Run([("A",ReadWork),("B",ReadWork),("C",ReadWork)],null,default);Check(peak==2&&entered==0,"Two independent read-only specialists genuinely overlap; third respects the concurrency cap");
using(var stopAll=new CancellationTokenSource()){
 var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int stopped=0;
 async Task WaitWork(CancellationToken ct){started.TrySetResult();try{await Task.Delay(30000,ct);}finally{Interlocked.Increment(ref stopped);}}
 var pending=SpecialistWork.Run([("One",WaitWork),("Two",WaitWork)],null,stopAll.Token);await started.Task;stopAll.Cancel();try{await pending;}catch(OperationCanceledException){}Check(stopped==2,"Cancel-all reaches every running preparation specialist");
}
Reject(()=>GuideSafety.Validate(new("Minimize",[new("Click Minimize","min","Minimize","Button")]),new("Point to the word HI",context)),"INVALID_GUIDE","Reported Minimize detour is rejected for word pointing");
Reject(()=>GuideSafety.Validate(new("Title",[new("Click HI - Notepad","win","HI - Notepad","Window")]),new("Point to the word HI",context)),"INVALID_GUIDE","Window title cannot stand in for document text");
Reject(()=>GuideSafety.Validate(new("Invented",[new("Click Missing","invented","Missing","Button")]),new("Set up Amoeba",context)),"INVALID_GUIDE","Unobserved controls are rejected");
Check(GuideSafety.Validate(new("Export",[new("Choose Export","export","Export","Button")]),new("Show Export",context)).Steps!.Count==1,"Observed goal-relevant control remains guideable");
var progress=new GuideReviewProgress();progress.Advance(0,false,true);progress.Advance(1,false,false);
Check(!progress.AllObserved(2)&&progress.EndMessage(2).Contains("skipped")&&!progress.EndMessage(2).Contains("complete"),"Skip/end never claims task completion");
var reviewed=new GuideReviewProgress();reviewed.Advance(0,false,false);Check(reviewed.EndMessage(1)=="Guide reviewed. Task completion was not verified.","Manual Next means reviewed, not observed success");
var observed=new GuideReviewProgress();observed.Advance(0,true,false);Check(observed.AllObserved(1)&&observed.EndMessage(1).Contains("overall task"),"Observed step outcomes do not overclaim overall task success");
Check(ActionPolicy.Validate(new("Open",[new("open",Value:"Notepad.exe")])).Actions![0].Value=="notepad","Known Notepad executable alias canonicalizes safely");
Reject(()=>ActionPolicy.Validate(new("Bad",[new("open",Value:"Write a poem on a sailing boat in a lonely sea in the notepad")])),"INVALID_PLAN","Non-URL malformed launch receives a planning error, not an HTTPS demand");
Reject(()=>ActionPolicy.Validate(new("Bad",[new("open",Value:"cmd.exe /c whoami")])),"INVALID_PLAN","Alias normalization cannot introduce shell arguments");
foreach(var invalid in new[]{("notepad","other",false,false,""),("other","other",false,false,""),("notepad","notepad",false,true,""),("notepad","notepad",false,false,"existing user text")}){
 bool refused=false;try{NotepadWriting.CheckEmptyEditor(invalid.Item1,invalid.Item2,invalid.Item3,invalid.Item4,invalid.Item5);}catch(InvalidOperationException){refused=true;}Check(refused,"Insertion guard rejects stale identity, other app, read-only or nonempty editor");
}
NotepadWriting.CheckEmptyEditor("notepad","notepad",false,false,"");Check(true,"Only an unchanged empty writable Notepad editor passes insertion policy");
var root=Path.Combine(Path.GetTempPath(),"buddy-qa-"+Guid.NewGuid());Directory.CreateDirectory(root);
try{
 var store=new StateStore(root,DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(root,"keys"))));await store.EnsureSaved();
 using var model=new Model();using var http=new HttpClient(model){BaseAddress=new("http://127.0.0.1:11434")};var web=new Research();var service=new BuddyService(store,new(http),web){WebEnabled=true,AgentEnabled=true};
 model.Add(new{Text="A small sail drifts across the sea."});
 var bundle=await service.PrepareWorkflow(new("Teach me Notepad and then write a short poem",context,false),null,default);
 Check(bundle.Lesson.Lessons?.Count==3&&bundle.Plan.Actions!.Single().RequireEmpty&&bundle.Notes.Count==0,"Real specialist service produces a teaching lesson, separately reviewed insertion plan and actual imported-notes result");
 Check((await store.Read(s=>s.Jobs)).Count==0,"Read-only specialist preparation emits no desktop action or completion receipt");
 model.Payloads.Clear();
 model.Add(new{Text="A lonely sail upon the sea,\nThe moon alone keeps watch with me."});
 var exact="Write a poem on a sailing boat in a lonely sea in the notepad";
 var poem=await service.PlanAgent(new(exact,context,true),default);
 Check(poem.Actions!.Count==1&&poem.Actions[0] is {Kind:"type",Ref:"edit",RequireEmpty:true},"Exact reported poem input produces a targeted empty-editor insertion plan");
 Check(poem.Actions[0].Value.Contains("lonely sail")&&poem.Actions[0].Risk=="high","Generated draft is exact reviewable text requiring consequential approval");
 Check(web.Calls.Count==0&&model.Payloads.Count==1,"Creative Notepad task uses local drafting without search or URL routing");
 var continuation=await service.ContinueAgent(new(exact,context,[new(1,poem.Actions[0],true,"Updated and verified Text editor")],24),default);
 Check(continuation.Status=="done"&&continuation.Summary.Contains("No file was saved"),"Verified insertion receipt permits precise completion with no save claim");
 Check((await service.ContinueAgent(new(exact,context,[new(1,poem.Actions[0],false,"Target unavailable")],24),default)).Status=="clarify","Failed insertion cannot be completed by model assertion");
 Check((await service.ContinueAgent(new(exact,context,[new(1,new("wait"),true,"Waited")],24),default)).Status=="clarify","Waiting alone is not proof the poem was inserted");
 var point=await service.PlanGuide(new("point to the word HI in Notepad",context,true),default);
 Check(point.Steps!.Count==0&&point.Summary.Contains("window title"),"Exact bad-title case returns honest clarification, no Minimize/Close sequence");
 var word=context with{Elements=[..controls,new("word:edit:0","HI","Text",20,50,16,18)]};
 Check((await service.PlanGuide(new("point to the word HI in Notepad",word,true),default)).Steps!.Single().Ref=="word:edit:0","Verified document range can be underlined directly with no model guess");
 var docsQuery=@"Teach me Amoeba setup using https\://useamoeba.com/ | Video | Intro to Amoeba | no URL |";
 model.Add(new ConceptualExplanation("Amoeba documentation can explain the setup process.", "Documentation does not establish the installed app version or current controls, and setup permissions require a separate user decision.", "A manual check compares the focused app and its version with the fetched documentation before any setup decision."));
 var docs=await service.PlanGuide(new(docsQuery,new("amoeba","Amoeba",[]),true),default);
 Check(web.Calls.First()=="fetch:https://useamoeba.com/"&&!web.Calls.Any(x=>x.StartsWith("search:")),"User-supplied Amoeba URL bypasses search entirely");
 Check(web.Calls.Contains("fetch:https://useamoeba.com/docs/getting-started")&&docs.Sources!.Count==2,"Discovered official getting-started page is actually fetched and cited");
 Check(!web.Calls.Any(x=>x.Contains("youtube"))&&docs.Sources.All(x=>x.EvidenceKind=="fetched page"),"Title-only video table is never represented as read/watched source content");
 Check(model.Payloads.Last().Contains("active app")||model.Payloads.Last().Contains("current app identity"),"Research planning retains current-app grounding rules");
 Check(model.Payloads.Last().Contains("Video titles")||model.Payloads.Last().Contains("video titles"),"Local model is explicitly told that video titles are not watched evidence");
 web.ThrowSearch=true;model.Add(new GuidePlan("Choose Export.",[new("Choose Export","export","Export","Button")]));
 var failedSearch=await service.PlanGuide(new("research Export setup",context,true),default);
 Check(failedSearch.Steps!.Count==1&&failedSearch.Summary.StartsWith("Online sources could not be read"),"Search failure keeps honest screen-only guidance available");
 model.Add(new GuidePlan("Minimize",[new("Click Minimize","min","Minimize","Button")]));model.Add(new GuidePlan("Focus Amoeba to continue.",[]));
 var repaired=await service.PlanGuide(new("Help with exporting",context),default);Check(repaired.Steps!.Count==0,"Invalid window-management filler is repaired into clarification");
 model.Add(new GuidePlan("Minimize",[new("Click Minimize","min","Minimize","Button")]));model.Add(new GuidePlan("Close",[new("Click Close","close","Close","Button")]));model.Add(new{Summary="Export prepares a copy in the format you choose. Check the destination and review the file before sharing it."});
 var generalFallback=await service.PlanGuide(new("Help with exporting",context),default);
 Check(generalFallback.Lessons is {Count:2}&&generalFallback.Steps!.Count==0&&generalFallback.Lessons[0].Instruction.Contains("Export prepares"),"Two rejected pointer plans now retain a useful general lesson instead of the duplicate empty fallback");
 using var handler=new Pages();using var fetcher=new WebResearch(handler);handler.Location="https://169.254.169.254/latest";
 try{await fetcher.Fetch("https://example.com",default);throw new Exception("FAIL: redirect");}catch(BuddyException e){Check(e.Code=="WEB_URL_BLOCKED"&&handler.Calls==1,"Redirect to metadata endpoint is refused before a second request");}
 if(args.Contains("--live")){
   using var realHttp=new HttpClient{BaseAddress=new("http://127.0.0.1:11434"),Timeout=TimeSpan.FromMinutes(3)};using var realWeb=new WebResearch();var live=new BuddyService(store,new(realHttp),realWeb){AgentEnabled=true,WebEnabled=true};await store.Update(s=>{s.Model="gemma3:4b";return true;});
   var realPoem=await live.PlanAgent(new(exact,context,true),default);Check(realPoem.Actions!.Single().RequireEmpty&&realPoem.Actions[0].Value.Length>30&&realPoem.Actions[0].Value!="A lonely sail upon the sea,\nThe moon alone keeps watch with me.","LIVE local model turns exact poem request into an approved empty-editor draft (not executed)");
   Console.WriteLine("LIVE DRAFT: "+realPoem.Actions![0].Value);
   var page=await realWeb.Fetch("https://useamoeba.com/docs/getting-started",default);Check(page.Text.Contains("GitHub",StringComparison.OrdinalIgnoreCase)&&page.Text.Contains("workspace",StringComparison.OrdinalIgnoreCase),"LIVE bounded public fetch reads official Amoeba setup docs");
   var realGuide=await live.PlanGuide(new(docsQuery,new("amoeba","Amoeba",[]),true),default);Console.WriteLine("LIVE GUIDE INSPECTION: "+realGuide.Summary);Check(realGuide.Sources!.Any(s=>s.Url.Contains("getting-started"))&&realGuide.Steps!.Count==0&&realGuide.Summary.Contains("version"),"LIVE Amoeba request cites read pages and fabricates no controls for an unobserved UI");Console.WriteLine("LIVE GUIDE: "+realGuide.Summary);Console.WriteLine("LIVE SOURCES: "+string.Join(", ",realGuide.Sources.Select(s=>s.Url)));
   Check(realGuide.Lessons is {Count:>0}&&realGuide.Lessons[0].Instruction.Length>70,"LIVE fetched Amoeba guidance remains a navigable explanatory lesson without any screen target");
   var sessionContext=new ScreenContext("amoeba","Amoeba",[new("new-session","New session","Button",20,40,120,32)]);
   var sessionGuide=await live.PlanGuide(new("Teach me how to create a session in Amoeba using https://useamoeba.com/docs/getting-started",sessionContext,true),default);
   Console.WriteLine("LIVE NEW-SESSION LESSON: "+JsonSerializer.Serialize(sessionGuide,StateStore.Json));
   Check(sessionGuide.Lessons is {Count:>0}&&!sessionGuide.Summary.Contains("could not ground"),"LIVE Amoeba New session context produces usable lesson sections rather than the reported planner fallback");
   Check(sessionGuide.Steps!.All(s=>GuideSafety.Validate(new("",[s]),new("Teach me Amoeba",sessionContext)).Steps!.Count==1),"LIVE model output retains only independently validated current-control pointers");
 }
 Console.WriteLine($"ALL {count} QA REGRESSION CHECKS PASSED");
}finally{Directory.Delete(root,true);}

sealed class Model:HttpMessageHandler{
 public List<string> Payloads=[];private readonly Queue<string> replies=new();public void Add<T>(T value)=>replies.Enqueue(JsonSerializer.Serialize(value,StateStore.Json));
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){Payloads.Add(await r.Content!.ReadAsStringAsync(ct));return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{message=new{content=replies.Dequeue()},done=true}))};}
}
sealed class Research:IWebResearch{
 public List<string> Calls=[];public bool ThrowSearch;
 public Task<IReadOnlyList<WebSource>> Search(string query,CancellationToken ct){Calls.Add("search:"+query);throw new BuddyException("SEARCH_UNAVAILABLE","Search blocked for fixture");}
 public Task<WebSource> Fetch(string url,CancellationToken ct){ct.ThrowIfCancellationRequested();Calls.Add("fetch:"+url);return Task.FromResult(new WebSource("Official Amoeba",url,"GitHub sign-in, organization and connected workspace; check the installed version. Never claim a control is visible from documentation.",Links:url=="https://useamoeba.com/"?[new("Getting started","https://useamoeba.com/docs/getting-started")]:[]));}
}
sealed class Pages:HttpMessageHandler{
 public string? Location;public int Calls;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){Calls++;var response=new HttpResponseMessage(HttpStatusCode.Redirect){Content=new StringContent("",Encoding.UTF8,"text/html")};response.Headers.Location=new(Location!);return Task.FromResult(response);}
}
