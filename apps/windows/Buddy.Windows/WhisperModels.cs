using System.Net.Http;
using System.Security.Cryptography;

namespace Buddy.Windows;

internal sealed record WhisperModel(string Id,string Label,string FileName,long Size,string Sha256,bool EnglishOnly)
{
    public override string ToString()=>Label;
}
internal static class WhisperModels
{
    internal const string Revision="5359861c739e955e79d9a303bcbc70fb988958b1";
    internal static readonly WhisperModel[] Choices=[
        new("small.en","Whisper small English - accuracy","ggml-small.en-q5_1.bin",190098681,"bfdff4894dcb76bbf647d56263ea2a96645423f1669176f4844a1bf8e478ad30",true),
        new("base.en","Whisper base English - faster","ggml-base.en.bin",147964211,"a03779c86df3323075f5e796cb2ce5029f00ec8869eee3fdfb897afe36c6d002",true),
        new("small","Whisper small multilingual","ggml-small-q5_1.bin",190085487,"ae85e4a935d7a567bd102fe55afc16bb595bdb618e11b2fc7591bc08120411bb",false)];
    internal static string DirectoryPath=>Path.Combine(PreviewEnvironment.DataDirectory,"SpeechModels","Whisper");
    internal static WhisperModel Find(string id)=>Choices.FirstOrDefault(m=>m.Id==id)??throw new InvalidOperationException("Choose a supported local Whisper model in Voice settings.");
    internal static string PathFor(WhisperModel model)=>Path.Combine(DirectoryPath,model.FileName);
    internal static async Task Verify(string path,WhisperModel model,CancellationToken ct)
    {
        if(!File.Exists(path)||new FileInfo(path).Length!=model.Size)throw new InvalidOperationException("Download the selected Whisper model in Voice settings before listening. Recognition will not switch engines silently.");
        using var file=File.OpenRead(path);var hash=await SHA256.HashDataAsync(file,ct);
        if(!Convert.ToHexString(hash).Equals(model.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Whisper model checksum failed. Restore it using Voice settings; recognition stayed off.");
    }
    internal static async Task Download(WhisperModel model,CancellationToken ct)
    {
        Directory.CreateDirectory(DirectoryPath);var path=PathFor(model);
        if(File.Exists(path)){await Verify(path,model,ct);return;}
        var temporary=path+"."+Guid.NewGuid().ToString("N")+".download";
        try {
            using var http=new HttpClient{Timeout=TimeSpan.FromMinutes(15)};
            using var response=await http.GetAsync($"https://huggingface.co/ggerganov/whisper.cpp/resolve/{Revision}/{model.FileName}",HttpCompletionOption.ResponseHeadersRead,ct);
            response.EnsureSuccessStatusCode();
            if(response.Content.Headers.ContentLength is long length&&length!=model.Size)throw new InvalidDataException("Unexpected Whisper model download length.");
            await using(var source=await response.Content.ReadAsStreamAsync(ct))await using(var target=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true)){
                var buffer=new byte[81920];long total=0;int read;
                while((read=await source.ReadAsync(buffer,ct))>0){total+=read;if(total>model.Size)throw new InvalidDataException("Whisper model exceeds the pinned size.");await target.WriteAsync(buffer.AsMemory(0,read),ct);}
            }
            await Verify(temporary,model,ct);ct.ThrowIfCancellationRequested();File.Move(temporary,path,false);
        }finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
}
