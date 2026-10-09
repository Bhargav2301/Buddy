using Buddy.Server;
using Microsoft.AspNetCore.DataProtection;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal sealed class Fixture : IDisposable
{
    // These cases exercise wording-path assembly and cancellation; structured plans have separate coverage.
    internal const string Original = "Explain how to request Friday off in a polite email.";
    internal const string Improved = "Please explain how to request Friday off in a polite email.";
    internal readonly string Folder = Path.Combine(Path.GetTempPath(), "Buddy-options-boundary-" + Guid.NewGuid());
    internal readonly Model Model = new();
    private readonly HttpClient client;
    internal StateStore Store { get; }
    internal BuddyService Service { get; }
    internal Fixture()
    {
        if (RefinementContract.Analyze(Original).CanStructure)
            throw new InvalidOperationException("Options fixture must exercise the conservative wording path.");
        Store = new(Folder, new EphemeralDataProtectionProvider());
        client = new(Model) { BaseAddress = new("http://127.0.0.1:11434/") };
        Service = new(Store, new(client)) { AgentEnabled = false, WebEnabled = false };
    }
    public void Dispose() { client.Dispose(); FixtureFiles.Delete(Folder); }
}

// A leaf mock handler: every model operation stays in memory, with no socket fallback.
internal sealed class Model : HttpMessageHandler
{
    internal string Candidate = Fixture.Improved;
    internal readonly List<JsonElement> Requests = [];
    internal Func<CancellationToken, Task>? BeforeChat;
    internal int ChatCalls, AssessmentCalls, EmbeddingCalls;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone();
        Requests.Add(payload);
        if (request.RequestUri!.AbsolutePath == "/api/embed") {
            EmbeddingCalls++;
            return Json(new { embeddings = Enumerable.Range(0, payload.GetProperty("input").GetArrayLength()).Select(_ => new[] { 1f, 0f }) });
        }
        if (request.RequestUri.AbsolutePath != "/api/chat") throw new InvalidOperationException("Unexpected mocked route.");
        if (payload.TryGetProperty("format", out var format) && format.ValueKind == JsonValueKind.Object &&
            format.TryGetProperty("properties", out var properties) && properties.TryGetProperty("sections", out _))
            throw new InvalidOperationException("Unexpected structured plan in wording-path options fixture.");
        if (!payload.GetProperty("stream").GetBoolean()) {
            AssessmentCalls++;
            return Json(new { message = new { content = JsonSerializer.Serialize(new { preserved = true, scoreBefore = 70, scoreAfter = 80, changes = new[] { "Polished wording" } }) }, done = true });
        }
        ChatCalls++;
        if (BeforeChat is not null) await BeforeChat(ct);
        return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { message = new { content = Candidate }, done = true }) + "\n", Encoding.UTF8, "application/x-ndjson") };
    }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
}

internal static class FixtureFiles
{
    internal static void Delete(string folder)
    {
        var path = Path.GetFullPath(folder);
        var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(path).StartsWith("Buddy-options-boundary-", StringComparison.Ordinal)) throw new InvalidOperationException("Unsafe fixture cleanup.");
        if (Directory.Exists(path)) Directory.Delete(path, true);
    }
    internal static void VerifySources()
    {
        string root = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "BuddySourceRoot").Value!;
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("QaReviewedInputs") ?? throw new InvalidOperationException("Missing source receipt.");
        using var reader = new StreamReader(resource);
        var server = Path.GetFullPath(Path.Combine(root, "services", "Buddy.Server"));
        var compiled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is { } line) {
            var fields = line.Split('|');
            if (fields.Length != 2 || !File.Exists(fields[0]) || !Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fields[0]))).Equals(fields[1], StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Source changed after compilation: " + fields[0]);
            if (Path.GetDirectoryName(fields[0])!.Equals(server, StringComparison.OrdinalIgnoreCase)) compiled.Add(Path.GetFullPath(fields[0]));
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "reviewed_source", path = fields[0], sha256 = fields[1] }));
        }
        if (!compiled.SetEquals(Directory.GetFiles(server, "*.cs").Select(Path.GetFullPath))) throw new InvalidOperationException("Server source inventory changed after compilation.");
        Console.WriteLine("SCOPE: production source links; mock-only model; explicit owned temporary files; no desktop, installed profile, accounts, socket transport or executed actions.");
    }
}
