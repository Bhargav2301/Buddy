using System.Text.Json;

namespace Buddy.Server;

public static class BrowserContextLimits
{
    public const int Version=1,MaximumFrameBytes=65_536,MaximumChunkBytes=32_768;
    public const int MaximumDraftCharacters=20_000,MaximumDraftBytes=65_536;
    public const int MaximumHistoryBytes=4*1024*1024,MaximumHistoryPairs=256;
    public const int MaximumAssets=8,MaximumAssetBytes=2_000_000,MaximumAssetTotalBytes=16_000_000;
    public const int MaximumConnections=4,MaximumSequence=4096;
    public static readonly TimeSpan PairingLifetime=TimeSpan.FromMinutes(2),SessionLifetime=TimeSpan.FromMinutes(30),ReviewLifetime=TimeSpan.FromMinutes(2),UndoLifetime=TimeSpan.FromSeconds(30);
}

// Registration is unpaired and carries no account identifiers, content or files.
// transportPeerId is a separate, host-owned argument and never accepted from JSON.
public sealed record BrowserContextRegistration(int Version,string Kind,string ProviderId,string Origin,long TabId,int FrameId,string DocumentId,string Generation);
public sealed record BrowserContextRegistrationReply(int Version,string Kind,string ConnectionId,string Nonce,string PairingChallenge,string Status)
{
    public override string ToString()=>"BrowserContextRegistrationReply { pairing = [private] }";
}
public sealed record BrowserContextBinding(string ProviderId,long TabId,string DocumentId,string Generation,string AccountId,string WorkspaceId,string ConversationId)
{
    public override string ToString()=>"BrowserContextBinding { identity = [private] }";
}
public sealed record BrowserContextEnvelope(int Version,string ConnectionId,string Nonce,long Sequence,string Kind,BrowserContextBinding Binding,JsonElement Payload)
{
    public override string ToString()=>"BrowserContextEnvelope { private session payload }";
}
public sealed record BrowserContextReply(int Version,string ConnectionId,long Sequence,string Kind,string Status,string? Code,object? Payload);

// Structural data only. No arbitrary free-form diagnostic text can carry history
// through production readiness mode.
public sealed record BrowserContextReadiness(int ComposerCount,int RenderedUserCount,int RenderedAssistantCount,
    bool IdentitySignalAvailable,bool CompleteHistorySignalAvailable,bool AttachmentReceiptSignalAvailable);
public sealed record BrowserContextReadinessReply(string Status,bool ProfileAdmitted,BrowserContextCapabilities Capabilities);
public sealed record BrowserContextHistoryReceipt(int PairCount,string HistorySha256,string OriginalDraftSha256,string ComposerId);
public sealed record BrowserContextIdentityEvidence(string AccountSignal,string WorkspaceSignal,string ConversationSignal,
    string HistorySignal,string ComposerId,string CapabilityRevision);
public sealed record BrowserContextIdentityBind(BrowserContextIdentityEvidence Evidence);
public sealed record BrowserContextCapabilities(bool CaptureCompleteHistory,bool ReplaceDraft,bool TextUndo,bool StageOriginalFiles,bool ObserveReadyAttachments);
public sealed record BrowserContextCoverage(bool Complete,bool HasEarlier,bool HasLater,string Signal,string FirstMessageId,string LastMessageId);
public sealed record BrowserContextHistoryBegin(string TransferId,int ByteLength,string Sha256,int PairCount,BrowserContextCoverage Coverage);
public sealed record BrowserContextChunk(string TransferId,int Index,string DataBase64);
public sealed record BrowserContextTransferEnd(string TransferId);
public sealed record BrowserContextHistoryPair(string UserId,string UserText,string AssistantId,string AssistantText)
{
    public override string ToString()=>"BrowserContextHistoryPair { completed messages = [private] }";
}
public sealed record BrowserContextHistoryDocument(IReadOnlyList<BrowserContextHistoryPair> Pairs,string OriginalDraft,string ComposerId);
public sealed record BrowserContextCapturedHistory(string Sha256,BrowserContextCoverage Coverage,IReadOnlyList<BrowserContextHistoryPair> Pairs,
    string OriginalDraft,string ComposerId,string OriginalDraftSha256)
{
    public override string ToString()=>"BrowserContextCapturedHistory { selected chat = [private] }";
}

public sealed record BrowserContextContent(string ContentId,string Role,string Name,string MimeType,int ByteLength,string Sha256);
public sealed record BrowserContextDraftOffer(string ReviewId,string Digest,string OriginalDraftSha256,string ComposerId,
    IReadOnlyList<BrowserContextContent> Contents);
public sealed record BrowserContextChunkRead(string ReviewId,string ContentId,int Index);
public sealed record BrowserContextChunkReply(string ReviewId,string ContentId,int Index,string DataBase64,bool Last);
public sealed record BrowserContextAttachmentState(string ContentId,string Name,string MimeType,int ByteLength,string Sha256,
    string State,string ProviderAttachmentId);
public sealed record BrowserContextDraftState(string ReviewId,string Digest,string DraftSha256,string ComposerId,
    string State,IReadOnlyList<BrowserContextAttachmentState> Attachments);
public sealed record BrowserContextUndoOffer(string ReviewId,string ReceiptDigest,string CurrentDraftSha256,string ComposerId,
    BrowserContextContent OriginalDraft,IReadOnlyList<BrowserContextAttachmentState> Attachments);
public sealed record BrowserContextUndoState(string ReviewId,string ReceiptDigest,string DraftSha256,string ComposerId,
    IReadOnlyList<BrowserContextAttachmentState> Attachments);
public sealed record BrowserContextEmpty();
public sealed record BrowserContextInvalidate(string Reason);
public sealed record BrowserContextReviewSnapshot(string ReviewId,string Digest,string State,string OriginalDraftSha256,
    string DraftSha256,IReadOnlyList<BrowserContextContent> Contents,string? ReceiptDigest);
public sealed record BrowserContextSnapshot(string ConnectionId,string ProviderId,string Origin,long TabId,string DocumentId,
    string Generation,string Status,string PairingChallenge,bool ProfileAdmitted,BrowserContextCapabilities Capabilities,BrowserContextBinding? Binding,
    BrowserContextReadiness? Readiness,BrowserContextCapturedHistory? History,BrowserContextReviewSnapshot? Review,string ResultCode)
{
    public override string ToString()=>"BrowserContextSnapshot { private paired session }";
}

/// <summary>
/// A host-owned profile, never deserialized from wire messages, page data or user
/// storage. Production constructs BrowserContextBroker without profiles until a
/// provider's exact identity/history/composer/receipt contract is characterized.
/// Fixtures inject a profile explicitly; its declarations are not live acceptance.
/// </summary>
public sealed class BrowserContextProviderProfile
{
    public string ProviderId {get;}
    public string Origin {get;}
    public BrowserContextIdentityEvidence Evidence {get;}
    public BrowserContextCapabilities Capabilities {get;}
    public IReadOnlyList<string> OriginalMimeTypes {get;}
    public BrowserContextProviderProfile(string providerId,string origin,BrowserContextIdentityEvidence evidence,IEnumerable<string> originalMimeTypes,
        BrowserContextCapabilities capabilities)
    {
        ProviderId=providerId;Origin=origin;Evidence=evidence;Capabilities=capabilities;
        OriginalMimeTypes=Array.AsReadOnly(originalMimeTypes.ToArray());
    }
}
