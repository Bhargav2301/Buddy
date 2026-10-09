using Buddy.Server;
using Buddy.Windows;
using System.Runtime.CompilerServices;
using System.Text.Json;

internal static class Program
{
    private static int checks;
    private static readonly DateTimeOffset Now=new(2026,10,7,12,0,0,TimeSpan.Zero);
    private static readonly RefinementResult Accepted=new("Synthetic accepted result","fixture","quick","zero-shot",true,1,null,null,[],[],"Ready for review.");
    private static async Task<int> Main()
    {
        try {
            await HistoryCapacity();await RepeatedHistoryAndClear();await DispatcherIsolation();
            await RequestInvalidation();await ExternalDelivery();Budgets();await CapacityClear();
            Console.WriteLine($"PASS: {checks} context integration assertions; synthetic chat/streams/fields only. No WPF, native calls, model, network or user files.");
            return 0;
        }catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    private static void Check(bool value,string label){if(!value)throw new Exception("FAIL: "+label);checks++;Console.WriteLine("PASS: "+label);}
    private static void Reject(Action action,string label)
    {
        try{action();}catch(Exception e)when(e is InvalidOperationException or OperationCanceledException or BuddyException){Check(true,label);return;}
        throw new Exception("FAIL: "+label);
    }
    private static async Task RejectAsync(Func<Task> action,string label)
    {
        try{await action();}catch(Exception e)when(e is InvalidOperationException or OperationCanceledException or BuddyException){Check(true,label);return;}
        throw new Exception("FAIL: "+label);
    }
    private static NotchChatSession Chat()
    {
        long tick=0;
        return new(()=>[new("synthetic-local","Synthetic local")],
            (r,_)=>Task.FromResult(new NotchChatReply("Answer to "+r.Text)),
            readFile:(_,_)=>throw new Exception("The fixture must never read a user file."),clock:()=>Now.AddTicks(++tick));
    }
    private static async Task Send(NotchChatSession chat,string text)
    {if(!chat.SetDraft(text)||!await chat.Send())throw new Exception("Synthetic chat failed to complete "+text);}
    private static FrozenRefinementContext Freeze(RefinementWorkspace workspace,RefinementChatScope scope,string? referent=null)
    {
        var s=workspace.Snapshot(scope);
        return workspace.Freeze(scope,s.Revision,s.Sources.Select(x=>x.Id).ToArray(),s.Turns.Select(x=>x.Id).ToArray(),referent);
    }
    private static void Add(RefinementWorkspace workspace,RefinementChatScope scope,string text="Use metric units.",bool required=true)
    {
        var s=workspace.StageSource(scope,new("Synthetic note",text,Required:required),workspace.Snapshot(scope).Revision);
        var source=s.Sources[^1];workspace.ReviewSource(scope,source.Id,source.ReviewDigest,s.Revision);
    }
    private static string Payload(RefinementContextProjection p)=>"Please revise this synthetic draft.\n\n"+string.Join("\n\n",RefinementContext.Build(p.Sources).Blocks.Select(x=>x.Text));
    private static RefinementDraftOptions Options(RefinementContextProjection p,bool budget=false,string limit="4000")=>
        new("auto","general",false,false,"","","",[],p.Sources,budget,"Owned synthetic destination",limit,"utf16-code-units");
    private static ContextDestinationBinding Target(string chat)=>new("synthetic-exact-field","Owned synthetic destination","owned-field",new string('c',64),chat,false);

    private static async Task HistoryCapacity()
    {
        using var chat=Chat();using var host=new MainWindow(chat);
        for(int i=1;i<=21;i++)await Send(chat,"prompt "+i);
        var s=host.Workspace.Snapshot(host.Scope);
        Check(chat.Snapshot.HistoryRevision==21&&chat.Snapshot.Messages.Count==40,"21 completed pairs advance history revision while the notch retains 20 pairs");
        Check(s.Turns.Count==20&&s.Turns.All(x=>x.UserText!="prompt 21"),"Full workspace refuses turn 21 without evicting reviewed history");
        Check(chat.Snapshot.ContextNotice.Contains("full",StringComparison.OrdinalIgnoreCase),"Capacity refusal remains visible");
        host.Workspace.RemoveTurn(host.Scope,s.Turns[0].Id,s.Revision);
        var frozen=Freeze(host.Workspace,host.Scope);var projection=ContextDeliveryPlan.Project(frozen,8000);
        host.ArmSelection(frozen,projection);
        Check(host.Armed&&host.Workspace.IsCurrent(frozen),"Removing an older pair then arming survives its own ready-notice event");
        var revision=host.Workspace.Snapshot(host.Scope).Revision;
        chat.SetContextNotice("A presentation-only update.");chat.SetDraft("an edited draft");chat.SelectModel("synthetic-local");host.Sync();
        Check(host.Workspace.Snapshot(host.Scope).Revision==revision&&host.Workspace.IsCurrent(frozen),"Notice, draft, model and duplicate sync do not import pending unseen history");
        Check(host.Workspace.Snapshot(host.Scope).Turns.Count==19,"Presentation refresh leaves the 19 explicitly reviewed pairs unchanged");
        var consumed=host.Consume();
        Check(consumed is not null&&!consumed.Value.Invalidated.IsCancellationRequested,"Clearing the notice during one-use consumption does not invalidate the selection");
        Check(consumed!.Value.Text==string.Join("\n\n",RefinementContext.Build(projection.Sources).Blocks.Select(x=>x.Text)),"Local consumption delivers the exact projected context once");
        Check(host.Consume() is null&&!host.Armed,"A local preparation cannot accompany two messages");
        host.ArmSelection(frozen,projection);host.Revoke();
        Check(!host.Armed&&host.Suspends==1&&host.CancelledOptions==1,"Reopening context revokes the local slot and suspends the old external review");
        Check(host.Workspace.IsCurrent(frozen),"Revoking a delivery slot does not silently clear retained workspace data");
        var window=host.OpenSyntheticWindow();var oldScope=host.Scope;
        Check(chat.NewChat(),"New chat succeeds when idle");
        Check(window.Closed&&host.Scope.Id!=oldScope.Id&&frozen.Invalidated.IsCancellationRequested,"New chat closes the old context window and invalidates the old session");
        Check(host.Workspace.Snapshot(host.Scope).Turns.Count==0&&!host.Armed,"New chat begins without previous turns or armed context");
    }
    private static async Task RepeatedHistoryAndClear()
    {
        using var chat=Chat();using var host=new MainWindow(chat);
        await Send(chat,"repeat this");await Send(chat,"repeat this");
        var s=host.Workspace.Snapshot(host.Scope);
        Check(s.Turns.Count==2&&s.Turns.Select(x=>x.Id).Distinct().Count()==2,"Repeated text from two completed exchanges retains distinct completion identities");
        var before=chat.Snapshot.HistoryRevision;chat.Stop();chat.SetContextNotice("Stopped display");
        Check(chat.Snapshot.HistoryRevision==before&&host.Workspace.Snapshot(host.Scope).Turns.Count==2,"Stop and presentation callbacks do not duplicate completed pairs");
        var frozen=Freeze(host.Workspace,host.Scope);host.ArmSelection(frozen,ContextDeliveryPlan.Project(frozen,8000));
        host.Workspace.Clear(host.Scope);
        Check(frozen.Invalidated.IsCancellationRequested,"Clear immediately invalidates an armed history lease");
        Reject(()=>host.Consume(),"A cleared prepared selection cannot be consumed");
        host.Sync();chat.SetContextNotice("Cleared display");
        Check(host.Workspace.Snapshot(host.Scope).Turns.Count==0,"Presentation callbacks do not repopulate cleared history");
        await Send(chat,"fresh after clear");s=host.Workspace.Snapshot(host.Scope);
        Check(s.Turns.Count==1&&s.Turns[0].UserText=="fresh after clear","A new completion after clear imports only the new pair when earlier pairs were observed");
        using var other=Chat();using var otherHost=new MainWindow(other);
        Check(otherHost.Scope.Id!=host.Scope.Id&&otherHost.Workspace.Snapshot(otherHost.Scope).Turns.Count==0,"Independent local chat hosts never share completed history");
    }
    private static async Task CapacityClear()
    {
        using var chat=Chat();using var host=new MainWindow(chat);
        for(int i=1;i<=21;i++)await Send(chat,"capacity-clear "+i);
        host.Workspace.Clear(host.Scope);chat.SetContextNotice("Cleared");
        Check(host.Workspace.Snapshot(host.Scope).Turns.Count==0,"Clear removes retained context even after a capacity refusal");
        await Send(chat,"only after clear");var state=host.Workspace.Snapshot(host.Scope);
        Check(state.Turns.Count==1&&state.Turns[0].UserText=="only after clear","A post-Clear completion does not resurrect a previously refused pre-Clear pair");
    }
    private static async Task DispatcherIsolation()
    {
        using var chat=Chat();using var host=new MainWindow(chat);host.Dispatcher.OnDispatcher=false;
        await Send(chat,"background completion");
        Check(host.Dispatcher.Pending>0&&host.Workspace.Snapshot(host.Scope).Turns.Count==0,"Background callbacks queue context synchronization instead of mutating the workspace");
        host.Dispatcher.Drain();
        Check(host.Workspace.Snapshot(host.Scope).Turns.Count==1,"Multiple queued callbacks import a completion only once on the synthetic dispatcher");
    }
    private static async Task RequestInvalidation()
    {
        using var workspace=new RefinementWorkspace();var scope=new RefinementChatScope("manual-external","request-fixture");workspace.Open(scope);Add(workspace,scope);
        var frozen=Freeze(workspace,scope);var selection=new ExternalContextSelection(workspace,frozen,ContextDeliveryPlan.Project(frozen));
        using var lifetime=new CancellationTokenSource();var stream=new HeldStream();RefinementRequest? observed=null;
        var running=ProductionSeams.RunSelected(lifetime,selection,token=>{stream.Token=token;return stream;},r=>observed=r);
        await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));workspace.Clear(scope);
        var outcome=await running.WaitAsync(TimeSpan.FromSeconds(3));
        Check(stream.Token.IsCancellationRequested,"Production inline token linkage cancels the injected pending stream on Clear");
        Check(outcome.State==RefinementRequestState.Cancelled&&outcome.Result is null&&observed?.Result is null,"Cleared context produces a terminal cancellation with no acceptable result");
        Check(!stream.Disposed,"Cancellation does not concurrently dispose an iterator whose MoveNext ignores cancellation");
        stream.Release.TrySetResult();await stream.Finished.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Check(observed?.State==RefinementRequestState.Cancelled&&observed.Result is null,"Late synthetic completion cannot revive a cleared-context result");
        bool called=false;
        outcome=await ProductionSeams.RunSelected(lifetime,selection,_=>{called=true;return Events();});
        Check(!called&&outcome.State==RefinementRequestState.Cancelled,"An already invalid selection cannot start refinement");
        Add(workspace,scope);frozen=Freeze(workspace,scope);selection=new(workspace,frozen,ContextDeliveryPlan.Project(frozen));
        stream=new();running=ProductionSeams.RunSelected(lifetime,selection,token=>{stream.Token=token;return stream;});
        await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));lifetime.Cancel();outcome=await running.WaitAsync(TimeSpan.FromSeconds(3));
        Check(outcome.State==RefinementRequestState.Cancelled&&stream.Token.IsCancellationRequested,"Closing or suspending the inline host cancels through its existing lifetime");
        stream.Release.TrySetResult();await stream.Finished.Task.WaitAsync(TimeSpan.FromSeconds(3));
    }
    private static async Task ExternalDelivery()
    {
        using var workspace=new RefinementWorkspace();var scope=new RefinementChatScope("manual-external","explicit-chat-fixture");workspace.Open(scope);Add(workspace,scope);
        var frozen=Freeze(workspace,scope);var projection=ContextDeliveryPlan.Project(frozen);
        var selection=new ExternalContextSelection(workspace,frozen,projection);var slot=new ExternalRefinementOptionsSlot();
        var mutable=projection.Sources.ToList();slot.Arm(Options(projection) with{References=mutable},"quick",100,()=>workspace.IsCurrent(frozen),selection);mutable.Clear();
        var plan=slot.Consume(101)!;var prepared=plan.Bind("original synthetic draft");
        Check(ReferenceEquals(plan.ContextSelection,selection)&&prepared.Request.Inputs!.Context!.Count==projection.Sources.Count,"The one-use option plan preserves the exact selection and copies its source list");
        Check(prepared.Request.Inputs!.Context![0].Text==projection.Sources[0].Text,"The captured request receives exact selected context text");
        Check(slot.Consume(102) is null,"Prepared external options belong to one capture");
        Reject(()=>plan.Bind("different draft"),"A consumed plan cannot bind a second field draft");
        slot.Clear();Check(!plan.IsCurrent,"Reopening preparation invalidates its earlier captured plan");
        slot.Arm(Options(projection),"quick",100,contextSelection:selection);
        Reject(()=>slot.Consume(100+ExternalRefinementOptionsSlot.LifetimeMilliseconds),"Expired prepared options cannot start a capture");
        string payload=Payload(projection);var target=Target(scope.Id);
        var field=new SyntheticField("original synthetic draft");var draft=new SyntheticDraft(new GuardedEdit(field,field.Text));var editor=new SyntheticEditor(Now);
        using var lifetime=new CancellationTokenSource();
        using(var review=selection.Reviews.Create(frozen,projection,target,field.Text,payload)){
            await ProductionSeams.ApplySelected(selection,review,target,editor,draft,"synthetic UI value changed after review",lifetime);
            Check(field.Text==payload&&field.Writes==1&&editor.ApplyCalls==1,"Production Apply seam writes the review-bound exact payload through the existing guarded writer");
            await RejectAsync(()=>ProductionSeams.ApplySelected(selection,review,target,editor,draft,payload,lifetime),"A consumed review cannot apply twice");
            draft.Edit.Undo(Now.AddSeconds(1),default);
            Check(field.Text=="original synthetic draft"&&field.Writes==2,"The existing guarded Undo restores exactly the original draft");
            Reject(()=>selection.Reviews.Consume(review,target,field.Text),"Undo does not restore spent context authority");
        }
        foreach(var changed in new[]{target with{TargetIdentity="other-field"},target with{ChatIdentity="other-chat"},target with{RecipientLabel="other-app"},target with{CapabilityDigest=new string('d',64)}}){
            field=new("original synthetic draft");draft=new(new GuardedEdit(field,field.Text));editor=new(Now);
            using var review=selection.Reviews.Create(frozen,projection,target,field.Text,payload);
            await RejectAsync(()=>ProductionSeams.ApplySelected(selection,review,changed,editor,draft,payload,lifetime),"Changed destination binding is rejected: "+(changed.TargetIdentity!=target.TargetIdentity?"field":changed.ChatIdentity!=target.ChatIdentity?"chat":changed.RecipientLabel!=target.RecipientLabel?"recipient":"capability"));
            Check(field.Writes==0&&editor.ApplyCalls==0,"Destination mismatch reaches no field write");
            Reject(()=>selection.Reviews.Consume(review,target,field.Text),"A failed binding attempt spends its review");
        }
        field=new("original synthetic draft");draft=new(new GuardedEdit(field,field.Text));editor=new(Now);
        using(var review=selection.Reviews.Create(frozen,projection,target,field.Text,payload)){
            field.Text="user edited the draft";
            await RejectAsync(()=>ProductionSeams.ApplySelected(selection,review,target,editor,draft,payload,lifetime),"Changed live draft is refused before context Apply");
            Check(field.Writes==0&&field.Text=="user edited the draft","A user edit remains untouched");
        }
        field=new("original synthetic draft");draft=new(new GuardedEdit(field,field.Text));editor=new(Now);
        using(var review=selection.Reviews.Create(frozen,projection,target,field.Text,payload)){
            field.BeforeWrite=()=>workspace.Clear(scope);
            await RejectAsync(()=>ProductionSeams.ApplySelected(selection,review,target,editor,draft,payload,lifetime),"Clear between review consumption and field write cancels the linked writer");
            Check(field.Writes==0&&field.Text=="original synthetic draft","Late invalidation preserves the original field");
        }
        Add(workspace,scope);frozen=Freeze(workspace,scope);projection=ContextDeliveryPlan.Project(frozen);selection=new(workspace,frozen,projection);payload=Payload(projection);
        field=new("original synthetic draft");draft=new(new GuardedEdit(field,field.Text));editor=new(Now);
        using(var review=selection.Reviews.Create(frozen,projection,target,field.Text,payload)){
            await ProductionSeams.ApplySelected(selection,review,target,editor,draft,payload,lifetime);field.Text="typed after Apply";
            Reject(()=>draft.Edit.Undo(Now.AddSeconds(1),default),"Undo does not overwrite text edited after Apply");
            Check(field.Text=="typed after Apply"&&field.Writes==1,"Refused Undo preserves the latest user edit");
        }
        Reject(()=>selection.Reviews.Create(frozen,projection,target,"original","context omitted"),"A candidate omitting required selected context cannot become a review");
        Reject(()=>selection.Reviews.Create(frozen,projection,target,"original",payload+"\n"+payload),"A candidate duplicating selected context cannot become a review");
    }
    private static void Budgets()
    {
        using var workspace=new RefinementWorkspace();var scope=new RefinementChatScope("manual-external","budget-fixture");workspace.Open(scope);
        for(int i=1;i<=6;i++)workspace.AppendCompletedTurn(scope,"t"+i,"request "+i,"complete response "+i,RefinementTurnOrigin.UserImported,workspace.Snapshot(scope).Revision);
        var frozen=Freeze(workspace,scope,"t1");var projection=ContextDeliveryPlan.Project(frozen,8000,maximumHistoryPairs:2);
        Check(projection.Ready&&projection.IncludedIds.Contains("turn:t1")&&projection.IncludedIds.Contains("turn:t6"),"A selected prior response survives reduction with the latest whole exchange");
        Check(projection.OmittedIds.Count==4&&projection.OmittedIds.Contains("turn:t2"),"History budget reports every omitted complete pair by stable ID");
        using(var data=JsonDocument.Parse(projection.Sources.Single().Text)){
            var turns=data.RootElement.GetProperty("turns");
            Check(turns.GetArrayLength()==2&&turns[0].GetProperty("messages").GetArrayLength()==2,"Reduced history retains both roles without partial-turn clipping");
            Check(data.RootElement.GetProperty("selectedPriorResponse").GetString()=="t1"&&turns[0].GetProperty("origin").GetString()=="UserImported","Selected referent and imported-answer provenance remain data");
        }
        Check(!ContextDeliveryPlan.Project(frozen,80).Ready,"An insufficient budget cannot silently omit the selected prior response");
        var request=Options(projection,true,"100").ToRequest("original draft","quick");var prepared=RefinementPreparation.Prepare(request);
        Check(!prepared.Ready&&!prepared.Budget.Fits&&prepared.Budget.Removed.Count==0,"A too-small complete-destination budget blocks readiness rather than accepting clipped required context");
        Check(RefinementContext.Build(projection.Sources).Blocks.All(b=>prepared.AssembledText.Contains(b.Text,StringComparison.Ordinal)),"Every required projected block remains exact in a refused destination preview");
        Check(prepared.AssembledText.Contains("BEGIN_UNTRUSTED_CONTEXT_JSON"),"Budget refusal retains the exact required block for diagnostic review");
        workspace.Clear(scope);Add(workspace,scope,new string('x',2500),required:false);frozen=Freeze(workspace,scope);projection=ContextDeliveryPlan.Project(frozen,4000,excerptScalars:128);
        Check(projection.Ready&&projection.ExcerptedIds.Count==1&&projection.Sources.Single().Text.Contains("excerpt omitted"),"Optional source reduction marks the excerpt before a review can be created");
        var omitted=ContextDeliveryPlan.Project(frozen,80);
        Check(omitted.Ready&&omitted.Sources.Count==0&&omitted.OmittedIds.Count==1,"Optional source overflow is explicitly reported as omitted");
        workspace.Clear(scope);Add(workspace,scope,new string('x',2500));
        Check(!ContextDeliveryPlan.Project(Freeze(workspace,scope),80).Ready,"Required source overflow refuses preparation");
    }
    private static async IAsyncEnumerable<RefinementEvent> Events([EnumeratorCancellation]CancellationToken ct=default)
    {ct.ThrowIfCancellationRequested();await Task.CompletedTask;yield return new("done",Result:Accepted);}
    private sealed class HeldStream : IAsyncEnumerable<RefinementEvent>,IAsyncEnumerator<RefinementEvent>
    {
        internal readonly TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Finished=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationToken Token;
        internal bool Disposed;
        public RefinementEvent Current=>new("done",Result:Accepted);
        public IAsyncEnumerator<RefinementEvent> GetAsyncEnumerator(CancellationToken cancellationToken=default)=>this;
        public async ValueTask<bool> MoveNextAsync(){Entered.TrySetResult();await Release.Task;return true;}
        public ValueTask DisposeAsync(){Disposed=true;Finished.TrySetResult();return ValueTask.CompletedTask;}
    }
}
