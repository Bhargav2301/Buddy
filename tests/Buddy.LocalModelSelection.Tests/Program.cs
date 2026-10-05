using Buddy.Server;
using Microsoft.AspNetCore.DataProtection;
using System.Net;
using System.Text;
using System.Text.Json;

int checks = 0, failures = 0;
void Check(bool ok, string label) { checks++; if (!ok) failures++; Console.WriteLine((ok ? "PASS: " : "FAIL: ") + label); }
async Task<List<StreamEvent>> Complete(Fixture f, ChatRequest request, CancellationToken ct = default)
{ var result = new List<StreamEvent>(); await foreach (var item in f.Service.Chat(request, ct)) result.Add(item); return result; }
async Task Reject(Fixture f, ChatRequest request, string code, string label)
{
    try { await Complete(f, request); Check(false, label + " (no rejection)"); }
    catch (BuddyException ex) { Check(ex.Code == code, label + " (" + ex.Code + ")"); }
}
await using (var f = new Fixture()) {
    var conversation = await f.Initialize(); var request = new ChatRequest(conversation.Id, "Explain the word pebble.", Guid.NewGuid().ToString(), LocalModel: Fixture.Alternate);
    var answer = await Complete(f, request);
    Check(f.Http.ChatModels.SequenceEqual([Fixture.Alternate]), "Actual Chat route sends the installed alternate model to the engine");
    Check(f.Http.Tags == 1 && f.Http.Shows == 1 && f.Http.Pulls == 0, "Override checks installed models and local metadata once without downloading anything");
    using (var metadataRequest = JsonDocument.Parse(f.Http.ShowPayloads.Single()))
        Check(metadataRequest.RootElement.EnumerateObject().Count() == 1 && metadataRequest.RootElement.GetProperty("model").GetString() == Fixture.Alternate,
            "Model verification payload contains only the selected model identifier, no prompt or context");
    Check(answer.Single(e => e.Type == "delta").Text == Fixture.Answer && answer.Last().Type == "done", "Selected-model request completes the production response/store path");
    Check(answer.Any(e => e.Type == "brain" && e.BrainId == "local"), "Response identifies the local brain");
    Check(await f.Store.Read(s => s.Model == Fixture.Default && s.VisionModel == Fixture.Vision), "Per-request override leaves saved chat and vision defaults unchanged");
    Check(await f.Store.Read(s => s.Conversations.Single().Messages.Count == 2 && s.Conversations.Single().Messages.All(m => m.LocalModel == Fixture.Alternate)), "Both stored message roles retain the exact per-request model selection");
    Check(!Encoding.UTF8.GetString(File.ReadAllBytes(f.StatePath)).Contains(request.Text, StringComparison.Ordinal), "Owned conversation state is encrypted instead of stored as plaintext");
    int calls = f.Http.Calls;
    f.Http.Installed = [Fixture.Default];
    var replay = await Complete(f, request);
    Check(f.Http.Calls == calls && replay.Single(e => e.Type == "delta").Text == Fixture.Answer, "Exact request replay uses the stored answer with no status or inference call, even after model removal");
    Check(await f.Store.Read(s => s.Conversations.Single().Messages.Count) == 2, "Exact replay appends no duplicate messages");
    await Reject(f, request with { LocalModel = Fixture.Default }, "REQUEST_CONFLICT", "Same request ID cannot select a different model");
    await Reject(f, request with { LocalModel = null }, "REQUEST_CONFLICT", "Same request ID cannot omit the original explicit model selection");
    await Reject(f, request with { Text = "A different question." }, "REQUEST_CONFLICT", "Model selection does not relax request text binding");
    Check(f.Http.Calls == calls && await f.Store.Read(s => s.Conversations.Single().Messages.Count) == 2, "Conflicting replays neither infer nor change the conversation");
}
await using (var f = new Fixture()) {
    var c = await f.Initialize(); var request = new ChatRequest(c.Id, "Explain the word pebble.", Guid.NewGuid().ToString());
    var result = await Complete(f, request);
    Check(f.Http.ChatModels.SequenceEqual([Fixture.Default]) && f.Http.Tags == 0, "Legacy request without an override keeps its saved default and old engine route");
    Check(await f.Store.Read(s => s.Conversations.Single().Messages.All(m => m.LocalModel is null)), "Legacy messages retain null selection metadata");
    int calls = f.Http.Calls; await Complete(f, request);
    Check(f.Http.Calls == calls, "Legacy null-selection replay remains idempotent");
    await Reject(f, request with { LocalModel = Fixture.Default }, "REQUEST_CONFLICT", "A legacy request cannot acquire an explicit model on replay even when defaults match");
    Check(result.Last().Type == "done", "Legacy request still completes normally");
}
foreach (string selected in new[] { "owned-missing:latest", "OWNED-ALTERNATE:latest" }) await using (var f = new Fixture()) {
    var c = await f.Initialize(); await Reject(f, new(c.Id, "Explain pebble.", Guid.NewGuid().ToString(), LocalModel: selected), "LOCAL_MODEL_UNAVAILABLE", "Missing or case-mismatched installed model fails closed: " + selected);
    Check(f.Http.Tags == 1 && f.Http.ChatModels.Count == 0 && f.Http.Pulls == 0, "Unavailable selection causes no chat inference or download");
    Check(await f.Store.Read(s => s.Conversations.Single().Messages.Count == 0 && s.Model == Fixture.Default), "Unavailable selection leaves history and default unchanged");
}
foreach (string invalid in new[] { "", " ", " owned-alternate:latest", "owned-alternate:latest ", "owned\nalternate", "owned\0alternate", "owned\talternate", new string('a', 201) }) await using (var f = new Fixture()) {
    var c = await f.Initialize(); await Reject(f, new(c.Id, "Explain pebble.", Guid.NewGuid().ToString(), LocalModel: invalid), "INVALID_LOCAL_MODEL", "Malformed model identifier is rejected before transport: " + JsonSerializer.Serialize(invalid));
    Check(f.Http.Calls == 0 && await f.Store.Read(s => s.Conversations.Single().Messages.Count) == 0, "Malformed selection never queries models, infers or saves a turn");
}
foreach (string? brain in new[] { "chatgpt", "claude", "gemini", "grok", "unknown-cloud", "" }) await using (var f = new Fixture()) {
    var c = await f.Initialize(); await Reject(f, new(c.Id, "Explain pebble.", Guid.NewGuid().ToString(), BrainId: brain, LocalModel: Fixture.Alternate), "LOCAL_MODEL_SCOPE", "Explicit nonlocal brain cannot use a local override: " + brain);
    Check(f.Http.Calls == 0, "Rejected nonlocal override makes no engine request");
}
foreach (string phrase in new[] { "Use ChatGPT for this explanation.", "use Claude for this explanation.", "Use Gemini for this explanation.", "Use Grok for this explanation.", "Use Codex for this explanation." }) await using (var f = new Fixture()) {
    var c = await f.Initialize(); await Reject(f, new(c.Id, phrase, Guid.NewGuid().ToString(), LocalModel: Fixture.Alternate), "LOCAL_MODEL_SCOPE", "Explicit cloud phrase cannot silently combine with local selection: " + phrase);
    Check(f.Http.Calls == 0, "Rejected explicit cloud phrase makes no engine request");
}
foreach (string image in new[] { "", Convert.ToBase64String([1, 2, 3]) }) await using (var f = new Fixture()) {
    var c = await f.Initialize(); await Reject(f, new(c.Id, "Explain this image.", Guid.NewGuid().ToString(), ImageBase64: image, LocalModel: Fixture.Alternate), "LOCAL_MODEL_SCOPE", "Image request cannot use text-only per-request selection");
    Check(f.Http.Calls == 0, "Image/model scope rejection precedes any engine request");
}
await using (var f = new Fixture()) {
    var c = await f.Initialize(); var request = new ChatRequest(c.Id, "Use local to explain the word pebble.", Guid.NewGuid().ToString(), Context: "Owned nonprivate text context.", BrainId: "local", LocalModel: Fixture.Alternate);
    var events = await Complete(f, request);
    Check(f.Http.ChatModels.SequenceEqual([Fixture.Alternate]) && events.Last().Type == "done", "Explicit local brain and phrase support installed text override with local context");
    using var payload = JsonDocument.Parse(f.Http.ChatPayloads.Single());
    Check(payload.RootElement.GetProperty("messages").EnumerateArray().Any(m => m.GetProperty("content").GetString()!.Contains("Owned nonprivate text context.")), "Allowed text context reaches the selected local engine");
}
await using (var f = new Fixture()) {
    var c = await f.Initialize(); f.Http.Offline = true;
    await Reject(f, new(c.Id, "Explain pebble.", Guid.NewGuid().ToString(), LocalModel: Fixture.Alternate), "LOCAL_MODEL_UNAVAILABLE", "Unreachable installed-model query refuses the override");
    Check(f.Http.Tags == 1 && f.Http.ChatModels.Count == 0 && f.Http.Pulls == 0, "Offline selection cannot infer or download by fallback");
}
await using (var f = new Fixture()) {
    var c = await f.Initialize(); using var stop = new CancellationTokenSource(); stop.Cancel();
    try { await Complete(f, new(c.Id, "Explain pebble.", Guid.NewGuid().ToString(), LocalModel: Fixture.Alternate), stop.Token); Check(false, "Precancelled override stops"); }
    catch (OperationCanceledException) { Check(true, "Precancelled override stops"); }
    Check(f.Http.Calls == 0 && await f.Store.Read(s => s.Conversations.Single().Messages.Count) == 0, "Precancelled override performs no engine request or saved turn");
}
foreach (var (label, metadata) in new (string, string)[] {
    ("remote host", """{"remote_host":"https://cloud.example","details":{"format":"gguf"},"model_info":{"general.architecture":"gemma3"}}"""),
    ("remote model", """{"remote_model":"cloud-model","details":{"format":"gguf"},"model_info":{"general.architecture":"gemma3"}}"""),
    ("typed remote host", """{"remote_host":false,"details":{"format":"gguf"},"model_info":{"general.architecture":"gemma3"}}"""),
    ("typed remote model", """{"remote_model":42,"details":{"format":"gguf"},"model_info":{"general.architecture":"gemma3"}}"""),
    ("missing details", """{"model_info":{"general.architecture":"gemma3"}}"""),
    ("null details", """{"details":null,"model_info":{"general.architecture":"gemma3"}}"""),
    ("missing format", """{"details":{},"model_info":{"general.architecture":"gemma3"}}"""),
    ("unknown format", """{"details":{"format":"remote"},"model_info":{"general.architecture":"gemma3"}}"""),
    ("null format", """{"details":{"format":null},"model_info":{"general.architecture":"gemma3"}}"""),
    ("missing model info", """{"details":{"format":"gguf"}}"""),
    ("null model info", """{"details":{"format":"gguf"},"model_info":null}"""),
    ("missing architecture", """{"details":{"format":"gguf"},"model_info":{}}"""),
    ("null architecture", """{"details":{"format":"gguf"},"model_info":{"general.architecture":null}}"""),
    ("blank architecture", """{"details":{"format":"gguf"},"model_info":{"general.architecture":" "}}"""),
    ("typed architecture", """{"details":{"format":"gguf"},"model_info":{"general.architecture":12}}"""),
    ("null metadata", "null"), ("array metadata", "[]"), ("malformed JSON", "{broken")
}) await using (var f = new Fixture()) {
    var c = await f.Initialize(); f.Http.Metadata = metadata;
    await Reject(f, new(c.Id, "Explain pebble.", Guid.NewGuid().ToString(), LocalModel: Fixture.Alternate), "LOCAL_MODEL_UNVERIFIED", "Unverified local model metadata is refused: " + label);
    Check(f.Http.Tags == 1 && f.Http.Shows == 1 && f.Http.ChatModels.Count == 0 && f.Http.Pulls == 0,
        "Unverified metadata sends no prompt and never falls back or pulls: " + label);
    Check(await f.Store.Read(s => s.Conversations.Single().Messages.Count == 0 && s.Model == Fixture.Default), "Unverified metadata preserves the conversation and saved model: " + label);
}
await using (var f = new Fixture()) {
    var c = await f.Initialize(); f.Http.Metadata = """{"remote_host":null,"remote_model":"","details":{"format":"gguf"},"model_info":{"general.architecture":"gemma3"}}""";
    await Complete(f, new(c.Id, "Explain pebble.", Guid.NewGuid().ToString(), LocalModel: Fixture.Alternate));
    Check(f.Http.ChatModels.SequenceEqual([Fixture.Alternate]) && f.Http.Shows == 1, "Verified local weights allow absent/empty remote fields without a cloud route");
}
await using (var f = new Fixture()) {
    var c = await f.Initialize(); f.Http.ShowStatus = HttpStatusCode.NotFound;
    await Reject(f, new(c.Id, "Explain pebble.", Guid.NewGuid().ToString(), LocalModel: Fixture.Alternate), "LOCAL_MODEL_UNVERIFIED", "Missing metadata response refuses the selected model");
    Check(f.Http.ChatModels.Count == 0 && f.Http.Pulls == 0, "Metadata HTTP failure sends no prompt or download");
}
foreach (string address in new[] { "http://example.invalid:11434/", "https://127.0.0.1:11434/" }) await using (var f = new Fixture(address)) {
    var c = await f.Initialize();
    await Reject(f, new(c.Id, "Explain pebble.", Guid.NewGuid().ToString(), LocalModel: Fixture.Alternate), "LOCAL_MODEL_UNVERIFIED", "Per-request model rejects a non-loopback-HTTP engine: " + address);
    Check(f.Http.ChatModels.Count == 0 && f.Http.Shows == 0 && f.Http.Pulls == 0, "Rejected engine location receives no model-show request, prompt or pull");
}
await using (var f = new Fixture()) {
    var c = await f.Initialize(); const string attached = "OWNED_REVIEWED_FILE_CONTEXT_54";
    var events = await Complete(f, new(c.Id, "Explain pebble.", Guid.NewGuid().ToString(), Context: attached, ScreenApp: "not-a-screen", LocalModel: Fixture.Alternate, ContextKind: "file"));
    var evidence = events.Last(e => e.Type == "evidence").Evidence!;
    Check(evidence.File && !evidence.Screen && !evidence.Image && evidence.App is null, "Reviewed file-only context is not presented as screen capture");
    using var payload = JsonDocument.Parse(f.Http.ChatPayloads.Single());
    string input = payload.RootElement.GetProperty("messages").EnumerateArray().Last().GetProperty("content").GetString()!;
    Check(input.Contains("<untrusted_file_context>") && input.Contains(attached) && !input.Contains("<untrusted_screen_context>"), "Reviewed text reaches the model with truthful untrusted file provenance");
    var messages = await f.Store.Read(s => s.Conversations.Single().Messages);
    Check(messages.All(m => m.Evidence is { File: true, Screen: false, Image: false, App: null }), "Saved user/assistant evidence retains file provenance");
    Check(messages.All(m => !m.Text.Contains(attached)), "Raw attachment context is not silently saved as a message");
}
foreach (string? kind in new string?[] { null, "screen" }) await using (var f = new Fixture()) {
    var c = await f.Initialize(); var events = await Complete(f, new(c.Id, "Explain pebble.", Guid.NewGuid().ToString(), Context: "Owned visible text.", ScreenApp: "owned-app", ContextKind: kind));
    Check(events.Last(e => e.Type == "evidence").Evidence is { Screen: true, File: false, App: "owned-app" }, "Legacy and explicit screen provenance remain compatible");
}
foreach (string kind in new[] { "", "cloud", "FILE", "file\n" }) await using (var f = new Fixture()) {
    var c = await f.Initialize(); await Reject(f, new(c.Id, "Explain pebble.", Guid.NewGuid().ToString(), Context: "Owned text.", ContextKind: kind), "INVALID_CONTEXT_KIND", "Unknown context provenance refuses before inference");
    Check(f.Http.Calls == 0, "Invalid provenance has no engine request");
}
await using (var f = new Fixture()) {
    var c = await f.Initialize(); await Reject(f, new(c.Id, "Explain pebble.", Guid.NewGuid().ToString(), ContextKind: "file", ImageBase64: "AQ=="), "INVALID_CONTEXT_KIND", "Image cannot be relabelled as reviewed text file");
    Check(f.Http.Calls == 0, "Image provenance refusal precedes inference");
}
Console.WriteLine($"LOCAL MODEL SERVICE CHECKS: {checks - failures} passed, {failures} failed, {checks} total. Fake HTTP only; no live model/network/native work.");
return failures == 0 ? 0 : 1;

sealed class Fixture : IAsyncDisposable
{
    internal const string Default = "owned-default:latest", Alternate = "owned-alternate:latest", Vision = "owned-vision:latest", Answer = "Pebbles are small stones.";
    private readonly string folder = Path.Combine(Path.GetTempPath(), "Buddy-local-model-owned-" + Guid.NewGuid().ToString("N"));
    private readonly HttpClient client;
    internal readonly FakeEngine Http = new();
    internal readonly StateStore Store;
    internal readonly BuddyService Service;
    internal string StatePath => Path.Combine(folder, "buddy.v1.encrypted");
    internal Fixture(string address = "http://127.0.0.1:11434/")
    {
        Directory.CreateDirectory(folder); Store = new(folder, new EphemeralDataProtectionProvider());
        client = new(Http) { BaseAddress = new(address) };
        Service = new(Store, new(client)) { AgentEnabled = false, WebEnabled = false };
    }
    internal async Task<Conversation> Initialize()
    {
        await Store.Update(s => { s.Model = Default; s.VisionModel = Vision; return true; });
        return await Service.CreateConversation("Owned model-selection fixture");
    }
    public ValueTask DisposeAsync()
    {
        client.Dispose(); string path = Path.GetFullPath(folder), temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(path).StartsWith("Buddy-local-model-owned-", StringComparison.Ordinal)) throw new Exception("Unexpected fixture cleanup path");
        Directory.Delete(path, true); return ValueTask.CompletedTask;
    }
}
sealed class FakeEngine : HttpMessageHandler
{
    internal int Calls, Tags, Shows, Pulls;
    internal bool Offline;
    internal string Metadata = """{"details":{"format":"gguf"},"model_info":{"general.architecture":"gemma3"}}""";
    internal HttpStatusCode ShowStatus = HttpStatusCode.OK;
    internal string[] Installed = [Fixture.Default, Fixture.Alternate];
    internal readonly List<string> ChatModels = [], ChatPayloads = [], ShowPayloads = [];
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); Calls++;
        if (request.RequestUri!.AbsolutePath == "/api/tags" && request.Method == HttpMethod.Get) {
            Tags++; if (Offline) throw new HttpRequestException("Owned offline fixture");
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { models = Installed.Select(name => new { name }) })) };
        }
        if (request.RequestUri.AbsolutePath == "/api/show" && request.Method == HttpMethod.Post) {
            Shows++; ShowPayloads.Add(await request.Content!.ReadAsStringAsync(ct));
            return new(ShowStatus) { Content = new StringContent(Metadata) };
        }
        if (request.RequestUri.AbsolutePath == "/api/chat" && request.Method == HttpMethod.Post) {
            string body = await request.Content!.ReadAsStringAsync(ct); ChatPayloads.Add(body); using var parsed = JsonDocument.Parse(body);
            ChatModels.Add(parsed.RootElement.GetProperty("model").GetString()!);
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { message = new { content = Fixture.Answer }, done = true }) + "\n") };
        }
        if (request.RequestUri.AbsolutePath == "/api/pull") Pulls++;
        throw new InvalidOperationException("Unexpected fake HTTP operation: " + request.Method + " " + request.RequestUri.AbsolutePath);
    }
}
