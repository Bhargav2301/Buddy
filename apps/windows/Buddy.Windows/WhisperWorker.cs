using System.Diagnostics;
using System.Text.Json;

namespace Buddy.Windows;

internal sealed record WhisperWork(string Path,string Model,string Language,int Samples);

// Fixed own executable, anonymous inherited pipes only. No shell, listening port,
// filesystem audio, credentials or cloud endpoint. Stop kills only this owned worker.
internal static class WhisperInference
{
    private static readonly SemaphoreSlim gate=new(1,1);private static readonly object sync=new();
    private static Process? worker;private static long idleSince;
    private static readonly System.Threading.Timer idle=new(_=>{lock(sync){if(idleSince!=0&&Environment.TickCount64-idleSince>=60000)StopWorker(worker);}},null,10000,10000);
    internal static async Task<WhisperTranscript> Transcribe(float[] samples,string path,string language,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();if(!SpeechSignal.HasSpeech(samples))return new("",0,1,0);
        var model=WhisperModels.Choices.SingleOrDefault(m=>m.FileName==System.IO.Path.GetFileName(path))??throw new InvalidOperationException("Unsupported Whisper model.");
        if(language!="auto"&&!System.Text.RegularExpressions.Regex.IsMatch(language,"^[a-z]{2,3}$"))throw new InvalidOperationException("Select a supported recognition language.");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(45));var token=deadline.Token;
        await gate.WaitAsync(token);Process? process=null;byte[]? audio=null;
        try {
            lock(sync){
                token.ThrowIfCancellationRequested();idleSince=0;
                if(worker is null||worker.HasExited){
                    worker?.Dispose();var info=new ProcessStartInfo(System.IO.Path.Combine(AppContext.BaseDirectory,"Buddy.exe")){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
                    info.ArgumentList.Add("--whisper-worker");worker=Process.Start(info)??throw new InvalidOperationException("Local Whisper worker could not start.");_=Drain(worker.StandardError);
                }
                process=worker;
            }
            using var cancellation=token.Register(()=>StopWorker(process));
            var header=JsonSerializer.SerializeToUtf8Bytes(new WhisperWork(System.IO.Path.GetFullPath(path),model.Id,language,samples.Length));
            await WritePacket(process.StandardInput.BaseStream,header,token);
            audio=new byte[samples.Length*4];Buffer.BlockCopy(samples,0,audio,0,audio.Length);
            await process.StandardInput.BaseStream.WriteAsync(audio,token);await process.StandardInput.BaseStream.FlushAsync(token);
            var result=await ReadPacket(process.StandardOutput.BaseStream,16000,token);token.ThrowIfCancellationRequested();
            return JsonSerializer.Deserialize<WhisperTranscript>(result)??throw new InvalidDataException("Invalid local speech result.");
        }catch(Exception) when(token.IsCancellationRequested){StopWorker(process);throw new OperationCanceledException("Local speech recognition stopped.",token);}
        catch{StopWorker(process);throw;}
        finally{if(audio is not null)Array.Clear(audio);lock(sync){if(ReferenceEquals(worker,process))idleSince=Environment.TickCount64;}gate.Release();}
    }
    internal static void Stop(){lock(sync)StopWorker(worker);}
    private static void StopWorker(Process? expected){lock(sync){if(expected is null||!ReferenceEquals(worker,expected))return;worker=null;idleSince=0;try{if(!expected.HasExited)expected.Kill();}catch(InvalidOperationException){}finally{expected.Dispose();}}}
    private static async Task Drain(StreamReader reader){try{var data=new char[512];while(await reader.ReadAsync(data)>0)Array.Clear(data);}catch(Exception e)when(e is IOException or ObjectDisposedException or InvalidOperationException){}}
    internal static async Task WritePacket(Stream stream,byte[] data,CancellationToken ct){await stream.WriteAsync(BitConverter.GetBytes(data.Length),ct);await stream.WriteAsync(data,ct);}
    internal static async Task<byte[]> ReadPacket(Stream stream,int maximum,CancellationToken ct){var prefix=new byte[4];await stream.ReadExactlyAsync(prefix,ct);int length=BitConverter.ToInt32(prefix);if(length is<1||length>maximum)throw new InvalidDataException("Invalid local speech packet.");var data=new byte[length];await stream.ReadExactlyAsync(data,ct);return data;}
}

internal static class WhisperWorker
{
    internal static async Task<int> Run()
    {
        try {
            using var input=Console.OpenStandardInput();using var output=Console.OpenStandardOutput();string verified="";
            while(true){
                byte[] header;try{header=await WhisperInference.ReadPacket(input,4096,default);}catch(EndOfStreamException){return 0;}
                var work=JsonSerializer.Deserialize<WhisperWork>(header)??throw new InvalidDataException();var model=WhisperModels.Find(work.Model);
                if(work.Samples is<1 or>SpeechSignal.MaximumSamples||!System.IO.Path.IsPathFullyQualified(work.Path)||System.IO.Path.GetFileName(work.Path)!=model.FileName)throw new InvalidDataException();
                if(verified!=work.Path){await WhisperModels.Verify(work.Path,model,default);verified=work.Path;}
                var bytes=new byte[work.Samples*4];var samples=new float[work.Samples];
                try{
                    await input.ReadExactlyAsync(bytes);Buffer.BlockCopy(bytes,0,samples,0,bytes.Length);Array.Clear(bytes);
                    var result=await WhisperNativeInference.Transcribe(samples,work.Path,work.Language,default);
                    await WhisperInference.WritePacket(output,JsonSerializer.SerializeToUtf8Bytes(result),default);await output.FlushAsync();
                }finally{Array.Clear(bytes);Array.Clear(samples);}
            }
        }catch{return 1;}
    }
}
