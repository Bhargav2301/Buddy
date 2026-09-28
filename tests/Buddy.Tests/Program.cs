using Buddy.Server;
using Microsoft.AspNetCore.DataProtection;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

static void Assert(bool value, string title) { if (!value) throw new Exception("FAILED: " + title); Console.WriteLine("PASS: " + title); }
static async Task<List<StreamEvent>> Collect(IAsyncEnumerable<StreamEvent> source) { var events = new List<StreamEvent>(); await foreach (var e in source) events.Add(e); return events; }
static async Task Reject(Func<Task> action, string code, string title) { try { await action(); } catch (BuddyException e) when (e.Code == code) { Assert(true, title); return; } throw new Exception("FAILED: " + title); }

var directory = Path.Combine(Path.GetTempPath(), "buddy-tests-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
try
{
    var provider = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(directory, "keys")));
    var store = new StateStore(directory, provider); await store.EnsureSaved();
    var handler = new StubOllama(); var engine = new OllamaEngine(new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:11434/") });
    var service = new BuddyService(store, engine);
    Assert((await engine.Status("qwen3:4b-instruct-2507-q4_K_M", "gemma3:4b")).Ready, "model availability reflects Ollama response");
    Assert(!(await engine.Status("missing:model", "gemma3:4b")).Ready, "missing model is not reported ready");
    handler.Fragments = ["Internal planning that must not be shown</th", "ink>Final answer"];
    var cleaned = new StringBuilder(); await foreach (var part in engine.Chat("qwen3:0.6b", [new { role = "user", content = "Hello" }], CancellationToken.None)) cleaned.Append(part);
    Assert(cleaned.ToString() == "Final answer", "reasoning prefix is excluded even when closing tag spans stream chunks");
    handler.Fragments = ["Hello ", "from Buddy."];
    var chat = await service.CreateConversation(null); var id = Guid.NewGuid().ToString();
    var request = new ChatRequest(chat.Id, "Hello Buddy", id, "hybrid", "otp: 123456", Convert.ToBase64String(Encoding.UTF8.GetBytes("private-frame-marker")));
    var stream = await Collect(service.Chat(request, CancellationToken.None));
    Assert(stream.Any(e => e.Type == "done") && string.Concat(stream.Where(e => e.Type == "delta").Select(e => e.Text)) == "Hello from Buddy.", "streamed answer completes and preserves deltas");
    var saved = await store.Read(s => s);
    Assert(saved.Conversations.Single().Messages.Count == 2, "one completed request stores exactly one user/assistant pair");
    var serialized = JsonSerializer.Serialize(saved);
    Assert(!serialized.Contains("private-frame-marker") && !serialized.Contains(request.ImageBase64!) && !serialized.Contains("123456"), "frames and raw screen context never enter persisted conversation data");
    Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(directory, "buddy.v1.encrypted"))).Contains("Hello Buddy"), "conversation store is encrypted at rest");
    Assert(handler.LastPayload.Contains("[redacted]"), "screen OTP is redacted before inference");
    var reloaded = new StateStore(directory, provider); Assert((await reloaded.Read(s => s.Conversations)).Single().Messages.Count == 2, "encrypted history survives reopening");
    int count = handler.Calls; await Collect(service.Chat(request, CancellationToken.None)); Assert(handler.Calls == count, "duplicate request replays stored answer without a second model call");
    await Reject(async () => await Collect(service.Chat(request with { Text = "Different" }, CancellationToken.None)), "REQUEST_CONFLICT", "idempotency key cannot be reused for changed text");
    await Reject(async () => await Collect(service.Chat(request with { RequestId = "invalid" }, CancellationToken.None)), "INVALID_ID", "malformed request IDs are rejected");
    await Reject(async () => await Collect(service.Chat(request with { Text = "" }, CancellationToken.None)), "INVALID_INPUT", "empty messages are rejected");
    var originalCount = (await store.Read(s => s.Conversations.Single().Messages.Count));
    await service.Refine("Draft an email", CancellationToken.None); Assert(await store.Read(s => s.Conversations.Single().Messages.Count) == originalCount, "refine does not send a chat message");
    var pairing = new PairingWindow(); var open = pairing.Open(); pairing.Redeem(open.Code);
    await Reject(() => { pairing.Redeem(open.Code); return Task.CompletedTask; }, "PAIRING_CLOSED", "pairing codes are single-use");
    open = pairing.Open(); for (int i = 0; i < 5; i++) { try { pairing.Redeem("000000"); } catch (BuddyException) { } }
    await Reject(() => { pairing.Redeem(open.Code); return Task.CompletedTask; }, "PAIRING_CLOSED", "five wrong pairing attempts close the pairing window");
    var token = Security.NewToken(); await store.Update(s => { s.Devices.Add(new("phone", "Test phone", Security.Hash(token), DateTimeOffset.UtcNow)); return true; });
    Assert(await store.Authenticate(token) == "phone" && await store.Authenticate("bad") is null, "device authentication validates hashed tokens");
    await store.Update(s => s.Devices.ClearResult()); Assert(await store.Authenticate(token) is null, "revocation takes effect on the next request");
    handler.Delay = true;
    using var cancel = new CancellationTokenSource(); var generating = Collect(service.Chat(new(chat.Id, "Wait", Guid.NewGuid().ToString()), cancel.Token));
    await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await Reject(async () => await Collect(service.Chat(new(chat.Id, "Parallel", Guid.NewGuid().ToString()), CancellationToken.None)), "BUSY", "concurrent writes to one conversation are blocked");
    await Reject(() => service.DeleteConversation(chat.Id), "BUSY", "deleting an active conversation is rejected");
    cancel.Cancel(); try { await generating; } catch (OperationCanceledException) { }
    Assert(await store.Read(s => s.Conversations.Single().Messages.Count) == originalCount, "cancelled answers do not leave partial history");
    handler.Delay = false; await Collect(service.Chat(new(chat.Id, "After cancel", Guid.NewGuid().ToString()), CancellationToken.None));
    Assert(await store.Read(s => s.Conversations.Single().Messages.Count) == originalCount + 2, "cancellation releases generation lock");
    await service.DeleteConversation(chat.Id); Assert((await store.Read(s => s.Conversations)).Count == 0, "conversation deletion persists");

    // Real TLS and HTTP integration, with only the upstream inference response stubbed.
    var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
    await using var host = await BuddyHost.Start(Path.Combine(directory, "http"), port);
    using var tls = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, certificate, _, _) => certificate is not null && Convert.ToHexString(SHA256.HashData(certificate.RawData)) == host.Fingerprint }) { BaseAddress = new Uri($"https://127.0.0.1:{port}") };
    Assert((await tls.GetAsync("/v1/conversations")).StatusCode == HttpStatusCode.Unauthorized, "API refuses unauthenticated history access");
    var code = host.Service.Pairing.Open().Code;
    var pairResponse = await tls.PostAsJsonAsync("/v1/pair", new PairRequest(code, "Integration phone")); pairResponse.EnsureSuccessStatusCode();
    var pairJson = await pairResponse.Content.ReadFromJsonAsync<JsonElement>(); tls.DefaultRequestHeaders.Authorization = new("Bearer", pairJson.GetProperty("token").GetString());
    var created = await tls.PostAsJsonAsync("/v1/conversations", new NoteRequest("Across devices", "")); created.EnsureSuccessStatusCode();
    Assert((await tls.PostAsJsonAsync("/v1/agent/plan", new PlanningRequest("Open Notepad", new("example", "Example", [])))).StatusCode == HttpStatusCode.Forbidden, "paired phones cannot request PC action plans");
    Assert((await tls.GetFromJsonAsync<JsonElement>("/v1/conversations")).GetArrayLength() == 1, "paired phone sees shared conversation store through pinned HTTPS");
    Assert((await tls.PostAsJsonAsync("/v1/pair", new PairRequest(code, "Other phone"))).StatusCode == HttpStatusCode.Forbidden, "HTTP pairing code replay is rejected");
    await host.Service.Store.Update(s => s.Devices.ClearResult()); Assert((await tls.GetAsync("/v1/conversations")).StatusCode == HttpStatusCode.Unauthorized, "revoked phone loses API access");
    using var wrong = new HttpClient(new HttpClientHandler { ServerCertificateCustomValidationCallback = (_, _, _, _) => false });
    bool rejected = false; try { await wrong.GetAsync($"https://127.0.0.1:{port}/health"); } catch (HttpRequestException) { rejected = true; }
    Assert(rejected, "untrusted TLS certificate is rejected");
    Console.WriteLine("ALL BUDDY SERVICE TESTS PASSED");
}
finally { Directory.Delete(directory, true); }

static class Helpers { public static bool ClearResult<T>(this List<T> list) { list.Clear(); return true; } }
sealed class StubOllama : HttpMessageHandler
{
    public int Calls; public string LastPayload = ""; public bool Delay; public string[] Fragments = ["Hello ", "from Buddy."]; public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri!.AbsolutePath == "/api/tags") return new(HttpStatusCode.OK) { Content = new StringContent("{\"models\":[{\"name\":\"qwen3:4b-instruct-2507-q4_K_M\"},{\"name\":\"gemma3:4b\"}]}") };
        Calls++; LastPayload = await request.Content!.ReadAsStringAsync(ct);
        if (Delay) { Started.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
        return new(HttpStatusCode.OK) { Content = new StringContent(string.Join("\n", Fragments.Select(content => JsonSerializer.Serialize(new { message = new { content }, done = false }))) + "\n{\"message\":{\"content\":\"\"},\"done\":true}\n") };
    }
}
