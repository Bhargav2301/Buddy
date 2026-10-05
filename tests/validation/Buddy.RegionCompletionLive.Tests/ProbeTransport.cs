using Buddy.Server;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Buddy.RegionCompletion;

public sealed class ProbeLog : IDisposable
{
    private readonly StreamWriter? file;
    private readonly object sync = new();
    public List<string> Records { get; } = [];
    public ProbeLog(string? output = null) {
        if (output is not null) {
            if (!Path.IsPathFullyQualified(output) || output.StartsWith(@"\\")) throw new ArgumentException("Use an absolute local evidence output path.");
            file = new(new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
        }
    }
    public void Write(object value) { var json = JsonSerializer.Serialize(value); lock (sync) { Records.Add(json); Console.WriteLine(json); file?.WriteLine(json); } }
    public void Dispose() => file?.Dispose();
}

public sealed class ModelAuditHandler(HttpMessageHandler inner, ProbeLog log) : DelegatingHandler(inner)
{
    public string Stage { get; set; } = "readiness";
    public string? ExpectedImageSha256 { get; set; }
    public int ImageRequests { get; private set; }
    public int TextRequests { get; private set; }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri ?? throw new InvalidOperationException("Missing local URI.");
        if (uri.Scheme != "http" || uri.Host != "127.0.0.1" || uri.Port != 11434 || uri.AbsolutePath is not ("/api/chat" or "/api/tags"))
            throw new InvalidOperationException("Harness permits only the existing loopback Ollama endpoint.");
        if (request.Content is not null) {
            using var payload = JsonDocument.Parse(await request.Content.ReadAsStringAsync(ct));
            var messages = new List<object>(); int images = 0;
            foreach (var message in payload.RootElement.GetProperty("messages").EnumerateArray()) {
                var hashes = new List<string>();
                if (message.TryGetProperty("images", out var attached)) foreach (var img in attached.EnumerateArray()) {
                    byte[] bytes = Convert.FromBase64String(img.GetString()!);
                    try { hashes.Add(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()); } finally { Array.Clear(bytes); }
                    images++;
                }
                if (hashes.Any(h => h != ExpectedImageSha256)) throw new InvalidOperationException("Unexpected local image input.");
                messages.Add(new { role = message.GetProperty("role").GetString(), content = message.GetProperty("content").GetString(), imageHashes = hashes });
            }
            if (Stage == "region" && images != 1 || Stage == "research" && images != 0) throw new InvalidOperationException("Image/text research boundary violated.");
            if (images > 0) ImageRequests++; else TextRequests++;
            log.Write(new { kind = "local_model_request", stage = Stage, destination = uri.AbsoluteUri, model = payload.RootElement.GetProperty("model").GetString(), imageCount = images, messages });
        }
        var response = await base.SendAsync(request, ct);
        if (uri.AbsolutePath == "/api/chat") {
            string raw = await response.Content.ReadAsStringAsync(ct);
            if (raw.Length > 60000) { response.Dispose(); throw new InvalidOperationException("Owned model diagnostic response exceeded its bound."); }
            log.Write(new { kind = "local_model_response", stage = Stage, status = (int)response.StatusCode, body = raw });
            response.Content.Dispose(); response.Content = new StringContent(raw, Encoding.UTF8, "application/json");
        }
        return response;
    }
}

public sealed class WebAuditHandler(HttpMessageHandler inner, ProbeLog log) : DelegatingHandler(inner)
{
    public List<Uri> Destinations { get; } = [];
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = WebResearch.ValidateUrl(request.RequestUri?.AbsoluteUri ?? "");
        if (request.Method != HttpMethod.Get || request.Content is not null || request.Headers.Authorization is not null || request.Headers.Contains("Cookie"))
            throw new InvalidOperationException("Research must be public GET text retrieval without body, authorization or cookies.");
        Destinations.Add(uri);
        log.Write(new { kind = "public_web_request", destination = uri.AbsoluteUri, method = "GET", content = (string?)null, image = false, audio = false, cookie = false, authorization = false });
        return await base.SendAsync(request, ct);
    }

    // Same production WebResearch policy, with an additional public-address log.
    // TLS certificate verification remains the SocketsHttpHandler default.
    public static SocketsHttpHandler LiveTransport(ProbeLog log) => new() {
        AllowAutoRedirect = false, UseCookies = false, UseProxy = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        ConnectCallback = async (context, ct) => {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
            if (addresses.Length == 0 || addresses.Any(a => !WebResearch.IsPublicAddress(a))) throw new HttpRequestException("Only public internet addresses are allowed.");
            log.Write(new { kind = "public_dns", host = context.DnsEndPoint.Host, addresses = addresses.Select(a => a.ToString()).ToArray() });
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            try { await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct); return new NetworkStream(socket, ownsSocket: true); }
            catch { socket.Dispose(); throw; }
        }
    };
}

public sealed class ObservedResearch(IWebResearch inner, ProbeLog log) : IWebResearch
{
    public List<string> Queries { get; } = [];
    public List<WebSource> Fetched { get; } = [];
    public List<string> FetchAttempts { get; } = [];
    public async Task<IReadOnlyList<WebSource>> Search(string query, CancellationToken ct) {
        if (query != RegionFixture.ReviewedQuery || Queries.Count != 0) throw new InvalidOperationException("Only the one exact reviewed canned query is authorized in this harness.");
        Queries.Add(query); log.Write(new { kind = "reviewed_query", text = query, utf8Sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(query))), image = false, audio = false, context = false });
        return await inner.Search(query, ct);
    }
    public async Task<WebSource> Fetch(string url, CancellationToken ct) {
        WebResearch.ValidateUrl(url); if (FetchAttempts.Count >= 3) throw new InvalidOperationException("Fetch attempt bound exceeded.");
        FetchAttempts.Add(url); var page = await inner.Fetch(url, ct); WebResearch.ValidateUrl(page.Url); Fetched.Add(page);
        log.Write(new { kind = "fetched_page", url = page.Url, page.Title, textCharacters = page.Text.Length, textSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(page.Text))) }); return page;
    }
}

public sealed class NoWeb : IWebResearch
{
    public Task<IReadOnlyList<WebSource>> Search(string query, CancellationToken ct) => throw new InvalidOperationException("No web consent for region image question.");
    public Task<WebSource> Fetch(string url, CancellationToken ct) => throw new InvalidOperationException("No web consent for region image question.");
}
