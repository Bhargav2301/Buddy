using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

[assembly: InternalsVisibleTo("Buddy.Provider.Tests")]

namespace Buddy.Server;

public interface IProviderTextTransport
{
    Task<string> CompleteAsync(string provider, string model, string question, CancellationToken cancellationToken = default);
}

// Parameterless creation remains disconnected. Explicit session setup never reads ambient credentials.
public static class ProviderTransportFactory
{
    public static bool LiveActivationAvailable => true;
    public static ProviderRoutingSession CreateProduction(ProviderConsent consent, ReadOnlySpan<char> credential)
        => ProviderRoutingSession.CreateProduction(consent, credential);
    public static IProviderTextTransport CreateProduction() => new Disconnected();

    private sealed class Disconnected : IProviderTextTransport
    {
        public Task<string> CompleteAsync(string provider, string model, string question, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromException<string>(new NotSupportedException("Live provider activation is unavailable."));
        }
    }
}

internal sealed record ProviderTransportLimits
{
    public int MaxResponseBytes { get; init; } = 65536;
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (MaxResponseBytes is < 1 or > 65536 || Timeout <= TimeSpan.Zero || Timeout > TimeSpan.FromSeconds(60))
            throw new ArgumentOutOfRangeException(nameof(ProviderTransportLimits));
    }
}

// Bounded wire implementation. No credential lookup, logging, retries, URI override, or ambient client.
// Production construction is internal and uses its own redirect/cookie/credential-free handler.
// A mock handler is a trusted code dependency, not a security sandbox for arbitrary code.
internal sealed class ProviderHttpTextTransport : IDisposable
{
    private readonly HttpClient client;
    private readonly ProviderTransportLimits limits;
    private readonly CancellationTokenSource lifetime = new();
    private int disposed;

    internal ProviderHttpTextTransport(HttpMessageHandler mockHandler, ProviderTransportLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(mockHandler);
        // Do not accidentally accept a redirect-enabled live handler, including hidden inner handlers.
        if (mockHandler is HttpClientHandler or SocketsHttpHandler or DelegatingHandler)
            throw new ArgumentException("Only an injected fixture handler is supported.", nameof(mockHandler));
        this.limits = limits ?? new();
        this.limits.Validate();
        client = new HttpClient(mockHandler, disposeHandler: true) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    }

    private ProviderHttpTextTransport(SocketsHttpHandler handler)
    {
        limits = new();
        client = new HttpClient(handler, disposeHandler: true) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    }

    internal static ProviderHttpTextTransport CreateProduction() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false, UseCookies = false, Credentials = null,
        ConnectTimeout = TimeSpan.FromSeconds(10), MaxConnectionsPerServer = 1,
        AutomaticDecompression = System.Net.DecompressionMethods.None
    });

    internal async Task<string> CompleteAsync(string provider, string model, string question, string fixtureToken, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        deadline.CancelAfter(limits.Timeout);
        var ct = deadline.Token;
        try
        {
            ValidateToken(fixtureToken);
            var packet = ProviderProtocols.Build(provider, model, question);
            ValidateEndpoint(provider, model, packet.Endpoint);
            if (Encoding.UTF8.GetByteCount(packet.Json) > 65536) throw Rejected();
            using var request = new HttpRequestMessage(HttpMethod.Post, packet.Endpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Content = new StringContent(packet.Json, Encoding.UTF8, "application/json");
            if (provider == "anthropic")
            {
                request.Headers.Add("x-api-key", fixtureToken);
                request.Headers.Add("anthropic-version", "2023-06-01");
            }
            else if (provider == "gemini") request.Headers.Add("x-goog-api-key", fixtureToken);
            else request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixtureToken);

            using var response = await SendAsync(request, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            // Redirects are never replayed, and a handler that reports a different final URI fails closed.
            if (!response.IsSuccessStatusCode || response.RequestMessage?.RequestUri is { } finalUri && finalUri != packet.Endpoint)
                throw Rejected();
            if (response.Content.Headers.ContentType?.MediaType != "application/json" || response.Content.Headers.ContentEncoding.Count != 0)
                throw Rejected();
            if (response.Content.Headers.ContentLength is > 0 && response.Content.Headers.ContentLength > limits.MaxResponseBytes)
                throw Rejected();
            using var stream = await response.Content.ReadAsStreamAsync(ct).WaitAsync(ct).ConfigureAwait(false);
            using var bytes = new MemoryStream();
            var buffer = new byte[Math.Min(4096, limits.MaxResponseBytes + 1)];
            while (true)
            {
                var remaining = limits.MaxResponseBytes + 1 - (int)bytes.Length;
                var count = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), ct).AsTask().WaitAsync(ct).ConfigureAwait(false);
                if (count == 0) break;
                bytes.Write(buffer, 0, count);
                if (bytes.Length > limits.MaxResponseBytes) throw Rejected();
            }
            ct.ThrowIfCancellationRequested();
            var body = bytes.ToArray();
            ProviderTextOnlyValidation.ValidateHttp(provider, body);
            var answer = ProviderProtocols.Parse(provider, body, ct);
            ct.ThrowIfCancellationRequested();
            return answer;
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
            if (lifetime.IsCancellationRequested) throw new OperationCanceledException("Provider transport disposed.");
            throw new TimeoutException("Provider response exceeded its deadline.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or JsonException or ArgumentException or KeyNotFoundException or IndexOutOfRangeException or FormatException)
        {
            // Deliberately omit the original exception: handler/provider errors can contain headers or input.
            throw Rejected();
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var pending = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        try { return await pending.WaitAsync(ct).ConfigureAwait(false); }
        catch
        {
            // A faulty injected handler may ignore cancellation. Dispose any response it returns later.
            _ = pending.ContinueWith(task =>
            {
                if (task.Status == TaskStatus.RanToCompletion) task.Result.Dispose();
                else _ = task.Exception;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
    }

    internal static void ValidateEndpoint(string provider, string model, Uri endpoint)
    {
        var expected = provider switch
        {
            "openai" => "https://api.openai.com/v1/chat/completions",
            "anthropic" => "https://api.anthropic.com/v1/messages",
            "gemini" => "https://generativelanguage.googleapis.com/v1beta/models/" + Uri.EscapeDataString(model) + ":generateContent",
            "openrouter" => "https://openrouter.ai/api/v1/chat/completions",
            _ => throw Rejected()
        };
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != "https" || endpoint.Port != 443 || endpoint.UserInfo.Length != 0 ||
            endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 || endpoint.AbsoluteUri != expected)
            throw Rejected();
    }

    private static void ValidateToken(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 512 || token.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_' and not '.'))
            throw Rejected();
    }

    internal static InvalidOperationException Rejected() => new("No complete supported provider answer was accepted.");

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        lifetime.Cancel();
        client.Dispose();
        // Keep the source readable by in-flight cancellation handlers; it has no remaining timer/registrations.
        lifetime.Dispose();
    }
}

internal static class ProviderTextOnlyValidation
{
    internal static JsonDocument Parse(ReadOnlyMemory<byte> bytes)
    {
        var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 24 });
        try { RejectDuplicateProperties(document.RootElement); return document; }
        catch { document.Dispose(); throw; }
    }

    private static void RejectDuplicateProperties(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in node.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw ProviderHttpTextTransport.Rejected();
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var child in node.EnumerateArray()) RejectDuplicateProperties(child);
    }

    internal static void ValidateHttp(string provider, ReadOnlyMemory<byte> bytes)
    {
        using var document = Parse(bytes);
        var root = document.RootElement;
        if (provider is "openai" or "openrouter")
        {
            var message = root.GetProperty("choices")[0].GetProperty("message");
            if (message.GetProperty("role").GetString() != "assistant" || message.GetProperty("content").ValueKind != JsonValueKind.String ||
                message.TryGetProperty("audio", out _) || message.TryGetProperty("refusal", out var refusal) && refusal.ValueKind != JsonValueKind.Null)
                throw ProviderHttpTextTransport.Rejected();
        }
        else if (provider == "gemini")
        {
            var content = root.GetProperty("candidates")[0].GetProperty("content");
            if (content.GetProperty("role").GetString() != "model") throw ProviderHttpTextTransport.Rejected();
            foreach (var part in content.GetProperty("parts").EnumerateArray())
                if (part.EnumerateObject().Any(p => p.Name != "text") || part.GetProperty("text").ValueKind != JsonValueKind.String)
                    throw ProviderHttpTextTransport.Rejected();
        }
        // The existing parser requires Anthropic end_turn and exclusively text blocks.
    }
}
