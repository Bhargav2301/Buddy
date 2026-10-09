using System.Security.Cryptography;

namespace Buddy.Server;

public sealed record ContextOriginalAssetInfo(string Name,string MimeType,long ByteCount,string Sha256);

/// <summary>
/// One disposable lease on an immutable, bounded, memory-only original. Creation
/// copies caller bytes; neither this type nor its metadata uploads or writes them.
/// Format decoding and image dimension checks remain the selected-file reader's job.
/// </summary>
public sealed class ContextOriginalAsset : IDisposable
{
    public const int MaximumTextBytes=64*1024,MaximumImageBytes=2_000_000;
    private sealed class Shared(byte[] bytes)
    {
        internal readonly object Gate=new();
        internal byte[]? Bytes=bytes;
        internal int References=1;
    }
    private readonly Shared shared;
    private bool disposed;
    public ContextOriginalAssetInfo Info {get;}
    public string Name=>Info.Name;
    public string MimeType=>Info.MimeType;
    public long ByteCount=>Info.ByteCount;
    public string Sha256=>Info.Sha256;
    private ContextOriginalAsset(Shared shared,ContextOriginalAssetInfo info){this.shared=shared;Info=info;}
    public static ContextOriginalAsset CreateCopy(byte[] bytes,string displayName,string mimeType)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        RefinementWorkspace.ValidateText(displayName,160,"Original asset name");
        if(displayName is "." or ".."||displayName.IndexOfAny(['\\','/'])>=0||displayName.Any(char.IsControl))
            throw new InvalidOperationException("Use an original asset display name without paths or controls.");
        int limit=mimeType switch {
            "text/plain" or "text/markdown"=>MaximumTextBytes,
            "image/png" or "image/jpeg"=>MaximumImageBytes,
            _=>throw new InvalidOperationException("This original asset format is not supported.")
        };
        if(bytes.Length==0||bytes.Length>limit)throw new InvalidOperationException("The original asset exceeds its supported byte limit.");
        var owned=bytes.ToArray();
        try {
            var info=new ContextOriginalAssetInfo(displayName,mimeType,owned.LongLength,Convert.ToHexString(SHA256.HashData(owned)).ToLowerInvariant());
            return new(new(owned),info);
        }catch{CryptographicOperations.ZeroMemory(owned);throw;}
    }
    public void EnsureAvailable(){lock(shared.Gate)CheckAvailable();}
    public ContextOriginalAsset Retain()
    {
        lock(shared.Gate){CheckAvailable();shared.References=checked(shared.References+1);return new(shared,Info);}
    }
    public byte[] CopyBytes(){lock(shared.Gate){CheckAvailable();return shared.Bytes!.ToArray();}}
    public void Dispose()
    {
        lock(shared.Gate){
            if(disposed)return;disposed=true;
            if(--shared.References==0){CryptographicOperations.ZeroMemory(shared.Bytes!);shared.Bytes=null;}
        }
    }
    private void CheckAvailable(){if(disposed||shared.Bytes is null)throw new ObjectDisposedException(nameof(ContextOriginalAsset));}
    public override string ToString()=>$"ContextOriginalAsset {{ {Name}, {MimeType}, {ByteCount} bytes, retained locally }}";
}
