using Buddy.Server;
using Buddy.Windows;

int checks = 0;
void Check(bool value, string description) { if (!value) throw new Exception("FAIL: " + description); checks++; Console.WriteLine("PASS: " + description); }
void Refuse(Action action, string description) { bool refused=false; try { action(); } catch(Exception ex) when(ex is InvalidOperationException or BuddyException) { refused=true; } Check(refused,description); }
RefinementDraftOptions Basic() => new("auto", "general", false, false, "", "", "", [], [], false, "", "", "utf16-code-units");
var slot = new ExternalRefinementOptionsSlot();
Check(!slot.HasPending && slot.Consume(0) is null, "No preparation means ordinary quick capture remains available");
var examples = new List<RefinementExample> { new("seed input", "seed output") };
var sources = new List<RefinementContextSource> { new("notes", "Reviewed notes", "Only supplied facts", "document", "confirmed-decision", true) };
var options = Basic() with { Technique="few-shot", Examples=examples, References=sources, Constraints="Do not add an audience.", Tools="Local editor", Stages="Read the supplied notes\nDraft the answer", HasBudget=true,Destination="My field",Limit="5000",Unit="utf8-bytes" };
slot.Arm(options,"guided",100);
Check(slot.HasPending,"Explicit preparation arms one capture");
examples[0]=new("changed input","changed output"); sources[0]=sources[0] with { Text="MUTATED AFTER ARM" }; sources.Clear();
var plan=slot.Consume(101)!;
Check(!slot.HasPending&&slot.Consume(102) is null,"Taking options burns pending token before capture succeeds");
const string original="Write a poem about a boat sailing in a sea on a lonely night.";
var prepared=plan.Bind(original);
Check(prepared.Request.Prompt==original,"Fresh exact source, never a preparation placeholder, becomes the request");
Check(prepared.Request.Mode=="guided"&&prepared.Request.Technique=="few-shot","Prepared mode and technique reach request intact");
Check(prepared.Request.Inputs!.Examples![0].Input=="seed input","Mutating caller example list cannot change frozen options");
Check(prepared.Request.Inputs.Context![0].Text=="Only supplied facts","Mutating or clearing caller references cannot change frozen options");
Check(prepared.Request.Inputs.ConfirmedConstraints!.Single()=="Do not add an audience.","Explicit confirmed constraint is retained");
Check(prepared.Request.Inputs.AvailableTools!.Single()=="Local editor"&&prepared.Request.Inputs.Stages!.SequenceEqual(new[]{"Read the supplied notes","Draft the answer"}),"Ordered stages and listed tools retain their supplied values");
Check(prepared.Budget is { Fits:true,Limit:5000,Unit:"utf8-bytes" }&&prepared.Ready,"Destination count and supported prerequisites check against full fresh content");
Check(prepared.AssembledText.Contains("BEGIN_UNTRUSTED_CONTEXT_JSON")&&prepared.AssembledText.Contains("confirmed-decision"),"Reviewed source is visible marked data, not a hidden instruction");
Refuse(()=>plan.Bind("Another field"),"Consumed preparation cannot bind a second field");
slot.Clear();Check(!plan.IsCurrent,"Stop invalidates options authority even after consumption and binding");

slot.Arm(Basic(),"quick",1000); var obsolete=slot.Consume(1001)!;slot.Arm(Basic(),"auto",1002);
Check(!obsolete.IsCurrent,"Replacing options invalidates previous in-flight capture lease");
Refuse(()=>obsolete.Bind(original),"Late capture cannot bind after options replacement");
Check(slot.Consume(1003)!.Bind(original).Request.Mode=="auto","Only replacement snapshot can bind next field");
slot.Arm(Basic(),"quick",2000);Refuse(()=>slot.Consume(2000+ExternalRefinementOptionsSlot.LifetimeMilliseconds),"Five-minute boundary expires before source capture");
Check(!slot.HasPending,"Expired preparation is removed, never applied to next unrelated field");
slot.Arm(Basic(),"quick",2000);Refuse(()=>slot.Consume(1999),"Backward clock invalidates preparation");
slot.Arm(Basic(),"quick",2000);Check(slot.Consume(301999)!.Bind(original).Ready,"Last millisecond inside lease remains valid");
slot.Arm(Basic(),"quick",2000);var old=slot.Consume(2001)!;
Refuse(()=>slot.Arm(Basic(),"unsupported",2002),"Unsupported mode is rejected before capture");
Check(!slot.HasPending&&!old.IsCurrent,"Invalid replacement cannot resurrect previous prepared options");
Refuse(()=>slot.Arm(Basic(),"quick",-1),"Invalid preparation clock is refused");
Refuse(()=>slot.Arm(Basic() with {HasBudget=true,Destination="My field",Limit="0"},"quick",3000),"Invalid explicit destination limit is rejected during preparation");
Refuse(()=>slot.Arm(Basic() with {Examples=[new("", "answer")]},"quick",3000),"Incomplete example pair is rejected during preparation");

slot.Arm(Basic() with {Technique="chaining"},"auto",3000);
var staged=slot.Consume(3001)!.Bind("First summarize the notes. Then list risks.");
Check(staged.Ready&&staged.Choice.Technique=="chaining","Task-dependent technique readiness uses fresh field, not placeholder");
slot.Arm(Basic() with {Technique="chaining"},"auto",3000);
Check(!slot.Consume(3001)!.Bind(original).Ready,"Missing task prerequisites block refinement after capture");
slot.Arm(Basic() with {HasBudget=true,Destination="Small field",Limit="5"},"quick",3000);
var overflow=slot.Consume(3001)!.Bind(original);
Check(!overflow.Ready&&!overflow.Budget.Fits&&overflow.AssembledText==original,"Required source overflow remains visible and cannot be clipped into approval");
slot.Arm(Basic() with {HasBudget=true,Destination="Small field",Limit="100",References=[new("optional","Long optional note",new string('a',300))]},"quick",3000);
var omitted=slot.Consume(3001)!.Bind(original);
Check(omitted.Ready&&omitted.Budget.Removed.SequenceEqual(new[]{"source-optional"})&&omitted.AssembledText==original,"Only optional source data is omitted and explicitly enumerated under budget");
slot.Arm(Basic() with {References=[new("link","Required link","See https://example.com/private",Required:true)]},"quick",3000);
Check(!slot.Consume(3001)!.Bind(original).Ready,"Required unverified URL does not gain retrieval authority from prepared options");
slot.Arm(Basic(),"quick",3000);var empty=slot.Consume(3001)!;
Refuse(()=>empty.Bind(""),"Invalid captured source cannot be bound");
Refuse(()=>empty.Bind(original),"Failed binding does not permit reuse for another capture");

var field = new MemoryField(original); var edit = new GuardedEdit(field,original);
var now = DateTimeOffset.Parse("2026-10-05T17:00:00Z");
const string proposal="Request: Write a poem.\n\nSubject: a boat sailing in a sea on a lonely night.";
Check(field.Writes==0&&field.Read()==original,"Preparing options and results does not edit the source");
edit.Apply(proposal,now,default);Check(field.Read()==proposal&&field.Writes==1,"Existing explicit guarded Apply performs one expected-value write");
Refuse(()=>edit.Apply(proposal,now,default),"The same review cannot apply twice");
edit.Undo(now.AddSeconds(1),default);Check(field.Read()==original&&field.Writes==2,"Existing guarded Undo restores exact original without sending");
var staleEdit=new GuardedEdit(field,original);field.Value="USER CHANGED FIELD";
Refuse(()=>staleEdit.Apply(proposal,now,default),"Changed source value refuses overwrite");Check(field.Writes==2,"Refused stale edit performs no write");
field.Value=original;var identityEdit=new GuardedEdit(field,original);field.Id="replacement";
Refuse(()=>identityEdit.Apply(proposal,now,default),"Replaced source identity refuses overwrite");
field.Id="original";var cancelled=new GuardedEdit(field,original);using var cts=new CancellationTokenSource();cts.Cancel();
bool cancelledBeforeWrite=false;try{cancelled.Apply(proposal,now,cts.Token);}catch(OperationCanceledException){cancelledBeforeWrite=true;}
Check(cancelledBeforeWrite&&field.Writes==2,"Cancellation still prevents external write");
var undoExpired=new GuardedEdit(field,original);undoExpired.Apply(proposal,now,default);
Refuse(()=>undoExpired.Undo(now.AddSeconds(31),default),"Prepared options cannot extend existing thirty-second Undo expiry");
Check(!RefinementCore.Fidelity(original,"Write a 20-line rhyming poem about a boat sailing in a sea on a lonely night.").Allowed,"Additional desired quality does not authorize invented constraints");
Console.WriteLine($"{checks} refinement followup checks passed. Pure snapshots and memory fields only; no model, browser, native UI or physical acceptance.");

sealed class MemoryField(string text) : IVerifiedTextField
{
    internal string Value=text,Id="original"; internal int Writes;
    public string Identity=>Id;
    public string Read()=>Value;
    public void Write(string expected,string replacement,CancellationToken ct){ct.ThrowIfCancellationRequested();if(Value!=expected)throw new InvalidOperationException("changed");Value=replacement;Writes++;}
}
