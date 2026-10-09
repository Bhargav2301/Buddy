using Buddy.Windows;
using System.IO;
using System.Speech.Recognition;

internal static class RecognitionLifecycleChecks
{
    internal static async Task Run(Action<bool,string> check)
    {
        byte[] Clip(int seconds){var b=new byte[seconds*32000];for(int i=0;i<b.Length/2;i++){short sample=(short)(Math.Sin(i*.08)*(3000+2000*Math.Sin(i*.0008)));b[i*2]=(byte)sample;b[i*2+1]=(byte)(sample>>8);}return b;}
        var bytes=Clip(40);var stream=new MemoryStream(bytes);int received=0;
        var done=new TaskCompletionSource<LocalSpeechCompleted>(TaskCreationOptions.RunContinuationsAsynchronously);
        using(var recorder=new WhisperRecognizer(new(),WhisperModels.Choices[0],default,()=>stream,(audio,ct)=>{received=audio.Length;return Task.FromResult(new WhisperTranscript("Fixture words",.9f,0,1));})){
            recorder.RecognizeCompleted+=(_,e)=>done.TrySetResult(e);recorder.RecognizeAsync(RecognizeMode.Multiple);var result=await done.Task.WaitAsync(TimeSpan.FromSeconds(3));
            check(received==SpeechSignal.MaximumSamples&&result.Result?.RequiresReview==true,"Actual capture pump bounds held dictation to 30 seconds and marks Whisper words for review");
            check(!stream.CanRead,"Capture stream is closed before transcript delivery");
        }Array.Clear(bytes);
        bytes=Clip(3);stream=new MemoryStream(bytes);done=new(TaskCreationOptions.RunContinuationsAsynchronously);var processing=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);int delivered=0;
        using(var recorder=new WhisperRecognizer(new(),WhisperModels.Choices[0],default,()=>stream,async(audio,ct)=>{processing.TrySetResult();await Task.Delay(30000,ct);return new("Must not arrive",.9f,0,1);})){
            recorder.SpeechRecognized+=(_,_)=>delivered++;recorder.RecognizeCompleted+=(_,e)=>done.TrySetResult(e);recorder.RecognizeAsync(RecognizeMode.Multiple);
            await processing.Task.WaitAsync(TimeSpan.FromSeconds(3));recorder.Dispose();var result=await done.Task.WaitAsync(TimeSpan.FromSeconds(2));
            check(result.Cancelled&&delivered==0&&!stream.CanRead,"Stop during transcription cancels the captured utterance and emits no late recognized words");
        }Array.Clear(bytes);
        bytes=new byte[32000];stream=new MemoryStream(bytes);done=new(TaskCreationOptions.RunContinuationsAsynchronously);bool inferred=false;
        using(var recorder=new WhisperRecognizer(new(),WhisperModels.Choices[0],default,()=>stream,(audio,ct)=>{inferred=true;return Task.FromResult(new WhisperTranscript("Bad",.9f,0,1));})){
            recorder.RecognizeCompleted+=(_,e)=>done.TrySetResult(e);recorder.RecognizeAsync(RecognizeMode.Single);var result=await done.Task.WaitAsync(TimeSpan.FromSeconds(2));
            check(!inferred&&result.Result is null,"Capture silence never starts recognition or fabricates words");
        }
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();bool opened=false,refused=false;
        using(var recorder=new WhisperRecognizer(new(),WhisperModels.Choices[0],cancelled.Token,()=>{opened=true;return new MemoryStream();})){
            try{recorder.RecognizeAsync(RecognizeMode.Single);}catch(OperationCanceledException){refused=true;}
            check(refused&&!opened,"Cancelled preparation never opens a microphone");
        }
    }
}
