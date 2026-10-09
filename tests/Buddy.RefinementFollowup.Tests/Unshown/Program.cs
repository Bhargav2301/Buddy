using Buddy.Server;
using Buddy.Windows;
using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;

// No Show, Activate, EnsureHandle, UIA providers, screen/audio capture or model calls.
internal static class Program
{
    private const string Original="Write a poem about a boat sailing in a sea on a lonely night.";
    private const string Proposal="Request: Write a poem.\n\nSubject: a boat sailing in a sea on a lonely night.";
    private static int checks;
    [STAThread]
    private static int Main()
    {
        var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};
        var windows=new List<Window>();
        try {
            var slot=new ExternalRefinementOptionsSlot(); int armed=0;
            var options=new ExternalRefinementOptionsWindow((value,mode)=>{slot.Arm(value,mode,1000);armed++;});windows.Add(options);
            Check(new WindowInteropHelper(options).Handle==IntPtr.Zero&&!options.IsVisible,"Options window remains unshown with no HWND");
            Named<TextBox>(options,"Reference title").Text="My note";
            Named<TextBox>(options,"Reference text").Text="Keep this reviewed local fact.";
            Click(options,"Use for next capture");
            Check(armed==0&&!slot.HasPending&&Named<TextBlock>(options,"Source-field options status").Text.Contains("add or clear"),"Unadded pasted reference cannot be silently dropped when preparing");
            Click(options,"Add pasted reference");
            Named<ComboBox>(options,"External refinement mode").SelectedItem="guided";
            Named<ComboBox>(options,"Refinement technique").SelectedValue="role";
            Click(options,"Use for next capture");
            var plan=slot.Consume(1001)!; var prepared=plan.Bind(Original);
            Check(armed==1&&prepared.Request.Mode=="guided"&&prepared.Request.Technique=="role","Actual mode and technique controls prepare one complete snapshot");
            Check(prepared.Request.Inputs!.Context!.Single().Text=="Keep this reviewed local fact.","Explicitly added pasted reference reaches fresh captured request");

            var invalid=new ExternalRefinementOptionsWindow((value,mode)=>{slot.Arm(value,mode,2000);armed++;});windows.Add(invalid);
            Named<CheckBox>(invalid,"Use a destination size limit").IsChecked=true;
            Named<TextBox>(invalid,"Destination label").Text="Selected field";
            Named<TextBox>(invalid,"Destination limit").Text="0";Click(invalid,"Use for next capture");
            Check(armed==1&&!slot.HasPending&&Named<TextBlock>(invalid,"Source-field options status").Text.Contains("1 to 20,000"),"Invalid destination input stays in configuration with actionable validation");
            invalid.Close();

            var field=new MemoryField();
            var draft=new FocusedDraft(new GuardedEdit(field,Original),"Owned memory field","Prompt",new FieldAnchor(new IntPtr(42),"memory",new Rect(0,0,600,200)));
            var editor=new FocusedFieldEditor(new ScreenPerception(()=>new DesktopPreferences()));
            using(var watcher=new LocalPromptWatcher(editor,()=>null,()=>{})){
                watcher.PauseForPreparedOptions();var paused=(long)Field(watcher,"automaticPausedUntil");
                Check(paused>Environment.TickCount64,"Preparing explicit options pauses automatic suggestions without starting capture");
                watcher.Suspend();Check((long)Field(watcher,"automaticPausedUntil")==paused,"Other surface handoffs cannot shorten prepared-options pause");
                watcher.ClearPreparedOptionsPause();Check((long)Field(watcher,"automaticPausedUntil")==0,"Cancelling preparation releases automatic-suggestion pause");
                var captureOptions=new ExternalRefinementOptionsSlot();captureOptions.Arm(Basic(),"quick",1000);watcher.PauseForPreparedOptions();
                var consumed=watcher.ConsumePreparedOptions(captureOptions,1001);
                Check(consumed is not null&&!captureOptions.HasPending&&(long)Field(watcher,"automaticPausedUntil")==0,"Consumption clears suggestion pause before a capture can fail");
                captureOptions.Arm(Basic(),"quick",2000);watcher.PauseForPreparedOptions();bool expired=false;
                try{watcher.ConsumePreparedOptions(captureOptions,302000);}catch(InvalidOperationException){expired=true;}
                Check(expired&&!captureOptions.HasPending&&(long)Field(watcher,"automaticPausedUntil")==0,"Expired preparation also releases its pause when consumption refuses");
                string activity="field",newSurfaceStatus="New Buddy-draft review";int requestGeneration=1,presentations=0;
                var oldFeedback=new RefinementCaptureFeedback(()=>requestGeneration==1,()=>activity);oldFeedback.EnterFieldActivity();
                var pending=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                var successfulCapture=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                bool CompleteOld(Task pendingCapture,bool cancelled){try{pendingCapture.GetAwaiter().GetResult();return false;}catch(Exception error){return oldFeedback.Report(error,cancelled,message=>{presentations++;newSurfaceStatus=message;});}}
                activity="refine";pending.SetException(new InvalidOperationException("Old provider lost focus"));
                Check(!CompleteOld(pending.Task,false)&&presentations==0&&newSurfaceStatus=="New Buddy-draft review","Uncancelled old provider failure cannot navigate or overwrite a newer Buddy-draft activity");
                Check(!CompleteOld(pending.Task,true)&&presentations==0,"Cancelled old provider failure also cannot overwrite the newer draft activity");
                successfulCapture.SetResult("Captured original field");
                string captured=successfulCapture.Task.GetAwaiter().GetResult();int openedReviews=0;
                if(oldFeedback.OwnsFieldActivity){openedReviews++;newSurfaceStatus=captured;}
                Check(openedReviews==0&&newSurfaceStatus=="New Buddy-draft review","Successful old capture cannot open a review over the newer Buddy-draft activity");
                captureOptions.Arm(Basic(),"guided",3000);watcher.PauseForPreparedOptions();long newPause=(long)Field(watcher,"automaticPausedUntil");requestGeneration=2;
                Check(!CompleteOld(pending.Task,false)&&captureOptions.HasPending&&(long)Field(watcher,"automaticPausedUntil")==newPause,"Old capture failure cannot consume new options or shorten the newer snapshot's suggestion pause");
                Check(!oldFeedback.OwnsFieldActivity,"Replaced generation cannot present an otherwise successful capture");
                int previousRevision=(int)Field(watcher,"revision");bool staleReview=false;
                try{watcher.ShowReview(draft,captureCurrent:()=>oldFeedback.OwnsFieldActivity).GetAwaiter().GetResult();}catch(OperationCanceledException){staleReview=true;}
                Check(staleReview&&(int)Field(watcher,"revision")==previousRevision&&captureOptions.HasPending&&(long)Field(watcher,"automaticPausedUntil")==newPause,"Stale successful review refuses before provider access or any newer preparation mutation");
                var beforeEntry=new RefinementCaptureFeedback(()=>true,()=>"home");string feedbackMessage="";
                Check(beforeEntry.Report(new InvalidOperationException("Focus the original field"),false,message=>feedbackMessage=message)&&feedbackMessage=="Focus the original field","Owned validation failure before field entry retains useful feedback");
                Check(!beforeEntry.Report(new OperationCanceledException(),true,_=>presentations++),"Cancelled pre-entry operation cannot acquire presentation authority");
                var currentFeedback=new RefinementCaptureFeedback(()=>true,()=>"field");currentFeedback.EnterFieldActivity();
                Check(currentFeedback.OwnsFieldActivity&&!beforeEntry.OwnsFieldActivity,"Only an owned entered-field activity may continue a successful capture");
                Check(currentFeedback.Report(new InvalidOperationException("Field unavailable"),false,message=>feedbackMessage=message)&&feedbackMessage=="Field unavailable","Current field failure still reports its actionable error");
                Check(currentFeedback.Report(new OperationCanceledException(),true,message=>feedbackMessage=message)&&feedbackMessage=="Field operation stopped.","Current field timeout still reports stopped without pretending completion");
                watcher.ClearPreparedOptionsPause();
            }
            slot.Arm(Basic() with {Constraints="Keep supplied details.",References=[new("notes","Reviewed source","Local supporting data")],HasBudget=true,Destination="Selected field",Limit="2000"},"guided",3000);
            var reviewPlan=slot.Consume(3001)!;var request=reviewPlan.Bind(Original).Request;
            var inline=new InlinePromptWindow(null!,editor,draft,()=>{},request:request,optionsCurrent:()=>reviewPlan.IsCurrent);windows.Add(inline);
            Check(new WindowInteropHelper(inline).Handle==IntPtr.Zero&&!inline.ShowActivated,"Inline review remains unshown and configured not to activate");
            Check(Named<TextBlock>(inline,"Exact prepared source-field prompt").Text.Contains("Keep supplied details.")&&Named<TextBlock>(inline,"Exact prepared source-field prompt").Text.Contains("Local supporting data"),"Complete prepared source and supporting data are visibly reviewable in control tree");
            Check(Named<TextBlock>(inline,"Frozen source-field preparation").Text.Contains("user-supplied destination limit"),"Budget display distinguishes user-supplied limit from verified app capacity");
            request.Inputs!.ConfirmedConstraints![0]="CHANGED AFTER REVIEW";request.Inputs.Context!.Clear();
            var frozen=(RefinementPreparationResult)Field(inline,"preparation");
            Check(frozen.Request.Inputs!.ConfirmedConstraints!.Single()=="Keep supplied details."&&frozen.Request.Inputs.Context!.Count==1,"Inline request deeply snapshots mutable caller lists");
            Invoke(inline,"ShowProposal",Original);
            Check(!Button(inline,"Accept").IsEnabled&&Button(inline,"Accept").Visibility==Visibility.Collapsed,"No-change result cannot enable Accept in actual inline controls");
            Invoke(inline,"ShowProposal",Proposal);
            Check(!Button(inline,"Accept").IsEnabled&&field.Writes==0,"Context proposal requires explicit recipient confirmation before Accept");
            ((CheckBox)Field(inline,"contextConsent")).IsChecked=true;
            Check(Button(inline,"Accept").IsEnabled&&field.Writes==0,"Confirmed changed proposal enables review only and performs no source write");
            slot.Clear(); ((Task)Invoke(inline,"Apply")!).GetAwaiter().GetResult();
            Check(field.Writes==0&&!Button(inline,"Accept").IsEnabled,"Stale options authority refuses forced Apply before any field adapter call");

            slot.Arm(Basic() with {HasBudget=true,Destination="Tiny field",Limit="5"},"quick",4000);
            var overflow=slot.Consume(4001)!.Bind(Original);
            var blocked=new InlinePromptWindow(null!,editor,draft,()=>{},request:overflow.Request);windows.Add(blocked);
            blocked.Refine().GetAwaiter().GetResult();
            Check(Named<TextBlock>(blocked,"Refinement status").Text.Contains("no refinement ran")&&!Button(blocked,"Accept").IsEnabled,"Non-ready required-content budget blocks inference without a service or source edit");
            Check(Named<TextBlock>(blocked,"Exact prepared source-field prompt").Text==Original,"Overflow review shows full original, never clipped text");
            bool mismatch=false;try{_ =new InlinePromptWindow(null!,editor,draft,()=>{},request:new RefineRequest("Different source","quick"));}catch(InvalidOperationException){mismatch=true;}
            Check(mismatch,"Request prepared for different source is refused at review construction");
            Check(new WindowInteropHelper(blocked).Handle==IntPtr.Zero&&field.Writes==0,"All unshown review checks finish without HWND, source write or submission");
            ContextReviewChecks(windows);
            BrowserContextUiChecks(windows);
            Console.WriteLine($"{checks} unshown WPF refinement checks passed. No native field, foreground, model, browser or physical acceptance.");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        finally{foreach(var window in windows){try{window.Close();}catch{}}app.Shutdown();}
    }
    private static RefinementDraftOptions Basic()=>new("auto","general",false,false,"","","",[],[],false,"","","utf16-code-units");
    private static void ContextReviewChecks(List<Window> windows)
    {
        using var workspace=new RefinementWorkspace();var scope=new RefinementChatScope("buddy-local","synthetic-ocr-review");workspace.Open(scope);
        var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(System.Windows.Media.Imaging.BitmapSource.Create(1,1,96,96,System.Windows.Media.PixelFormats.Bgra32,null,new byte[]{255,255,255,255},4)));
        using var bytes=new MemoryStream();encoder.Save(bytes);var snapshot=new ContextImageSnapshot(bytes.ToArray(),new("image/png",1,1));
        var image=new ContextSourceReadResult("synthetic-image","owned-fixture.png",ContextSourceKind.LocalImage,"image/png","",snapshot.Sha256,ContextSourceReader.DigestText(""),snapshot.ByteCount,"synthetic-image",Image:snapshot,OriginalAsset:snapshot.RetainOriginalAsset());
        int dispatched=0,ocrCalls=0;
        var window=new ContextWorkspaceWindow(workspace,scope,"Synthetic context",(_,_)=>dispatched++,readSelected:(_,_)=>Task.FromResult(image),
            extractSelected:(source,_)=>{ocrCalls++;return Task.FromResult(ContextSourceReader.FromOcr(source,"ALPHA 125","injected-ocr-review-fixture"));});windows.Add(window);
        window.StageFile("owned-fixture.png").GetAwaiter().GetResult();
        Check(new WindowInteropHelper(window).Handle==IntPtr.Zero&&!window.IsVisible,"Context review remains unshown and has no HWND");
        Click(window,"Prepare for an external chat");Check(dispatched==0,"Unreviewed original image cannot dispatch text context");
        ((Task)Invoke(window,"ExtractImage")!).GetAwaiter().GetResult();
        var source=workspace.Snapshot(scope).Sources.Single();
        Check(source.Text=="ALPHA 125"&&!source.Reviewed&&source.Kind==RefinementSourceKind.LocalOcr,"Fallible injected OCR remains explicitly unreviewed text");
        Check(Field(window,"pendingImage") is not null&&((StackPanel)Field(window,"imageReview")).Children.OfType<Image>().Any(),"Original image stays beside extracted text until review or discard");
        Check(Tree(window).OfType<TextBlock>().Any(t=>t.Text.Contains("misread letters and numbers")),"Review explicitly warns that OCR may change letters and numbers");
        ((Task)Invoke(window,"ExtractImage")!).GetAwaiter().GetResult();Check(ocrCalls==1,"Repeated extraction cannot replace an unreviewed result");
        Click(window,"Prepare for an external chat");Check(dispatched==0,"OCR text cannot dispatch before separate source review");
        Click(window,"Add reviewed source");Check(workspace.Snapshot(scope).Sources.Single().Reviewed,"Explicit source review records only the selected extracted text");
        Check(Field(window,"pendingImage") is null&&((StackPanel)Field(window,"imageReview")).Children.Count==0,"Completed extraction review releases pending preview ownership");
        var reviewed=workspace.Snapshot(scope);var frozen=workspace.Freeze(scope,reviewed.Revision,[reviewed.Sources.Single().Id],[]);
        using(var original=workspace.RetainOriginalAsset(frozen,reviewed.Sources.Single().Id))Check(original.CopyBytes().SequenceEqual(bytes.ToArray()),"Review retains exact original image bytes independently from OCR text");
        var requireOriginal=Tree(window).OfType<CheckBox>().Single(c=>c.Content is string s&&s.StartsWith("Require the original"));requireOriginal.IsChecked=true;requireOriginal.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Check(!workspace.Snapshot(scope).Sources.Single().Reviewed&&frozen.Invalidated.IsCancellationRequested,"Changing original-delivery requirement invalidates the old selection and requires review again");
        Click(window,"Add reviewed source");Click(window,"Prepare for an external chat");Check(dispatched==0,"Text destination refuses original-required image context instead of substituting OCR");
        Click(window,"Clear this context");Check(workspace.Snapshot(scope).Sources.Count==0&&dispatched==0,"Clear removes local reviewed context without a destination write");
        window.Close();
        var fullScope=new RefinementChatScope("buddy-local","synthetic-full-context");workspace.Open(fullScope);
        for(int i=0;i<8;i++){var s=workspace.StageSource(fullScope,new("Source "+i,"Reviewed text "+i),workspace.Snapshot(fullScope).Revision);var item=s.Sources.Last();workspace.ReviewSource(fullScope,item.Id,item.ReviewDigest,s.Revision);}
        var rejected=ContextSourceReader.FromSelectedTextBytes("ninth.txt",System.Text.Encoding.UTF8.GetBytes("Ninth source"),"text/plain");
        var fullWindow=new ContextWorkspaceWindow(workspace,fullScope,"Full synthetic context",(_,_)=>dispatched++,readSelected:(_,_)=>Task.FromResult(rejected));windows.Add(fullWindow);
        fullWindow.StageFile("ninth.txt").GetAwaiter().GetResult();
        Check(((TextBlock)Field(fullWindow,"status")).Text.Contains("Remove a source"),"Source-capacity refusal remains visible after asynchronous read cleanup");
        Check(workspace.Snapshot(fullScope).Sources.Count==8&&dispatched==0,"Refused new file leaves the existing reviewed selection unchanged");
        bool released=false;try{rejected.OriginalAsset!.EnsureAvailable();}catch(ObjectDisposedException){released=true;}
        Check(released,"Refused file staging releases the newly read original asset");fullWindow.Close();
    }
    private static void Check(bool value,string text){if(!value)throw new Exception("FAIL: "+text);checks++;Console.WriteLine("PASS: "+text);}
    private static void BrowserContextUiChecks(List<Window> windows)
    {
        using var workspace=new RefinementWorkspace();
        FrozenRefinementContext Source(string id,string text){var scope=new RefinementChatScope("manual-external",id);workspace.Open(scope);var staged=workspace.StageSource(scope,new("Selected source",text),0);var item=staged.Sources.Single();workspace.ReviewSource(scope,item.Id,item.ReviewDigest,staged.Revision);return workspace.Freeze(scope,workspace.Snapshot(scope).Revision,[item.Id],[]);}
        var old=Source("browser-old","Old selected data");var next=Source("browser-new","New selected data");
        int callbacks=0;FrozenRefinementContext? prepared=null;
        var context=new ContextWorkspaceWindow(workspace,next.Scope,"Owned browser context",(_,_)=>throw new Exception("Unexpected text dispatch"),browser:f=>{prepared=f;callbacks++;});windows.Add(context);
        Click(context,"Review with browser adapter");
        Check(callbacks==1&&prepared?.Sources.Single().Text=="New selected data","Browser preparation carries the exact selected frozen context without a text destination effect");
        var browser=new BrowserContextReviewWindow(workspace);windows.Add(browser);
        Check(new WindowInteropHelper(browser).Handle==IntPtr.Zero&&!browser.IsVisible,"Browser review window remains unshown with no HWND");
        browser.SetSelection(old);workspace.Clear(old.Scope);browser.SetSelection(next);
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background,new Action(()=>{}));
        Check(((BrowserSelectedContext)Field(browser,"selected")).Selection==next,"Queued old selection invalidation cannot replace or revoke the new selected context");
        Check(!((TextBlock)Field(browser,"notice")).Text.Contains("changed or was cleared"),"Stale queued invalidation cannot overwrite the replacement review status");
        Check(((TextBox)Field(browser,"draft")).Text.Contains("New selected data")&&!((TextBox)Field(browser,"draft")).Text.Contains("Old selected data"),"Replacement review shows only the new selected context");
        Click(browser,"Prepare exact browser review");
        Check(Field(browser,"review") is null,"Unpaired production browser cannot prepare a mutation review");
        var listener=(BrowserPipeListener)Field(browser,"listener");browser.Close();listener.Completion.GetAwaiter().GetResult();
        Check(listener.Completion.IsCompletedSuccessfully,"Closing unshown browser review settles its owned empty pipe listener");
    }
    private static IEnumerable<DependencyObject> Tree(DependencyObject root){yield return root;foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var item in Tree(child))yield return item;}
    private static T Named<T>(DependencyObject root,string name)where T:DependencyObject=>Tree(root).OfType<T>().Single(x=>AutomationProperties.GetName(x)==name);
    private static Button Button(DependencyObject root,string label)=>Tree(root).OfType<Button>().Single(x=>Equals(x.Content,label));
    private static void Click(DependencyObject root,string label)=>Button(root,label).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    private static object Field(object target,string name)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(target)!;
    private static object? Invoke(object target,string name,params object[] args)=>target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(target,args);
    private sealed class MemoryField:IVerifiedTextField{internal int Writes;public string Identity=>"memory";public string Read()=>Original;public void Write(string expected,string text,CancellationToken ct){Writes++;throw new Exception("Unexpected unshown field write");}}
}
