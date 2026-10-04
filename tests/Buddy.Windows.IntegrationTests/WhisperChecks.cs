using Buddy.Windows;
using System.Diagnostics;
using System.IO;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class WhisperChecks
{
    internal static async Task<int> Run(string models,string output)
    {
        try {
            var rows=new List<object>();int count=0;
            void Check(bool ok,string note){if(!ok)throw new Exception("FAIL: "+note);count++;Console.WriteLine("PASS: "+note);}
            await RecognitionLifecycleChecks.Run(Check);
            var phrases=new[]{"Open Comet Browser.","Write a poem about a boat sailing on a lonely sea.","Show me where to change the settings in Grok.",
                "Please open Comet Browser, then wait for my approval.","Refine this prompt in Chat GPT without sending it.","Open Notepad, not Calculator.",
                "Search my notes about Hyderabad and Bengaluru.","Explain this chart slowly, one step at a time.","Stop listening and cancel the current action.",
                "Compare Whisper with the Windows speech recognizer.","Keep the original draft and show me the differences.","Ask before changing anything in Perplexity or Deep Seek."};
            using var synth=new SpeechSynthesizer();synth.Rate=-1;
            var voices=synth.GetInstalledVoices().Where(v=>v.Enabled&&v.VoiceInfo.Culture.TwoLetterISOLanguageName=="en").Select(v=>v.VoiceInfo).ToArray();
            Console.WriteLine("Synthetic English voices: "+string.Join(", ",voices.Select(v=>v.Name+" "+v.Culture.Name)));
            var fixtures=phrases.Select((text,index)=>{var voice=voices[index%voices.Length];synth.SelectVoice(voice.Name);synth.Rate=new[]{-2,0,1}[index%3];return(Text:text,Voice:voice.Name,Culture:voice.Culture.Name,Rate:synth.Rate,Samples:Audio(synth,text));}).ToArray();
            Check(fixtures.All(f=>f.Samples.Length>16000),"Known synthetic speech generated locally into memory without playing or saving audio");
            foreach(var model in WhisperModels.Choices){
                var path=Path.Combine(models,model.FileName);await WhisperModels.Verify(path,model,default);
                Check(true,"Pinned model size and SHA-256 verified: "+model.Id);
                foreach(var fixture in fixtures){
                    var clock=Stopwatch.StartNew();var result=await WhisperInference.Transcribe(fixture.Samples,path,"en",default);clock.Stop();
                    var errors=WordErrors(fixture.Text,result.Text);
                    if(clock.Elapsed>TimeSpan.FromSeconds(45))throw new Exception("Inference exceeded its bounded deadline.");
                    rows.Add(new{model=model.Id,expected=fixture.Text,fixture.Voice,fixture.Culture,fixture.Rate,actual=result.Text,elapsedMs=clock.ElapsedMilliseconds,audioSeconds=fixture.Samples.Length/16000d,wordErrors=errors.Errors,wordCount=errors.Words,result.Probability,result.NoSpeechProbability,synthetic=true});
                    Console.WriteLine(JsonSerializer.Serialize(rows[^1]));
                    Check(result.Text.Length>0&&float.IsFinite(result.Probability),"Real Whisper inference produced a bounded transcript: "+model.Id);
                }
                var random=new Random(17);
                var nonspeech=new[]{new float[32000],Enumerable.Range(0,32000).Select(_=>(float)(random.NextDouble()-.5)*.1f).ToArray(),Enumerable.Range(0,32000).Select(i=>(float)Math.Sin(2*Math.PI*200*i/16000)*.1f).ToArray()};
                foreach(var noise in nonspeech){var silence=await WhisperInference.Transcribe(noise,path,"en",default);Check(silence.Text.Length==0,"Silence, deterministic hiss or steady hum produces no transcript: "+model.Id);Array.Clear(noise);}
                var longAudio=Enumerable.Range(0,4).SelectMany(_=>fixtures[1].Samples).Take(16000*30).ToArray();
                using var cancel=new CancellationTokenSource(100);var elapsed=Stopwatch.StartNew();bool cancelled=false;
                try{await WhisperInference.Transcribe(longAudio,path,"en",cancel.Token);}catch(OperationCanceledException){cancelled=true;}finally{Array.Clear(longAudio);}
                rows.Add(new{model=model.Id,cancellationMs=elapsed.ElapsedMilliseconds,cancelled,synthetic=true});Console.WriteLine(JsonSerializer.Serialize(rows[^1]));
                Check(cancelled&&elapsed.ElapsedMilliseconds<2000,"Native inference cancellation returns within two seconds: "+model.Id);
                var again=await WhisperInference.Transcribe(fixtures[0].Samples,path,"en",default);
                Check(again.Text.Contains("Comet",StringComparison.OrdinalIgnoreCase),"Recognition restarts after cancellation and preserves Comet: "+model.Id);
            }
            foreach(var f in fixtures)Array.Clear(f.Samples);
            WhisperInference.Stop();
            File.WriteAllText(output,JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine($"ALL {count} WHISPER CHECKS PASSED; synthetic speech, no physical microphone recognition claimed");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    private static float[] Audio(SpeechSynthesizer synth,string text)
    {
        using var stream=new MemoryStream();synth.SetOutputToAudioStream(stream,new SpeechAudioFormatInfo(16000,AudioBitsPerSample.Sixteen,AudioChannel.Mono));synth.Speak(text);synth.SetOutputToNull();
        var bytes=stream.ToArray();var samples=new float[bytes.Length/2+3200];
        for(int i=0;i<bytes.Length/2;i++)samples[i]=(short)(bytes[2*i]|bytes[2*i+1]<<8)/32768f;
        Array.Clear(bytes);return samples;
    }
    private static (int Errors,int Words) WordErrors(string expected,string actual)
    {
        string[] Words(string value)=>Regex.Matches(value.ToLowerInvariant(),"[a-z0-9]+").Select(x=>x.Value).ToArray();
        var a=Words(expected);var b=Words(actual);var d=new int[a.Length+1,b.Length+1];for(int i=0;i<=a.Length;i++)d[i,0]=i;for(int j=0;j<=b.Length;j++)d[0,j]=j;
        for(int i=1;i<=a.Length;i++)for(int j=1;j<=b.Length;j++)d[i,j]=Math.Min(Math.Min(d[i-1,j]+1,d[i,j-1]+1),d[i-1,j-1]+(a[i-1]==b[j-1]?0:1));return(d[a.Length,b.Length],a.Length);
    }
}
