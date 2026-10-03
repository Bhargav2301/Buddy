using Buddy.Server;
using System.Windows;
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
    private readonly Button yes,accept,undo;private readonly TextBlock diff=new(){TextWrapping=TextWrapping.Wrap,MaxHeight=220};
    private bool started,applied,closed,mutating,escapeRegistered;private string? proposal;private Rect fieldBounds;
    private readonly ScrollViewer preview;
    internal bool AwaitingConsent=>!started&&!closed;
    internal bool IsMutating=>mutating;
    internal bool IsInFieldPreview {get;private set;}
    internal Task<bool> IsCurrent(CancellationToken ct)=>editor.Matches(draft,applied?proposal!:draft.Edit.Original,ct);
    internal InlinePromptWindow(BuddyService service,FocusedFieldEditor editor,FocusedDraft draft,Action voice,string voiceShortcut="your voice shortcut")
    {
        this.service=service;this.editor=editor;this.draft=draft;fieldBounds=draft.Anchor.Bounds;
        Title="Buddy - inline prompt suggestion";Width=360;SizeToContent=SizeToContent.Height;WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;Topmost=true;ShowInTaskbar=false;ShowActivated=false;Background=BuddyTheme.Surface;Foreground=BuddyTheme.Ink;
        var header=new DockPanel();var face=AppBranding.Image(28);face.Margin=new(0,0,8,0);DockPanel.SetDock(face,Dock.Left);header.Children.Add(face);header.Children.Add(status);body.Children.Add(header);
        body.Children.Add(new TextBlock{Text="Source: "+draft.App+" / "+draft.FieldName,FontSize=11,TextWrapping=TextWrapping.Wrap,Margin=new(0,3,0,5)});
        preview=new ScrollViewer{Content=diff,MaxHeight=230,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};body.Children.Add(preview);var buttons=new WrapPanel();
        Button Add(string label,Action action){var button=new Button{Content=label,Focusable=false,Padding=new(7,4,7,4),Margin=new(0,6,5,0)};button.Click+=(_,_)=>action();buttons.Children.Add(button);return button;}
        yes=Add("Yes, refine",()=>_ = Refine());Add("Voice reply",voice);Add("Dismiss / reject",Close);accept=Add("Accept",()=>_ = Apply());undo=Add("Undo",()=>_ = Undo());accept.Visibility=undo.Visibility=Visibility.Collapsed;
        body.Children.Add(buttons);body.Children.Add(new TextBlock{Text="Esc dismisses · Voice reply: "+voiceShortcut+" · Never sends",TextWrapping=TextWrapping.Wrap,FontSize=11,Margin=new(0,6,0,0)});Content=new Border{BorderBrush=BuddyTheme.Deep,BorderThickness=new(1),CornerRadius=new(12),Child=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled}};
        SourceInitialized+=(_,_)=>{var handle=new WindowInteropHelper(this).Handle;OverlayNative.Configure(handle,false,noActivate:true);escapeRegistered=Native.RegisterHotKey(handle,0x4250,0x4000,27);if(!escapeRegistered){var hint=(TextBlock)body.Children[body.Children.Count-1];hint.Text=hint.Text.Replace("Esc dismisses","Dismiss / reject closes");}HwndSource.FromHwnd(handle).AddHook((IntPtr h,int m,IntPtr w,IntPtr l,ref bool handled)=>{if(m==0x312&&w.ToInt32()==0x4250){handled=true;Close();return IntPtr.Zero;}if(m==0x21){handled=true;return new IntPtr(3);}return IntPtr.Zero;});OverlayNative.Place(this,new(){X=(int)draft.Anchor.Bounds.Right,Y=(int)draft.Anchor.Bounds.Top});};
        Closed+=(_,_)=>{if(escapeRegistered)Native.UnregisterHotKey(new WindowInteropHelper(this).Handle,0x4250);closed=true;lifetime.Cancel();proposal=null;diff.Inlines.Clear();};
    }
    internal bool Reply(string text){if(!AwaitingConsent||PromptSuggestionPolicy.Reply(text) is not {} consent)return false;if(consent)_=Refine();else Close();return true;}
    internal async Task Refine(){
        if(started||closed)return;started=true;yes.IsEnabled=false;status.Text="Refining locally; your original field is unchanged.";
        try {
            var result=await service.RefineDetailed(new(draft.Edit.Original,"quick"),lifetime.Token);lifetime.Token.ThrowIfCancellationRequested();
            if(!result.Accepted){status.Text=result.Message;return;}ShowProposal(result.RefinedPrompt);
        } catch(Exception ex){if(!closed)status.Text=ex is OperationCanceledException?"Stopped; original unchanged.":ex.Message;}
    }
    private void ShowProposal(string text){
        proposal=text;var delta=PromptSuggestionPolicy.Difference(draft.Edit.Original,text);diff.Inlines.Clear();
        diff.Inlines.Add(new Run(delta.Prefix));diff.Inlines.Add(new Run(delta.Removed){TextDecorations=TextDecorations.Strikethrough,Background=Brushes.MistyRose});diff.Inlines.Add(new Run(delta.Added){Background=Brushes.Honeydew,Foreground=Brushes.DarkGreen});diff.Inlines.Add(new Run(delta.Suffix));
        status.Text="Review this preview; your original is unchanged.";accept.Visibility=Visibility.Visible;Reanchor(fieldBounds);
    }
    internal void Reanchor(Rect bounds){
        fieldBounds=bounds;var handle=new WindowInteropHelper(this).Handle;if(handle==IntPtr.Zero)return;
        double scale=OverlayNative.Scale(handle);
        IsInFieldPreview=proposal is not null&&bounds.Width/scale>=280&&bounds.Height/scale>=180;
        if(IsInFieldPreview){SizeToContent=SizeToContent.Manual;Width=bounds.Width/scale;Height=Math.Min(360,bounds.Height/scale);preview.MaxHeight=Math.Max(35,Height-125);diff.MaxHeight=double.PositiveInfinity;OverlayNative.SetBounds(this,(int)bounds.X,(int)bounds.Y,(int)bounds.Width,(int)(Height*scale));}
        else {SizeToContent=SizeToContent.Height;Width=360;preview.MaxHeight=230;OverlayNative.Place(this,new(){X=(int)bounds.Right,Y=(int)bounds.Top});if(proposal is not null&&!applied)status.Text="This field is too small for the full preview; review beside it. Original unchanged.";}
    }
    private async Task Apply(){
        if(proposal is null||applied||closed||mutating)return;accept.IsEnabled=false;mutating=true;
        try {await editor.Apply(draft,proposal,lifetime.Token);applied=true;status.Text="Applied without sending. Undo is available for 30 seconds.";undo.Visibility=Visibility.Visible;}
        catch(Exception ex){status.Text=ex.Message;}finally{mutating=false;}
    }
    private async Task Undo(){if(mutating)return;mutating=true;undo.IsEnabled=false;try{await editor.Undo(draft,lifetime.Token);applied=false;status.Text="Exact original restored without sending.";}catch(Exception ex){status.Text=ex.Message;}finally{mutating=false;}}
}
