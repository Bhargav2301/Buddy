using Buddy.Server;
using System.Windows.Threading;

namespace Buddy.Windows;

// Default-off: metadata polling binds ONE allowlisted focused field's text-change events.
// Only a debounced event reads up to the bounded prompt limit. No global keystrokes/screenshots.
internal sealed class LocalPromptWatcher:IDisposable
{
    private readonly FocusedFieldEditor editor;private readonly Func<BuddyService?> service;private readonly Action voice;private readonly Func<string> voiceShortcut;private readonly Action? configureOptions;
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(350)};
    private CancellationTokenSource? enabled,review;private IDisposable? subscription;private FieldAnchor? anchor;
    private InlinePromptWindow? bubble;private long changedAt,cooldown,automaticPausedUntil;private bool busy;private int revision,binding;
    internal LocalPromptWatcher(FocusedFieldEditor editor,Func<BuddyService?> service,Action voice,Func<string>? voiceShortcut=null,Action? configureOptions=null){this.editor=editor;this.service=service;this.voice=voice;this.voiceShortcut=voiceShortcut??(()=>"your voice shortcut");this.configureOptions=configureOptions;timer.Tick+=async(_,_)=>await Check();}
    internal void SetEnabled(bool value){Suspend();enabled?.Cancel();enabled=null;timer.Stop();if(value){enabled=new();timer.Start();}}
    internal void Suspend(){revision++;review?.Cancel();review?.Dispose();review=null;subscription?.Dispose();subscription=null;anchor=null;changedAt=0;cooldown=Environment.TickCount64+30000;bubble?.Close();bubble=null;if(enabled is null)timer.Stop();}
    internal void PauseForPreparedOptions(){Suspend();automaticPausedUntil=Environment.TickCount64+ExternalRefinementOptionsSlot.LifetimeMilliseconds;}
    internal void ClearPreparedOptionsPause(){automaticPausedUntil=0;}
    internal ExternalRefinementPlan? ConsumePreparedOptions(ExternalRefinementOptionsSlot options,long nowMilliseconds){
        // This is one synchronous dispatcher operation. Clear the consumed/expired
        // preparation's pause now; a later failed capture must not clear a new pause.
        try{return options.Consume(nowMilliseconds);}finally{ClearPreparedOptionsPause();}
    }
    internal bool Reply(string text)=>bubble?.Reply(text)==true;
    internal async Task ShowReview(FocusedDraft draft,RefineRequest? request=null,Func<bool>? optionsCurrent=null,Func<bool>? captureCurrent=null){
        if(captureCurrent?.Invoke()==false)throw new OperationCanceledException("The source-field capture was replaced.");
        Suspend();ClearPreparedOptionsPause();if(service() is not {} host)throw new InvalidOperationException("Buddy is still starting.");
        if(request is not null&&request.Prompt!=draft.Edit.Original)throw new InvalidOperationException("Prepared options do not match this source field. Capture it again.");
        if(optionsCurrent?.Invoke()==false)throw new InvalidOperationException("Prepared options changed. Capture the original field again.");
        review=new();var ct=review.Token;int current=revision;anchor=draft.Anchor;
        var observed=await editor.Watch(draft.Anchor,()=>timer.Dispatcher.BeginInvoke(new Action(()=>{if(current==revision)changedAt=Environment.TickCount64;})),ct);
        if(ct.IsCancellationRequested||current!=revision){observed?.Dispose();ct.ThrowIfCancellationRequested();throw new OperationCanceledException("The source-field review was replaced.");}
        // The initial capture must still own presentation after asynchronous watching.
        // Do not retain this callback on the card: its capture lease ends on return.
        if(captureCurrent?.Invoke()==false){observed?.Dispose();Suspend();throw new OperationCanceledException("The source-field capture was replaced.");}
        subscription=observed;
        if(subscription is null){Suspend();throw new InvalidOperationException("The original field is no longer focused or does not expose safe change events. Refine source field again from that field.");}
        if(optionsCurrent?.Invoke()==false){Suspend();throw new InvalidOperationException("Prepared options changed during capture. Capture the original field again.");}
        var card=Present(host,draft,request,optionsCurrent);timer.Start();_ = card.Refine();
    }
    private InlinePromptWindow Present(BuddyService host,FocusedDraft draft,RefineRequest? request=null,Func<bool>? optionsCurrent=null){
        var card=new InlinePromptWindow(host,editor,draft,voice,voiceShortcut(),request:request,optionsCurrent:optionsCurrent,configureOptions:configureOptions);bubble=card;
        card.Closed+=(_,_)=>{if(!ReferenceEquals(bubble,card))return;bubble=null;review?.Cancel();review?.Dispose();review=null;cooldown=Environment.TickCount64+60000;Detach();if(enabled is null)timer.Stop();};card.Show();return card;
    }
    private async Task Check(){
        if(busy||enabled is null&&bubble is null)return;
        if(bubble is not null){
            var card=bubble;var selected=anchor;int currentReview=revision;var foreground=Native.GetForegroundWindow();
            if((InputNative.GetAsyncKeyState(27)&0x8000)!=0||foreground!=selected?.Window&&!Native.IsOwnWindow(foreground)){Suspend();return;}
            if(Native.IsOwnWindow(foreground)||card.IsMutating||selected is null)return;
            var reviewToken=review?.Token??enabled?.Token??default;
            busy=true;try{var candidate=await editor.Probe(selected.Window,reviewToken,selected.ExplicitInvocation);if(!ReferenceEquals(card,bubble))return;
                if(candidate?.Identity!=selected.Identity){Suspend();return;}card.Reanchor(candidate.Bounds);
                if(changedAt!=0){long stamp=changedAt;bool matches=await card.IsCurrent(reviewToken);
                    if(currentReview!=revision||!ReferenceEquals(card,bubble))return;
                    if(!matches){Suspend();return;}if(changedAt==stamp)changedAt=0;}
            }catch{if(currentReview==revision&&ReferenceEquals(card,bubble))Suspend();}finally{busy=false;}return;
        }
        if(Environment.TickCount64<cooldown||Environment.TickCount64<automaticPausedUntil)return;
        busy=true;int current=revision;var ct=enabled!.Token;
        try {
            var window=Native.GetForegroundWindow();if(window==IntPtr.Zero||Native.IsOwnWindow(window)||OverlayNative.IsFullscreenForeground()||PromptSuggestionPolicy.PrivateMetadata(Native.Label(window),"")){Detach();return;}
            var candidate=await editor.Probe(window,ct);ct.ThrowIfCancellationRequested();if(current!=revision)return;
            if(candidate is null){Detach();return;}
            if(candidate.Identity!=anchor?.Identity){Detach();anchor=candidate;int observedBinding=binding;
                var observed=await editor.Watch(candidate,()=>timer.Dispatcher.BeginInvoke(new Action(()=>{if(current==revision&&observedBinding==binding)changedAt=Environment.TickCount64;})),ct);
                if(ct.IsCancellationRequested||current!=revision){observed?.Dispose();ct.ThrowIfCancellationRequested();return;}
                subscription=observed;return;}
            if(subscription is null||changedAt==0||Environment.TickCount64-changedAt<1500)return;
            changedAt=0;var draft=await editor.Capture(window,ct,expectedIdentity:candidate.Identity,strictFocus:true);ct.ThrowIfCancellationRequested();
            if(current!=revision||!PromptSuggestionPolicy.ShouldOffer(draft.Edit.Original)||service() is not {} host)return;
            cooldown=Environment.TickCount64+60000;
            Present(host,draft);
        }catch{if(current==revision&&!ct.IsCancellationRequested){Detach();cooldown=Environment.TickCount64+5000;}}finally{busy=false;}
    }
    private void Detach(){binding++;subscription?.Dispose();subscription=null;anchor=null;changedAt=0;}
    public void Dispose(){SetEnabled(false);}
}
