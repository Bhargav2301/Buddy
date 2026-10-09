using Buddy.Server;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Buddy.Windows;

// A local review surface. Drop only stages data; these callbacks prepare drafts,
// never submit messages or authorize an external recipient implicitly.
internal sealed class ContextWorkspaceWindow : Window
{
    private readonly RefinementWorkspace workspace;
    private readonly RefinementChatScope scope;
    private readonly Action<FrozenRefinementContext,RefinementContextProjection>? refine, local;
    private readonly Action<FrozenRefinementContext,RefinementContextProjection> external;
    private readonly Func<string,CancellationToken,Task<ContextSourceReadResult>> readSelected;
    private readonly Func<ContextSourceReadResult,CancellationToken,Task<ContextSourceReadResult>> extractSelected;
    private readonly StackPanel sources = new(), turns = new();
    private readonly TextBlock status = Note("");
    private readonly TextBox pasted = Editor("", 20000, 90), userTurn = Editor("",20000,70), assistantTurn = Editor("",20000,100);
    private readonly TextBox exact = Editor("",20000,160);
    private sealed record PriorChoice(string? Id,string Label){public override string ToString()=>Label;}
    private readonly ComboBox referent = new(){Margin=new(0,5,0,8)};
    private readonly Dictionary<string,CheckBox> selectedSources = [], selectedTurns = [];
    private readonly HashSet<string> deselected = [];
    private CancellationTokenSource? reading;
    private ContextSourceReadResult? pendingImage;
    private string? pendingOcrSourceId;
    private readonly StackPanel imageReview = new();
    private bool closed;
    internal ContextWorkspaceWindow(RefinementWorkspace workspace,RefinementChatScope scope,string label,
        Action<FrozenRefinementContext,RefinementContextProjection> external,
        Action<FrozenRefinementContext,RefinementContextProjection>? refine=null,
        Action<FrozenRefinementContext,RefinementContextProjection>? local=null,
        Func<string,CancellationToken,Task<ContextSourceReadResult>>? readSelected=null,
        Func<ContextSourceReadResult,CancellationToken,Task<ContextSourceReadResult>>? extractSelected=null,
        Action? newExternalSession=null,
        Action<FrozenRefinementContext>? browser=null)
    {
        this.workspace=workspace;this.scope=scope;this.external=external;this.refine=refine;this.local=local;
        this.readSelected=readSelected??ContextSourceReader.ReadSelectedAsync;
        this.extractSelected=extractSelected??((source,ct)=>new SelectedImageOcr().ExtractAsync(source,ct));
        BuddyTheme.Ensure();Title="Buddy — Chat context";Width=660;Height=780;MinWidth=420;MinHeight=420;
        MaxHeight=SystemParameters.WorkArea.Height;Background=BuddyTheme.Surface;Foreground=BuddyTheme.Ink;FontFamily=BuddyTheme.Font;
        var panel=new StackPanel{Margin=new(18)};
        panel.Children.Add(Note(label));
        if(newExternalSession is not null)panel.Children.Add(BuddyTheme.Button("Start new external chat context",newExternalSession));
        panel.Children.Add(Note("Selected originals and context stay in memory until removed, cleared, or Buddy closes. Sending a draft that contains context saves that text with the local conversation; saved answers may also quote it. Clear here does not delete saved messages or external drafts. External history is imported explicitly. Apply creates a text draft and never attaches files or presses Send."));
        panel.Children.Add(BuddyTheme.Button("Choose local file or image",Choose));
        panel.Children.Add(Note("Drop a local TXT, Markdown, PNG or JPEG file, browser text or an HTTPS link here. Links are stored as references; pages are not fetched. Images stay local and require reviewed text extraction for a text destination."));
        panel.Children.Add(imageReview);panel.Children.Add(pasted);
        panel.Children.Add(BuddyTheme.Button("Stage pasted text or link",()=>{if(StageText(pasted.Text))pasted.Clear();}));
        panel.Children.Add(sources);
        panel.Children.Add(new Expander{Header="Import a completed prompt and AI response",Content=ImportPanel(),Foreground=BuddyTheme.Ink});
        panel.Children.Add(turns);
        panel.Children.Add(Note("Pin a prior response as context (optional). This does not rewrite an ambiguous reference automatically."));panel.Children.Add(referent);referent.SelectionChanged+=(_,_)=>Preview();
        var actions=new WrapPanel();
        if(refine is not null)actions.Children.Add(BuddyTheme.Button("Refine current local draft",()=>Use(refine)));
        if(local is not null)actions.Children.Add(BuddyTheme.Button("Use for next local message",()=>Use(local)));
        actions.Children.Add(BuddyTheme.Button("Prepare for an external chat",()=>Use(external)));
        if(browser is not null)actions.Children.Add(BuddyTheme.Button("Review with browser adapter",()=>Try(()=>{browser(FreezeSelection());Close();})));
        actions.Children.Add(BuddyTheme.Button("Clear this context",()=>Try(()=>{StopReading();workspace.Clear(scope);deselected.Clear();Refresh();})));
        panel.Children.Add(actions);exact.IsReadOnly=true;panel.Children.Add(Note("Exact selected context and named omissions (not sent):"));panel.Children.Add(exact);panel.Children.Add(status);
        Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
        AllowDrop=true;PreviewDragOver+=(_,e)=>{e.Effects=Supported(e.Data)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;};
        Drop+=(_,e)=>{e.Handled=true;Try(()=>{if(e.Data.GetDataPresent(DataFormats.FileDrop,false)&&e.Data.GetData(DataFormats.FileDrop,false) is string[]{Length:1} paths)_=StageFile(paths[0]);else if(e.Data.GetDataPresent(DataFormats.UnicodeText,false)&&e.Data.GetData(DataFormats.UnicodeText,false) is string text)StageText(text);else status.Text="Save virtual browser files locally, then choose one supported file.";});};
        Closed+=(_,_)=>{closed=true;StopReading();};
        SourceInitialized+=(_,_)=>CaptureProtection.Apply(new WindowInteropHelper(this).Handle);
        Refresh();
    }
    private static bool Supported(IDataObject data){try{return data.GetDataPresent(DataFormats.FileDrop,false)&&data.GetData(DataFormats.FileDrop,false) is string[]{Length:1}||data.GetDataPresent(DataFormats.UnicodeText,false);}catch{return false;}}
    private StackPanel ImportPanel()
    {
        var panel=new StackPanel();panel.Children.Add(Note("Import only the prompt and completed response you select for this chat. Imported answers are untrusted supporting data, not confirmed requirements."));
        panel.Children.Add(Note("Previous user prompt"));panel.Children.Add(userTurn);panel.Children.Add(Note("Completed AI response"));panel.Children.Add(assistantTurn);
        panel.Children.Add(BuddyTheme.Button("Add this reviewed pair",()=>Try(()=>{var s=workspace.Snapshot(scope);workspace.AppendCompletedTurn(scope,Guid.NewGuid().ToString("N"),userTurn.Text,assistantTurn.Text,RefinementTurnOrigin.UserImported,s.Revision);userTurn.Clear();assistantTurn.Clear();Refresh();})));return panel;
    }
    internal bool StageText(string text)
    {
        try{using var value=ContextSourceReader.ClassifyDropText(text);Stage(value);Refresh();return true;}catch(Exception e){status.Text=e.Message;return false;}
    }
    internal async Task StageFile(string path)
    {
        StopReading();var owner=new CancellationTokenSource();reading=owner;ContextSourceReadResult? loaded=null;bool completed=false;status.Text="Reading selected source locally. Nothing has been sent.";
        try {
            var value=loaded=await readSelected(path,owner.Token);owner.Token.ThrowIfCancellationRequested();
            if(closed||reading!=owner)return;
            if(value.Image is not null){var decoded=ContextSourceImagePreview.Decode(value.Image,owner.Token);pendingImage=value;imageReview.Children.Clear();var preview=new System.Windows.Controls.Image{Source=decoded,MaxHeight=240,Stretch=Stretch.Uniform};imageReview.Children.Add(preview);
                imageReview.Children.Add(Note(value.Name+" - OCR can misread letters and numbers. Compare the extracted text with this image before adding it; use Correct extracted text to fix mistakes. Only reviewed text is used. Closing discards unreviewed image extraction."));
                imageReview.Children.Add(BuddyTheme.Button("Extract text locally for review",()=>_=ExtractImage()));
                imageReview.Children.Add(BuddyTheme.Button("Keep original image for attachment review",()=>Try(()=>{if(pendingOcrSourceId is not null)throw new InvalidOperationException("Review or discard the current image source first.");pendingOcrSourceId=Stage(value,requireOriginal:true);Refresh();})));
                imageReview.Children.Add(BuddyTheme.Button("Discard image and unreviewed extraction",()=>Try(()=>{StopReading();Refresh();})));
            }else{Stage(value);Refresh();}
            completed=true;
        }catch(Exception e){if(!closed&&reading==owner)status.Text=e is OperationCanceledException?"Source reading stopped.":e.Message;}
        finally{if(loaded!=pendingImage)loaded?.Dispose();if(reading==owner){reading=null;if(!closed&&completed)Refresh();}owner.Dispose();}
    }
    private async Task ExtractImage()
    {
        if(pendingImage is not {} selected||reading is not null||pendingOcrSourceId is not null)return;var owner=new CancellationTokenSource();reading=owner;
        try{using var result=await extractSelected(selected,owner.Token);owner.Token.ThrowIfCancellationRequested();if(closed||pendingImage!=selected)return;pendingOcrSourceId=Stage(result);Refresh();}
        catch(Exception e){if(!closed&&reading==owner&&pendingImage==selected)status.Text=e is OperationCanceledException?"Image extraction stopped.":e.Message;}
        finally{if(reading==owner)reading=null;owner.Dispose();}
    }
    private string Stage(ContextSourceReadResult value,bool requireOriginal=false)
    {
        var kind=value.Kind switch{ContextSourceKind.LocalTextFile=>RefinementSourceKind.LocalTextFile,ContextSourceKind.LinkReference=>RefinementSourceKind.UserLink,ContextSourceKind.ImageText=>RefinementSourceKind.LocalOcr,ContextSourceKind.LocalImage=>RefinementSourceKind.LocalImage,_=>RefinementSourceKind.SelectedText};
        var s=workspace.Snapshot(scope);return workspace.StageSource(scope,new(value.Name,value.Text,kind,FullImageRequired:kind==RefinementSourceKind.LocalImage,OriginalSha256:value.OriginalSha256,OriginalBytes:value.ByteCount,ExtractionMethod:value.ExtractionMethod,SuppliedUrl:value.SuppliedLocator,OriginalDeliveryRequired:requireOriginal),s.Revision,value.OriginalAsset).Sources.Last().Id;
    }
    private void Refresh()
    {
        if(closed)return;var snapshot=workspace.Snapshot(scope);sources.Children.Clear();turns.Children.Clear();selectedSources.Clear();selectedTurns.Clear();
        foreach(var source in snapshot.Sources){var row=new StackPanel();row.Children.Add(Note(source.Title+" - "+source.Kind+" - "+(source.Reviewed?"reviewed":"staged; not reviewed")));
            row.Children.Add(Note(source.OriginalAsset is {} original?$"Original retained locally: {original.MimeType}, {original.ByteCount} bytes; SHA-256 {original.Sha256}. No file is attached or sent.":source.Kind==RefinementSourceKind.UserLink?"Link text only; page contents were not fetched. Reference-text SHA-256 "+source.TextSha256:"Selected-text SHA-256 "+source.TextSha256));
            if(source.Kind==RefinementSourceKind.LocalOcr){
                row.Children.Add(Note(source.Correction is null?"Extracted OCR text; this is not the original image. Text SHA-256 "+source.TextSha256:"User-corrected OCR text; original image retained. Review these changes before use. Text SHA-256 "+source.TextSha256));
                if(source.OriginalAsset is not null)row.Children.Add(BuddyTheme.Button("Correct extracted text",()=>Try(()=>{var correction=CreateOcrCorrection(source,snapshot.Revision);correction.Owner=this;correction.ShowDialog();})));
            }
            if(source.OriginalAsset is not null){var originalRequired=new CheckBox{Content="Require the original file/image attachment (current text destinations will refuse)",IsChecked=source.OriginalDeliveryRequired,Foreground=BuddyTheme.Ink};originalRequired.Click+=(_,_)=>Try(()=>{workspace.SetOriginalDeliveryRequired(scope,source.Id,originalRequired.IsChecked==true,workspace.Snapshot(scope).Revision);Refresh();});row.Children.Add(originalRequired);}
            var text=Editor(source.Text,20000,100);text.IsReadOnly=true;row.Children.Add(text);
            if(!source.Reviewed)row.Children.Add(BuddyTheme.Button("Add reviewed source",()=>Try(()=>{workspace.ReviewSource(scope,source.Id,source.ReviewDigest,workspace.Snapshot(scope).Revision);if(source.Id==pendingOcrSourceId){pendingOcrSourceId=null;ReleaseImage();}Refresh();})));
            else{var box=new CheckBox{Content="Include this source",IsChecked=!deselected.Contains(source.Id),Foreground=BuddyTheme.Ink};selectedSources.Add(source.Id,box);box.Checked+=(_,_)=>Select(source.Id,true);box.Unchecked+=(_,_)=>Select(source.Id,false);row.Children.Add(box);}
            row.Children.Add(BuddyTheme.Button("Remove source",()=>Try(()=>{workspace.RemoveSource(scope,source.Id,workspace.Snapshot(scope).Revision);if(source.Id==pendingOcrSourceId){pendingOcrSourceId=null;ReleaseImage();}Refresh();})));sources.Children.Add(BuddyTheme.Card(row,8));}
        foreach(var turn in snapshot.Turns){var row=new StackPanel();var box=new CheckBox{Content="Include completed exchange ("+turn.Origin+")",IsChecked=!deselected.Contains(turn.Id),Foreground=BuddyTheme.Ink};selectedTurns.Add(turn.Id,box);box.Checked+=(_,_)=>Select(turn.Id,true);box.Unchecked+=(_,_)=>Select(turn.Id,false);row.Children.Add(box);
            var text=Editor("User: "+turn.UserText+"\n\nAssistant: "+turn.AssistantText,40000,100);text.IsReadOnly=true;row.Children.Add(text);row.Children.Add(BuddyTheme.Button("Remove this pair from context",()=>Try(()=>{workspace.RemoveTurn(scope,turn.Id,workspace.Snapshot(scope).Revision);Refresh();})));turns.Children.Add(BuddyTheme.Card(row,8));}
        var prior=(referent.SelectedItem as PriorChoice)?.Id;
        var choices=new[]{new PriorChoice(null,"No specific prior response")}.Concat(snapshot.Turns.Select(t=>new PriorChoice(t.Id,t.UserText.Length>70?t.UserText[..70]+"…":t.UserText))).ToArray();
        referent.ItemsSource=choices;referent.SelectedItem=choices.FirstOrDefault(c=>c.Id==prior)??choices[0];Preview();
    }
    private void Select(string id,bool yes){if(yes)deselected.Remove(id);else deselected.Add(id);Preview();}
    internal OcrCorrectionWindow CreateOcrCorrection(RefinementSourceSnapshot source,long revision)=>new(workspace,scope,source,revision,Refresh);
    private FrozenRefinementContext FreezeSelection()
    {
        if(reading is not null||pendingImage is not null)throw new InvalidOperationException("Finish or discard the pending image/source review first.");
        var snapshot=workspace.Snapshot(scope);
        if(snapshot.Sources.Any(s=>!s.Reviewed))throw new InvalidOperationException("Review or remove each staged source first.");
        var frozen=workspace.Freeze(scope,snapshot.Revision,selectedSources.Where(x=>x.Value.IsChecked==true).Select(x=>x.Key).ToArray(),selectedTurns.Where(x=>x.Value.IsChecked==true).Select(x=>x.Key).ToArray(),(referent.SelectedItem as PriorChoice)?.Id);
        if(frozen.Sources.Count==0&&frozen.Turns.Count==0)throw new InvalidOperationException("Select a reviewed source or a completed exchange first.");
        return frozen;
    }
    private (FrozenRefinementContext,RefinementContextProjection) Selection(){var frozen=FreezeSelection();return(frozen,ContextDeliveryPlan.Project(frozen,2000));}
    private void Preview()
    {
        try{var (_,p)=Selection();var snapshot=workspace.Snapshot(scope);string Label(string id)=>snapshot.Sources.FirstOrDefault(s=>s.Id==id)?.Title??snapshot.Turns.Where(t=>"turn:"+t.Id==id).Select(t=>"Exchange: "+(t.UserText.Length>65?t.UserText[..65]+"…":t.UserText)).FirstOrDefault()??id;exact.Text=string.Join("\n\n",RefinementContext.Build(p.Sources).Blocks.Select(b=>b.Text));status.Text=(p.Ready?"Prepared for review. ":"Needs attention. ")+string.Join(" ",p.Reasons.Concat(p.Warnings))+"\nExcerpted: "+string.Join(", ",p.ExcerptedIds.Select(Label))+"\nOmitted: "+string.Join(", ",p.OmittedIds.Select(Label));}
        catch(Exception e){exact.Clear();status.Text=e.Message;}
    }
    private void Use(Action<FrozenRefinementContext,RefinementContextProjection> action)=>Try(()=>{var(f,p)=Selection();if(!p.Ready)throw new InvalidOperationException(string.Join(" ",p.Reasons));if(p.Sources.Count==0)throw new InvalidOperationException("None of the selected context fits. Choose fewer or shorter sources; no empty context was prepared.");action(f,p);Close();});
    private void Choose(){var d=new Microsoft.Win32.OpenFileDialog{Filter="Local text or image|*.txt;*.md;*.png;*.jpg;*.jpeg",Multiselect=false,CheckFileExists=true};if(d.ShowDialog(this)==true)_=StageFile(d.FileName);}
    private void ReleaseImage(){pendingImage?.Dispose();pendingImage=null;imageReview.Children.Clear();}
    private void StopReading(){reading?.Cancel();reading=null;if(pendingOcrSourceId is {} id){pendingOcrSourceId=null;var s=workspace.Snapshot(scope);if(s.Sources.Any(x=>x.Id==id&&!x.Reviewed))workspace.RemoveSource(scope,id,s.Revision);}ReleaseImage();}
    internal void CancelReading(){StopReading();if(!closed)status.Text="Stopped. Reviewed context remains local; no new source was attached or sent.";}
    private void Try(Action work){try{work();}catch(Exception e){status.Text=e.Message;}}
    private static TextBlock Note(string s)=>new(){Text=s,TextWrapping=TextWrapping.Wrap,Foreground=BuddyTheme.Muted,Margin=new(0,5,0,5)};
    private static TextBox Editor(string value,int limit,double height)=>new(){Text=value,MaxLength=limit,MinHeight=height,MaxHeight=260,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new(0,4,0,8),Foreground=BuddyTheme.Ink,Background=BuddyTheme.Surface};
}
