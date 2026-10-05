using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Buddy.Server;

/// <summary>
/// Explicit adapter API. The caller supplies a locally paired lease; construction installs nothing and connects nowhere.
/// It relays only events and consumes exact question/denial decisions, never executes returned text.
/// </summary>
public sealed class LocalAgentPipeClient
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    private readonly string pipeName;
    private readonly LocalAgentLease lease;
    public LocalAgentPipeClient(string pipeName, LocalAgentLease lease)
    {
        const string prefix = "Buddy.LocalAgent.";
        if (pipeName is null || lease is null || lease.SessionId == Guid.Empty || lease.Token is null || lease.Token.Length != 64 ||
            !lease.Token.All(char.IsAsciiHexDigit) || !pipeName.StartsWith(prefix, StringComparison.Ordinal) || pipeName.Length != prefix.Length + 32 ||
            !pipeName[prefix.Length..].All(char.IsAsciiHexDigit)) throw LocalAgentBroker.Rejected();
        this.pipeName = pipeName; this.lease = lease;
    }
    public async Task SendAsync(LocalAgentEvent input, CancellationToken ct = default)
    {
        if (input is null || input.SessionId != lease.SessionId) throw LocalAgentBroker.Rejected();
        var response = await Exchange(JsonSerializer.SerializeToUtf8Bytes(new { kind = "event", token = lease.Token, @event = input }, Json), ct).ConfigureAwait(false);
        ParseAcknowledgement(response, false); ct.ThrowIfCancellationRequested();
    }
    public async Task<LocalAgentDecision?> TakeDecisionAsync(string requestId, string requestDigest, CancellationToken ct = default)
    {
        var response = await Exchange(JsonSerializer.SerializeToUtf8Bytes(new { kind = "decision", token = lease.Token, sessionId = lease.SessionId, requestId, requestDigest }, Json), ct).ConfigureAwait(false);
        var decision = ParseDecisionResponse(response, requestId, requestDigest);
        ct.ThrowIfCancellationRequested(); return decision;
    }
    internal static void ParseAcknowledgement(ReadOnlyMemory<byte> response, bool includesDecision)
    {
        try
        {
            if (response.Length > 16384) throw LocalAgentBroker.Rejected();
            using var document = ProviderTextOnlyValidation.Parse(response); var root = document.RootElement;
            RequireKeys(root, includesDecision ? ["accepted", "decision"] : ["accepted"]);
            if (!root.GetProperty("accepted").GetBoolean()) throw LocalAgentBroker.Rejected();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        { throw LocalAgentBroker.Rejected(); }
    }
    internal static LocalAgentDecision? ParseDecisionResponse(ReadOnlyMemory<byte> response, string requestId, string requestDigest)
    {
        try
        {
            ParseAcknowledgement(response, true);
            using var document = ProviderTextOnlyValidation.Parse(response); var item = document.RootElement.GetProperty("decision");
            if (item.ValueKind == JsonValueKind.Null) return null;
            // The server's wire contract uses these exact PascalCase decision property names.
            RequireKeys(item, ["RequestId", "RequestDigest", "Kind", "Text"]);
            var id = item.GetProperty("RequestId").GetString(); var digest = item.GetProperty("RequestDigest").GetString();
            var kind = item.GetProperty("Kind").GetString(); var text = item.GetProperty("Text").GetString();
            if (id != requestId || digest != requestDigest || kind is not ("answer" or "denied") || string.IsNullOrWhiteSpace(text) ||
                text.Length > 2000 || text.Any(c => char.IsControl(c) && c is not '\n' and not '\t')) throw LocalAgentBroker.Rejected();
            _ = new UTF8Encoding(false, true).GetByteCount(text);
            return new(id!, digest!, kind, text);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException)
        { throw LocalAgentBroker.Rejected(); }
    }
    private static void RequireKeys(JsonElement item, string[] names)
    {
        if (item.ValueKind != JsonValueKind.Object || item.EnumerateObject().Count() != names.Length ||
            item.EnumerateObject().Any(x => !names.Contains(x.Name, StringComparer.Ordinal))) throw LocalAgentBroker.Rejected();
    }
    private async Task<byte[]> Exchange(byte[] request, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            if (DateTimeOffset.UtcNow >= lease.Expires || request.Length > 16384) throw LocalAgentBroker.Rejected();
            deadline.Token.ThrowIfCancellationRequested();
            await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(deadline.Token).ConfigureAwait(false);
            await pipe.WriteAsync(request, deadline.Token).ConfigureAwait(false);
            await pipe.WriteAsync("\n"u8.ToArray(), deadline.Token).ConfigureAwait(false); await pipe.FlushAsync(deadline.Token).ConfigureAwait(false);
            using var bytes = new MemoryStream(); var one = new byte[1];
            while (bytes.Length <= 16384)
            {
                if (await pipe.ReadAsync(one, deadline.Token).ConfigureAwait(false) == 0) throw LocalAgentBroker.Rejected();
                if (one[0] == (byte)'\n') break;
                bytes.WriteByte(one[0]);
            }
            if (bytes.Length > 16384) throw LocalAgentBroker.Rejected();
            var answer = bytes.ToArray(); using var document = ProviderTextOnlyValidation.Parse(answer);
            if (!document.RootElement.GetProperty("accepted").GetBoolean()) throw LocalAgentBroker.Rejected();
            deadline.Token.ThrowIfCancellationRequested();
            return answer;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw new OperationCanceledException(ct); }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidOperationException or JsonException or KeyNotFoundException or UnauthorizedAccessException)
        { throw LocalAgentBroker.Rejected(); }
        finally { Array.Clear(request); }
    }
    public override string ToString() => "LocalAgentPipeClient { pairing = [private] }";
}
