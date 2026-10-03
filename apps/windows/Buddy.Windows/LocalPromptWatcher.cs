using Buddy.Server;
using System.Windows.Threading;

namespace Buddy.Windows;

// Default-off: metadata polling binds ONE allowlisted focused field's text-change events.
// Only a debounced event reads up to the bounded prompt limit. No global keystrokes/screenshots.
internal sealed class LocalPromptWatcher:IDisposable
{
    private readonly FocusedFieldEditor editor;private readonly Func<BuddyService?> service;private readonly Action voice;private readonly Func<string> voiceShortcut;
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(350)};
    private CancellationTokenSource? enabled,review;private IDisposable? subscription;private FieldAnchor? anchor;
    private InlinePromptWindow? bubble;private long changedAt,cooldown;private bool busy;private int revision,binding;
    internal LocalPromptWatcher(FocusedFieldEditor editor,Func<BuddyService?> service,Action voice,Func<string>? voiceShortcut=null){this.editor=editor;this.service=service;this.voice=voice;this.voiceShortcut=voiceShortcut??(()=>"your voice shortcut");timer.Tick+=async(_,_)=>await Check();}
    internal void SetEnabled(bool value){Suspend();enabled?.Cancel();enabled=null;timer.Stop();if(value){enabled=new();timer.Start();}}
    internal void Suspend(){revision++;review?.Cancel();review?.Dispose();review=null;subscription?.Dispose();subscription=null;anchor=null;changedAt=0;cooldown=Environment.TickCount64+30000;bubble?.Close();bubble=null;if(enabled is null)timer.Stop();}
    internal bool Reply(string text)=>bubble?.Reply(text)==true;
    internal async Task ShowReview(FocusedDraft draft){
        Suspend();if(service() is not {} host)throw new InvalidOperationException("Buddy is still starting.");
        review=new();var ct=review.Token;int current=revision;anchor=draft.Anchor;
        subscription=await editor.Watch(draft.Anchor,()=>timer.Dispatcher.BeginInvoke(new Action(()=>{if(current==revision)changedAt=Environment.TickCount64;})),ct);
        ct.ThrowIfCancellationRequested();
        if(subscription is null){Suspend();throw new InvalidOperationException("The original field is no longer focused or does not expose safe change events. Refine source field again from that field.");}
        var card=Present(host,draft);timer.Start();_ = card.Refine();
    }
    private InlinePromptWindow Present(BuddyService host,FocusedDraft draft){
        var card=new InlinePromptWindow(host,editor,draft,voice,voiceShortcut());bubble=card;
        card.Closed+=(_,_)=>{if(ReferenceEquals(bubble,card)){bubble=null;review?.Cancel();review?.Dispose();review=null;}cooldown=Environment.TickCount64+60000;Detach();if(enabled is null)timer.Stop();};card.Show();return card;
    }
    private async Task Check(){
        if(busy||enabled is null&&bubble is null)return;
        if(bubble is not null){
            var card=bubble;var selected=anchor;var foreground=Native.GetForegroundWindow();
            if((InputNative.GetAsyncKeyState(27)&0x8000)!=0||foreground!=selected?.Window&&!Native.IsOwnWindow(foreground)){Suspend();return;}
            if(Native.IsOwnWindow(foreground)||card.IsMutating||selected is null)return;
            var reviewToken=review?.Token??enabled?.Token??default;
            busy=true;try{var candidate=await editor.Probe(selected.Window,reviewToken);if(!ReferenceEquals(card,bubble))return;
                if(candidate?.Identity!=selected.Identity){Suspend();return;}card.Reanchor(candidate.Bounds);
                if(changedAt!=0){long stamp=changedAt;if(!await card.IsCurrent(reviewToken)){Suspend();return;}if(changedAt==stamp)changedAt=0;}
            }catch{Suspend();}finally{busy=false;}return;
        }
        if(Environment.TickCount64<cooldown)return;
        busy=true;int current=revision;var ct=enabled!.Token;
        try {
            var window=Native.GetForegroundWindow();if(window==IntPtr.Zero||Native.IsOwnWindow(window)||OverlayNative.IsFullscreenForeground()||PromptSuggestionPolicy.PrivateMetadata(Native.Label(window),"")){Detach();return;}
            var candidate=await editor.Probe(window,ct);ct.ThrowIfCancellationRequested();if(current!=revision)return;
            if(candidate is null){Detach();return;}
            if(candidate.Identity!=anchor?.Identity){Detach();anchor=candidate;int observedBinding=binding;subscription=await editor.Watch(candidate,()=>timer.Dispatcher.BeginInvoke(new Action(()=>{if(current==revision&&observedBinding==binding)changedAt=Environment.TickCount64;})),ct);ct.ThrowIfCancellationRequested();if(current!=revision){Detach();return;}return;}
            if(subscription is null||changedAt==0||Environment.TickCount64-changedAt<1500)return;
            changedAt=0;var draft=await editor.Capture(window,ct,expectedIdentity:candidate.Identity,strictFocus:true);ct.ThrowIfCancellationRequested();
            if(current!=revision||!PromptSuggestionPolicy.ShouldOffer(draft.Edit.Original)||service() is not {} host)return;
            cooldown=Environment.TickCount64+60000;
            Present(host,draft);
        }catch{if(!ct.IsCancellationRequested){Detach();cooldown=Environment.TickCount64+5000;}}finally{busy=false;}
    }
    private void Detach(){binding++;subscription?.Dispose();subscription=null;anchor=null;changedAt=0;}
    public void Dispose(){SetEnabled(false);}
}
