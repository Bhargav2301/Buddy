using System.Speech.Recognition;
using Whisper.net;
using Whisper.net.LibraryLoader;

namespace Buddy.Windows;

internal sealed record WhisperTranscript(string Text,float Probability,float NoSpeechProbability,int Segments);

// A single bounded local inference owner. No microphone audio is saved or sent over HTTP.
internal static class WhisperNativeInference
{
    private static readonly SemaphoreSlim gate=new(1,1);
    private static WhisperFactory? factory;private static string loaded="";
    internal static async Task<WhisperTranscript> Transcribe(float[] samples,string path,string language,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if(!SpeechSignal.HasSpeech(samples))return new("",0,1,0);
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(45));ct=timeout.Token;
        await gate.WaitAsync(ct);
        try {
            ct.ThrowIfCancellationRequested();
            RuntimeOptions.RuntimeLibraryOrder=[RuntimeLibrary.Cpu];
            if(factory is null||loaded!=path){factory?.Dispose();factory=null;loaded="";factory=WhisperFactory.FromPath(path,new(){UseGpu=false});loaded=path;}
            ct.ThrowIfCancellationRequested();
            await using var processor=factory.CreateBuilder().WithLanguage(language).WithThreads(Math.Clamp(Environment.ProcessorCount/2,2,8))
                .WithNoContext().WithPrompt("Names may include Buddy, Comet Browser, Grok, ChatGPT, Codex, Perplexity, DeepSeek, Notepad, Calculator.").WithTemperature(0).WithTemperatureInc(0).WithProbabilities().Build();
            var words=new List<string>();float probability=1,noSpeech=0;int segments=0;
            await foreach(var segment in processor.ProcessAsync(samples,ct)){
                ct.ThrowIfCancellationRequested();segments++;probability=Math.Min(probability,segment.Probability);noSpeech=Math.Max(noSpeech,segment.NoSpeechProbability);
                if(segment.NoSpeechProbability<.6f&&!string.IsNullOrWhiteSpace(segment.Text))words.Add(segment.Text.Trim());
            }
            return new(string.Join(" ",words),probability,noSpeech,segments);
        }finally{gate.Release();}
    }
}

internal sealed class WhisperRecognizer(DesktopPreferences preferences,WhisperModel model,CancellationToken parent,Func<Stream>? microphoneFactory=null,Func<float[],CancellationToken,Task<WhisperTranscript>>? transcription=null):LocalRecognizer
{
    private readonly CancellationTokenSource lifetime=CancellationTokenSource.CreateLinkedTokenSource(parent);
    private Stream? microphone;private int started,finished,disposed;
    public override void RecognizeAsync(RecognizeMode mode)
    {
        if(Interlocked.Exchange(ref started,1)!=0)throw new InvalidOperationException("This microphone session has already started.");
        try{lifetime.Token.ThrowIfCancellationRequested();microphone=microphoneFactory?.Invoke()??new MicrophoneStream(preferences.MicrophoneId);_=Task.Run(()=>Run(mode));}
        catch{microphone?.Dispose();lifetime.Dispose();throw;}
    }
    private async Task Run(RecognizeMode mode)
    {
        var samples=new float[16000*30];var bytes=new byte[1280];int used=0;bool speech=false;int loudFrames=0,quietSamples=0;
        var ct=lifetime.Token;
        try {
            using var cancellation=ct.Register(()=>microphone?.Dispose());
            while(used<samples.Length&&Volatile.Read(ref finished)==0){
                ct.ThrowIfCancellationRequested();int read=microphone?.Read(bytes,0,bytes.Length)??0;if(read==0)break;
                int count=Math.Min(read/2,samples.Length-used);double energy=0;
                for(int i=0;i<count;i++){float value=(short)(bytes[i*2]|bytes[i*2+1]<<8)/32768f;samples[used+i]=value;energy+=value*value;}
                used+=count;double rms=Math.Sqrt(energy/Math.Max(1,count));Level(Math.Clamp((int)(rms*500),0,100));
                if(rms>=.004){if(++loudFrames>=2)speech=true;quietSamples=0;}else{loudFrames=0;quietSamples+=count;}
                if(!speech&&used>=InitialSilenceTimeout.TotalSeconds*16000)break;
                if(mode==RecognizeMode.Single&&speech&&quietSamples>=Math.Max(.9,EndSilenceTimeout.TotalSeconds)*16000)break;
            }
            microphone?.Dispose();microphone=null;ct.ThrowIfCancellationRequested();Level(0);
            if(!speech||used<3200){Completed(null);return;}
            Transcribing();
            // Add silence padding for the decoder; this cannot recover speech recorded after Finish.
            int padded=Math.Min(samples.Length,Math.Max(16000,used+1600));var audio=samples.AsSpan(0,padded).ToArray();
            WhisperTranscript result;
            try {
                var language=preferences.RecognitionLanguage.Length==0?"en":preferences.RecognitionLanguage.Split('-')[0].ToLowerInvariant();
                if(model.EnglishOnly&&language is not "en" and not "auto")throw new InvalidOperationException("Choose the multilingual Whisper model for this recognition language.");
                result=transcription is null ? await WhisperInference.Transcribe(audio,WhisperModels.PathFor(model),model.EnglishOnly?"en":language,ct) : await transcription(audio,ct);
            }finally{Array.Clear(audio);}
            ct.ThrowIfCancellationRequested();
            if(string.IsNullOrWhiteSpace(result.Text)||result.NoSpeechProbability>=.6||result.Probability<.15){Rejected();Completed(null);return;}
            // Token probabilities are not calibrated recognition confidence. Always review Whisper voice requests.
            var transcript=new LocalRecognitionResult(result.Text,result.Probability,[],RequiresReview:true);
            Recognized(transcript);Completed(transcript);
        }catch(OperationCanceledException){Completed(null,cancelled:true);}
        catch(Exception error){Completed(null,error);}
        finally{microphone?.Dispose();microphone=null;Array.Clear(samples);Array.Clear(bytes);lifetime.Dispose();}
    }
    public override void RecognizeAsyncStop(){Interlocked.Exchange(ref finished,1);microphone?.Dispose();}
    public override void Dispose(){if(Interlocked.Exchange(ref disposed,1)!=0)return;try{lifetime.Cancel();}catch(ObjectDisposedException){}microphone?.Dispose();if(Volatile.Read(ref started)==0)lifetime.Dispose();}
}
