using Buddy.Server;

namespace Buddy.Windows;

// Only presentation/field adapters are synthetic. The generated host compiles the
// exact production sync, arm, consume, revoke, cancellation and Apply fragments.
internal sealed class ContextWorkspaceWindow { internal bool Closed; internal void Close()=>Closed=true; }
internal sealed class SyntheticDispatcher
{
    internal bool OnDispatcher=true;
    private readonly Queue<Action> pending=[];
    internal bool CheckAccess()=>OnDispatcher;
    internal object BeginInvoke(Action action){pending.Enqueue(action);return action;}
    internal int Pending=>pending.Count;
    internal void Drain(){OnDispatcher=true;while(pending.TryDequeue(out var action))action();}
}
internal sealed class SyntheticText { internal string Text=""; }
internal sealed class SyntheticWatcher { internal int Suspends; internal void Suspend()=>Suspends++; }

internal sealed partial class MainWindow : IDisposable
{
    private readonly NotchChatSession? notchChat;
    private bool shuttingDown;
    internal SyntheticDispatcher Dispatcher { get; }=new();
    private readonly SyntheticText status=new();
    private readonly SyntheticWatcher promptWatcher=new();
    internal int CancelledOptions {get;private set;}
    internal MainWindow(NotchChatSession chat){notchChat=chat;manualContextScope=null;chat.Changed+=SyncNotchContext;SyncNotchContext();}
    internal RefinementWorkspace Workspace=>refinementContext;
    internal RefinementChatScope Scope=>notchContextScope!;
    internal bool Armed=>nextNotchContext is not null;
    internal int Suspends=>promptWatcher.Suspends;
    internal string Status=>status.Text;
    internal void Sync()=>SyncNotchContext();
    internal void Revoke()=>RevokePreparedContext();
    internal (string Text,CancellationToken Invalidated)? Consume()=>ConsumeNotchContext(Scope.Id);
    internal ContextWorkspaceWindow OpenSyntheticWindow()=>contextWindow=new();
    private void CancelExternalRefinementOptions()=>CancelledOptions++;
    // Reference the unused manual scope from the generated production declarations.
    internal bool HasManualScope=>manualContextScope is not null;
    public void Dispose(){shuttingDown=true;notchChat!.Changed-=SyncNotchContext;refinementContext.Dispose();}
}

internal sealed class SyntheticField(string text,string identity="owned-field") : IVerifiedTextField
{
    public string Identity {get;set;}=identity;
    internal string Text {get;set;}=text;
    internal int Writes;
    internal Action? BeforeWrite;
    public string Read()=>Text;
    public void Write(string expected,string text,CancellationToken ct)
    {
        BeforeWrite?.Invoke();ct.ThrowIfCancellationRequested();
        if(Text!=expected)throw new InvalidOperationException("Synthetic field changed.");
        Writes++;Text=text;
    }
}
internal sealed record SyntheticDraft(GuardedEdit Edit);
internal sealed class SyntheticEditor(DateTimeOffset now)
{
    internal int ApplyCalls;
    internal Task<bool> Matches(SyntheticDraft draft,string original,CancellationToken ct)
    {ct.ThrowIfCancellationRequested();return Task.FromResult(draft.Edit.Matches(original));}
    internal Task Apply(SyntheticDraft draft,string payload,CancellationToken ct)
    {ApplyCalls++;draft.Edit.Apply(payload,now,ct);return Task.CompletedTask;}
}
