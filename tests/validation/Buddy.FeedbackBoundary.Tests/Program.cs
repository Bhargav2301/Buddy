using Buddy.Server;
using System.Diagnostics;
using System.Text.Json;

SourceReceipt.Verify("October 5 independent request binding and lifecycle checks; injected model only; no actions or native calls");
#if HAS_LIFECYCLE
const bool lifecycleAvailable=true;
#else
const bool lifecycleAvailable=false;
#endif
if(args.Contains("--lifecycle-only")&&!lifecycleAvailable){Console.WriteLine("NOT RUN: the selected source root has no lifecycle implementation.");return 2;}
int passed = 0, failed = 0;
async Task Case(string name, Func<Task> test)
{
    if (args.Contains("--settings-only") && !name.StartsWith("preferences: ", StringComparison.Ordinal)) return;
    if (args.Contains("--lifecycle-only") && !name.StartsWith("lifecycle: ", StringComparison.Ordinal)) return;
    if (args.Contains("--binding-only") && (name.StartsWith("preferences: ", StringComparison.Ordinal) || name.StartsWith("lifecycle: ", StringComparison.Ordinal))) return;
    var timer = Stopwatch.StartNew();
    try { await test(); passed++; Console.WriteLine(JsonSerializer.Serialize(new { kind="case", name, status="PASS", elapsedMs=timer.ElapsedMilliseconds })); }
    catch(Exception ex) { failed++; Console.WriteLine(JsonSerializer.Serialize(new { kind="case", name, status="FAIL", reason=ex.Message, error=ex.GetType().Name, elapsedMs=timer.ElapsedMilliseconds })); }
}
void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
ScreenContext Empty() => new("", "Owned fixture: no observed window", []);
AssistantPlan Open(string alias) => new("Review requested launch; nothing has run.", [new("open", Value:alias, Description:"Open " + alias, Risk:"high")]);

foreach (var row in new[] {
    (Query:"Open Camera app", Wrong:"calculator"),
    (Query:"Open spotify", Wrong:"comet"),
    (Query:"Open Notepad", Wrong:"calculator"),
    (Query:"Open File Explorer", Wrong:"notepad"),
    (Query:"Open https://example.com/manual", Wrong:"https://example.org/other")
}) await Case("requested target cannot be replaced: " + row.Query, async () => {
    using var f = new ModelFixture(); f.Model.Reply = _ => Open(row.Wrong);
    AssistantPlan result;
    try { result = await f.Service.PlanAgent(new(row.Query, Empty(), false), default); }
    catch(BuddyException ex) when(ex.Code is "INVALID_PLAN" or "INVALID_URL") { result = new("Rejected unsupported proposal", []); }
    Require(!result.Actions!.Any(a => a.Kind == "open" && a.Value == row.Wrong), "Host returned a supported but unrequested launch: " + row.Wrong);
    Require(f.Model.Calls <= 2, "Repair must remain bounded.");
    Require(await f.Saved() == 0, "Plan review must not persist execution or completion.");
});

foreach (var row in new[] { (Query:"Open Calculator", Alias:"calculator"), (Query:"Open Comet Browser", Alias:"comet"), (Query:"Open https://example.com/manual", Alias:"https://example.com/manual") })
await Case("explicit matching target remains reviewable: " + row.Query, async () => {
    using var f = new ModelFixture(); f.Model.Reply = _ => Open(row.Alias);
    var result = await f.Service.PlanAgent(new(row.Query, Empty()), default);
    Require(result.Actions is { Count:1 } && result.Actions[0].Kind == "open" && result.Actions[0].Value == row.Alias && result.Actions[0].Risk == "high", "Supported exact target must retain approval and target identity.");
});

await Case("unknown application cannot become a web substitution", async () => {
    using var f = new ModelFixture(); f.Model.Reply = _ => Open("https://open.spotify.com/");
    AssistantPlan result;
    try { result = await f.Service.PlanAgent(new("Open spotify", Empty()), default); }
    catch(BuddyException ex) when(ex.Code is "INVALID_PLAN" or "INVALID_URL") { result = new("Rejected unsupported proposal", []); }
    Require(result.Actions!.All(a => a.Kind=="open" && a.Value=="spotify"), "A browser destination requires a new explicit choice, not an inferred native-app substitution.");
});

await Case("explicitly prohibited URL cannot authorize the same launch", async () => {
    using var f = new ModelFixture(); f.Model.Reply = _ => Open("https://example.com/manual");
    AssistantPlan result;
    try { result=await f.Service.PlanAgent(new("Do not open https://example.com/manual", Empty()),default); }
    catch(BuddyException ex) when(ex.Code is "INVALID_PLAN" or "INVALID_URL") { result=new("Rejected",[]); }
    Require(result.Actions is {Count:0}, "A URL occurrence within a prohibition is not launch authorization.");
});

await Case("continuation retains original requested app binding", async () => {
    using var f = new ModelFixture(); f.Model.Reply = _ => new AgentDecision("continue", "Open calculator next.", [new("open", Value:"calculator", Description:"Open calculator")]);
    var previous = new ActionResult(1, new("read", "owned", "Status", "Text"), true, "Read owned status fixture");
    AgentDecision result;
    try { result = await f.Service.ContinueAgent(new("Open Camera app", Empty(), [previous], 24), default); }
    catch(BuddyException ex) when(ex.Code is "INVALID_PLAN" or "INVALID_URL") { result=new("clarify","Rejected",[]); }
    Require(result.Status != "continue" || result.Actions is { Count:0 } || result.Actions!.All(a => a.Kind != "open"), "A continuation must not substitute calculator for Camera.");
});

await Case("wrong-app prior receipt cannot justify task completion", async () => {
    using var f=new ModelFixture();f.Model.Reply=_=>new AgentDecision("done","Camera opened.",[]);
    var wrongReceipt=new ActionResult(1,new("open",Value:"calculator",Description:"Open calculator"),true,"Calculator window observed");
    AgentDecision result;
    try{result=await f.Service.ContinueAgent(new("Open Camera app",Empty(),[wrongReceipt],24),default);}
    catch(BuddyException ex) when(ex.Code is "INVALID_PLAN" or "INVALID_RUN"){result=new("clarify","Wrong requested target rejected",[]);}
    Require(result.Status!="done"&&f.Model.Calls==0,"A mismatched launch receipt must fail before model completion can legitimize it.");
});

await Case("pre-cancelled continuation performs no inference",async()=>{
    using var f=new ModelFixture();using var stop=new CancellationTokenSource();stop.Cancel();bool cancelled=false;
    var previous=new ActionResult(1,new("read","owned","Status","Text"),true,"Owned status");
    try{await f.Service.ContinueAgent(new("Open Camera app",Empty(),[previous],24),stop.Token);}catch(OperationCanceledException){cancelled=true;}
    Require(cancelled&&f.Model.Calls==0,"Continuation cancellation must precede result inspection/model dispatch.");
});

await Case("late cancelled continuation cannot claim task completion",async()=>{
    using var f=new ModelFixture();using var stop=new CancellationTokenSource();
    var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    f.Model.BeforeReply=async _=>{entered.TrySetResult();await release.Task;};
    f.Model.Reply=_=>new AgentDecision("done","Camera opened.",[]);
    var previous=new ActionResult(1,new("read","owned","Status","Text"),true,"Owned status");
    var pending=f.Service.ContinueAgent(new("Open Camera app",Empty(),[previous],24),stop.Token);
    try {await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));stop.Cancel();}
    finally {release.TrySetResult();}
    bool cancelled=false;
    try {await pending.WaitAsync(TimeSpan.FromSeconds(5));}catch(OperationCanceledException){cancelled=true;}
    Require(cancelled,"A late transport completion after Stop must not return a completed action claim.");
    Require(await f.Saved()==0,"Canceled continuation cannot persist task completion.");
});

await Case("pre-cancelled launch cannot plan or infer", async () => {
    using var f = new ModelFixture(); f.Model.Reply = _ => Open("comet"); using var stop = new CancellationTokenSource(); stop.Cancel();
    bool cancelled = false; try { await f.Service.PlanAgent(new("Open Comet Browser", Empty()), stop.Token); } catch(OperationCanceledException) { cancelled = true; }
    Require(cancelled && f.Model.Calls == 0, "Cancellation must precede deterministic or model-based plan creation.");
});

await Case("late cancelled plan cannot become the replacement request", async () => {
    using var f = new ModelFixture(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    f.Model.Reply = _ => Open("calculator");
    f.Model.BeforeReply = async _ => { entered.TrySetResult(); await release.Task; }; using var stop = new CancellationTokenSource();
    var old = f.Service.PlanAgent(new("Prepare a calculator action plan", Empty()), stop.Token);
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); stop.Cancel(); release.TrySetResult();
    bool cancelled = false; try { await old.WaitAsync(TimeSpan.FromSeconds(5)); } catch(OperationCanceledException) { cancelled = true; }
    f.Model.BeforeReply = null; f.Model.Reply = _ => Open("notepad");
    var current = await f.Service.PlanAgent(new("Open Notepad", Empty()), default);
    Require(cancelled && current.Actions is { Count:1 } && current.Actions[0].Value == "notepad", "Old completion must be discarded and inference ownership released.");
    Require(await f.Saved() == 0, "Canceled and reviewed plans do not persist action completion.");
});

await Case("Comet Guide cannot point into Buddy's own synthetic panel", async () => {
    using var f = new ModelFixture(); var context = new ScreenContext("buddy", "Owned Buddy panel fixture", [new("buddy-close", "Close", "Button", 0, 0, 60, 30), new("buddy-back", "Back", "Button", 70, 0, 60, 30)]);
    f.Model.Reply = _ => new GuidePlan("Use the controls.", [new("Click Back.", "buddy-back", "Back", "Button")]);
    var result = await f.Service.PlanGuide(new("Guide me through Comet browser", context), default);
    Require(result.Steps is { Count:0 }, "Coincidental Close/Back names in Buddy are not Comet evidence.");
    Require(f.Model.Calls == 0, "Known wrong-app orientation needs no invented model pointer.");
});

await PreferenceCases.Run(Case, Require);
#if HAS_LIFECYCLE
await LifecycleCases.Run(Case, Require);
#endif
Console.WriteLine(JsonSerializer.Serialize(new {kind="summary", passed,failed,lifecycleAvailable, actualActions=0, native="NOT RUN", realModel="NOT RUN", installedProfile="NOT READ"}));
return failed == 0 ? 0 : 1;
