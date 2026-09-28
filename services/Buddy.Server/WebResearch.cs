using System.Net;
using System.Net.Sockets;
using System.Text;
using AngleSharp.Html.Parser;
using Microsoft.AspNetCore.WebUtilities;

namespace Buddy.Server;

public interface IWebResearch
{
    Task<IReadOnlyList<WebSource>> Search(string query, CancellationToken ct);
    Task<WebSource> Fetch(string url, CancellationToken ct);
}

// No browser cookies, credentials, script execution or screenshot transport.
public sealed class WebResearch : IWebResearch, IDisposable
{
    public const int MaxBytes = 500_000;
    private readonly HttpClient client;
    private readonly SemaphoreSlim gate = new(1, 1);
    private DateTimeOffset lastRequest;
    public WebResearch() : this(new SocketsHttpHandler {
        AllowAutoRedirect = false, UseCookies = false, UseProxy = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        ConnectCallback = async (context, ct) => {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
            if (addresses.Length == 0 || addresses.Any(a => !IsPublicAddress(a))) throw new HttpRequestException("Only public internet addresses are allowed.");
            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
            try { await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct); return new NetworkStream(socket, ownsSocket: true); }
            catch { socket.Dispose(); throw; }
        }
    }) { }
    public WebResearch(HttpMessageHandler handler) { client = new(handler) { Timeout = Timeout.InfiniteTimeSpan }; client.DefaultRequestHeaders.UserAgent.ParseAdd("Buddy/0.3 (personal web research)"); }
    public static Uri ValidateUrl(string text)
    {
        if (text.Length > 2048 || !Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Port != 443 || uri.UserInfo.Length > 0 || uri.HostNameType == UriHostNameType.Unknown ||
            uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || !uri.Host.Contains('.') || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && !IsPublicAddress(ip)))
            throw new BuddyException("WEB_URL_BLOCKED", "Use a public HTTPS page, without credentials or a custom port.");
        return uri;
    }
    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return false;
        var b = address.GetAddressBytes();
        // Exclude transition/special-use space as well as documentation ranges.
        if (b.Length == 16) return (b[0] & 0xe0) == 0x20 && !(b[0] == 0x20 && b[1] == 2) &&
            !(b[0] == 0x20 && b[1] == 1 && (b[2] < 2 || (b[2] == 0x0d && b[3] == 0xb8)));
        return b[0] is not (0 or 10 or 127) && b[0] < 224 && !(b[0] == 169 && b[1] == 254) && !(b[0] == 172 && b[1] is >= 16 and <= 31) &&
            !(b[0] == 192 && (b[1] == 168 || b[1] == 0 || b[1] == 2)) && !(b[0] == 100 && b[1] is >= 64 and <= 127) &&
            !(b[0] == 198 && (b[1] is 18 or 19 || b[1] == 51)) && !(b[0] == 203 && b[1] == 0 && b[2] == 113);
    }
    private async Task<(string Text, Uri Url)> Download(string url, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await gate.WaitAsync(timeout.Token);
        try {
            var delay = lastRequest.AddMilliseconds(700) - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero) await Task.Delay(delay, timeout.Token);
            lastRequest = DateTimeOffset.UtcNow;
            var uri = ValidateUrl(url);
            for (int hop = 0; hop < 4; hop++) {
                using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } next) { uri = ValidateUrl(new Uri(uri, next).AbsoluteUri); continue; }
                if (!response.IsSuccessStatusCode) throw new BuddyException("WEB_UNAVAILABLE", "The website declined the request. Try another public page.");
                var mime = response.Content.Headers.ContentType?.MediaType;
                if (mime is not ("text/html" or "text/plain" or "application/xhtml+xml")) throw new BuddyException("WEB_CONTENT", "Only text and HTML pages are supported.");
                if (response.Content.Headers.ContentLength > MaxBytes) throw new BuddyException("WEB_TOO_LARGE", "The page exceeds the 500 KB research limit.");
                using var stream = await response.Content.ReadAsStreamAsync(timeout.Token); using var buffer = new MemoryStream();
                var bytes = new byte[8192]; int read;
                while ((read = await stream.ReadAsync(bytes, timeout.Token)) > 0) { if (buffer.Length + read > MaxBytes) throw new BuddyException("WEB_TOO_LARGE", "The page exceeds the 500 KB research limit."); buffer.Write(bytes, 0, read); }
                return (Encoding.UTF8.GetString(buffer.ToArray()), uri);
            }
            throw new BuddyException("WEB_REDIRECT", "The website redirected too many times.");
        } finally { gate.Release(); }
    }
    public async Task<WebSource> Fetch(string url, CancellationToken ct)
    {
        var page = await Download(url, ct); using var doc = new HtmlParser().ParseDocument(page.Text);
        foreach (var node in doc.QuerySelectorAll("script,style,noscript,iframe,svg,nav,footer,form")) node.Remove();
        var text = string.Join(' ', (doc.QuerySelector("main,article") ?? doc.Body)?.TextContent.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) ?? []);
        var title = Security.Redact(string.IsNullOrWhiteSpace(doc.Title) ? page.Url.Host : doc.Title);
        return new(title[..Math.Min(180, title.Length)], page.Url.AbsoluteUri, Security.Redact(text[..Math.Min(16000, text.Length)]));
    }
    public async Task<IReadOnlyList<WebSource>> Search(string query, CancellationToken ct)
    {
        query = Security.Redact(Security.Text(query, 300, "Search query"));
        var page = await Download("https://html.duckduckgo.com/html/?q=" + Uri.EscapeDataString(query), ct);
        using var doc = new HtmlParser().ParseDocument(page.Text); var results = new List<WebSource>();
        foreach (var link in doc.QuerySelectorAll("a.result__a")) {
            var href = link.GetAttribute("href") ?? "";
            if (Uri.TryCreate(page.Url, href, out var redirect) && QueryHelpers.ParseQuery(redirect.Query).TryGetValue("uddg", out var destination)) href = destination.ToString();
            try { var target = ValidateUrl(href); var snippet = link.Closest(".result")?.QuerySelector(".result__snippet")?.TextContent ?? ""; results.Add(new(link.TextContent.Trim(), target.AbsoluteUri, Security.Redact(snippet))); } catch (BuddyException) { }
            if (results.Count == 5) break;
        }
        if (results.Count == 0) throw new BuddyException("SEARCH_UNAVAILABLE", "Web search returned no readable results. The provider may be limiting requests; paste a public HTTPS page instead.");
        return results;
    }
    public void Dispose() { client.Dispose(); gate.Dispose(); }
}
