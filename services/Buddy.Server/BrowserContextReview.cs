using System.Security.Cryptography;
using System.Text.Json;

namespace Buddy.Server;

internal sealed class BrowserContextReview : IDisposable
{
    internal readonly string Id=Guid.NewGuid().ToString("N");
    internal readonly string Digest,OriginalHash,DraftHash,ComposerId;
    internal readonly long Created;
    internal long StagedAt;
    internal readonly BrowserContextBinding Binding;
    internal readonly IReadOnlyList<BrowserContextContent> Contents;
    internal string State="prepared";
    internal string? ReceiptDigest;
    internal IReadOnlyList<BrowserContextAttachmentState> ReceiptAttachments=Array.AsReadOnly(Array.Empty<BrowserContextAttachmentState>());
    internal CancellationTokenRegistration ContextRegistration;
    private readonly Dictionary<string,ContextOriginalAsset> assets=[];
    private readonly Dictionary<string,int> next=[];
    private byte[]? draft,original;
    internal BrowserContextReview(BrowserContextBinding binding,string composerId,string originalDraft,string exactDraft,
        IReadOnlyList<ContextOriginalAsset> originals,long created)
    {
        Binding=binding;ComposerId=composerId;Created=created;
        draft=DraftBytes(exactDraft);original=DraftBytes(originalDraft);DraftHash=BrowserContextProtocol.Sha256(draft);OriginalHash=BrowserContextProtocol.Sha256(original);
        var contents=new List<BrowserContextContent>{new("draft","draft","Reviewed draft","text/plain",draft.Length,DraftHash)};
        try{
            foreach(var asset in originals){
                if(asset is null)throw BrowserContextProtocol.Error("invalid_asset");
                asset.EnsureAvailable();
                if(asset.ByteCount>BrowserContextLimits.MaximumAssetBytes)throw BrowserContextProtocol.Error("asset_limit");
                string id="asset-"+assets.Count;var retained=asset.Retain();assets.Add(id,retained);
                contents.Add(new(id,"original",retained.Name,retained.MimeType,checked((int)retained.ByteCount),retained.Sha256));
            }
            Contents=Array.AsReadOnly(contents.ToArray());
            foreach(var c in Contents)next.Add(c.ContentId,0);
            Digest=BrowserContextProtocol.Sha256(JsonSerializer.SerializeToUtf8Bytes(new{Id,Binding,ComposerId,OriginalHash,DraftHash,Contents},BrowserContextProtocol.Json));
        }catch{Dispose();throw;}
    }
    internal static byte[] DraftBytes(string value)
    {
        if(value is null||value.Length>BrowserContextLimits.MaximumDraftCharacters)throw BrowserContextProtocol.Error("draft_limit");
        var bytes=BrowserContextProtocol.TextBytes(value);
        if(bytes.Length>BrowserContextLimits.MaximumDraftBytes){CryptographicOperations.ZeroMemory(bytes);throw BrowserContextProtocol.Error("draft_limit");}
        return bytes;
    }
    internal BrowserContextReviewSnapshot Snapshot()=>new(Id,Digest,State,OriginalHash,DraftHash,Contents,ReceiptDigest);
    internal BrowserContextDraftOffer Offer()
    {
        if(State!="approved")throw BrowserContextProtocol.Error("review_not_approved");
        State="transferring";return new(Id,Digest,OriginalHash,ComposerId,Contents);
    }
    internal BrowserContextChunkReply Chunk(BrowserContextChunkRead request)
    {
        if(request.ReviewId!=Id||request.Index<0)throw BrowserContextProtocol.Error("chunk_binding");
        bool undo=request.ContentId=="undo-original";
        if(undo?State!="undo-transferring":State!="transferring")throw BrowserContextProtocol.Error("chunk_phase");
        if(!next.TryGetValue(request.ContentId,out var expected)||request.Index!=expected)throw BrowserContextProtocol.Error("chunk_order");
        byte[] bytes;bool clearCopy=false;
        if(undo)bytes=original??throw BrowserContextProtocol.Error("review_closed");
        else if(request.ContentId=="draft")bytes=draft??throw BrowserContextProtocol.Error("review_closed");
        else if(assets.TryGetValue(request.ContentId,out var asset)){bytes=asset.CopyBytes();clearCopy=true;}
        else throw BrowserContextProtocol.Error("unknown_content");
        try{
            long offset=(long)request.Index*BrowserContextLimits.MaximumChunkBytes;
            if(offset>=bytes.Length&&!(bytes.Length==0&&offset==0))throw BrowserContextProtocol.Error("chunk_order");
            int count=Math.Min(BrowserContextLimits.MaximumChunkBytes,bytes.Length-(int)offset);
            string encoded=Convert.ToBase64String(bytes,(int)offset,count);next[request.ContentId]=expected+1;
            return new(Id,request.ContentId,request.Index,encoded,offset+count==bytes.Length);
        }finally{if(clearCopy)CryptographicOperations.ZeroMemory(bytes);}
    }
    internal void RecordState(BrowserContextDraftState input,long timestamp)
    {
        if(State!="transferring"||input.ReviewId!=Id||input.Digest!=Digest||input.ComposerId!=ComposerId)
            throw BrowserContextProtocol.Error("review_binding");
        if(input.State is not ("pending" or "staged" or "partial-or-unknown"))throw BrowserContextProtocol.Error("invalid_stage_state");
        if(input.State=="partial-or-unknown"){Cancel();return;}
        var attachments=ValidateAttachments(input.Attachments,input.State=="staged");
        if(attachments.Any(a=>a.State is "error" or "removed")){Cancel();return;}
        if(input.State=="pending")return;
        if(input.DraftSha256!=DraftHash||Contents.Any(c=>next[c.ContentId]!=Math.Max(1,(c.ByteLength+BrowserContextLimits.MaximumChunkBytes-1)/BrowserContextLimits.MaximumChunkBytes)))
            throw BrowserContextProtocol.Error("stage_not_confirmed");
        ReceiptAttachments=Array.AsReadOnly(attachments.ToArray());
        ReceiptDigest=BrowserContextProtocol.Sha256(JsonSerializer.SerializeToUtf8Bytes(new{Id,Digest,Binding,ComposerId,DraftHash,Attachments=ReceiptAttachments},BrowserContextProtocol.Json));
        State="staged";StagedAt=timestamp;ReleaseAssets();
    }
    private IReadOnlyList<BrowserContextAttachmentState> ValidateAttachments(IReadOnlyList<BrowserContextAttachmentState>? attachments,bool complete)
    {
        if(attachments is null||attachments.Count>BrowserContextLimits.MaximumAssets||attachments.Select(a=>a?.ContentId).Distinct(StringComparer.Ordinal).Count()!=attachments.Count)
            throw BrowserContextProtocol.Error("attachment_state");
        var expected=Contents.Where(c=>c.Role=="original").ToArray();
        if(complete&&attachments.Count!=expected.Length)throw BrowserContextProtocol.Error("attachment_not_confirmed");
        var receiptIds=new HashSet<string>(StringComparer.Ordinal);
        foreach(var a in attachments){
            if(a is null||a.State is not ("ready" or "pending" or "error" or "removed"))throw BrowserContextProtocol.Error("attachment_state");
            var e=expected.SingleOrDefault(x=>x.ContentId==a.ContentId);
            if(e is null||a.Name!=e.Name||a.MimeType!=e.MimeType||a.ByteLength!=e.ByteLength||a.Sha256!=e.Sha256)throw BrowserContextProtocol.Error("attachment_binding");
            if(complete&&a.State!="ready")throw BrowserContextProtocol.Error("attachment_not_confirmed");
            if(a.State=="ready"){
                BrowserContextProtocol.Id(a.ProviderAttachmentId);
                if(!receiptIds.Add(a.ProviderAttachmentId))throw BrowserContextProtocol.Error("attachment_receipt_reused");
            }
        }
        return attachments;
    }
    internal BrowserContextUndoOffer UndoOffer()
    {
        if(State!="undo-requested"||ReceiptDigest is null||original is null)throw BrowserContextProtocol.Error("undo_not_requested");
        State="undo-transferring";next["undo-original"]=0;
        return new(Id,ReceiptDigest,DraftHash,ComposerId,new("undo-original","undo-original","Exact original draft","text/plain",original.Length,OriginalHash),ReceiptAttachments);
    }
    internal void RecordUndo(BrowserContextUndoState input)
    {
        if(State!="undo-transferring"||input.ReviewId!=Id||input.ReceiptDigest!=ReceiptDigest||input.ComposerId!=ComposerId||input.DraftSha256!=OriginalHash||original is null||
            next["undo-original"]!=Math.Max(1,(original.Length+BrowserContextLimits.MaximumChunkBytes-1)/BrowserContextLimits.MaximumChunkBytes))throw BrowserContextProtocol.Error("undo_not_confirmed");
        var attachments=ValidateAttachments(input.Attachments,true);
        if(attachments.Count!=ReceiptAttachments.Count||attachments.Any(a=>!ReceiptAttachments.Contains(a)))throw BrowserContextProtocol.Error("undo_attachment_changed");
        State="text-restored";ReleaseBytes();
    }
    internal void Cancel(bool expired=false)
    {
        if(State is "text-restored" or "cancelled" or "expired" or "partial-or-unknown" or "staged-expired")return;
        State=State switch{"transferring" or "undo-transferring"=>"partial-or-unknown","staged" or "undo-requested"=>expired?"staged-expired":"partial-or-unknown",_=>expired?"expired":"cancelled"};
        ReleaseBytes();
    }
    private void ReleaseAssets(){foreach(var a in assets.Values)a.Dispose();assets.Clear();}
    private void ReleaseBytes()
    {
        ReleaseAssets();if(draft is not null){CryptographicOperations.ZeroMemory(draft);draft=null;}
        if(original is not null){CryptographicOperations.ZeroMemory(original);original=null;}
        ContextRegistration.Unregister();
    }
    public void Dispose(){Cancel();ReleaseBytes();}
}
