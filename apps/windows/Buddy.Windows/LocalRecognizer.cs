using System.Speech.Recognition;

namespace Buddy.Windows;

internal sealed record LocalRecognitionResult(string Text,float Confidence,IReadOnlyList<LocalRecognitionResult> Alternates,bool RequiresReview=false);
internal sealed class LocalSpeechEvent(LocalRecognitionResult result):EventArgs { public LocalRecognitionResult Result{get;}=result; }
internal sealed class LocalSpeechCompleted(LocalRecognitionResult? result,Exception? error=null,bool cancelled=false):EventArgs
{ public LocalRecognitionResult? Result{get;}=result;public Exception? Error{get;}=error;public bool Cancelled{get;}=cancelled; }
internal sealed class LocalAudioLevel(int level):EventArgs { public int AudioLevel{get;}=level; }

internal abstract class LocalRecognizer:IDisposable
{
    private readonly object resultGate=new();
    private readonly List<LocalRecognitionResult> accepted=[];
    private LocalRecognitionResult? tentative;
    private bool completed,rejected;
    public TimeSpan InitialSilenceTimeout{get;set;}=TimeSpan.FromSeconds(8);
    public TimeSpan EndSilenceTimeout{get;set;}=TimeSpan.FromMilliseconds(900);
    public event EventHandler<LocalAudioLevel>? AudioLevelUpdated;
    public event EventHandler<LocalSpeechEvent>? SpeechHypothesized,SpeechRecognized;
    public event EventHandler? SpeechRecognitionRejected,Processing;
    public event EventHandler<LocalSpeechCompleted>? RecognizeCompleted;
    protected void Level(int n)=>AudioLevelUpdated?.Invoke(this,new(n));
    protected void Hypothesis(LocalRecognitionResult result){lock(resultGate){if(completed)return;tentative=Bound(result);}SpeechHypothesized?.Invoke(this,new(result));}
    protected void Recognized(LocalRecognitionResult result){lock(resultGate){if(completed)return;var words=Bound(result);if(words is not null&&accepted.Sum(w=>w.Text.Length)<4000)accepted.Add(words);tentative=null;}SpeechRecognized?.Invoke(this,new(result));}
    protected void Rejected(LocalRecognitionResult? result=null){lock(resultGate){if(completed)return;rejected=true;tentative=Bound(result)??tentative;}SpeechRecognitionRejected?.Invoke(this,EventArgs.Empty);}
    protected void Transcribing()=>Processing?.Invoke(this,EventArgs.Empty);
    protected void Completed(LocalRecognitionResult? result,Exception? error=null,bool cancelled=false)
    {
        lock(resultGate){
            if(completed)return;completed=true;
            if(cancelled)result=null;
            else {
                var final=Bound(result);var words=accepted.ToList();
                if(words.Count==0&&final is not null)words.Add(final);
                if(tentative is not null&&final is null)words.Add(tentative with{RequiresReview=true});
                result=words.Count==0?null:new(string.Join(" ",words.Select(w=>w.Text)),words.Min(w=>w.Confidence),words.Count==1?words[0].Alternates:[],
                    RequiresReview:rejected||error is not null||final is null||words.Any(w=>w.RequiresReview));
            }
            accepted.Clear();tentative=null;
        }
        RecognizeCompleted?.Invoke(this,new(result,error,cancelled));
    }
    private static LocalRecognitionResult? Bound(LocalRecognitionResult? result)=>result is null||string.IsNullOrWhiteSpace(result.Text)?null:
        result with{Text=result.Text.Trim()[..Math.Min(4000,result.Text.Trim().Length)]};
    public abstract void RecognizeAsync(RecognizeMode mode);
    public abstract void RecognizeAsyncStop();
    public abstract void Dispose();
}

internal sealed class WindowsLocalRecognizer:LocalRecognizer
{
    private readonly SpeechRecognitionEngine engine;
    internal WindowsLocalRecognizer(SpeechRecognitionEngine engine){
        this.engine=engine;
        engine.AudioLevelUpdated+=(_,e)=>Level(e.AudioLevel);
        engine.SpeechHypothesized+=(_,e)=>Hypothesis(Map(e.Result));
        engine.SpeechRecognized+=(_,e)=>Recognized(Map(e.Result));
        engine.SpeechRecognitionRejected+=(_,e)=>Rejected(e.Result is null?null:Map(e.Result));
        engine.RecognizeCompleted+=(_,e)=>Completed(e.Result is null?null:Map(e.Result),e.Error,e.Cancelled);
    }
    private static LocalRecognitionResult Map(RecognitionResult r)=>new(r.Text,r.Confidence,r.Alternates.Select(a=>new LocalRecognitionResult(a.Text,a.Confidence,[])).ToArray());
    public override void RecognizeAsync(RecognizeMode mode){engine.InitialSilenceTimeout=InitialSilenceTimeout;engine.EndSilenceTimeout=EndSilenceTimeout;engine.RecognizeAsync(mode);}
    public override void RecognizeAsyncStop()=>engine.RecognizeAsyncStop();
    public override void Dispose()=>WindowsSpeechInput.Stop(engine);
}

internal static class LocalSpeechInput
{
    internal static Func<DesktopPreferences> Preferences{get;set;}=()=>new();
    internal static string[] Languages()=>new[]{"en","auto","hi","te","ta","kn","ml","mr","bn","gu","pa","ur","es","fr","de","it","pt","ja","ko","zh","ar","ru"}.Concat(WindowsSpeechInput.Languages()).Distinct().ToArray();
    internal static async Task<LocalRecognizer> Create(CancellationToken ct,DesktopPreferences? preferences=null)
    {
        var p=preferences??Preferences();ct.ThrowIfCancellationRequested();
        if(p.RecognitionEngine=="windows")return new WindowsLocalRecognizer(await WindowsSpeechInput.Create(ct,p));
        if(p.RecognitionEngine!="whisper")throw new InvalidOperationException("Choose a local speech recognition engine in Voice settings.");
        var model=WhisperModels.Find(p.WhisperModel);await WhisperModels.Verify(WhisperModels.PathFor(model),model,ct);
        return new WhisperRecognizer(p,model,ct);
    }
    internal static void Stop(LocalRecognizer? engine)=>engine?.Dispose();
}
