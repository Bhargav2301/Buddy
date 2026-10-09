using Buddy.Server;
using System.Security.Cryptography;
using System.Text;

namespace Buddy.Windows;

public sealed partial class MainWindow
{
    private readonly RefinementWorkspace refinementContext = new();
    private RefinementChatScope? notchContextScope, manualContextScope;
    private readonly HashSet<string> contextTurnsSeen = [];
    private long contextHistoryRevision = -1;
    private ContextWorkspaceWindow? contextWindow;
    private (FrozenRefinementContext Selection,RefinementContextProjection Projection)? nextNotchContext;
    private void SyncNotchContext()
    {
        if(notchChat is null||shuttingDown)return;
        if(!Dispatcher.CheckAccess()){_=Dispatcher.BeginInvoke(new Action(SyncNotchContext));return;}
        var chat=notchChat.Snapshot;
        if(notchContextScope?.Id!=chat.SessionId){
            contextWindow?.Close();contextWindow=null;
            if(notchContextScope is not null)refinementContext.Close(notchContextScope);
            notchContextScope=new("buddy-local",chat.SessionId);refinementContext.Open(notchContextScope);contextTurnsSeen.Clear();contextHistoryRevision=-1;nextNotchContext=null;
        }
        // Presentation notices and draft edits must never import an unseen pair
        // into a selection that the user has just reviewed and armed.
        if(contextHistoryRevision==chat.HistoryRevision)return;
        contextHistoryRevision=chat.HistoryRevision;
        for(int i=0;i+1<chat.Messages.Count;i+=2){var user=chat.Messages[i];var answer=chat.Messages[i+1];
            if(user.Role!="You"||answer.Role!="Buddy")continue;
            string id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user.At.ToString("O")+"\n"+user.Text+"\n"+answer.Text))).ToLowerInvariant();
            if(!contextTurnsSeen.Add(id))continue;
            try{var s=refinementContext.Snapshot(notchContextScope);refinementContext.AppendCompletedTurn(notchContextScope,id,user.Text,answer.Text,RefinementTurnOrigin.BuddyCompleted,s.Revision);}
            catch(InvalidOperationException){notchChat.SetContextNotice("Context history is full. This exchange was not added. Remove older pairs, then explicitly import the exchange if needed.");}
        }
    }
    private ContextWorkspaceWindow OpenNotchContext()
    {
        SyncNotchContext();if(notchContextScope is null)throw new InvalidOperationException("Open the local chat first.");
        RevokePreparedContext();
        contextWindow?.Close();var scope=notchContextScope;
        var window=new ContextWorkspaceWindow(refinementContext,scope,"Context for this Buddy local chat",PrepareContextForExternal,RefineNotchContext,(f,p)=>{
            if(notchChat?.Snapshot.SessionId!=scope.Id||!refinementContext.IsCurrent(f))throw new InvalidOperationException("The local chat changed. Review context again.");
            nextNotchContext=(f,p);notchChat.SetContextNotice($"Reviewed context ready for the next local message: {p.IncludedIds.Count} selected items. Reopen Context to change or clear it.");status.Text="Reviewed context is ready for one message in this local chat.";
        },browser:OpenBrowserContext);contextWindow=window;window.Closed+=(_,_)=>{if(contextWindow==window)contextWindow=null;};window.Show();return window;
    }
    private void OpenManualContext()
    {
        RevokePreparedContext();
        if(manualContextScope is null){manualContextScope=new("manual-external",Guid.NewGuid().ToString("N"));refinementContext.Open(manualContextScope);}
        contextWindow?.Close();var window=new ContextWorkspaceWindow(refinementContext,manualContextScope,
            "Manually selected external chat context - confirm the destination chat again for every Apply. Start a new context when changing chats.",PrepareContextForExternal,newExternalSession:NewManualContext,browser:OpenBrowserContext);
        contextWindow=window;window.Closed+=(_,_)=>{if(contextWindow==window)contextWindow=null;};window.Show();
    }
    private void NewManualContext()
    {
        RevokePreparedContext();contextWindow?.Close();contextWindow=null;
        if(manualContextScope is not null)refinementContext.Close(manualContextScope);
        manualContextScope=null;OpenManualContext();
    }
    private static RefinementOptionsPanel ContextOptions(RefinementContextProjection projection)
    {
        var panel=new RefinementOptionsPanel();foreach(var source in projection.Sources)panel.AddReviewedResource(source,locked:true);panel.SetDestinationBudget("Buddy local chat draft",4000);return panel;
    }
    private void PrepareContextForExternal(FrozenRefinementContext selection,RefinementContextProjection projection)
    {
        if(!refinementContext.IsCurrent(selection)||!projection.Ready)throw new InvalidOperationException("Context changed. Review it again.");
        PrepareExternalRefinementOptions(projection.Sources,()=>refinementContext.IsCurrent(selection),new(refinementContext,selection,projection));
    }
    private void RefineNotchContext(FrozenRefinementContext selection,RefinementContextProjection projection)
    {
        if(host is null||notchChat is null||!refinementContext.IsCurrent(selection))throw new InvalidOperationException("The local chat is unavailable or changed.");
        var chat=notchChat.Snapshot;if(chat.SessionId!=selection.Scope.Id||chat.Busy||string.IsNullOrWhiteSpace(chat.Draft))throw new InvalidOperationException("Write a draft in this local chat before refining it.");
        var field=new NotchDraftField(notchChat,chat.SessionId);var edit=new GuardedEdit(field,chat.Draft);
        var reviews=new ContextDeliveryReviews(refinementContext);
        var target=new ContextDestinationBinding("buddy-notch-text-v1","This Buddy local chat",field.Identity,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("buddy-notch-exact-text-4000-v1"))).ToLowerInvariant(),chat.SessionId,true);
        refineWindow?.Close();var window=new RefineWindow(host.Service,chat.Draft,
            (text,ct)=>{using var review=reviews.Create(selection,projection,target,chat.Draft,text);using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,review.Invalidated);var payload=reviews.Consume(review,target,field.Read(),linked.Token);edit.Apply(payload,DateTimeOffset.UtcNow,linked.Token);return Task.CompletedTask;},
            ct=>{edit.Undo(DateTimeOffset.UtcNow,ct);return Task.CompletedTask;},"This Buddy local chat; reviewed context is included in the complete draft",optionsPanel:ContextOptions(projection),localTasks:localTasks,
            contextCurrent:()=>refinementContext.IsCurrent(selection));
        refineWindow=window;var invalidated=selection.Invalidated.Register(()=>Dispatcher.BeginInvoke(new Action(window.Cancel)));
        window.Closed+=(_,_)=>{invalidated.Dispose();if(refineWindow==window)refineWindow=null;};window.Show();_=window.Refine("quick");
    }
    private (string Text,CancellationToken Invalidated)? ConsumeNotchContext(string session)
    {
        var prepared=nextNotchContext;nextNotchContext=null;notchChat?.SetContextNotice("");if(prepared is null)return null;
        var(f,p)=prepared.Value;if(f.Scope.Id!=session||!refinementContext.IsCurrent(f)||!p.Ready)throw new InvalidOperationException("Prepared context changed or belongs to another chat. Review it again.");
        return (string.Join("\n\n",RefinementContext.Build(p.Sources).Blocks.Select(b=>b.Text)),f.Invalidated);
    }
    private void RevokePreparedContext(){nextNotchContext=null;notchChat?.SetContextNotice("");promptWatcher?.Suspend();CancelExternalRefinementOptions();}
    private sealed class NotchDraftField(NotchChatSession chat,string session):IVerifiedTextField
    {
        public string Identity=>session;
        public string Read(){var s=chat.Snapshot;if(s.SessionId!=session||s.Busy)throw new InvalidOperationException("The local chat changed or is busy.");return s.Draft;}
        public void Write(string expected,string value,CancellationToken ct){ct.ThrowIfCancellationRequested();if(Read()!=expected||!chat.ReplaceDraft(session,expected,value))throw new InvalidOperationException("The local draft changed or exceeds its limit.");}
    }
}
