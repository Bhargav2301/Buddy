using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Diagnostics;

namespace Buddy.Server;

public sealed record LocalAgentLease(Guid SessionId, string Token, string Label, DateTimeOffset Expires)
{
    public override string ToString() => $"LocalAgentLease {{ SessionId = {SessionId}, Token = [redacted], Expires = {Expires:O} }}";
}
public sealed record LocalAgentEvent(Guid SessionId, long Sequence, string Kind, string TaskId, string Text,
    string? RequestId = null, string? RequestDigest = null);
public sealed record LocalAgentRequest(string Id, string Digest, string Kind, string Text, DateTimeOffset Expires);
public sealed record LocalAgentSnapshot(Guid SessionId, string Label, bool Connected, string TaskId, string State,
    string Detail, long LastSequence, LocalAgentRequest? Request, DateTimeOffset Expires);
public sealed record LocalAgentDecision(string RequestId, string RequestDigest, string Kind, string Text);

/// <summary>
/// Memory-only, possession-bound local sessions. A token proves possession, not the identity of a coding product.
/// Events are display data and never commands. Execution approvals may be denied, never allowed here.
/// </summary>
public sealed class LocalAgentBroker
{
    private sealed class Session(LocalAgentLease lease, long createdTimestamp)
    {
        internal readonly Guid Id = lease.SessionId;
        internal readonly byte[] TokenHash = SHA256.HashData(Encoding.UTF8.GetBytes(lease.Token));
        internal readonly string Label = lease.Label;
        internal readonly DateTimeOffset Expires = lease.Expires;
        internal readonly long CreatedTimestamp = createdTimestamp;
        internal bool Connected = true;
        internal long Sequence;
        internal string Task = "", State = "Paired; awaiting an event", Detail = "";
        internal LocalAgentRequest? Request;
        internal LocalAgentDecision? Decision;
        internal DateTimeOffset DecisionExpires;
        internal long RequestTimestamp;
        internal readonly HashSet<string> Tasks = new(StringComparer.Ordinal);
        internal readonly HashSet<string> Requests = new(StringComparer.Ordinal);
    }
    private readonly object gate = new();
    private readonly Dictionary<Guid, Session> sessions = [];
    private readonly Func<DateTimeOffset> clock;
    private readonly Func<long> timestamp;
    private DateTimeOffset lastNow = DateTimeOffset.MinValue;
    private long lastTimestamp;
    private bool hasTimestamp;
    public LocalAgentBroker(Func<DateTimeOffset>? clock = null, Func<long>? timestamp = null)
    { this.clock = clock ?? (() => DateTimeOffset.UtcNow); this.timestamp = timestamp ?? Stopwatch.GetTimestamp; }
    private DateTimeOffset Now()
    {
        var now = clock();
        var tick = timestamp();
        if (now < lastNow || hasTimestamp && tick < lastTimestamp) { foreach (var s in sessions.Values) Close(s); throw Rejected(); }
        hasTimestamp = true; lastTimestamp = tick;
        return lastNow = now;
    }
    public LocalAgentLease Pair(string label, bool allowLocalStatusAndQuestions)
    {
        if (!allowLocalStatusAndQuestions) throw new InvalidOperationException("Explicitly enable this local session before pairing.");
        label = Label(label, 80);
        lock (gate)
        {
            var now = Now(); Expire(now);
            foreach (var id in sessions.Where(p => !p.Value.Connected).Select(p => p.Key).ToArray()) sessions.Remove(id);
            if (sessions.Count >= 8) throw new InvalidOperationException("Disconnect a local session before adding another.");
            var lease = new LocalAgentLease(Guid.NewGuid(), Convert.ToHexString(RandomNumberGenerator.GetBytes(32)), label, now.AddMinutes(30));
            sessions.Add(lease.SessionId, new Session(lease, lastTimestamp)); return lease;
        }
    }
    public IReadOnlyList<LocalAgentSnapshot> Snapshot
    {
        get
        {
            lock (gate)
            {
                Expire(Now());
                return Array.AsReadOnly(sessions.Values.Select(s => new LocalAgentSnapshot(s.Id, s.Label, s.Connected,
                    s.Task, s.State, s.Detail, s.Sequence, s.Request, s.Expires)).ToArray());
            }
        }
    }
    public void Receive(string token, LocalAgentEvent input)
    {
        ArgumentNullException.ThrowIfNull(input);
        string task = Identifier(input.TaskId), detail = Label(input.Text, 2000);
        if (input.Kind is not ("started" or "status" or "completed" or "failed" or "question" or "approval")) throw Rejected();
        lock (gate)
        {
            var now = Now(); var s = Authenticate(input.SessionId, token, now);
            if (input.Sequence != s.Sequence + 1 || input.Sequence > 4096) throw Rejected();
            if (input.Kind == "started")
            {
                if (s.Request is not null || s.Decision is not null || s.Tasks.Count >= 64 || s.Tasks.Contains(task) || input.RequestId is not null || input.RequestDigest is not null) throw Rejected();
                s.Tasks.Add(task); s.Task = task; s.State = "Running";
            }
            else
            {
                if (task != s.Task || s.State is "Completed" or "Failed" || s.Request is not null || s.Decision is not null) throw Rejected();
                if (input.Kind is "question" or "approval")
                {
                    var request = Identifier(input.RequestId);
                    var expected = RequestDigest(s.Id, task, request, input.Kind, detail);
                    if (input.RequestDigest != expected || s.Requests.Count >= 128 || s.Requests.Contains(request)) throw Rejected();
                    s.Requests.Add(request);
                    s.RequestTimestamp = lastTimestamp;
                    s.Request = new(request, expected, input.Kind, detail, now.AddMinutes(2)); s.State = "Review required";
                }
                else
                {
                    if (input.RequestId is not null || input.RequestDigest is not null) throw Rejected();
                    s.State = input.Kind switch { "completed" => "Completed", "failed" => "Failed", _ => "Running" };
                }
            }
            s.Detail = detail; s.Sequence = input.Sequence;
        }
    }
    public static string RequestDigest(Guid sessionId, string taskId, string requestId, string kind, string text)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new[] { sessionId.ToString("D"), taskId, requestId, kind, text });
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
    // This is a local user-interface entry point, not an endpoint exposed to the paired agent.
    public void Decide(Guid sessionId, string taskId, string requestId, string digest, string decision, string? reply = null)
    {
        lock (gate)
        {
            var now = Now(); Expire(now);
            if (!sessions.TryGetValue(sessionId, out var s) || !s.Connected || s.Task != taskId || s.Decision is not null ||
                s.Request is not { } request || request.Id != requestId || request.Digest != digest || now >= request.Expires) throw Rejected();
            if (decision == "deny") s.Decision = new(request.Id, request.Digest, "denied", "Declined in Buddy.");
            else if (decision == "answer" && request.Kind == "question") s.Decision = new(request.Id, request.Digest, "answer", Label(reply, 2000));
            else throw new InvalidOperationException("Execution approval is unavailable. Review and approve commands in the originating coding tool.");
            s.DecisionExpires = request.Expires;
            s.Request = null; s.State = "Decision waiting for the paired client";
        }
    }
    public LocalAgentDecision? TakeDecision(Guid sessionId, string token, string requestId, string digest)
    {
        lock (gate)
        {
            var s = Authenticate(sessionId, token, Now());
            if (s.Decision is not { } decision) return null;
            if (decision.RequestId != requestId || decision.RequestDigest != digest) throw Rejected();
            s.Decision = null; s.State = "Running"; return decision;
        }
    }
    public void Disconnect(Guid sessionId) { lock (gate) if (sessions.TryGetValue(sessionId, out var s)) Close(s); }
    public void DisconnectAll() { lock (gate) foreach (var s in sessions.Values) Close(s); }
    private Session Authenticate(Guid id, string token, DateTimeOffset now)
    {
        Expire(now);
        if (token is null || token.Length != 64 || !sessions.TryGetValue(id, out var s) || !s.Connected ||
            !CryptographicOperations.FixedTimeEquals(s.TokenHash, SHA256.HashData(Encoding.UTF8.GetBytes(token)))) throw Rejected();
        return s;
    }
    private void Expire(DateTimeOffset now)
    {
        foreach (var s in sessions.Values)
        {
            if (now >= s.Expires || Stopwatch.GetElapsedTime(s.CreatedTimestamp, lastTimestamp) >= TimeSpan.FromMinutes(30)) Close(s);
            else if (s.Request is { } request && now >= request.Expires || s.Decision is not null && now >= s.DecisionExpires ||
                (s.Request is not null || s.Decision is not null) && Stopwatch.GetElapsedTime(s.RequestTimestamp, lastTimestamp) >= TimeSpan.FromMinutes(2))
            {
                // No answer is silently supplied. The client sees that the session must be paired again.
                Close(s);
            }
        }
    }
    private static void Close(Session s) { s.Connected = false; s.State = "Disconnected"; s.Request = null; s.Decision = null; Array.Clear(s.TokenHash); }
    private static string Identifier(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 80 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_')) throw Rejected();
        return value;
    }
    private static string Label(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > max || value.Any(c => char.IsControl(c) && c is not '\n' and not '\t')) throw Rejected();
        try { _ = new UTF8Encoding(false, true).GetByteCount(value); }
        catch (EncoderFallbackException) { throw Rejected(); }
        return value;
    }
    internal static InvalidOperationException Rejected() => new("The local-agent event or decision was not accepted. Recheck the current paired session and request.");
}
