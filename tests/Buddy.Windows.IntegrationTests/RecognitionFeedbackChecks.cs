using Buddy.Windows;
using System.Reflection;
using System.Speech.Recognition;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

internal static class RecognitionFeedbackChecks
{
    internal sealed class Fake : LocalRecognizer
    {
        internal bool Started,Disposed;
        internal void Partial(string text)=>Hypothesis(new(text,.4f,[]));
        internal void Words(string text)=>Recognized(new(text,.9f,[]));
        internal void Reject(string? text=null)=>Rejected(text is null?null:new(text,.2f,[]));
        internal void End(string? text=null, bool cancel=false,Exception? error=null)=>Completed(text is null?null:new(text,.9f,[]),error,cancel);
        public override void RecognizeAsync(RecognizeMode mode)=>Started=true;
        public override void RecognizeAsyncStop()=>End();
        public override void Dispose()=>Disposed=true;
    }
    internal static void Policy(Action<bool,string> check)
    {
        LocalSpeechCompleted? last=null;int events=0;var f=new Fake();
        void Bind(Fake next){last=null;events=0;next.RecognizeCompleted+=(_,e)=>{last=e;events++;};}
        Bind(f);f.Words("Open Notepad");f.Reject();f.End();
        check(last?.Result is {Text:"Open Notepad",RequiresReview:true},"Accepted words followed by rejection and empty completion survive for review");
        f.End();f.Words("late");check(events==1,"One recognition session completes once and suppresses late results");
        f=new();Bind(f);f.Partial("Open Notepad");f.Reject();f.End();
        check(last?.Result is {Text:"Open Notepad",RequiresReview:true},"Displayed hypothesis survives empty completion as explicitly tentative words");
        f=new();Bind(f);f.Reject("Open Calculator");f.End();check(last?.Result is {Text:"Open Calculator",RequiresReview:true},"Rejected candidate remains reviewable without being accepted as a command");
        f=new();Bind(f);f.Words("Open");f.Partial("Notepad");f.End();check(last?.Result?.Text=="Open Notepad","Held dictation keeps accepted prefix and tentative suffix");
        f=new();Bind(f);f.Words("Open Notepad");f.End("Open Notepad");check(last?.Result?.Text=="Open Notepad","Final result does not duplicate a prior recognized event");
        f=new();Bind(f);f.Words("Open Notepad");f.End(cancel:true);check(last?.Cancelled==true&&last.Result is null,"Cancellation never recovers words as a usable command");
        f=new();Bind(f);f.Reject();f.End();check(last?.Result is null,"True no-speech completion stays empty");
        f=new();Bind(f);f.Words("Keep these words");f.End(error:new InvalidOperationException("Fixture capture error"));check(last?.Result is {RequiresReview:true}&&last.Error is not null,"Capture error retains valid words with review and error provenance");
        var segments=new WhisperSegments();segments.Add("Open Notepad",.9f,.05f);segments.Add("",.01f,.99f);segments.Add("noise hallucination",.01f,.99f);
        check(segments.Result() is {Text:"Open Notepad",NoSpeechProbability:<.6f,Probability:>=.15f},"Trailing Whisper silence cannot invalidate a prior verified speech segment");
        segments=new();segments.Add("noise hallucination",.1f,.8f);segments.Add("",.9f,.1f);
        check(segments.Result().Text.Length==0,"Whisper low-confidence and no-speech gates still discard unsupported words");
    }
    static T Field<T>(object o,string name)=>(T)o.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(o)!;
    internal static int Run()
    {
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};var owner=new Window{Title="Buddy speech feedback fixture",Width=480,Height=240};int n=0,exit=1;
        void Check(bool ok,string note){if(!ok)throw new Exception("FAIL: "+note);n++;Console.WriteLine("PASS: "+note);}
        owner.Loaded+=async(_,_)=>{
            VoiceOverlayWindow? voice=null;
            try {
                Policy(Check);var queue=new Queue<Fake>();var h=new WindowInteropHelper(owner).Handle;owner.Activate();InputNative.SetForegroundWindow(h);
                voice=new(()=>null,()=>Task.FromResult<string?>(null),()=>new(){ReadVoiceAnswers=false},_=>{},()=>h,new(()=>new()),(_,_)=>throw new Exception("No action expected"),()=>{},createRecognizer:(ct,p)=>Task.FromResult<LocalRecognizer>(queue.Dequeue()));
                async Task<Fake> Open(){owner.Activate();InputNative.SetForegroundWindow(h);await Task.Delay(80);var fake=new Fake();queue.Enqueue(fake);voice.Open(false);for(int i=0;i<100&&!fake.Started;i++)await Task.Delay(10);if(!fake.Started)Console.WriteLine("Start diagnostic: "+Field<TextBlock>(voice,"state").Text+"; foreground="+Native.GetForegroundWindow()+"; source="+h);Check(fake.Started,"Owned voice fixture started injected local recognizer");return fake;}
                var f=await Open();f.Partial("Open Notepad");f.Reject();f.End();await Task.Delay(100);
                Check(Field<TextBox>(voice,"review").Text=="Open Notepad"&&Field<TextBox>(voice,"review").Visibility==Visibility.Visible,"Exact screenshot sequence shows Open Notepad in editable review");
                Check(!Field<TextBlock>(voice,"state").Text.Contains("No speech")&&!voice.IsListening&&f.Disposed,"Empty completion cannot overwrite visible words with no-speech status; microphone closes");
                f=await Open();f.Words("Open Calculator");f.End();await Task.Delay(100);
                Check(Field<TextBox>(voice,"review").Text=="Open Calculator"&&Field<Button>(voice,"confirmTranscript").Visibility==Visibility.Visible,"Recognized words plus empty final completion require confirmation");
                var old=await Open();old.Partial("Stale words");var current=await Open();old.End();current.Words("Current words");current.End();await Task.Delay(100);
                Check(Field<TextBox>(voice,"review").Text=="Current words","New recording rejects previous session's late completion");
                f=await Open();f.Partial("Must not run");voice.Cancel();f.End();await Task.Delay(100);
                Check(Field<TextBox>(voice,"review").Visibility==Visibility.Collapsed&&!voice.IsBusy&&!voice.IsListening,"Stop suppresses late transcript review and dispatch");
                f=await Open();f.End();await Task.Delay(100);
                Check(Field<TextBlock>(voice,"state").Text.Contains("No speech")&&Field<TextBlock>(voice,"transcript").Text.Length==0,"True silence shows no-speech status with no stale visible transcript");
                Console.WriteLine($"ALL {n} SPEECH FEEDBACK CHECKS PASSED (injected callbacks in real WPF overlay, no physical microphone claim)");exit=0;
            }catch(Exception ex){Console.Error.WriteLine(ex);}finally{voice?.Dispose();owner.Close();app.Shutdown();}
        };app.Run(owner);return exit;
    }
}
