using Buddy.Server;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Buddy.Windows;

internal sealed class BrowserContextReviewWindow : Window
{
    private readonly RefinementWorkspace workspace;
    private readonly RefinementChatScope importedScope=new("manual-external",Guid.NewGuid().ToString("N"));
    private readonly BrowserContextBroker broker;
    private readonly BrowserPipeListener listener;
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(500)};
    private readonly TextBlock connectionStatus=Note(""),notice=Note(""),metadata=Note("");
    private readonly ComboBox connections=new(){Margin=new(0,6,0,6)},pairs=new(){Margin=new(0,6,0,6)};
    private readonly TextBox history=Editor(true),original=Editor(true),draft=Editor(false);
    private readonly CheckBox compared=new(){Content="I compared this code with the browser extension",Foreground=BuddyTheme.Ink},destination=new(){Content="I reviewed this exact destination and complete draft",Foreground=BuddyTheme.Ink},upload=new(){Content="I allow the listed originals to reach this page, which may upload them before Send",Foreground=BuddyTheme.Ink};
    private BrowserSelectedContext? selected;
    private BrowserContextReviewSnapshot? review;
    private ContextWorkspaceWindow? sourceWindow;
    private string? connectionId,historyDigest;
    private BrowserContextBinding? importedBinding;
    private bool updating,closed;
    private CancellationTokenRegistration selectionInvalidation;
    private long selectionGeneration;
    private readonly object selectionGate=new();
    private sealed record Choice(string Id,string Label){public override string ToString()=>Label;}
    private sealed record PairChoice(int Index,string Label){public override string ToString()=>Label;}
    internal BrowserContextReviewWindow(RefinementWorkspace workspace)
    {
        this.workspace=workspace;workspace.Open(importedScope);
        // There is intentionally no runtime/profile-file opt-in for unverified
        // identity contracts. A reviewed provider implementation is required.
        broker=new BrowserContextBroker();listener=new BrowserPipeListener(broker);
        BuddyTheme.Ensure();Title="Buddy - Browser context preview";Width=720;Height=800;MinWidth=450;MinHeight=420;MaxHeight=SystemParameters.WorkArea.Height;
        Background=BuddyTheme.Surface;Foreground=BuddyTheme.Ink;FontFamily=BuddyTheme.Font;
        var panel=new StackPanel{Margin=new(18)};
        panel.Children.Add(Note("ChatGPT browser integration preview. Current production mode checks readiness only: verified account/workspace identity and real attachment acceptance still need a narrowly scoped live characterization. An attach button does not establish support for another chat site."));
        panel.Children.Add(Note("This window opens a temporary local connection. It does not install an extension or register a native host. Close it or press Stop to disconnect. No automatic Send exists."));
        panel.Children.Add(new Expander{Header="Reviewed setup details",Content=Note("Native host: com.buddy.browser_context\nPipe: "+listener.PipeName+"\nPrepare-BrowserSetup.ps1 produces a review only. Extension loading, policy copy and registry registration need separate approval. No browser access is granted by this window.")});
        panel.Children.Add(connections);panel.Children.Add(connectionStatus);panel.Children.Add(compared);
        panel.Children.Add(BuddyTheme.Button("Approve matching pairing code",()=>Try(()=>{var s=Current();if(compared.IsChecked!=true)throw new InvalidOperationException("Compare the code in both windows first.");broker.ConfirmPairing(s.ConnectionId,s.PairingChallenge,true);compared.IsChecked=false;Refresh();})));
        panel.Children.Add(BuddyTheme.Button("Reject / disconnect this browser",()=>Try(()=>{broker.Cancel(Current().ConnectionId);listener.DisconnectCurrent();DropReview();Refresh();})));
        panel.Children.Add(pairs);panel.Children.Add(history);
        panel.Children.Add(BuddyTheme.Button("Add this complete pair to local context",ImportPair));
        panel.Children.Add(BuddyTheme.Button("Choose and review sources / history",OpenSources));
        panel.Children.Add(Note("Captured original browser draft (read-only):"));panel.Children.Add(original);
        panel.Children.Add(Note("Complete proposed draft, including selected supporting context:"));panel.Children.Add(draft);panel.Children.Add(metadata);
        panel.Children.Add(BuddyTheme.Button("Prepare exact browser review",Prepare));
        panel.Children.Add(destination);panel.Children.Add(upload);
        panel.Children.Add(BuddyTheme.Button("Approve this draft and the listed originals",Approve));
        panel.Children.Add(BuddyTheme.Button("Request exact text Undo",()=>Try(()=>{var s=Current();var r=s.Review;if(r?.ReceiptDigest is null)throw new InvalidOperationException("No confirmed draft receipt is available.");broker.RequestUndo(s.ConnectionId,r.ReviewId,r.ReceiptDigest);notice.Text="Text Undo requested. Existing attachments are retained; removal is a separate action on the site.";})));
        panel.Children.Add(BuddyTheme.Button("Stop browser session",Close));panel.Children.Add(notice);
        Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        connections.SelectionChanged+=(_,_)=>{if(updating)return;InvalidateReview();ClearImported();connectionId=(connections.SelectedItem as Choice)?.Id;Refresh();};
        pairs.SelectionChanged+=(_,_)=>ShowPair();
        draft.TextChanged+=(_,_)=>{if(!updating&&review is not null){InvalidateReview();notice.Text="The reviewed draft changed. Reconnect and capture a fresh browser binding.";}};
        timer.Tick+=(_,_)=>Refresh();timer.Start();
        SourceInitialized+=(_,_)=>CaptureProtection.Apply(new WindowInteropHelper(this).Handle);
        Closed+=(_,_)=>{lock(selectionGate){closed=true;selectionGeneration++;}timer.Stop();sourceWindow?.Close();selectionInvalidation.Dispose();selected?.Dispose();workspace.Close(importedScope);listener.Dispose();_=listener.Completion.ContinueWith(_=>broker.Dispose(),TaskScheduler.Default);};
        Refresh();
    }
    internal void SetSelection(FrozenRefinementContext selection)
    {
        var next=new BrowserSelectedContext(workspace,selection);
        InvalidateReview();
        long generation;BrowserSelectedContext? previous;CancellationTokenRegistration priorRegistration;
        lock(selectionGate){generation=++selectionGeneration;previous=selected;selected=next;priorRegistration=selectionInvalidation;selectionInvalidation=default;}
        // Dispose outside the ownership gate: an in-flight callback may be waiting
        // for that gate, and disposing a registration waits for its callback.
        priorRegistration.Dispose();previous?.Dispose();DropReview();
        selectionInvalidation=selection.Invalidated.Register(()=>{lock(selectionGate){if(closed||selectionGeneration!=generation||!ReferenceEquals(selected,next))return;listener.DisconnectCurrent();}Dispatcher.BeginInvoke(new Action(()=>{if(closed||Volatile.Read(ref selectionGeneration)!=generation||!ReferenceEquals(selected,next))return;if(connectionId is {} id)Try(()=>broker.Cancel(id));DropReview();notice.Text="Selected context changed or was cleared. Prepare a new review.";}));});
        updating=true;try{draft.Text=original.Text+(original.Text.Length==0?"":"\n\n")+next.Text;}finally{updating=false;}
        metadata.Text=string.Join("\n",next.Originals.Select(a=>$"Original requested: {a.Name}; {a.MimeType}; {a.ByteCount} bytes; SHA-256 {a.Sha256}"));
        if(next.Originals.Count==0)metadata.Text="Text context only. No original file or image is requested.";
        notice.Text="Selected context is staged locally. Review the complete draft and destination before approving.";
    }
    private BrowserContextSnapshot Current()=>broker.Snapshots.FirstOrDefault(s=>s.ConnectionId==connectionId)??throw new InvalidOperationException("Connect the reviewed extension and choose its session first.");
    private void Refresh()
    {
        if(closed)return;
        try
        {
            var snapshots=broker.Snapshots;
            updating=true;
            try{var choices=snapshots.Select(s=>new Choice(s.ConnectionId,s.ProviderId+" - "+s.Status)).ToArray();connections.ItemsSource=choices;connectionId??=choices.FirstOrDefault()?.Id;connections.SelectedItem=choices.FirstOrDefault(c=>c.Id==connectionId);}
            finally{updating=false;}
            var s=snapshots.FirstOrDefault(x=>x.ConnectionId==connectionId);
            if(s is null){if(importedBinding is not null)ClearImported();connectionStatus.Text=listener.Status;return;}
            if(importedBinding!=s.Binding){if(importedBinding is not null)ClearImported();importedBinding=s.Binding;}
            connectionStatus.Text=$"{s.Origin}\nPairing code: {s.PairingChallenge}\nState: {s.Status}; {s.ResultCode}\nVerified provider identity: {(s.ProfileAdmitted&&s.Binding is not null?"admitted":"unavailable - history, draft and originals blocked")}";
            if(s.Readiness is {} readiness)connectionStatus.Text+=$"\nObserved structure: {readiness.ComposerCount} composers, {readiness.RenderedUserCount} user nodes, {readiness.RenderedAssistantCount} assistant nodes. Counts are not transcript or account verification.";
            if(s.History is {} capture&&capture.Sha256!=historyDigest){historyDigest=capture.Sha256;updating=true;try{original.Text=capture.OriginalDraft;pairs.ItemsSource=capture.Pairs.Select((p,i)=>new PairChoice(i,$"Exchange {i+1}: "+(p.UserText.Length>65?p.UserText[..65]+"...":p.UserText))).ToArray();if(pairs.Items.Count>0)pairs.SelectedIndex=0;if(selected is null)draft.Text=original.Text;}finally{updating=false;}}
            if(s.Review is {} outcome&&outcome.State is not "prepared" and not "review")notice.Text="Browser receipt: "+outcome.State+" / "+s.ResultCode+". Attachment state is the page's observation, not proof of server byte retention.";
        }
        catch(ObjectDisposedException){ }
        catch(BrowserContextProtocolException){timer.Stop();listener.Dispose();broker.Dispose();DropReview();connectionStatus.Text="Browser session refused a stale clock or binding. Close this window and start a fresh session.";}
    }
    private void ShowPair()=>Try(()=>{if(pairs.SelectedItem is not PairChoice choice||Current().History is not {} capture)return;var pair=capture.Pairs[choice.Index];history.Text="User:\n"+pair.UserText+"\n\nAssistant:\n"+pair.AssistantText;});
    private void ImportPair()=>Try(()=>
    {
        var s=Current();if(s.Binding is null||!s.ProfileAdmitted||s.History is not {} capture||pairs.SelectedItem is not PairChoice choice)throw new InvalidOperationException("No admitted complete captured pair is selected.");
        var pair=capture.Pairs[choice.Index];string id=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s.ConnectionId+"|"+pair.UserId+"|"+pair.AssistantId))).ToLowerInvariant();
        workspace.AppendCompletedTurn(importedScope,id,pair.UserText,pair.AssistantText,RefinementTurnOrigin.AdapterCompleted,workspace.Snapshot(importedScope).Revision);
        notice.Text="This complete user/assistant pair was added to local review context. Nothing was sent.";
    });
    private void OpenSources()=>Try(()=>
    {
        InvalidateReview();
        sourceWindow?.Close();
        sourceWindow=new ContextWorkspaceWindow(workspace,importedScope,"Sources and selected complete pairs for this browser session. Original attachment delivery needs a supported, verified site adapter.",
            (f,_)=>SetSelection(f),browser:SetSelection);sourceWindow.Closed+=(_,_)=>sourceWindow=null;sourceWindow.Show();
    });
    private void Prepare()=>Try(()=>
    {
        var s=Current();if(!s.ProfileAdmitted||s.Binding is null||s.History is null)throw new InvalidOperationException("Verified account/workspace/chat and original composer capture are unavailable. Current ChatGPT production mode is readiness only.");
        if(string.IsNullOrWhiteSpace(draft.Text))throw new InvalidOperationException("Write the prompt to prepare.");
        selected?.ValidateFinalText(draft.Text);DropReview();
        review=broker.PreviewDraft(s.ConnectionId,s.Binding,s.History.OriginalDraft,draft.Text,selected?.Originals??Array.Empty<ContextOriginalAsset>(),selected?.Selection.Invalidated??default);
        notice.Text="Exact local review prepared. Confirm the destination and any original-file transfer separately. Nothing has been offered to the page yet.";
    });
    private void Approve()=>Try(()=>
    {
        var s=Current();if(review is null||destination.IsChecked!=true)throw new InvalidOperationException("Prepare and review the exact destination and draft first.");
        selected?.ValidateFinalText(draft.Text);bool hasOriginals=selected?.Originals.Count>0;
        if(hasOriginals&&upload.IsChecked!=true)throw new InvalidOperationException("Original attachments can upload before Send. Explicitly approve the listed originals first.");
        broker.ApproveDraft(s.ConnectionId,review.ReviewId,review.Digest,hasOriginals&&upload.IsChecked==true);
        destination.IsChecked=false;upload.IsChecked=false;notice.Text="One reviewed operation is available to the paired browser. Wait for its exact draft/attachment receipt. No Send will be pressed.";
    });
    private void DropReview(){review=null;destination.IsChecked=false;upload.IsChecked=false;}
    private void InvalidateReview(){if(review is not null&&connectionId is {} id){Try(()=>broker.Cancel(id));listener.DisconnectCurrent();}DropReview();}
    private void ClearImported()
    {
        BrowserSelectedContext? previous=null;CancellationTokenRegistration registration=default;
        lock(selectionGate){if(selected?.Selection.Scope==importedScope){selectionGeneration++;previous=selected;selected=null;registration=selectionInvalidation;selectionInvalidation=default;}}
        registration.Dispose();previous?.Dispose();if(previous is not null)metadata.Text="";
        workspace.Clear(importedScope);importedBinding=null;historyDigest=null;
        updating=true;try{original.Clear();history.Clear();pairs.ItemsSource=null;if(selected is null)draft.Clear();}finally{updating=false;}
        DropReview();
    }
    private void Try(Action action){try{action();}catch(Exception error)when(error is InvalidOperationException or ArgumentException or OperationCanceledException){notice.Text=error.Message;}}
    private static TextBlock Note(string text)=>new(){Text=text,TextWrapping=TextWrapping.Wrap,Foreground=BuddyTheme.Muted,Margin=new(0,6,0,6)};
    private static TextBox Editor(bool readOnly)=>new(){IsReadOnly=readOnly,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,MinHeight=70,MaxHeight=220,MaxLength=20_000,Foreground=BuddyTheme.Ink,Background=BuddyTheme.Surface,Margin=new(0,4,0,6)};
}
