using Buddy.Server;
using Microsoft.AspNetCore.DataProtection;
using System.Net;
using System.Text;
using System.Text.Json;

int count = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); count++; Console.WriteLine("PASS: " + name); }
void Reject(GuidePlan plan, ScreenContext screen, string name) { try { TeachingPolicy.Validate(plan, screen); } catch (BuddyException) { Check(true, name); return; } throw new Exception("FAIL: " + name); }
var context = new ScreenContext("fixture", "Private fixture", [new("observed-export", "Export", "Button", -150, 50, 90, 35)]);
var step = new GuideStep("Choose Export.", "observed-export", "Export", "Button", "circle");
var valid = new GuidePlan("Review the destination before saving. Choose Export.", [step]);
foreach (var primitive in TeachingPolicy.Primitives) Check(TeachingPolicy.Validate(valid with { Steps = [step with { Primitive = primitive }] }, context).Step?.Primitive == primitive, "Accepted grounded " + primitive + " annotation");
Reject(valid with { Steps = [step, step] }, context, "A teaching turn cannot contain a second action or step");
Reject(valid with { Summary = "One. Two. Three. Do not overwrite the original." }, context, "An overlong reply is rejected whole, never truncated before a safety qualification");
Reject(valid with { Steps = [step with { Ref = "invented" }] }, context, "Invented model reference is rejected");
Reject(valid with { Steps = [step with { Target = "Delete" }] }, context, "Known reference with a contradictory name is rejected");
Reject(valid with { Steps = [step with { Role = "Edit" }] }, context, "Known reference with the wrong role is rejected");
Reject(valid with { Steps = [step with { Primitive = "click" }] }, context, "An action cannot be encoded as an annotation primitive");
Reject(valid with { Steps = [step with { Instruction = "Delete the file." }] }, context, "Visual step cannot silently contradict the spoken instruction");
Reject(valid with { Summary = "Choose Export. This will open the Save dialog." }, context, "An unobserved future dialog transition is rejected, not spoken as fact");
Reject(valid with { Summary = "Choose Export. This opens the Save dialog." }, context, "Present-tense unobserved transition claims are also rejected");
Reject(valid, context with { Elements = [context.Elements[0] with { Enabled = false }] }, "Disabled control is not a teachable active target");
Reject(valid, context with { Elements = [context.Elements[0], context.Elements[0]] }, "Ambiguous duplicate references are rejected");
Check(TeachingPolicy.Validate(valid with { Steps = [step with { Expect = new("visible", "Export", "Button") }] }, context).Step?.Expect?.Kind == "manual", "Model cannot turn a teaching step into automatic advancement");
Check(TeachingPolicy.Validate(new("Please focus the Export dialog.", []), context).Step is null, "Clarification is allowed without invented annotations");
Check(ConversationalReply.PlainText("Choose Export. [circle: 12, 40, 8] <arrow x=5>") == "Choose Export.", "Annotation markup and coordinates are removed from spoken text");
Check(AssistantSchemas.Teaching.GetProperty("properties").GetProperty("steps").GetProperty("maxItems").GetInt32() == 4, "Model schema bounds current-screen annotation packets to four targets");
var second=new GuideStep("Compare Preview.","observed-preview","Preview","Button","arrow");
var multiContext=context with{Elements=[..context.Elements,new("observed-preview","Preview","Button",0,50,90,35)]};
var multi=TeachingPolicy.Validate(new("Choose Export. Compare Preview.",[step,second]),multiContext);
Check(multi.Targets.Count==2&&multi.Targets.All(s=>s.Expect?.Kind=="manual"),"Two independently observed target annotations survive validation without executable actions");
Reject(new("Choose Export. Compare Preview.",[step,second with{Ref="invented"}]),multiContext,"One invented target invalidates the entire annotation packet");
Reject(new("Choose Export.",Enumerable.Repeat(step,5).ToList()),context,"More than four markings are refused");
Check(!AssistantSchemas.Teaching.GetRawText().Contains("actions"), "Teaching schema exposes no executable action list");

var folder = Path.Combine(Path.GetTempPath(), "Buddy-teaching-tests-" + Guid.NewGuid()); Directory.CreateDirectory(folder);
try {
    var store = new StateStore(folder, DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(folder, "keys")))); await store.EnsureSaved();
    var notes=new TeachingMemoryStore(folder,DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(folder,"teaching-test-keys"))));
    Check((await notes.Read()).Count==0&&!File.Exists(Path.Combine(folder,"teaching.v1.encrypted")),"Disabled/empty teaching memory reads create no content file");
    for(int i=0;i<12;i++)await notes.Save("fixture","unique question "+i,"qualified answer");
    await notes.Save("another","another question","another answer");
    Check((await notes.Read("fixture")).Count==10&&(await notes.Read("another")).Count==1,"Opt-in memory bounds each app independently");
    Check(!Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(folder,"teaching.v1.encrypted"))).Contains("unique question"),"Saved teaching Q&A is encrypted on disk");
    var reopenedNotes=new TeachingMemoryStore(folder,DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(folder,"teaching-test-keys"))));
    Check((await reopenedNotes.Read("fixture")).Last().Question=="unique question 11","Saved Q&A reopens with intact app provenance");
    using(var stopSave=new CancellationTokenSource()){stopSave.Cancel();bool refused=false;try{await notes.Save("fixture","cancelled question","answer",stopSave.Token);}catch(OperationCanceledException){refused=true;}Check(refused&&!(await notes.Read()).Any(r=>r.Question=="cancelled question"),"Cancelled teaching memory writes preserve the previous file");}
    await notes.Clear();Check((await notes.Read()).Count==0,"Dedicated clear removes all saved teaching questions and answers");
    using var model = new TeachingModel(); using var client = new HttpClient(model) { BaseAddress = new("http://127.0.0.1:11434") };
    var service = new BuddyService(store, new(client)) { WebEnabled = true, AgentEnabled = true };
    model.Replies.Enqueue(valid);
    var reply = await service.Teach(new("Teach export", context), default);
    Check(reply.Speech == valid.Summary && reply.Step?.Target == "Export", "Real service returns the qualified one-step explanation");
    Check(model.Requests.Count == 1 && model.Requests[0].Path == "/api/chat", "Teaching uses one local structured inference and no web/tool call");
    Check(!model.Requests[0].Body.Contains("images"), "UIA-grounded teaching does not transmit a screenshot to inference");
    model.Replies.Enqueue(valid with { Steps = [step, step] }); model.Replies.Enqueue(valid);
    int start = model.Requests.Count; reply = await service.Teach(new("Teach export", context), default);
    Check(model.Requests.Count == start + 2 && reply.Speech.StartsWith("Review the destination"), "Invalid output is recomposed once with the safety qualification intact");
    model.Replies.Enqueue(valid with { Summary = "Choose Export. This will open another dialog. Review the destination before saving." });
    model.Replies.Enqueue(new { speech = "Choose Export. Review the destination before saving." });
    start = model.Requests.Count; reply = await service.Teach(new("Teach export", context), default);
    Check(reply.Step?.Ref == step.Ref && reply.Speech.Contains("Review the destination before saving.") && !reply.Speech.Contains("will") && model.Requests.Count == start + 2, "Dedicated whole-response repair preserves the full safety qualification and grounded step");
    model.Replies.Enqueue(valid with { Steps = [step, step] }); model.Replies.Enqueue(valid with { Steps = [step, step] });
    reply = await service.Teach(new("Teach export", context), default);
    Check(reply.Step is null && reply.Speech == ConversationalReply.Fallback, "Repeated invalid output becomes a safe concise answer with no annotation");
    var next = new GuideStep("Choose Save.", "fresh-save", "Save", "Button", "arrow");
    model.Replies.Enqueue(new GuidePlan("Check the filename. Choose Save.", [next]));
    reply = await service.Teach(new("Teach export", context with { Title = "Save dialog", Elements = [new("fresh-save", "Save", "Button", 4, 9, 80, 30)] }, [step.Instruction]), default);
    var last = model.Requests[^1].Body;
    Check(reply.Step?.Ref == "fresh-save" && last.Contains("fresh-save") && !last.Contains("observed-export"), "Continuation planning uses the fresh context instead of the previous target");
    Check(last.Contains("untrustedPreviousSuggestions") && last.Contains("not proof"), "History is explicitly distinguished from verified completion");
    model.Replies.Enqueue(valid);
    await service.Teach(new("Teach this area",context,RegionImageBase64:Convert.ToBase64String([1,2,3])),default);
    using(var regionPayload=JsonDocument.Parse(model.Requests[^1].Body)) {
        Check(regionPayload.RootElement.GetProperty("model").GetString()=="gemma3:4b"&&regionPayload.RootElement.GetProperty("messages")[1].TryGetProperty("images",out _),"Explicit regional image requests use the local vision model, not a cloud adapter");
    }
    int beforeInvalidImage=model.Requests.Count;bool invalidImage=false;
    model.Replies.Enqueue(new GuidePlan("The visible chart has two bars.",[]));
    await service.Teach(new("What about the second bar?",context,RegionImageBase64:Convert.ToBase64String([1,2,3]),Conversation:[new("Explain this chart.","There are two bars.")],ImageScope:"window"),default);
    Check(model.Requests[^1].Body.Contains("untrustedQuestionAnswerHistory")&&model.Requests[^1].Body.Contains("There are two bars.")&&model.Requests[^1].Body.Contains("selected scope is window"),"Explicit selected-window image and bounded follow-up Q&A reach local vision as untrusted evidence");
    beforeInvalidImage=model.Requests.Count;
    bool invalidHistory=false;try{await service.Teach(new("follow up",context,Conversation:Enumerable.Repeat(new TeachingExchange("q","a"),11).ToArray()),default);}catch(BuddyException){invalidHistory=true;}
    Check(invalidHistory&&model.Requests.Count==beforeInvalidImage,"Oversize conversational history is refused before inference");
    try{await service.Teach(new("area",context,RegionImageBase64:"not base64"),default);}catch(BuddyException e)when(e.Code=="INVALID_IMAGE"){invalidImage=true;}
    Check(invalidImage&&model.Requests.Count==beforeInvalidImage,"Malformed regional image fails before inference");
    invalidImage=false;try{await service.Teach(new("area",context,RegionImageBase64:new string('a',2_800_001)),default);}catch(BuddyException e)when(e.Code=="INVALID_IMAGE"){invalidImage=true;}
    Check(invalidImage&&model.Requests.Count==beforeInvalidImage,"Oversize regional input fails before decode or inference");
    start = model.Requests.Count; bool bounded = false;
    try { await service.Teach(new("task", context, Enumerable.Repeat("old", 9).ToArray()), default); } catch (BuddyException) { bounded = true; }
    Check(bounded && model.Requests.Count == start, "Excessive history fails before inference");
    using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); bool stopped = false;
    try { await service.Teach(new("task", context), cancelled.Token); } catch (OperationCanceledException) { stopped = true; }
    Check(stopped && model.Requests.Count == start, "Pre-cancelled request starts no inference or retry");
    model.Hold = true; using var active = new CancellationTokenSource();
    var pending = service.Teach(new("task", context), active.Token); await model.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
    active.Cancel(); stopped = false;
    try { await pending.WaitAsync(TimeSpan.FromSeconds(2)); } catch (OperationCanceledException) { stopped = true; }
    Check(stopped && model.Requests.Count == start + 1, "Active Stop cancels local inference without retrying");
    Check(!Directory.GetFiles(folder,"*",SearchOption.AllDirectories).Any(f => new[]{".png",".jpg",".wav"}.Contains(Path.GetExtension(f))), "Teaching tests persist no screen or audio files");
    if (args.Contains("--local-model")) {
        await store.Update(s => { s.Model = "gemma3:4b"; return true; });
        using var localClient = new HttpClient(new LocalTeachingProbe()) { BaseAddress = new("http://127.0.0.1:11434"), Timeout = TimeSpan.FromMinutes(3) };
        var localService = new BuddyService(store, new(localClient));
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var localTurn = await localService.Teach(new("Show me the next step to export this document. The Export button is visible.", context), deadline.Token);
        Console.WriteLine("LOCAL MODEL FIRST: " + JsonSerializer.Serialize(localTurn, StateStore.Json));
        Check(localTurn.Step?.Ref == "observed-export" && ConversationalReply.IsConcise(localTurn.Speech), "Installed Gemma model produces a grounded concise first teaching step");
        var saveContext = context with { Title = "Save dialog", Elements = [new("fresh-save", "Save", "Button", 4, 9, 80, 30)] };
        localTurn = await localService.Teach(new("Show me the next step to export this document.", saveContext, [localTurn.Step!.Instruction]), deadline.Token);
        Console.WriteLine("LOCAL MODEL NEXT: " + JsonSerializer.Serialize(localTurn, StateStore.Json));
        Check(localTurn.Step?.Ref == "fresh-save" && ConversationalReply.IsConcise(localTurn.Speech), "Installed Gemma model plans the next step from changed synthetic screen context");
        Check(!localTurn.Speech.Contains("open the Save dialog", StringComparison.OrdinalIgnoreCase) && !localTurn.Speech.Contains("open a Save dialog", StringComparison.OrdinalIgnoreCase), "Observed Save dialog is not incorrectly described as a future transition in the local-model sample");
    }
} finally { Directory.Delete(folder, true); }
Console.WriteLine($"ALL {count} TEACHING POLICY AND SERVICE CHECKS PASSED");

sealed class TeachingModel : HttpMessageHandler
{
    public readonly Queue<object> Replies = new();
    public readonly List<(string Path, string Body)> Requests = new();
    public bool Hold;
    public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = await request.Content!.ReadAsStringAsync(ct); Requests.Add((request.RequestUri!.AbsolutePath, body));
        if (Hold) { Entered.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
        var json = JsonSerializer.Serialize(new { message = new { content = JsonSerializer.Serialize(Replies.Dequeue(), StateStore.Json) }, done = true });
        return new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }
}
sealed class LocalTeachingProbe : DelegatingHandler
{
    public LocalTeachingProbe() : base(new HttpClientHandler()) { }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await base.SendAsync(request, ct);
        // Opt-in test only: response to synthetic screen descriptions, never live user content.
        Console.WriteLine("LOCAL STRUCTURED: " + await response.Content.ReadAsStringAsync(ct));
        return response;
    }
}
