using Buddy.Server;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;

namespace Buddy.Windows;

// Nonactivating, field-anchored review: no clipboard, keystroke dispatch or submit capability.
internal sealed class InlinePromptWindow:Window
{
    private readonly BuddyService service;private readonly FocusedFieldEditor editor;private readonly FocusedDraft draft;
    private readonly CancellationTokenSource lifetime=new();private readonly StackPanel body=new(){Margin=new(12)};
    private readonly TextBlock status=new(){Text="Want me to refine this prompt on your PC?",TextWrapping=TextWrapping.Wrap};
    private readonly Button yes,accept,undo;private readonly TextBlock diff=new(){TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock previewLabel=new(){Text="Original prompt",FontWeight=FontWeights.SemiBold,Margin=new(0,8,0,4)};
    private readonly TextBlock placementHint=new(){TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(0,5,0,0)};
    private readonly TextBlock footerHint;
    private readonly Func<OverlayNative.Point,PixelBounds> workArea;
    private readonly RefinementPreparationResult preparation;
    private readonly Func<bool> optionsCurrent;
    private readonly TextBlock resultDetails=new(){TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(0,4,0,4)};
    private bool started,applied,closed,mutating,escapeRegistered;private string? proposal;private Rect fieldBounds;
    private RefinementRequest? refinement;
    private readonly ScrollViewer preview;
    private readonly ScrollViewer footerScroll;
    internal bool AwaitingConsent=>!started&&!closed;
    internal bool IsMutating=>mutating;
    internal bool IsInFieldPreview {get;private set;}
    internal Task<bool> IsCurrent(CancellationToken ct)=>optionsCurrent()?editor.Matches(draft,applied?proposal!:draft.Edit.Original,ct):Task.FromResult(false);
    internal InlinePromptWindow(BuddyService service,FocusedFieldEditor editor,FocusedDraft draft,Action voice,string voiceShortcut="your voice shortcut",Func<OverlayNative.Point,PixelBounds>? workArea=null,RefineRequest? request=null,Func<bool>? optionsCurrent=null,Action? configureOptions=null)
    {
        this.service=service;this.editor=editor;this.draft=draft;fieldBounds=draft.Anchor.Bounds;this.workArea=workArea??OverlayNative.WorkArea;
        if(request is not null&&request.Prompt!=draft.Edit.Original)throw new InvalidOperationException("Prepared options do not match the captured field.");
        preparation=RefinementPreparation.Prepare(request??new(draft.Edit.Original,"quick"));
        this.optionsCurrent=optionsCurrent??(()=>true);
        Title="Buddy - inline prompt suggestion";Width=360;Height=400;SizeToContent=SizeToContent.Manual;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;Topmost=true;ShowInTaskbar=false;ShowActivated=false;Background=BuddyTheme.Surface;Foreground=BuddyTheme.Ink;
        var header=new DockPanel();var face=AppBranding.Image(28);face.Margin=new(0,0,8,0);DockPanel.SetDock(face,Dock.Left);header.Children.Add(face);header.Children.Add(status);body.Children.Add(header);
        body.Children.Add(new TextBlock{Text="Source: "+draft.App+" / "+draft.FieldName,FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new(0,3,0,5)});
        if(request is not null){
            var budget=preparation.Budget;
            var warnings=preparation.Warnings.ToList();
            if(budget.Conflict is {Length:>0} conflict)warnings.Add(conflict);
            if(budget.Removed.Count>0)warnings.Add("Omitted optional references: "+string.Join(", ",budget.Removed.Select(DescribeBlock)));
            var details=new TextBlock{Text=$"Captured inputs: {preparation.Mode} / {preparation.Choice.Technique}\n{preparation.Choice.Rationale}\n"+
                (budget.Limit is {} maximum?$"{budget.Count:N0} / {maximum:N0} {budget.Unit}; user-supplied destination limit.\n":"")+
                string.Join("\n",warnings.Distinct()),TextWrapping=TextWrapping.Wrap,FontSize=11};
            AutomationProperties.SetName(details,"Frozen source-field preparation");body.Children.Add(details);
            var preparedText=new TextBlock{Text=preparation.AssembledText,TextWrapping=TextWrapping.Wrap};
            AutomationProperties.SetName(preparedText,"Exact prepared source-field prompt");
            body.Children.Add(new Expander{Header="Exact prepared inputs",Content=preparedText,Focusable=false,Margin=new(0,4,0,6)});
        }
        AutomationProperties.SetName(resultDetails,"Source-field result details");body.Children.Add(resultDetails);
        body.Children.Add(previewLabel);diff.Text=draft.Edit.Original;
        preview=new ScrollViewer{Content=diff,MaxHeight=230,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};body.Children.Add(preview);body.Children.Add(placementHint);var buttons=new WrapPanel();
        Button Add(string label,Action action){var button=new Button{Content=label,Focusable=false,Padding=new(7,4,7,4),Margin=new(0,6,5,0)};button.Click+=(_,_)=>action();buttons.Children.Add(button);return button;}
        yes=Add("Yes, refine",()=>_ = Refine());Add("Voice reply",voice);Add("Stop",StopRefinement);Add("Dismiss / reject",Close);accept=Add("Accept",()=>_ = Apply());undo=Add("Undo",()=>_ = Undo());accept.Visibility=undo.Visibility=Visibility.Collapsed;accept.IsEnabled=false;
        if(configureOptions is not null)Add("Prepare new options",()=>{Close();configureOptions();});
        footerHint=new TextBlock{Text="Esc dismisses | Voice reply: "+voiceShortcut+" | Never sends",TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(0,6,0,0)};
        var footer=new StackPanel{Margin=new(12,0,12,12)};footer.Children.Add(buttons);footer.Children.Add(footerHint);
        AutomationProperties.SetName(footer,"Refinement actions");AutomationProperties.SetName(status,"Refinement status");AutomationProperties.SetName(diff,"Prompt review");
        footerScroll=new ScrollViewer{Content=footer,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Focusable=false};
        var layout=new DockPanel{LastChildFill=true};DockPanel.SetDock(footerScroll,Dock.Bottom);layout.Children.Add(footerScroll);layout.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        Content=new Border{BorderBrush=BuddyTheme.Deep,BorderThickness=new(1),CornerRadius=new(12),Child=layout};
        SourceInitialized+=(_,_)=>{var handle=new WindowInteropHelper(this).Handle;OverlayNative.Configure(handle,false,noActivate:true);escapeRegistered=Native.RegisterHotKey(handle,0x4250,0x4000,27);if(!escapeRegistered)footerHint.Text=footerHint.Text.Replace("Esc dismisses","Dismiss / reject closes");HwndSource.FromHwnd(handle).AddHook((IntPtr h,int m,IntPtr w,IntPtr l,ref bool handled)=>{if(m==0x312&&w.ToInt32()==0x4250){handled=true;Close();return IntPtr.Zero;}if(m==0x21){handled=true;return new IntPtr(3);}return IntPtr.Zero;});Reanchor(fieldBounds);};
        Loaded+=(_,_)=>Reanchor(fieldBounds);
        Closed+=(_,_)=>{if(escapeRegistered)Native.UnregisterHotKey(new WindowInteropHelper(this).Handle,0x4250);closed=true;refinement?.Cancel();refinement=null;lifetime.Cancel();proposal=null;accept.IsEnabled=false;diff.Inlines.Clear();};
    }
    internal bool Reply(string text){if(!AwaitingConsent||PromptSuggestionPolicy.Reply(text) is not {} consent)return false;if(consent)_=Refine();else Close();return true;}
    internal async Task Refine(){
        if(started||closed||applied||mutating)return;
        if(!optionsCurrent()){Close();return;}
        if(!preparation.Ready){Retry("Prepared inputs need attention. Prepare new options and capture the original field again; no refinement ran.");return;}
        started=true;yes.IsEnabled=false;yes.Visibility=Visibility.Collapsed;accept.IsEnabled=false;proposal=null;status.Text="Preparing a refinement locally. The original below is unchanged.";
        var request=new RefinementRequest(token:lifetime.Token);refinement=request;
        try {
            var outcome=await request.Run(token=>service.RefineStream(preparation.Request,token),
                onProgress:text=>{if(!closed&&ReferenceEquals(refinement,request))status.Text=text;});
            if(closed||!ReferenceEquals(refinement,request))return;
            if(!optionsCurrent()){Close();return;}
            if(outcome.Result is {Accepted:true} result){
                ShowProposal(result.RefinedPrompt);
                if(proposal is not null)ShowResultDetails(result);
            }
            else Retry(outcome.Message,outcome.Result);
        } catch(Exception ex){if(!closed&&ReferenceEquals(refinement,request))Retry(ex is OperationCanceledException?"Stopped; original unchanged.":ex.Message);}
        finally{if(ReferenceEquals(refinement,request))refinement=null;request.Dispose();}
    }
    private void Retry(string message,RefinementResult? result=null){
        proposal=null;started=false;accept.IsEnabled=false;accept.Visibility=Visibility.Collapsed;resultDetails.Text="";
        if(result is not null)ShowResultDetails(result);
        diff.Text=draft.Edit.Original;previewLabel.Text="Original prompt";status.Text=message;
        yes.Content="Try again";yes.IsEnabled=true;yes.Visibility=Visibility.Visible;Reanchor(fieldBounds);
    }
    private void ShowResultDetails(RefinementResult result){
        var messages=result.Warnings.ToList();
        if(result.DestinationBudget is {} budget){
            if(budget.Limit is {} maximum)messages.Insert(0,$"{(result.Accepted?"Proposal":"Attempt")}: {budget.Count:N0} / {maximum:N0} {budget.Unit}; user-supplied limit.");
            if(budget.Conflict is {} conflict)messages.Add(conflict);
            if(budget.Removed.Count>0)messages.Add("Omitted optional references: "+string.Join(", ",budget.Removed.Select(DescribeBlock)));
        }
        resultDetails.Text=string.Join("\n",messages.Distinct());
    }
    private string DescribeBlock(string id)=>id.StartsWith("source-",StringComparison.Ordinal)
        ?preparation.Request.Inputs?.Context?.FirstOrDefault(x=>"source-"+x.Id==id)?.Title??id:id;
    private void StopRefinement(){
        if(closed||applied||mutating)return;refinement?.Cancel();refinement=null;
        Retry("Stopped. Original unchanged. Try again when ready.");
    }
    private void ShowProposal(string text){
        if(closed||applied||mutating)return;
        if(!RefinementChange.HasMeaningfulChange(draft.Edit.Original,text)){
            Retry(RefinementChange.NoChangeMessage);return;
        }
        proposal=text;var delta=PromptSuggestionPolicy.Difference(draft.Edit.Original,text);diff.Inlines.Clear();
        diff.Inlines.Add(new Run(delta.Prefix));diff.Inlines.Add(BuddyTheme.DiffRun(delta.Removed,false));diff.Inlines.Add(BuddyTheme.DiffRun(delta.Added,true));diff.Inlines.Add(new Run(delta.Suffix));
        previewLabel.Text="Proposed changes";status.Text="Refinement ready. Review the changes; the original field is unchanged.";accept.IsEnabled=true;accept.Visibility=Visibility.Visible;Reanchor(fieldBounds);
    }
    internal void Reanchor(Rect bounds){
        fieldBounds=bounds;var handle=new WindowInteropHelper(this).Handle;if(handle==IntPtr.Zero)return;
        double scale=OverlayNative.Scale(handle);var point=new OverlayNative.Point{X=(int)bounds.Right,Y=(int)bounds.Top};var work=workArea(point);
        MaxWidth=Math.Max(1,work.Width/scale-16);MaxHeight=Math.Max(1,work.Height/scale-16);
        IsInFieldPreview=proposal is not null&&bounds.Width/scale>=280&&bounds.Height/scale>=180&&bounds.X>=work.Left&&bounds.Y>=work.Top&&bounds.Right<=work.Left+work.Width&&bounds.Bottom<=work.Top+work.Height;
        Width=Math.Min(IsInFieldPreview?bounds.Width/scale:360,MaxWidth);Height=Math.Min(IsInFieldPreview?Math.Min(360,bounds.Height/scale):400,MaxHeight);
        footerScroll.MaxHeight=Math.Max(1,Height*.55);
        preview.MaxHeight=Math.Max(30,Height-155);
        placementHint.Text=proposal is not null&&!applied&&!IsInFieldPreview?"The field is small. Review beside it; scroll to see the complete proposal.":"";
        if(IsInFieldPreview)OverlayNative.SetBounds(this,(int)bounds.X,(int)bounds.Y,(int)(Width*scale),(int)(Height*scale));
        else {var position=OverlayPlacement.NearPointer(point.X,point.Y,Width*scale,Height*scale,work,scale);OverlayNative.SetBounds(this,(int)position.X,(int)position.Y,(int)(Width*scale),(int)(Height*scale));}
    }
    private async Task Apply(){
        if(proposal is null||!RefinementChange.HasMeaningfulChange(draft.Edit.Original,proposal)||applied||closed||mutating||!accept.IsEnabled)return;
        if(!optionsCurrent()){Close();return;}
        accept.IsEnabled=false;mutating=true;
        try {await editor.Apply(draft,proposal,lifetime.Token);applied=true;status.Text="Applied without sending. Undo is available for 30 seconds.";undo.Visibility=Visibility.Visible;}
        catch(Exception ex){status.Text=ex.Message;}finally{mutating=false;}
    }
    private async Task Undo(){if(mutating||closed||!applied)return;mutating=true;undo.IsEnabled=false;try{await editor.Undo(draft,lifetime.Token);applied=false;proposal=null;accept.IsEnabled=false;diff.Text=draft.Edit.Original;previewLabel.Text="Original prompt";status.Text="Exact original restored without sending.";}catch(Exception ex){status.Text=ex.Message;}finally{mutating=false;}}
}
