using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Buddy.Server;

// There is deliberately no Connect method or concrete socket creator. The only current
// implementation lives in the synthetic tests. An eventual connector needs its own review.
internal interface IRealtimeTextSocket : IDisposable
{
    Task SendAsync(ReadOnlyMemory<byte> utf8Json, CancellationToken cancellationToken);
    Task<RealtimeTextFrame> ReceiveAsync(Memory<byte> destination, CancellationToken cancellationToken);
    Task CloseAsync(CancellationToken cancellationToken);
    void Abort();
}

internal readonly record struct RealtimeTextFrame(int Count, bool EndOfMessage, WebSocketMessageType MessageType);

internal sealed record RealtimeTextLimits
{
    public int MaxMessageBytes { get; init; } = 65536;
    public int MaxTotalBytes { get; init; } = 262144;
    public int MaxMessages { get; init; } = 256;
    public int MaxFrames { get; init; } = 512;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan CleanupTimeout { get; init; } = TimeSpan.FromMilliseconds(500);

    internal void Validate()
    {
        if (MaxMessageBytes is < 1 or > 65536 || MaxTotalBytes < MaxMessageBytes || MaxTotalBytes > 262144 ||
            MaxMessages is < 1 or > 256 || MaxFrames is < 1 or > 512 || Timeout <= TimeSpan.Zero || Timeout > TimeSpan.FromSeconds(60) ||
            CleanupTimeout <= TimeSpan.Zero || CleanupTimeout > TimeSpan.FromSeconds(1)) throw new ArgumentOutOfRangeException(nameof(RealtimeTextLimits));
    }
}

// One request owns one injected socket. No sessions, credentials or audio are shared or persisted.
internal sealed class RealtimeTextTransport
{
    private readonly IRealtimeTextSocket socket;
    private readonly RealtimeTextLimits limits;
    private int started;

    internal RealtimeTextTransport(IRealtimeTextSocket socket, RealtimeTextLimits? limits = null)
    {
        this.socket = socket ?? throw new ArgumentNullException(nameof(socket));
        this.limits = limits ?? new();
        this.limits.Validate();
    }

    internal async Task<string> CompleteAsync(string question, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref started, 1) != 0) throw new InvalidOperationException("Realtime transport is single use.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(limits.Timeout);
        var ct = deadline.Token;
        var reducer = new RealtimeTextProtocol();
        var correlation = Guid.NewGuid().ToString("N");
        string? responseId = null;
        string? itemId = null;
        bool sent = false, completed = false, textDone = false;
        var text = new StringBuilder();
        var seen = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        int frameCount = 0, totalBytes = 0;
        try
        {
            ct.ThrowIfCancellationRequested();
            question = Security.Text(question, 8000, "Provider question");
            var request = JsonSerializer.SerializeToUtf8Bytes(new
            {
                type = "response.create",
                response = new
                {
                    conversation = "none", output_modalities = new[] { "text" }, tools = Array.Empty<object>(), tool_choice = "none",
                    max_output_tokens = 1024, instructions = ConversationalReply.Policy + " No tools or actions are available.",
                    metadata = new { buddy_request_id = correlation },
                    input = new[] { new { type = "message", role = "user", content = new[] { new { type = "input_text", text = question } } } }
                }
            });
            if (request.Length > 65536) throw Rejected();
            // Set before sending: if cancellation interrupts an uncertain send, still cancel/close.
            sent = true;
            await socket.SendAsync(request, ct).WaitAsync(ct).ConfigureAwait(false);
            var buffer = new byte[4096];
            for (int messageCount = 0; messageCount < limits.MaxMessages; messageCount++)
            {
                using var message = new MemoryStream();
                while (true)
                {
                    if (++frameCount > limits.MaxFrames) throw Rejected();
                    var frame = await socket.ReceiveAsync(buffer, ct).WaitAsync(ct).ConfigureAwait(false);
                    ct.ThrowIfCancellationRequested();
                    if (frame.MessageType != WebSocketMessageType.Text || frame.Count < 0 || frame.Count > buffer.Length) throw Rejected();
                    totalBytes += frame.Count;
                    if (message.Length + frame.Count > limits.MaxMessageBytes || totalBytes > limits.MaxTotalBytes) throw Rejected();
                    message.Write(buffer, 0, frame.Count);
                    if (frame.EndOfMessage) break;
                }
                var bytes = message.ToArray();
                using var document = ProviderTextOnlyValidation.Parse(bytes);
                var root = document.RootElement;
                string type = root.GetProperty("type").GetString() ?? "";
                // Never accept side-channel audio, function/tool calls, or refusal, even with no response ID.
                if (type == "error" || type.Contains("audio", StringComparison.Ordinal) || type.Contains("function", StringComparison.Ordinal) ||
                    type.Contains("tool", StringComparison.Ordinal) || type.Contains("refusal", StringComparison.Ordinal)) throw Rejected();
                string eventId = Identifier(root.GetProperty("event_id"));
                var digest = SHA256.HashData(bytes);
                if (seen.TryGetValue(eventId, out var oldDigest))
                {
                    if (!oldDigest.AsSpan().SequenceEqual(digest)) throw Rejected();
                    continue;
                }
                seen.Add(eventId, digest);
                if (type is "session.created" or "session.updated" or "rate_limits.updated") continue;
                if (type == "response.created")
                {
                    var response = root.GetProperty("response");
                    if (!Matches(response, correlation)) continue;
                    var newId = Identifier(response.GetProperty("id"));
                    if (responseId is not null || response.GetProperty("status").GetString() != "in_progress") throw Rejected();
                    responseId = newId;
                    reducer.Begin(newId);
                    continue;
                }
                var incomingId = type == "response.done" ? root.GetProperty("response").GetProperty("id").GetString() :
                    root.TryGetProperty("response_id", out var rid) ? rid.GetString() : null;
                if (responseId is null || incomingId != responseId) continue;
                switch (type)
                {
                    case "response.output_text.delta":
                        CheckIndices(root);
                        BindItem(ref itemId, Identifier(root.GetProperty("item_id")));
                        if (textDone) throw Rejected();
                        string delta = root.GetProperty("delta").GetString() ?? throw Rejected();
                        if (text.Length + delta.Length > 1600) throw Rejected();
                        text.Append(delta);
                        reducer.Receive(bytes, ct);
                        break;
                    case "response.output_text.done":
                        CheckIndices(root);
                        BindItem(ref itemId, Identifier(root.GetProperty("item_id")));
                        if (textDone || root.GetProperty("text").GetString() != text.ToString()) throw Rejected();
                        textDone = true;
                        break;
                    case "response.output_item.added":
                    case "response.output_item.done":
                        if (root.GetProperty("output_index").GetInt32() != 0) throw Rejected();
                        var item = root.GetProperty("item");
                        ValidateItem(item);
                        BindItem(ref itemId, Identifier(item.GetProperty("id")));
                        break;
                    case "response.content_part.added":
                    case "response.content_part.done":
                        CheckIndices(root);
                        BindItem(ref itemId, Identifier(root.GetProperty("item_id")));
                        ValidatePart(root.GetProperty("part"));
                        break;
                    case "response.done":
                        var done = root.GetProperty("response");
                        if (!Matches(done, correlation) || !textDone || done.GetProperty("status").GetString() != "completed") throw Rejected();
                        var output = done.GetProperty("output");
                        if (output.GetArrayLength() != 1) throw Rejected();
                        var final = output[0];
                        ValidateItem(final);
                        BindItem(ref itemId, Identifier(final.GetProperty("id")));
                        if (final.GetProperty("status").GetString() != "completed" || final.GetProperty("content").GetArrayLength() != 1 ||
                            final.GetProperty("content")[0].GetProperty("text").GetString() != text.ToString()) throw Rejected();
                        string answer = reducer.Receive(bytes, ct) ?? throw Rejected();
                        ct.ThrowIfCancellationRequested();
                        completed = true;
                        return answer;
                    default: throw Rejected();
                }
            }
            throw Rejected();
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            throw new TimeoutException("Realtime response exceeded its deadline.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or IOException or WebSocketException or IndexOutOfRangeException or FormatException)
        {
            throw Rejected();
        }
        finally
        {
            reducer.Cancel();
            if (sent && !completed)
            {
                // Out-of-band responses must include response_id once known. Before response.created,
                // cancellation is best effort; closing the one-shot socket is the fallback.
                var cancel = responseId is null ? JsonSerializer.SerializeToUtf8Bytes(new { type = "response.cancel" }) :
                    JsonSerializer.SerializeToUtf8Bytes(new { type = "response.cancel", response_id = responseId });
                await CleanupAsync(token => socket.SendAsync(cancel, token)).ConfigureAwait(false);
            }
            await CleanupAsync(socket.CloseAsync).ConfigureAwait(false);
            // Cleanup exceptions are not allowed to replace the sanitized operation failure.
            try { socket.Dispose(); } catch { TryAbort(); }
        }
    }

    private async Task CleanupAsync(Func<CancellationToken, Task> operation)
    {
        using var timeout = new CancellationTokenSource(limits.CleanupTimeout);
        try { await operation(timeout.Token).WaitAsync(timeout.Token).ConfigureAwait(false); }
        catch { TryAbort(); }
    }

    private void TryAbort() { try { socket.Abort(); } catch { } }

    private static void ValidateItem(JsonElement item)
    {
        if (item.GetProperty("type").GetString() != "message" || item.GetProperty("role").GetString() != "assistant") throw Rejected();
        foreach (var part in item.GetProperty("content").EnumerateArray()) ValidatePart(part);
    }

    private static void ValidatePart(JsonElement part)
    {
        if (part.GetProperty("type").GetString() != "output_text" || part.GetProperty("text").ValueKind != JsonValueKind.String ||
            part.EnumerateObject().Any(property => property.Name is not "type" and not "text")) throw Rejected();
    }

    private static void CheckIndices(JsonElement root)
    {
        if (root.GetProperty("output_index").GetInt32() != 0 || root.GetProperty("content_index").GetInt32() != 0) throw Rejected();
    }

    private static void BindItem(ref string? current, string incoming)
    {
        if (current is not null && current != incoming) throw Rejected();
        current = incoming;
    }

    private static string Identifier(JsonElement element)
    {
        var value = element.GetString();
        if (string.IsNullOrEmpty(value) || value.Length > 100 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '_' and not '-')) throw Rejected();
        return value;
    }

    private static bool Matches(JsonElement response, string correlation) =>
        response.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object &&
        metadata.TryGetProperty("buddy_request_id", out var id) && id.GetString() == correlation;

    private static InvalidOperationException Rejected() => new("No complete supported realtime text answer was accepted.");
}
