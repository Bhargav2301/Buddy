using Buddy.Server;
using Microsoft.AspNetCore.DataProtection;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Synthetic canned data only. No socket, desktop, installed profile or action executor.
internal sealed class Fixture : IDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "Buddy-reliability-boundary-" + Guid.NewGuid());
    readonly HttpClient client;
    public readonly MockModel Model = new();
    public StateStore Store { get; }
    public BuddyService Service { get; }
    public Fixture()
    {
        Store = new(folder, new EphemeralDataProtectionProvider());
        client = new(Model) { BaseAddress = new("http://127.0.0.1:11434/") };
        Service = new(Store, new(client)) { AgentEnabled = true, WebEnabled = false };
    }
    public async Task<(List<StreamEvent> Events, List<ChatMessage> Saved)> Chat(string prompt, CancellationToken ct = default)
    {
        var conversation = await Service.CreateConversation("Owned synthetic QA reliability fixture");
        var events = new List<StreamEvent>();
        await foreach (var item in Service.Chat(new(conversation.Id, prompt, Guid.NewGuid().ToString()), ct)) events.Add(item);
        return (events, await Store.Read(s => s.Conversations.Single(c => c.Id == conversation.Id).Messages));
    }
    public void Dispose()
    {
        client.Dispose();
        FixtureFiles.DeleteTemporary(folder, "Buddy-reliability-boundary-");
    }
}

internal sealed class MockModel : HttpMessageHandler
{
    public Queue<Func<JsonElement, CancellationToken, Task<HttpResponseMessage>>> Replies { get; } = new();
    public List<JsonElement> Requests { get; } = [];
    public int PlainCalls => Requests.Count(r => r.TryGetProperty("stream", out var s) && s.GetBoolean());
    public int StructuredCalls => Requests.Count - PlainCalls;
    public void Text(string text) => Replies.Enqueue((_, _) => Task.FromResult(Response(text)));
    public void Json(object value) => Text(JsonSerializer.Serialize(value, StateStore.Json));
    public void Raw(string raw, string doneReason = "stop") => Replies.Enqueue((_, _) => Task.FromResult(Response(raw, doneReason)));
    public static HttpResponseMessage Response(string content, string doneReason = "stop") => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { message = new { content }, done = true, done_reason = doneReason }) + "\n", Encoding.UTF8, "application/x-ndjson")
    };
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var root = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone();
        if (request.RequestUri?.AbsolutePath == "/api/embed")
        {
            int count = root.GetProperty("input").GetArrayLength();
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { embeddings = Enumerable.Range(0, count).Select(_ => new[] { 1f, 0f, 0f }) }), Encoding.UTF8, "application/json") };
        }
        if (request.RequestUri?.AbsolutePath != "/api/chat") throw new InvalidOperationException("Unexpected mock-only route: " + request.RequestUri?.AbsolutePath);
        Requests.Add(root);
        if (!Replies.TryDequeue(out var next)) throw new InvalidOperationException("The service exceeded this fixture's inference allowance.");
        return await next(root, ct);
    }
}

internal static class FixtureFiles
{
    public static void DeleteTemporary(string folder, string prefix)
    {
        var resolved = Path.GetFullPath(folder);
        var root = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing fixture cleanup outside its owned temporary directory.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
    }
    public static void SourceManifest(bool realModel = false)
    {
        string root = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "BuddySourceRoot").Value!;
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("QaReviewedInputs") ?? throw new InvalidOperationException("Missing build-time source manifest.");
        using var reader = new StreamReader(resource);
        string serverRoot = Path.GetFullPath(Path.Combine(root, "services", "Buddy.Server"));
        var compiledServerFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is { } line)
        {
            var fields = line.Split('|');
            if (fields.Length != 2 || !File.Exists(fields[0]) || !Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fields[0]))).Equals(fields[1], StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Source changed since this fixture was built; rebuild against a stable checkpoint: " + fields[0]);
            if (Path.GetDirectoryName(fields[0])!.Equals(serverRoot, StringComparison.OrdinalIgnoreCase)) compiledServerFiles.Add(Path.GetFullPath(fields[0]));
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "reviewed_source", path = Path.GetRelativePath(root, fields[0]), sha256 = fields[1] }));
        }
        if (!compiledServerFiles.SetEquals(Directory.GetFiles(serverRoot, "*.cs").Select(Path.GetFullPath)))
            throw new InvalidOperationException("Production source inventory changed since build; rebuild the fixture.");
        var assembly = Assembly.GetExecutingAssembly();
        Console.WriteLine(JsonSerializer.Serialize(new { kind = "environment", sourceRoot = root, assembly = assembly.GetName().Version?.ToString(), assemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))), model = realModel ? "real local model" : "mock-only", installedProfile = false, desktop = false, executedActions = false }));
    }
}
