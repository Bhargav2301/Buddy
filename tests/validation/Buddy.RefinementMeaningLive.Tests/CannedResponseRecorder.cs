using System.Text;
using System.Text.Json;

// Use only in this canned golden runner. Never share this handler with native
// browser observation, installed profiles or arbitrary user-input pathways.
internal sealed class CannedResponseRecorder : DelegatingHandler
{
    internal string CaseId { get; set; } = "readiness";
    internal int Round { get; set; }
    private int calls;
    internal CannedResponseRecorder() : base(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false }) { }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri is not { Scheme: "http", Host: "127.0.0.1", Port: 11434 } uri ||
            uri.AbsolutePath is not ("/api/chat" or "/api/embed" or "/api/tags" or "/api/show" or "/api/ps" or "/api/version"))
            throw new InvalidOperationException("The canned recorder only permits the fixed loopback model endpoints.");
        var response = await base.SendAsync(request, ct);
        if (uri.AbsolutePath != "/api/chat") return response; // No vectors, headers or credentials are logged.
        try {
            const int maximum = 40 * 1024;
            using var buffer = new MemoryStream();
            await using var input = await response.Content.ReadAsStreamAsync(ct);
            byte[] chunk = new byte[4096];
            while (true) {
                int read = await input.ReadAsync(chunk, ct); if (read == 0) break;
                if (buffer.Length + read > maximum) {
                    Console.WriteLine(JsonSerializer.Serialize(new { kind = "canned_raw_response", caseId = CaseId, round = Round, call = ++calls, outcome = "REFUSED: response exceeds 40KiB", rawBodyLogged = false }));
                    throw new InvalidOperationException("Canned model response exceeded the bounded trace allowance.");
                }
                buffer.Write(chunk, 0, read);
            }
            byte[] body = buffer.ToArray();
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "canned_raw_response", caseId = CaseId, round = Round, call = ++calls,
                statusCode = (int)response.StatusCode, byteCount = body.Length, rawBody = Encoding.UTF8.GetString(body),
                scope = "fixed canned prompt response only; headers and embedding vectors omitted" }));
            var replacement = new ByteArrayContent(body);
            foreach (var header in response.Content.Headers) replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
            response.Content.Dispose(); response.Content = replacement;
            return response;
        } catch { response.Dispose(); throw; }
    }
}
