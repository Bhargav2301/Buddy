using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Buddy.Server;

public sealed record RefinementChatScope(string Kind, string Id);
public enum RefinementSourceKind { SelectedText, LocalTextFile, UserLink, LocalImage, LocalOcr }
public enum RefinementTurnOrigin { BuddyCompleted, AdapterCompleted, UserImported }
public sealed record RefinementSourceInput(string Title, string Text, RefinementSourceKind Kind = RefinementSourceKind.SelectedText,
    bool Required = false, bool FullImageRequired = false, string? OriginalSha256 = null,
    long? OriginalBytes = null, string ExtractionMethod = "selected-text", string? SuppliedUrl = null,
    bool OriginalDeliveryRequired = false);
public sealed record RefinementSourceSnapshot(string Id, string Title, string Text, RefinementSourceKind Kind,
    bool Required, bool FullImageRequired, string OriginalSha256, string TextSha256, long OriginalBytes,
    string ExtractionMethod, string? SuppliedUrl, DateTimeOffset AddedAt, string ReviewDigest, bool Reviewed,
    ContextOriginalAssetInfo? OriginalAsset = null, bool OriginalDeliveryRequired = false);
public sealed record RefinementCompletedTurn(string Id, string UserText, string AssistantText,
    RefinementTurnOrigin Origin, DateTimeOffset CompletedAt, string ContentSha256);
public sealed record RefinementWorkspaceSnapshot(RefinementChatScope Scope, long Revision,
    IReadOnlyList<RefinementSourceSnapshot> Sources, IReadOnlyList<RefinementCompletedTurn> Turns);

// Constructed only by Freeze. Public collections are read-only copies and cannot
// change the host-owned review generation. No native/model/network authority.
public sealed class FrozenRefinementContext
{
    internal Guid Owner { get; }
    internal Guid SessionOwner { get; }
    public RefinementChatScope Scope { get; }
    public long Revision { get; }
    public IReadOnlyList<RefinementSourceSnapshot> Sources { get; }
    public IReadOnlyList<RefinementCompletedTurn> Turns { get; }
    public string? SelectedReferentTurnId { get; }
    public string Digest { get; }
    public CancellationToken Invalidated { get; }
    internal FrozenRefinementContext(Guid owner, Guid sessionOwner, RefinementChatScope scope, long revision,
        RefinementSourceSnapshot[] sources, RefinementCompletedTurn[] turns, string? referent, CancellationToken invalidated)
    {
        Owner = owner; SessionOwner = sessionOwner; Scope = scope; Revision = revision;
        Sources = Array.AsReadOnly(sources); Turns = Array.AsReadOnly(turns);
        SelectedReferentTurnId = referent; Invalidated = invalidated;
        Digest = ContextHash.Of(new { sessionOwner, scope, revision, sources, turns, referent });
    }
}

// Memory-only. Hosts explicitly choose the scope and import sources/complete pairs;
// no external chat identity is inferred and no existing conversation is enumerated.
public sealed class RefinementWorkspace : IDisposable
{
    public const int MaximumSessions = 8, MaximumSources = 8, MaximumTurns = 20;
    public const int MaximumTextCharacters = 20000, MaximumSessionTextBytes = 128 * 1024;
    public const long MaximumSessionAssetBytes = 8_000_000, MaximumWorkspaceAssetBytes = 16_000_000;
    private sealed class Session
    {
        internal readonly Guid Identity = Guid.NewGuid();
        internal long Revision;
        internal readonly List<RefinementSourceSnapshot> Sources = [];
        internal readonly List<RefinementCompletedTurn> Turns = [];
        internal readonly Dictionary<string,ContextOriginalAsset> Assets = [];
        internal CancellationTokenSource Lease = new();
    }
    private readonly object gate = new();
    private readonly Guid owner = Guid.NewGuid();
    private readonly Dictionary<RefinementChatScope, Session> sessions = [];
    private readonly Func<DateTimeOffset> clock;
    private bool disposed;
    public RefinementWorkspace(Func<DateTimeOffset>? clock = null) => this.clock = clock ?? (() => DateTimeOffset.UtcNow);

    public RefinementWorkspaceSnapshot Open(RefinementChatScope scope)
    {
        ValidateScope(scope);
        lock (gate) {
            CheckAlive();
            if (!sessions.TryGetValue(scope, out var session)) {
                if (sessions.Count >= MaximumSessions) throw Refused("Close a context session before opening another.");
                sessions.Add(scope, session = new());
            }
            return Copy(scope, session);
        }
    }
    public RefinementWorkspaceSnapshot Snapshot(RefinementChatScope scope)
    { lock (gate) return Copy(scope, Get(scope)); }

    public RefinementWorkspaceSnapshot StageSource(RefinementChatScope scope, RefinementSourceInput input, long expectedRevision,
        ContextOriginalAsset? originalAsset = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ValidateText(input.Title, 160, "Source title"); ValidateText(input.Text, MaximumTextCharacters, "Source text", input.FullImageRequired);
        ValidateText(input.ExtractionMethod, 80, "Extraction method");
        if (!Enum.IsDefined(input.Kind)) throw Refused("Unsupported source kind.");
        if (input.Title.IndexOfAny(['\\', '/']) >= 0) throw Refused("Use a display name, not a local path, for the source.");
        if (input.FullImageRequired && input.Kind != RefinementSourceKind.LocalImage) throw Refused("Only an image source can require image delivery.");
        if (input.OriginalDeliveryRequired && input.Kind is not (RefinementSourceKind.LocalTextFile or RefinementSourceKind.LocalImage or RefinementSourceKind.LocalOcr))
            throw Refused("Only a selected local file can require original asset delivery.");
        if (input.Kind == RefinementSourceKind.UserLink && (input.SuppliedUrl is null || input.Text != input.SuppliedUrl)) throw Refused("A link source contains its exact explicit URL, not page text.");
        if (input.SuppliedUrl is not null) ValidateSuppliedUrl(input.SuppliedUrl);
        originalAsset?.EnsureAvailable();
        if(originalAsset is not null){
            bool textAsset=originalAsset.MimeType is "text/plain" or "text/markdown";
            if(input.Kind==RefinementSourceKind.LocalTextFile?!textAsset:input.Kind is not (RefinementSourceKind.LocalImage or RefinementSourceKind.LocalOcr)||textAsset)
                throw Refused("The retained original format does not match the selected source kind.");
            if(input.OriginalBytes is {} expectedBytes&&expectedBytes!=originalAsset.ByteCount||
               input.OriginalSha256 is {} expectedHash&&!string.Equals(expectedHash,originalAsset.Sha256,StringComparison.OrdinalIgnoreCase))
                throw Refused("The original asset differs from the reviewed source snapshot.");
        }
        if(input.OriginalDeliveryRequired&&originalAsset is null)throw Refused("The requested original asset is not retained. Select the file again.");
        long size = originalAsset?.ByteCount ?? input.OriginalBytes ?? Encoding.UTF8.GetByteCount(input.Text);
        if (size is < 0 or > 4 * 1024 * 1024) throw Refused("The source size exceeds the context snapshot limit.");
        string textHash = ContextHash.Text(input.Text), originalHash = originalAsset?.Sha256 ?? input.OriginalSha256 ?? textHash;
        ContextHash.Validate(originalHash);
        var id = "ctx_" + Guid.NewGuid().ToString("N"); var at = clock();
        var digest = ContextHash.Of(new { id, input.Title, input.Text, input.Kind, input.Required, input.FullImageRequired,
            originalHash, textHash, size, input.ExtractionMethod, input.SuppliedUrl, at, originalAsset=originalAsset?.Info,input.OriginalDeliveryRequired });
        var source = new RefinementSourceSnapshot(id, input.Title, input.Text, input.Kind, input.Required, input.FullImageRequired,
            originalHash, textHash, size, input.ExtractionMethod, input.SuppliedUrl, at, digest, false,originalAsset?.Info,input.OriginalDeliveryRequired);
        CancellationTokenSource previous; RefinementWorkspaceSnapshot result;
        lock (gate) {
            var session = Get(scope, expectedRevision);
            if (session.Sources.Count >= MaximumSources) throw Refused("Remove a source before adding another.");
            if (TextBytes(session) + Encoding.UTF8.GetByteCount(source.Text) > MaximumSessionTextBytes) throw Refused("This context session is full. Remove sources or turns first.");
            if(originalAsset is not null){
                if(AssetBytes(session)+originalAsset.ByteCount>MaximumSessionAssetBytes||sessions.Values.Sum(AssetBytes)+originalAsset.ByteCount>MaximumWorkspaceAssetBytes)
                    throw Refused("Retained original assets exceed this workspace's memory limit. Remove originals before adding more.");
                session.Assets.Add(id,originalAsset.Retain());
            }
            session.Sources.Add(source); previous = Advance(session); result = Copy(scope, session);
        }
        Cancel(previous); return result;
    }

    public RefinementWorkspaceSnapshot ReviewSource(RefinementChatScope scope, string id, string reviewDigest, long expectedRevision)
    {
        CancellationTokenSource previous; RefinementWorkspaceSnapshot result;
        lock (gate) {
            var session = Get(scope, expectedRevision); int index = session.Sources.FindIndex(s => s.Id == id);
            if (index < 0 || session.Sources[index].Reviewed || session.Sources[index].ReviewDigest != reviewDigest)
                throw Refused("The staged source changed or was already reviewed. Review it again.");
            session.Sources[index] = session.Sources[index] with { Reviewed = true };
            previous = Advance(session); result = Copy(scope, session);
        }
        Cancel(previous); return result;
    }

    public RefinementWorkspaceSnapshot SetOriginalDeliveryRequired(RefinementChatScope scope,string id,bool required,long expectedRevision)
    {
        CancellationTokenSource previous;RefinementWorkspaceSnapshot result;
        lock(gate){
            var session=Get(scope,expectedRevision);int index=session.Sources.FindIndex(s=>s.Id==id);
            if(index<0)throw Refused("The selected source is no longer present.");
            var source=session.Sources[index];
            if(source.OriginalAsset is null||!session.Assets.ContainsKey(id))throw Refused("Select the original file again before choosing its delivery mode.");
            if(source.OriginalDeliveryRequired==required)return Copy(scope,session);
            string digest=ContextHash.Of(new{source.ReviewDigest,id,required,revision=checked(session.Revision+1)});
            session.Sources[index]=source with{OriginalDeliveryRequired=required,Reviewed=false,ReviewDigest=digest};
            previous=Advance(session);result=Copy(scope,session);
        }
        Cancel(previous);return result;
    }

    public RefinementWorkspaceSnapshot RemoveSource(RefinementChatScope scope, string id, long expectedRevision)
    {
        CancellationTokenSource previous; RefinementWorkspaceSnapshot result;
        lock (gate) {
            var session = Get(scope, expectedRevision);
            if (session.Sources.RemoveAll(s => s.Id == id) != 1) throw Refused("The selected source is no longer present.");
            if(session.Assets.Remove(id,out var asset))asset.Dispose();
            previous = Advance(session); result = Copy(scope, session);
        }
        Cancel(previous); return result;
    }

    // A host calls this only for a completed pair or an explicitly imported pair.
    // Applying a draft or receiving a partial streamed answer never calls this API.
    public RefinementWorkspaceSnapshot AppendCompletedTurn(RefinementChatScope scope, string turnId, string userText,
        string assistantText, RefinementTurnOrigin origin, long expectedRevision)
    {
        ValidateText(turnId, 80, "Turn ID"); ValidateText(userText, MaximumTextCharacters, "User prompt");
        ValidateText(assistantText, MaximumTextCharacters, "Assistant response");
        if (!Enum.IsDefined(origin)) throw Refused("Unsupported turn provenance.");
        var turn = new RefinementCompletedTurn(turnId, userText, assistantText, origin, clock(), ContextHash.Of(new { userText, assistantText, origin }));
        CancellationTokenSource previous; RefinementWorkspaceSnapshot result;
        lock (gate) {
            var session = Get(scope, expectedRevision);
            if (session.Turns.Any(t => t.Id == turnId)) throw Refused("This completed turn is already present.");
            if (session.Turns.Count >= MaximumTurns || TextBytes(session) + Encoding.UTF8.GetByteCount(userText) + Encoding.UTF8.GetByteCount(assistantText) > MaximumSessionTextBytes)
                throw Refused("This context session is full. Remove older complete turns before adding another.");
            session.Turns.Add(turn); previous = Advance(session); result = Copy(scope, session);
        }
        Cancel(previous); return result;
    }

    public RefinementWorkspaceSnapshot RemoveTurn(RefinementChatScope scope, string id, long expectedRevision)
    {
        CancellationTokenSource previous; RefinementWorkspaceSnapshot result;
        lock (gate) {
            var session = Get(scope, expectedRevision);
            if (session.Turns.RemoveAll(t => t.Id == id) != 1) throw Refused("The selected turn is no longer present.");
            previous = Advance(session); result = Copy(scope, session);
        }
        Cancel(previous); return result;
    }

    public FrozenRefinementContext Freeze(RefinementChatScope scope, long expectedRevision,
        IReadOnlyList<string> sourceIds, IReadOnlyList<string> turnIds, string? selectedReferentTurnId = null)
    {
        ArgumentNullException.ThrowIfNull(sourceIds); ArgumentNullException.ThrowIfNull(turnIds);
        var selectedSources = sourceIds.ToArray(); var selectedTurns = turnIds.ToArray();
        if (selectedSources.Length > MaximumSources || selectedTurns.Length > MaximumTurns ||
            selectedSources.Distinct(StringComparer.Ordinal).Count() != selectedSources.Length || selectedTurns.Distinct(StringComparer.Ordinal).Count() != selectedTurns.Length)
            throw Refused("Select each source or turn at most once.");
        lock (gate) {
            var session = Get(scope, expectedRevision);
            var sources = session.Sources.Where(s => selectedSources.Contains(s.Id, StringComparer.Ordinal)).ToArray();
            var turns = session.Turns.Where(t => selectedTurns.Contains(t.Id, StringComparer.Ordinal)).ToArray();
            if (sources.Length != selectedSources.Length || turns.Length != selectedTurns.Length || sources.Any(s => !s.Reviewed))
                throw Refused("Only current, reviewed sources and completed turns can be selected.");
            if (selectedReferentTurnId is not null && !turns.Any(t => t.Id == selectedReferentTurnId))
                throw Refused("The selected prior response is not in this context selection.");
            return new(owner, session.Identity, scope, session.Revision, sources, turns, selectedReferentTurnId, session.Lease.Token);
        }
    }

    public bool IsCurrent(FrozenRefinementContext selected)
    {
        if (selected is null) return false;
        lock (gate) return !disposed && selected.Owner == owner && !selected.Invalidated.IsCancellationRequested &&
            sessions.TryGetValue(selected.Scope, out var session) && session.Identity == selected.SessionOwner && session.Revision == selected.Revision;
    }

    // Explicit local ownership transfer to a reviewed destination path. Metadata
    // snapshots never contain bytes. A retained lease grants no send authority.
    public ContextOriginalAsset RetainOriginalAsset(FrozenRefinementContext selected,string sourceId)
    {
        lock(gate){
            if(!IsCurrent(selected)||!selected.Sources.Any(s=>s.Id==sourceId&&s.OriginalAsset is not null))
                throw Refused("Select and review the current original asset before preparing delivery.");
            var session=Get(selected.Scope,selected.Revision);
            if(!session.Assets.TryGetValue(sourceId,out var asset))throw Refused("The original asset is no longer retained.");
            return asset.Retain();
        }
    }

    public RefinementWorkspaceSnapshot Clear(RefinementChatScope scope)
    {
        CancellationTokenSource previous; RefinementWorkspaceSnapshot result;
        lock (gate) { var session = Get(scope); ReleaseAssets(session); session.Sources.Clear(); session.Turns.Clear(); previous = Advance(session); result = Copy(scope, session); }
        Cancel(previous); return result;
    }
    public void Close(RefinementChatScope scope)
    {
        CancellationTokenSource previous;
        lock (gate) { var session = Get(scope); previous = session.Lease; ReleaseAssets(session); sessions.Remove(scope); }
        Cancel(previous);
    }
    public void Dispose()
    {
        CancellationTokenSource[] previous;
        lock (gate) { if (disposed) return; disposed = true; previous = sessions.Values.Select(s => s.Lease).ToArray(); foreach(var session in sessions.Values)ReleaseAssets(session); sessions.Clear(); }
        foreach (var lease in previous) Cancel(lease);
    }

    private static RefinementWorkspaceSnapshot Copy(RefinementChatScope scope, Session session) => new(scope, session.Revision,
        Array.AsReadOnly(session.Sources.ToArray()), Array.AsReadOnly(session.Turns.ToArray()));
    private static long TextBytes(Session session) => session.Sources.Sum(s => (long)Encoding.UTF8.GetByteCount(s.Text)) +
        session.Turns.Sum(t => (long)Encoding.UTF8.GetByteCount(t.UserText) + Encoding.UTF8.GetByteCount(t.AssistantText));
    private static long AssetBytes(Session session)=>session.Assets.Values.Sum(a=>a.ByteCount);
    private static void ReleaseAssets(Session session){foreach(var asset in session.Assets.Values)asset.Dispose();session.Assets.Clear();}
    private static CancellationTokenSource Advance(Session session) { var previous = session.Lease; session.Lease = new(); session.Revision = checked(session.Revision + 1); return previous; }
    private static void Cancel(CancellationTokenSource lease) { try { lease.Cancel(); } catch (AggregateException) { /* Subscribers cannot undo invalidation. */ } finally { lease.Dispose(); } }
    private Session Get(RefinementChatScope scope, long? expected = null)
    {
        CheckAlive(); ValidateScope(scope);
        if (!sessions.TryGetValue(scope, out var session) || expected is not null && expected != session.Revision) throw Refused("This context session changed. Review its current contents.");
        return session;
    }
    private void CheckAlive() { if (disposed) throw Refused("The context workspace is closed."); }
    private static void ValidateScope(RefinementChatScope scope)
    {
        if (scope is null || scope.Kind is not ("buddy-local" or "manual-external" or "local-agent")) throw Refused("Choose an explicit supported context session.");
        ValidateText(scope.Id, 160, "Session ID");
    }
    internal static void ValidateText(string text, int maximum, string name, bool allowEmpty = false)
    {
        if (text is null || text.Length > maximum || !allowEmpty && string.IsNullOrWhiteSpace(text) ||
            text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
            throw Refused(name + " is empty, too long or contains unsupported controls.");
        for (int i = 0; i < text.Length; i++) {
            if (!char.IsSurrogate(text[i])) continue;
            if (!char.IsHighSurrogate(text[i]) || i + 1 >= text.Length || !char.IsLowSurrogate(text[++i]))
                throw Refused(name + " contains invalid Unicode.");
        }
    }
    private static void ValidateSuppliedUrl(string text)
    {
        if (text.Length > 2048 || !Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Port != 443 ||
            uri.UserInfo.Length != 0 || uri.IsLoopback || !uri.Host.Contains('.') || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            System.Net.IPAddress.TryParse(uri.Host, out _)) throw Refused("Use an explicit public HTTPS reference without credentials.");
    }
    internal static InvalidOperationException Refused(string text) => new(text);
}

internal static class ContextHash
{
    internal static string Text(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    internal static string Of<T>(T value) => Text(JsonSerializer.Serialize(value));
    internal static void Validate(string value) { if (value is null || value.Length != 64 || !value.All(char.IsAsciiHexDigit)) throw RefinementWorkspace.Refused("A source content digest is invalid."); }
}
