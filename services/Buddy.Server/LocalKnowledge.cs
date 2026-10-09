using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Buddy.Server;

public record KnowledgeDocument(string RelativePath,string Sha256,string Text);
public record KnowledgePack(string Id,string App,string AppVersion,string SourceFolder,string Revision,DateTimeOffset Imported,IReadOnlyList<KnowledgeDocument> Documents);
public record KnowledgeHit(string PackId,string AppVersion,string RelativePath,string Revision,int Line,string Text);

public static class LocalKnowledge
{
    public static string AppKey(string app)
    {
        if(!Regex.IsMatch(app,@"^[a-zA-Z0-9_.-]{1,60}$",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(50)))throw new InvalidOperationException("Use the exact process name, for example notepad or FL64.");
        return app.ToLowerInvariant();
    }
    public static async Task<KnowledgePack> Import(string folder,string app,string appVersion,CancellationToken ct,Action<string>? progress=null)
    {
        app=AppKey(app);Security.Text(appVersion,100,"App version");
        var root=Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
        if(!Path.IsPathFullyQualified(root)||root.StartsWith(@"\\")||new DirectoryInfo(root).Attributes.HasFlag(FileAttributes.ReparsePoint))throw new InvalidOperationException("Choose a local folder without links.");
        for(var ancestor=new DirectoryInfo(root).Parent;ancestor is not null;ancestor=ancestor.Parent)if(ancestor.Attributes.HasFlag(FileAttributes.ReparsePoint))throw new InvalidOperationException("Choose a folder without linked parent directories.");
        // Explicit shallow import: no recursion, hidden files, links, binary files or background watching.
        var files=new List<string>();int scanned=0;
        foreach(var path in Directory.EnumerateFiles(root)) {
            ct.ThrowIfCancellationRequested();if(++scanned>500)throw new InvalidOperationException("Choose a smaller folder (at most 500 entries).");
            var info=new FileInfo(path);
            if((info.Attributes&(FileAttributes.ReparsePoint|FileAttributes.Hidden|FileAttributes.System))!=0)continue;
            if(Path.GetExtension(path).ToLowerInvariant() is ".md" or ".txt")files.Add(path);
        }
        if(files.Count is 0 or >40)throw new InvalidOperationException("Choose a folder containing 1 to 40 visible Markdown or text files.");
        var documents=new List<KnowledgeDocument>();long total=0,actualTotal=0;
        foreach(var path in files.Order(StringComparer.OrdinalIgnoreCase)) {
            ct.ThrowIfCancellationRequested();var info=new FileInfo(path);
            if(!Path.GetFullPath(path).StartsWith(root,StringComparison.OrdinalIgnoreCase)||(info.Attributes&FileAttributes.ReparsePoint)!=0||info.Length>64_000||(total+=info.Length)>512_000)
                throw new InvalidOperationException("Knowledge import is limited to 64 KB per file and 512 KB total, with no links.");
            progress?.Invoke("Reading "+info.Name);
            await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,4096,FileOptions.Asynchronous|FileOptions.SequentialScan);
            var bytes=new byte[64_001];int read=0,n;
            while(read<bytes.Length&&(n=await stream.ReadAsync(bytes.AsMemory(read),ct))>0)read+=n;
            if(read>64_000)throw new InvalidOperationException("A file grew beyond the import limit.");
            if((actualTotal+=read)>512_000)throw new InvalidOperationException("The imported content grew beyond 512 KB.");
            try {
                var text=new UTF8Encoding(false,true).GetString(bytes,0,read).TrimStart('\uFEFF');
                if(text.Contains('\0'))throw new InvalidOperationException("Binary files are not supported.");
                var hash=Convert.ToHexString(SHA256.HashData(bytes.AsSpan(0,read)));
                documents.Add(new(info.Name,hash,text));
            } finally {CryptographicOperations.ZeroMemory(bytes);}
        }
        var revision=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n",documents.Select(d=>d.RelativePath+":"+d.Sha256)))));
        return new(Guid.NewGuid().ToString("N"),app,appVersion,root,revision,DateTimeOffset.UtcNow,documents);
    }
    public static IReadOnlyList<KnowledgeHit> Search(IEnumerable<KnowledgePack> packs,string app,string query,CancellationToken ct)
    {
        app=AppKey(app);Security.Text(query,1000,"Knowledge query");
        var words=Regex.Matches(query.ToLowerInvariant(),@"[\p{L}\p{N}_-]{2,}",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)).Select(m=>m.Value).Distinct().Take(20).ToArray();
        var hits=new List<(int Score,KnowledgeHit Hit)>();
        foreach(var pack in packs.Where(p=>p.App==app).TakeLast(3))foreach(var doc in pack.Documents) {
            var lines=doc.Text.Split('\n');
            for(int i=0;i<lines.Length;i++) {
                ct.ThrowIfCancellationRequested();string text=lines[i].Trim();int score=words.Count(w=>text.Contains(w,StringComparison.OrdinalIgnoreCase));
                if(score>0)hits.Add((score,new(pack.Id,pack.AppVersion,doc.RelativePath,pack.Revision,i+1,Security.Redact(text[..Math.Min(900,text.Length)]))));
            }
        }
        return hits.OrderByDescending(h=>h.Score).Take(5).Select(h=>h.Hit).ToArray();
    }
    public static void StorePack(BuddyState state,KnowledgePack pack)
    {
        state.Knowledge.RemoveAll(p=>p.App==pack.App&&p.SourceFolder.Equals(pack.SourceFolder,StringComparison.OrdinalIgnoreCase));
        if(state.Knowledge.Count>=10)throw new InvalidOperationException("Remove an old knowledge pack first (maximum 10).");
        state.Knowledge.Add(pack);
    }
}
