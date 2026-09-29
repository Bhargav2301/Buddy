using Buddy.Server;
using Microsoft.AspNetCore.DataProtection;
using System.Net;
using System.Text;
using System.Text.Json;

int count = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); count++; Console.WriteLine("PASS: " + name); }
async Task Reject(Func<Task> action, string code, string name) { try { await action(); } catch (BuddyException e) when (e.Code == code) { Check(true, name); return; } throw new Exception("FAIL: " + name); }
async Task<List<StreamEvent>> Collect(IAsyncEnumerable<StreamEvent> source) { var items = new List<StreamEvent>(); await foreach (var item in source) items.Add(item); return items; }
var controls = new List<ScreenElement> { new("a", "Export", "Button", -120, 40, 80, 30), new("b", "Example text", "Edit", 0, 0, 300, 100) };
Check(OllamaEngine.AvailableDefault("qwen3:4b-instruct-2507-q4_K_M", ["gemma3:4b"]) == "gemma3:4b", "An unavailable factory default uses the installed Gemma model");
Check(OllamaEngine.AvailableDefault("custom:model", ["gemma3:4b"]) == "custom:model", "A user's explicit model choice is preserved");
Check(OllamaEngine.AvailableDefault("qwen3:4b-instruct-2507-q4_K_M", ["qwen3:4b-instruct-2507-q4_K_M", "gemma3:4b"]) == "qwen3:4b-instruct-2507-q4_K_M", "An installed default is preserved");
Check(GroundingResolver.Resolve(controls, "a", "wrong", "Button")?.Ref == "a", "A real UIA reference grounds across negative monitor origins");
Check(GroundingResolver.Resolve(controls, "stale", "Export", "Button") is null, "A stale reference cannot silently retarget a command");
Check(GroundingResolver.Resolve(controls, "", "Export", "Button")?.Ref == "a", "Unique name and role resolve a future window target");
Check(GroundingResolver.Resolve(controls, "", "Exporrt", "Button")?.Ref == "a", "Long unique names allow bounded spelling differences");
Check(GroundingResolver.Resolve(controls.Concat([controls[0] with { Ref = "c" }]), "", "Export", "Button") is null, "Ambiguous controls are refused");
Check(GroundingResolver.Resolve(controls.Concat([controls[0]]), "a", "", "Button") is null, "Duplicate references are refused without throwing");
Check(GroundingResolver.Resolve(controls, "", "", "Edit")?.Ref == "b", "The sole editor can be found without an invented name");
Check(GroundingResolver.Resolve([controls[0] with { Enabled = false }], "a", "", "") is null, "Disabled targets are excluded");
Check(ActionPolicy.Validate(new("Example", [new("click", Target: "Send", Risk: "low")])).Actions![0].Risk == "high", "A model cannot lower the risk of a mutating action");
foreach (var action in new[] { new AssistantAction("shell", Value:"echo bad"), new AssistantAction("keys", Value:"Win+R"), new AssistantAction("open", Value:"cmd.exe /c anything"), new AssistantAction("type") })
    await Reject(() => { ActionPolicy.Validate(new("Invalid", [action])); return Task.CompletedTask; }, action.Kind == "open" ? "WEB_URL_BLOCKED" : "INVALID_PLAN", "Unsupported action blocked: " + action.Kind);
await Reject(() => { ActionPolicy.Validate(new("Too long", Enumerable.Repeat(new AssistantAction("wait"), 26).ToList())); return Task.CompletedTask; }, "INVALID_PLAN", "Action count is capped at 25");
foreach (var url in new[] { "http://example.com", "https://127.0.0.1", "https://10.0.0.1", "https://169.254.169.254/latest", "https://user:secret@example.com", "https://example.com:444", "https://test.local", "file:///C:/private" })
    await Reject(() => { WebResearch.ValidateUrl(url); return Task.CompletedTask; }, "WEB_URL_BLOCKED", "Web URL blocked: " + url);
foreach (var ip in new[] { "::1", "::ffff:127.0.0.1", "fc00::1", "fe80::1", "2002:7f00:1::", "2001::1", "2001:db8::1", "224.0.0.1", "100.64.0.1", "192.0.2.1" })
    Check(!WebResearch.IsPublicAddress(IPAddress.Parse(ip)), "Nonpublic/transition address blocked: " + ip);
Check(WebResearch.IsPublicAddress(IPAddress.Parse("1.1.1.1")) && WebResearch.IsPublicAddress(IPAddress.Parse("2606:4700::1111")), "Public IPv4 and IPv6 remain usable");
var pages = new Pages(); using var web = new WebResearch(pages);
pages.Html = "<title>Test</title><main>Useful evidence<script>bad script</script><form>private form</form></main>";
var source = await web.Fetch("https://example.com/page", default);
Check(source.Text == "Useful evidence" && source.Url == "https://example.com/page", "HTML extraction strips executable and form content and preserves the source URL");
pages.Redirect = "https://127.0.0.1/private"; int calls = pages.Calls;
await Reject(async () => await web.Fetch("https://example.com/redirect", default), "WEB_URL_BLOCKED", "Redirect destinations are revalidated");
Check(pages.Calls == calls + 1, "A blocked redirect makes no second request"); pages.Redirect = null;
pages.Html = new string('x', WebResearch.MaxBytes + 1);
await Reject(async () => await web.Fetch("https://example.com/large", default), "WEB_TOO_LARGE", "Large pages fail before entering model context");
pages.Html = "<div class='result'><a class='result__a' href='//duckduckgo.com/l/?uddg=https%3A%2F%2Fexample.com%2Fguide'>Guide</a><span class='result__snippet'>Evidence</span></div>";
Check((await web.Search("example guide", default)).Single().Url == "https://example.com/guide", "Search links unwrap to their actual HTTPS sources");
using (var cancel = new CancellationTokenSource()) { cancel.Cancel(); bool cancelled = false; try { await web.Fetch("https://example.com", cancel.Token); } catch (OperationCanceledException) { cancelled = true; } Check(cancelled, "Cancelled research does not continue downloading"); }

var directory = Path.Combine(Path.GetTempPath(), "buddy-assistant-tests-" + Guid.NewGuid()); Directory.CreateDirectory(directory);
try {
    var store = new StateStore(directory, DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(directory, "keys"))));
    var model = new Model(); using var client = new HttpClient(model) { BaseAddress = new("http://127.0.0.1:11434/") };
    var research = new Research(); var service = new BuddyService(store, new(client), research);
    var conversation = await service.CreateConversation("Test"); var req = new ChatRequest(conversation.Id, "Explain https://example.com", Guid.NewGuid().ToString(), Context:"screen-private-marker", UseWeb:true);
    await Reject(async () => await Collect(service.Chat(req, default)), "WEB_DISABLED", "Web-off rejects research before calling model or network");
    Check(model.Payloads.Count == 0 && research.Calls == 0, "Web-off has no research side effects");
    var planning = new PlanningRequest("Click Export", new("example", "Example", controls));
    await Reject(async () => await service.PlanAgent(planning, default), "AGENT_DISABLED", "Agent-off rejects planning");
    service.WebEnabled = true;
    model.Replies.Enqueue("{\"tool\":\"web.fetch\",\"input\":\"https://example.com\"}"); model.Replies.Enqueue("{\"tool\":\"done\",\"input\":\"\"}");
    var events = await Collect(service.Chat(req, default));
    Check(research.Calls == 1 && events.Any(e => e.Type == "tool_result"), "The structured loop executes a real tool adapter and emits its result");
    Check(model.Payloads[1].Contains("web-evidence-marker") && !model.Payloads[0].Contains("screen-private-marker"), "Web decisions see evidence without sending screen context to research");
    Check(model.Payloads.Last().Contains("web-evidence-marker") && events.Any(e => e.Text?.Contains("https://example.com") == true), "The final answer receives fetched evidence and actual source citations");
    var recorded = await store.Read(s => s.Conversations.Single().Messages);
    Check(recorded.Last().Evidence is { Screen: true, Image: false, Sources.Count: 1 } && recorded.Last().Evidence!.Sources![0].Url == "https://example.com",
        "Screen-use and actual source links are stored as bounded conversation metadata");
    Check(!JsonSerializer.Serialize(recorded).Contains("screen-private-marker") && !JsonSerializer.Serialize(recorded).Contains("web-evidence-marker"),
        "Conversation metadata persists neither screen context nor retrieved page bodies");
    var replay = await Collect(service.Chat(req, default));
    Check(research.Calls == 1 && replay.Any(e => e.Type == "evidence" && e.Evidence?.Sources?.Count == 1),
        "Idempotent answer replay retains source metadata without new research");
    service.AgentEnabled = true;
    model.Replies.Enqueue(JsonSerializer.Serialize(new AssistantPlan("Click", [new("click", Ref:"a", Description:"Click Export", Risk:"low")]), StateStore.Json));
    var plan = await service.PlanAgent(planning, default);
    Check(plan.Actions![0].Risk == "high", "Structured Ollama output is validated before it reaches Windows");
    model.Replies.Enqueue("not-json"); await Reject(async () => await service.PlanAgent(planning, default), "INVALID_PLAN", "Malformed structured output never becomes a plan");
    model.Delay = true; using var cancelledPlan = new CancellationTokenSource(); var pending = service.PlanAgent(planning, cancelledPlan.Token);
    await model.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); service.StopAll(); bool stopped = false;
    try { await pending; } catch (OperationCanceledException) { stopped = true; } Check(stopped, "Global stop cancels planning inference"); model.Delay = false;
    model.Replies.Enqueue(JsonSerializer.Serialize(new AssistantPlan("Wait", [new("wait")]), StateStore.Json));
    Check((await service.PlanAgent(planning, default)).Actions!.Count == 1, "Cancelled planning releases the inference lock");
    var continuation = new AgentContinuation("Click Export", planning.Context, [new(1, new("click", Ref:"a"), true, "Activated Export")], 24);
    model.Replies.Enqueue("{\"status\":\"done\",\"summary\":\"Export dialog is visible\",\"actions\":[]}");
    Check((await service.ContinueAgent(continuation, default)).Status == "done", "Continuation verifies completion using a fresh screen and actual results");
    Check(model.Payloads.Last().Contains("Activated Export") && model.Payloads.Last().Contains("untrustedActionResults"), "Verification receives execution evidence as untrusted data");
    model.Replies.Enqueue("{\"status\":\"continue\",\"summary\":\"Next\",\"actions\":[{\"kind\":\"wait\"},{\"kind\":\"wait\"}]}");
    await Reject(async () => await service.ContinueAgent(continuation with { RemainingActions = 1 }, default), "INVALID_PLAN", "Continuation cannot exceed the remaining action budget");
    model.Replies.Enqueue("{\"status\":\"done\",\"summary\":\"Done\",\"actions\":[{\"kind\":\"wait\"}]}");
    await Reject(async () => await service.ContinueAgent(continuation, default), "INVALID_PLAN", "A completed decision cannot smuggle another action");
    await Reject(async () => await service.ContinueAgent(continuation with { Results = [new(7, new("wait"), true, "Waited")] }, default), "INVALID_RUN", "Out-of-order results are refused before inference");
    model.Replies.Enqueue("{\"tool\":\"done\",\"input\":\"\"}");
    model.Replies.Enqueue("{\"summary\":\"Export\",\"steps\":[{\"instruction\":\"Select Export\",\"ref\":\"a\",\"target\":\"Export\",\"role\":\"Button\",\"primitive\":\"ring\",\"expect\":{\"kind\":\"manual\",\"target\":\"\",\"role\":\"\"}}]}");
    int guidePayload = model.Payloads.Count;
    await service.PlanGuide(planning with { UseWeb = true, Context = planning.Context with { Title = "screen-private-marker" } }, default);
    Check(!model.Payloads[guidePayload].Contains("screen-private-marker") && model.Payloads.Last().Contains("screen-private-marker"), "Guide research never includes the screen in web-tool decisions");
    var visual = new VisionGroundingRequest("Export", "Button", "example", "Example", "fixture-image-marker", [new("ocr1", "Export", .95, 10, 10, 80, 30, true)]);
    model.Replies.Enqueue("{\"ref\":\"ocr1\",\"matches\":true,\"confidence\":0.92,\"reason\":\"Visible Export control\",\"kind\":\"control\"}");
    Check((await service.ConfirmVisualTarget(visual, default))?.Ref == "ocr1", "Local vision can corroborate a unique OCR label");
    Check(model.Payloads.Last().Contains("fixture-image-marker") && model.Payloads.Last().Contains("images"), "Vision receives the local image with bounded OCR evidence");
    Check(!model.Payloads.Last().Contains("\"confidence\":0.95"), "OCR accuracy is not supplied as visual-role confidence");
    model.Replies.Enqueue("{\"ref\":\"invented\",\"matches\":true,\"confidence\":1,\"reason\":\"Guess\",\"kind\":\"control\"}");
    Check(await service.ConfirmVisualTarget(visual, default) is null, "A model cannot invent a visual target reference");
    model.Replies.Enqueue("{\"ref\":\"ocr1\",\"matches\":true,\"confidence\":0.99,\"reason\":\"Label in notes\",\"kind\":\"document-text\"}");
    Check(await service.ConfirmVisualTarget(visual, default) is null, "A readable document mention cannot establish a button");
    model.Replies.Enqueue("{\"ref\":\"ocr1\",\"matches\":true,\"confidence\":0.99,\"reason\":\"Role unknown\"}");
    Check(await service.ConfirmVisualTarget(visual, default) is null, "Missing visual role evidence abstains even at high claimed confidence");
    int visionCalls = model.Payloads.Count;
    Check(await service.ConfirmVisualTarget(visual with { Evidence = [visual.Evidence[0] with { ControlBoundary = false }] }, default) is null && model.Payloads.Count == visionCalls, "Control guidance without independent visual boundary evidence abstains before inference");
    Check(await service.ConfirmVisualTarget(visual with { Evidence = [visual.Evidence[0] with { Confidence = .5 }] }, default) is null && model.Payloads.Count == visionCalls, "Low-confidence OCR abstains before invoking vision");
    await Reject(async () => await service.ConfirmVisualTarget(visual with { Evidence = [visual.Evidence[0], visual.Evidence[0] with { Ref = "duplicate" }] }, default), "INVALID_VISION_CONTEXT", "Ambiguous visual evidence cannot produce a pointing target");
    await service.Audit("type", "Example field", "completed");
    Check(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(directory,"buddy.v1.encrypted"))).Contains("Example field"), "Activity metadata remains encrypted at rest");

    if (args.Contains("--live")) {
        using var liveClient = new HttpClient { BaseAddress = new("http://127.0.0.1:11434/"), Timeout = TimeSpan.FromMinutes(4) };
        using var liveWeb = new WebResearch(); var live = new BuddyService(store, new(liveClient), liveWeb) { AgentEnabled = true, WebEnabled = true };
        await store.Update(s => { s.Model = "gemma3:4b"; return true; });
        var actualPlan = await live.PlanAgent(planning, default);
        Check(actualPlan.Actions!.Any(a => a.Kind is "click" or "invoke" && a.Ref == "a"), "LIVE Gemma grounds an action to the supplied Export control");
        var guide = await live.PlanGuide(planning with { Query = "Show me where Export is" }, default);
        Check(guide.Steps!.Any(s => s.Ref == "a"), "LIVE Gemma grounds an on-screen guide");
        var liveSource = await liveWeb.Fetch("https://example.com", default);
        Check(liveSource.Text.Contains("Example Domain"), "LIVE public HTTPS fetch and extraction work");
        var chat = await live.CreateConversation("Live research test");
        var answer = await Collect(live.Chat(new(chat.Id,"Fetch https://example.com and explain what the page is for in one sentence.",Guid.NewGuid().ToString(),UseWeb:true), default));
        Check(answer.Any(e => e.Type == "tool_result" && !e.Text!.StartsWith("Read 0")) && answer.Any(e => e.Text?.Contains("https://example.com") == true), "LIVE model uses fetched evidence and returns a cited answer");
    }
    Console.WriteLine($"ALL {count} ASSISTANT CHECKS PASSED");
} finally { Directory.Delete(directory, true); }

sealed class Pages : HttpMessageHandler {
    public string Html = ""; public string? Redirect; public int Calls;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Calls++; var r = new HttpResponseMessage(Redirect is null ? HttpStatusCode.OK : HttpStatusCode.Redirect) { Content = new StringContent(Html, Encoding.UTF8, "text/html") }; if (Redirect is not null) r.Headers.Location = new(Redirect); return Task.FromResult(r); }
}
sealed class Research : IWebResearch {
    public int Calls;
    public Task<IReadOnlyList<WebSource>> Search(string query, CancellationToken ct) => throw new Exception("Unexpected search");
    public Task<WebSource> Fetch(string url, CancellationToken ct) { Calls++; return Task.FromResult(new WebSource("Example",url,"web-evidence-marker")); }
}
sealed class Model : HttpMessageHandler {
    public readonly Queue<string> Replies = new(); public readonly List<string> Payloads = []; public bool Delay; public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
        var payload = await request.Content!.ReadAsStringAsync(ct); Payloads.Add(payload);
        if (Delay) { Started.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
        using var doc = JsonDocument.Parse(payload);
        string data = !doc.RootElement.GetProperty("stream").GetBoolean() ? JsonSerializer.Serialize(new { message = new { content = Replies.Dequeue() }, done = true }) : "{\"message\":{\"content\":\"A sourced answer.\"},\"done\":true}\n";
        return new(HttpStatusCode.OK) { Content = new StringContent(data) };
    }
}
