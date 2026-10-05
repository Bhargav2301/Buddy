using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;
using System.Text.Json;

namespace Buddy.Server;

public record SavedTeachingExchange(string App,string Question,string Answer,DateTimeOffset At);

// A separate bounded encrypted file avoids changing or downgrading the main profile schema.
public sealed class TeachingMemoryStore(string directory,IDataProtectionProvider provider)
{
    private static readonly SemaphoreSlim gate=new(1,1);
    private readonly string path=Path.Combine(directory,"teaching.v1.encrypted");
    private readonly IDataProtector protector=provider.CreateProtector("Buddy.Teaching.v1");
    private List<SavedTeachingExchange> Load()
    {
        if(!File.Exists(path))return [];
        if(new FileInfo(path).Length>512_000)throw new InvalidDataException("Saved teaching memory is too large; its file was preserved.");
        var clear=protector.Unprotect(File.ReadAllBytes(path));
        try{var rows=JsonSerializer.Deserialize<List<SavedTeachingExchange>>(clear,StateStore.Json)??throw new InvalidDataException("Invalid teaching memory.");
            if(rows.Count>100||rows.Any(r=>r is null||r.App is null||r.Question is null||r.Answer is null||r.App.Length>60||r.Question.Length>2000||r.Answer.Length>1600))throw new InvalidDataException("Invalid teaching memory; its file was preserved.");return rows;}
        finally{CryptographicOperations.ZeroMemory(clear);}
    }
    public async Task<IReadOnlyList<SavedTeachingExchange>> Read(string? app=null,CancellationToken ct=default)
    {
        if(app is not null)app=LocalKnowledge.AppKey(app);await gate.WaitAsync(ct);
        try{return Load().Where(r=>app is null||r.App==app).ToArray();}finally{gate.Release();}
    }
    public async Task Save(string app,string question,string answer,CancellationToken ct=default)
    {
        app=LocalKnowledge.AppKey(app);question=Security.Redact(Security.Text(question,2000,"Question"));answer=Security.Redact(Security.Text(answer,1600,"Answer"));
        await gate.WaitAsync(ct);
        try{var rows=Load();rows.Add(new(app,question,answer,DateTimeOffset.UtcNow));while(rows.Count(r=>r.App==app)>10)rows.RemoveAt(rows.FindIndex(r=>r.App==app));while(rows.Count>100)rows.RemoveAt(0);await Write(rows,ct);}
        finally{gate.Release();}
    }
    public async Task Clear(CancellationToken ct=default)
    {
        await gate.WaitAsync(ct);try{ct.ThrowIfCancellationRequested();if(File.Exists(path))await Write([],ct);}finally{gate.Release();}
    }
    private async Task Write(List<SavedTeachingExchange> rows,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();Directory.CreateDirectory(directory);var clear=JsonSerializer.SerializeToUtf8Bytes(rows,StateStore.Json);
        byte[] encrypted;try{encrypted=protector.Protect(clear);}finally{CryptographicOperations.ZeroMemory(clear);}
        var temporary=path+".new";await File.WriteAllBytesAsync(temporary,encrypted,ct);ct.ThrowIfCancellationRequested();File.Move(temporary,path,true);
    }
}
