using System.Security.Cryptography;

namespace Buddy.Server;

// Ordered, bounded transfer. It owns no transport and never fetches a path/URL.
internal sealed class BrowserContextTransfer : IDisposable
{
    internal readonly BrowserContextHistoryBegin Begin;
    private readonly byte[] bytes;
    private int position,index;
    private bool disposed,finished;
    internal BrowserContextTransfer(BrowserContextHistoryBegin begin)
    {
        BrowserContextProtocol.Id(begin.TransferId,80);BrowserContextProtocol.Digest(begin.Sha256);
        if(begin.ByteLength<1||begin.ByteLength>BrowserContextLimits.MaximumHistoryBytes||begin.PairCount<0||begin.PairCount>BrowserContextLimits.MaximumHistoryPairs)
            throw BrowserContextProtocol.Error("history_limit");
        Begin=begin;bytes=new byte[begin.ByteLength];
    }
    internal void Add(BrowserContextChunk chunk)
    {
        if(disposed||finished||chunk.TransferId!=Begin.TransferId||chunk.Index!=index||chunk.DataBase64 is null||chunk.DataBase64.Length>4*((BrowserContextLimits.MaximumChunkBytes+2)/3))
            throw BrowserContextProtocol.Error("chunk_order");
        byte[] part;
        try{part=Convert.FromBase64String(chunk.DataBase64);}catch(FormatException){throw BrowserContextProtocol.Error("chunk_encoding");}
        try{
            if(part.Length==0||part.Length>BrowserContextLimits.MaximumChunkBytes||position+part.Length>bytes.Length||
                chunk.DataBase64!=Convert.ToBase64String(part))throw BrowserContextProtocol.Error("chunk_limit");
            int expected=Math.Min(BrowserContextLimits.MaximumChunkBytes,bytes.Length-position);
            if(part.Length!=expected)throw BrowserContextProtocol.Error("chunk_size");
            part.CopyTo(bytes,position);position+=part.Length;index++;
        }finally{CryptographicOperations.ZeroMemory(part);}
    }
    internal BrowserContextHistoryDocument Complete(string transferId)
    {
        if(disposed||finished||transferId!=Begin.TransferId||position!=bytes.Length||BrowserContextProtocol.Sha256(bytes)!=Begin.Sha256)
            throw BrowserContextProtocol.Error("transfer_integrity");
        finished=true;
        return BrowserContextProtocol.Read<BrowserContextHistoryDocument>(bytes,BrowserContextLimits.MaximumHistoryBytes);
    }
    public void Dispose(){if(disposed)return;disposed=true;CryptographicOperations.ZeroMemory(bytes);}
}
