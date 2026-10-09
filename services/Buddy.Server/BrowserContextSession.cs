using System.Security.Cryptography;

namespace Buddy.Server;

/// <summary>Pure memory-only state and authorization. Transports assign peer IDs,
/// enforce their own sender/frame checks and CurrentUserOnly pipe policy. This
/// broker has no browser, path, network, model, process or native operations.</summary>
public sealed class BrowserContextBroker : IDisposable
{
    private sealed class Connection(string peer,BrowserContextRegistration input,BrowserContextProviderProfile? profile,long created)
    {
        internal readonly string Peer=peer,Id=Guid.NewGuid().ToString("N"),Nonce=Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant(),Challenge=Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
        internal readonly BrowserContextRegistration Input=input;
        internal readonly BrowserContextProviderProfile? Profile=profile;
        internal readonly long Created=created;
        internal long Sequence;
        internal string Status="awaiting-pairing",Result="pairing_required";
        internal BrowserContextBinding? Binding;
        internal BrowserContextIdentityEvidence? Evidence;
        internal BrowserContextReadiness? Readiness;
        internal BrowserContextTransfer? Transfer;
        internal BrowserContextCapturedHistory? History;
        internal BrowserContextReview? Review;
    }
    private static readonly BrowserContextCapabilities None=new(false,false,false,false,false);
    private readonly object gate=new();
    private readonly Dictionary<string,Connection> connections=new(StringComparer.Ordinal);
    private readonly Dictionary<string,BrowserContextProviderProfile> profiles=new(StringComparer.Ordinal);
    private readonly TimeProvider time;
    private bool disposed,clockObserved;
    private long lastTimestamp;
    private DateTimeOffset lastWall;
    public BrowserContextBroker(IEnumerable<BrowserContextProviderProfile>? profiles=null,TimeProvider? timeProvider=null)
    {
        time=timeProvider??TimeProvider.System;
        foreach(var profile in profiles??[]){ValidateProfile(profile);if(!this.profiles.TryAdd(profile.ProviderId,profile))throw BrowserContextProtocol.Error("duplicate_profile");}
    }
    public BrowserContextRegistrationReply Register(string transportPeerId,BrowserContextRegistration registration)
    {
        lock(gate){
            Alive();Tick();BrowserContextProtocol.Id(transportPeerId);ValidateRegistration(registration);
            foreach(var old in connections.Values.Where(c=>c.Peer==transportPeerId).ToArray())Terminate(old,"disconnected","replaced_connection");
            foreach(var id in connections.Where(x=>Terminal(x.Value)).Select(x=>x.Key).ToArray())connections.Remove(id);
            if(connections.Count>=BrowserContextLimits.MaximumConnections)throw BrowserContextProtocol.Error("connection_limit");
            profiles.TryGetValue(registration.ProviderId,out var profile);
            if(profile is null?(registration.ProviderId!="chatgpt"||registration.Origin!="https://chatgpt.com"):registration.Origin!=profile.Origin)
                throw BrowserContextProtocol.Error("provider_unavailable");
            var c=new Connection(transportPeerId,registration,profile,lastTimestamp);connections.Add(c.Id,c);
            return new(1,"registered",c.Id,c.Nonce,c.Challenge,c.Status);
        }
    }
    public void ConfirmPairing(string connectionId,string pairingChallenge,bool approved)
    {
        lock(gate){Alive();Tick();var c=Get(connectionId);
            if(c.Status!="awaiting-pairing"||!BrowserContextProtocol.EqualSecret(c.Challenge,pairingChallenge))throw BrowserContextProtocol.Error("pairing_changed");
            if(!approved){Terminate(c,"cancelled","pairing_declined");return;}
            c.Status=c.Profile is null?"readiness-only":"paired";c.Result=c.Profile is null?"identity_profile_unavailable":"paired";
        }
    }
    public IReadOnlyList<BrowserContextSnapshot> Snapshots
    {
        get{lock(gate){Alive();Tick();return Array.AsReadOnly(connections.Values.Select(Snapshot).ToArray());}}
    }
    public byte[] HandleUtf8(string transportPeerId,ReadOnlyMemory<byte> frame)
    {
        lock(gate){
            Connection? authenticated=null;BrowserContextEnvelope? envelope=null;
            try{
                Alive();Tick();envelope=BrowserContextProtocol.ReadEnvelope(frame);
                if(envelope.Version!=1||!connections.TryGetValue(envelope.ConnectionId??"",out var c)||c.Peer!=transportPeerId||!BrowserContextProtocol.EqualSecret(c.Nonce,envelope.Nonce))
                    throw BrowserContextProtocol.Error("session_mismatch");
                authenticated=c;
                if(Terminal(c))throw BrowserContextProtocol.Error("session_closed");
                if(envelope.Sequence!=c.Sequence+1||envelope.Sequence>BrowserContextLimits.MaximumSequence)throw BrowserContextProtocol.Error("sequence_mismatch");
                c.Sequence=envelope.Sequence;ValidateTransportBinding(c,envelope.Binding);
                if(envelope.Kind is "invalidate" or "disconnect"){
                    var invalid=BrowserContextProtocol.Payload<BrowserContextInvalidate>(envelope.Payload);
                    if(invalid.Reason is not ("navigation" or "account-change" or "workspace-change" or "chat-change" or "document-change" or "user-cancel" or "port-closed"))throw BrowserContextProtocol.Error("invalid_reason");
                    Terminate(c,envelope.Kind=="disconnect"?"disconnected":"cancelled",invalid.Reason);return Reply(c,envelope,"ok",null,null);
                }
                if(c.Binding is not null&&envelope.Binding!=c.Binding)throw BrowserContextProtocol.Error("identity_changed");
                if(envelope.Kind=="readiness.report"){
                    var r=BrowserContextProtocol.Payload<BrowserContextReadiness>(envelope.Payload);
                    if(r.ComposerCount is <0 or >128||r.RenderedUserCount is <0 or >10000||r.RenderedAssistantCount is <0 or >10000)throw BrowserContextProtocol.Error("invalid_readiness");
                    if(c.Binding is null&&!EmptyIdentity(envelope.Binding))throw BrowserContextProtocol.Error("readiness_identity_forbidden");
                    c.Readiness=r;return Reply(c,envelope,"ok",null,new BrowserContextReadinessReply(c.Status,c.Profile is not null,c.Profile?.Capabilities??None));
                }
                if(c.Status=="awaiting-pairing")throw BrowserContextProtocol.Error("pairing_required");
                if(c.Profile is null)throw BrowserContextProtocol.Error("identity_profile_unavailable");
                if(envelope.Kind=="identity.bind"){
                    if(c.Binding is not null)throw BrowserContextProtocol.Error("identity_already_bound");
                    var input=BrowserContextProtocol.Payload<BrowserContextIdentityBind>(envelope.Payload);ValidateIdentity(c,envelope.Binding,input.Evidence);
                    c.Binding=envelope.Binding;c.Evidence=input.Evidence;c.Status="bound";c.Result="identity_bound";
                    return Reply(c,envelope,"ok",null,new BrowserContextReadinessReply(c.Status,true,c.Profile.Capabilities));
                }
                if(c.Binding is null)throw BrowserContextProtocol.Error("identity_required");
                return Dispatch(c,envelope);
            }catch(BrowserContextProtocolException error){
                if(authenticated is not null&&error.Code is not ("pairing_required" or "identity_profile_unavailable" or "capability_unavailable" or "session_closed"))
                    Terminate(authenticated,"cancelled",error.Code);
                return BrowserContextProtocol.Serialize(new BrowserContextReply(1,authenticated?.Id??"",envelope?.Sequence??0,ReplyKind(envelope?.Kind),"refused",error.Code,null));
            }catch(Exception error)when(error is ArgumentException or ObjectDisposedException or OverflowException or FormatException){
                if(authenticated is not null)Terminate(authenticated,"cancelled","invalid_request");
                return BrowserContextProtocol.Serialize(new BrowserContextReply(1,authenticated?.Id??"",envelope?.Sequence??0,"invalid","refused","invalid_request",null));
            }
        }
    }
    private static string ReplyKind(string? kind)=>kind is "readiness.report" or "identity.bind" or "history.begin" or "history.chunk" or "history.end" or
        "draft.poll" or "draft.chunk.read" or "draft.state" or "undo.poll" or "undo.state" or "invalidate" or "disconnect" ? kind : "invalid";
    public BrowserContextReviewSnapshot PreviewDraft(string connectionId,BrowserContextBinding binding,string originalDraft,string exactDraft,
        IReadOnlyList<ContextOriginalAsset> originals,CancellationToken contextInvalidated=default)
    {
        lock(gate){Alive();Tick();contextInvalidated.ThrowIfCancellationRequested();var c=GetBound(connectionId);
            if(!c.Profile!.Capabilities.ReplaceDraft)throw BrowserContextProtocol.Error("capability_unavailable");
            if(binding!=c.Binding||c.History is null||originalDraft!=c.History.OriginalDraft||c.History.ComposerId!=c.Evidence!.ComposerId)throw BrowserContextProtocol.Error("captured_original_mismatch");
            if(originals is null||originals.Count>BrowserContextLimits.MaximumAssets||originals.Any(a=>a is null)||originals.Sum(a=>a.ByteCount)>BrowserContextLimits.MaximumAssetTotalBytes)
                throw BrowserContextProtocol.Error("asset_limit");
            if(originals.Count>0&&(!c.Profile.Capabilities.StageOriginalFiles||!c.Profile.Capabilities.ObserveReadyAttachments||originals.Any(a=>!c.Profile.OriginalMimeTypes.Contains(a.MimeType,StringComparer.Ordinal))))
                throw BrowserContextProtocol.Error("capability_unavailable");
            if(c.Review is {State:"transferring" or "staged" or "undo-requested" or "undo-transferring"})throw BrowserContextProtocol.Error("review_in_progress");
            c.Review?.Dispose();var review=new BrowserContextReview(binding,c.History.ComposerId,originalDraft,exactDraft,originals,lastTimestamp);c.Review=review;
            review.ContextRegistration=contextInvalidated.Register(()=>Cancel(connectionId));
            if(contextInvalidated.IsCancellationRequested||Terminal(c)){review.Dispose();throw BrowserContextProtocol.Error("context_invalidated");}
            c.Result="draft_prepared_for_review";return review.Snapshot();
        }
    }
    public void ApproveDraft(string connectionId,string reviewId,string digest,bool allowOriginalTransfer)
    {
        lock(gate){Alive();Tick();var c=GetBound(connectionId);var r=Review(c,reviewId);
            if(r.State!="prepared"||r.Digest!=digest)throw BrowserContextProtocol.Error("review_changed");
            if(r.Contents.Any(x=>x.Role=="original")&&!allowOriginalTransfer)throw BrowserContextProtocol.Error("original_transfer_not_approved");
            r.State="approved";c.Result="draft_transfer_approved";
        }
    }
    public void RequestUndo(string connectionId,string reviewId,string receiptDigest)
    {
        lock(gate){Alive();Tick();var c=GetBound(connectionId);var r=Review(c,reviewId);
            if(!c.Profile!.Capabilities.TextUndo)throw BrowserContextProtocol.Error("capability_unavailable");
            if(r.State!="staged"||r.ReceiptDigest!=receiptDigest||time.GetElapsedTime(r.StagedAt,lastTimestamp)>=BrowserContextLimits.UndoLifetime)
                throw BrowserContextProtocol.Error("undo_unavailable");
            r.State="undo-requested";c.Result="text_undo_requested";
        }
    }
    public void Cancel(string connectionId){lock(gate){if(connections.TryGetValue(connectionId,out var c))Terminate(c,"cancelled","user_cancelled");}}
    public void DisconnectPeer(string transportPeerId){lock(gate){foreach(var c in connections.Values.Where(c=>c.Peer==transportPeerId))Terminate(c,"disconnected","port_closed");}}
    public void Dispose(){lock(gate){if(disposed)return;disposed=true;foreach(var c in connections.Values)Terminate(c,"disconnected","broker_closed");connections.Clear();}}

    private byte[] Dispatch(Connection c,BrowserContextEnvelope e)
    {
        switch(e.Kind){
            case "history.begin":{
                if(!c.Profile!.Capabilities.CaptureCompleteHistory)throw BrowserContextProtocol.Error("capability_unavailable");
                if(c.History is not null||c.Transfer is not null||c.Review is not null)throw BrowserContextProtocol.Error("capture_already_started");
                var b=BrowserContextProtocol.Payload<BrowserContextHistoryBegin>(e.Payload);ValidateCoverage(c,b);
                c.Transfer=new(b);return Reply(c,e,"ok",null,null);
            }
            case "history.chunk":c.Transfer?.Add(BrowserContextProtocol.Payload<BrowserContextChunk>(e.Payload));if(c.Transfer is null)throw BrowserContextProtocol.Error("transfer_missing");return Reply(c,e,"ok",null,null);
            case "history.end":{
                var end=BrowserContextProtocol.Payload<BrowserContextTransferEnd>(e.Payload);var t=c.Transfer??throw BrowserContextProtocol.Error("transfer_missing");
                try{var data=t.Complete(end.TransferId);ValidateHistory(c,t.Begin,data);
                    c.History=new(t.Begin.Sha256,t.Begin.Coverage,Array.AsReadOnly(data.Pairs.ToArray()),data.OriginalDraft,data.ComposerId,BrowserContextProtocol.TextSha256(data.OriginalDraft));
                    c.Result="complete_history_captured";return Reply(c,e,"ok",null,new BrowserContextHistoryReceipt(c.History.Pairs.Count,c.History.Sha256,c.History.OriginalDraftSha256,c.History.ComposerId));
                }finally{t.Dispose();c.Transfer=null;}
            }
            case "draft.poll":{
                _=BrowserContextProtocol.Payload<BrowserContextEmpty>(e.Payload);
                if(c.Review?.State!="approved")return Reply(c,e,"waiting",null,null);
                var offer=c.Review.Offer();c.Result="draft_transfer_started";return Reply(c,e,"ok",null,offer);
            }
            case "draft.chunk.read":{
                var read=BrowserContextProtocol.Payload<BrowserContextChunkRead>(e.Payload);return Reply(c,e,"ok",null,Review(c,read.ReviewId).Chunk(read));
            }
            case "draft.state":{
                var state=BrowserContextProtocol.Payload<BrowserContextDraftState>(e.Payload);Review(c,state.ReviewId).RecordState(state,lastTimestamp);
                c.Result=c.Review!.State=="staged"?"draft_and_originals_confirmed":c.Review.State=="partial-or-unknown"?"possible_partial_effects":"attachments_pending";
                return Reply(c,e,"ok",null,c.Review.Snapshot());
            }
            case "undo.poll":{
                _=BrowserContextProtocol.Payload<BrowserContextEmpty>(e.Payload);if(c.Review?.State!="undo-requested")return Reply(c,e,"waiting",null,null);
                return Reply(c,e,"ok",null,c.Review.UndoOffer());
            }
            case "undo.state":{
                var state=BrowserContextProtocol.Payload<BrowserContextUndoState>(e.Payload);Review(c,state.ReviewId).RecordUndo(state);c.Result="text_restored_originals_remain";
                return Reply(c,e,"ok",null,c.Review!.Snapshot());
            }
            default:throw BrowserContextProtocol.Error("unsupported_operation");
        }
    }
    private static BrowserContextReview Review(Connection c,string reviewId)=>c.Review is {} r&&r.Id==reviewId?r:throw BrowserContextProtocol.Error("review_missing");
    private static bool EmptyIdentity(BrowserContextBinding b)=>b.AccountId==""&&b.WorkspaceId==""&&b.ConversationId=="";
    private static void ValidateTransportBinding(Connection c,BrowserContextBinding? b)
    {
        if(b is null||b.ProviderId!=c.Input.ProviderId||b.TabId!=c.Input.TabId||b.DocumentId!=c.Input.DocumentId||b.Generation!=c.Input.Generation)
            throw BrowserContextProtocol.Error("document_changed");
    }
    private static void ValidateIdentity(Connection c,BrowserContextBinding b,BrowserContextIdentityEvidence? e)
    {
        BrowserContextProtocol.Id(b.AccountId);BrowserContextProtocol.Id(b.WorkspaceId);BrowserContextProtocol.Id(b.ConversationId);
        var expected=c.Profile!.Evidence;
        if(e is null||e.AccountSignal!=expected.AccountSignal||e.WorkspaceSignal!=expected.WorkspaceSignal||e.ConversationSignal!=expected.ConversationSignal||
            e.HistorySignal!=expected.HistorySignal||e.CapabilityRevision!=expected.CapabilityRevision)throw BrowserContextProtocol.Error("identity_evidence_unavailable");
        BrowserContextProtocol.Id(e.ComposerId);
    }
    private static void ValidateCoverage(Connection c,BrowserContextHistoryBegin b)
    {
        if(b.Coverage is not {} p||!p.Complete||p.HasEarlier||p.HasLater||p.Signal!=c.Profile!.Evidence.HistorySignal)throw BrowserContextProtocol.Error("complete_history_unproven");
        if(b.PairCount==0){if(p.FirstMessageId!=""||p.LastMessageId!="")throw BrowserContextProtocol.Error("coverage_bounds");}
        else{BrowserContextProtocol.Id(p.FirstMessageId);BrowserContextProtocol.Id(p.LastMessageId);}
    }
    private static void ValidateHistory(Connection c,BrowserContextHistoryBegin b,BrowserContextHistoryDocument data)
    {
        if(data.Pairs is null||data.Pairs.Count!=b.PairCount||data.ComposerId!=c.Evidence!.ComposerId)throw BrowserContextProtocol.Error("history_binding");
        var draft=BrowserContextReview.DraftBytes(data.OriginalDraft);CryptographicOperations.ZeroMemory(draft);
        var ids=new HashSet<string>(StringComparer.Ordinal);
        foreach(var pair in data.Pairs){
            if(pair is null)throw BrowserContextProtocol.Error("invalid_history");BrowserContextProtocol.Id(pair.UserId);BrowserContextProtocol.Id(pair.AssistantId);
            if(!ids.Add(pair.UserId)||!ids.Add(pair.AssistantId)||string.IsNullOrWhiteSpace(pair.UserText)||string.IsNullOrWhiteSpace(pair.AssistantText))throw BrowserContextProtocol.Error("unsupported_history_content");
            _=BrowserContextProtocol.TextBytes(pair.UserText);_=BrowserContextProtocol.TextBytes(pair.AssistantText);
        }
        if(data.Pairs.Count>0&&(data.Pairs[0].UserId!=b.Coverage.FirstMessageId||data.Pairs[^1].AssistantId!=b.Coverage.LastMessageId))throw BrowserContextProtocol.Error("coverage_bounds");
    }
    private static void ValidateRegistration(BrowserContextRegistration? r)
    {
        if(r is null||r.Version!=1||r.Kind!="register"||r.TabId<0||r.TabId>int.MaxValue||r.FrameId!=0)throw BrowserContextProtocol.Error("invalid_registration");
        BrowserContextProtocol.Id(r.ProviderId,80);BrowserContextProtocol.Id(r.DocumentId);BrowserContextProtocol.Id(r.Generation);
        if(r.Origin is null||!Uri.TryCreate(r.Origin,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.UserInfo.Length!=0||uri.Port!=443||uri.AbsolutePath!="/"||uri.Query.Length!=0||uri.Fragment.Length!=0||r.Origin!=uri.GetLeftPart(UriPartial.Authority))
            throw BrowserContextProtocol.Error("invalid_origin");
    }
    private static void ValidateProfile(BrowserContextProviderProfile? p)
    {
        if(p is null||p.Evidence is null||p.Capabilities is null)throw BrowserContextProtocol.Error("invalid_profile");
        ValidateRegistration(new(1,"register",p.ProviderId,p.Origin,0,0,"profile","profile"));
        BrowserContextProtocol.Id(p.Evidence.AccountSignal);BrowserContextProtocol.Id(p.Evidence.WorkspaceSignal);BrowserContextProtocol.Id(p.Evidence.ConversationSignal);BrowserContextProtocol.Id(p.Evidence.CapabilityRevision);
        if(p.Capabilities.CaptureCompleteHistory)BrowserContextProtocol.Id(p.Evidence.HistorySignal);
        if(p.OriginalMimeTypes.Count>4||p.OriginalMimeTypes.Any(m=>m is not ("text/plain" or "text/markdown" or "image/png" or "image/jpeg")))throw BrowserContextProtocol.Error("invalid_profile");
    }
    private Connection Get(string id)=>connections.TryGetValue(id,out var c)&&!Terminal(c)?c:throw BrowserContextProtocol.Error("session_closed");
    private Connection GetBound(string id){var c=Get(id);if(c.Binding is null||c.Profile is null||c.Status!="bound")throw BrowserContextProtocol.Error("identity_required");return c;}
    private static bool Terminal(Connection c)=>c.Status is "cancelled" or "disconnected" or "expired";
    private static BrowserContextSnapshot Snapshot(Connection c)=>new(c.Id,c.Input.ProviderId,c.Input.Origin,c.Input.TabId,c.Input.DocumentId,c.Input.Generation,c.Status,c.Challenge,c.Profile is not null,c.Profile?.Capabilities??None,c.Binding,c.Readiness,c.History,c.Review?.Snapshot(),c.Result);
    private static byte[] Reply(Connection c,BrowserContextEnvelope e,string status,string? code,object? payload)=>BrowserContextProtocol.Serialize(new BrowserContextReply(1,c.Id,e.Sequence,e.Kind,status,code,payload));
    private static void Terminate(Connection c,string status,string result)
    {
        if(Terminal(c))return;c.Review?.Cancel();c.Transfer?.Dispose();c.Transfer=null;c.History=null;c.Binding=null;c.Evidence=null;c.Status=status;c.Result=result;
    }
    private void Tick()
    {
        long stamp=time.GetTimestamp();var wall=time.GetUtcNow();
        if(clockObserved&&(stamp<lastTimestamp||wall<lastWall)){
            foreach(var c in connections.Values)Terminate(c,"expired","clock_changed");throw BrowserContextProtocol.Error("clock_changed");
        }
        clockObserved=true;lastTimestamp=stamp;lastWall=wall;
        foreach(var c in connections.Values){
            if(Terminal(c))continue;
            if(time.GetElapsedTime(c.Created,stamp)>=(c.Status=="awaiting-pairing"?BrowserContextLimits.PairingLifetime:BrowserContextLimits.SessionLifetime)){Terminate(c,"expired","session_expired");continue;}
            if(c.Review is {} r&&time.GetElapsedTime(r.Created,stamp)>=BrowserContextLimits.ReviewLifetime)r.Cancel(expired:true);
        }
    }
    private void Alive(){if(disposed)throw BrowserContextProtocol.Error("broker_closed");}
}
