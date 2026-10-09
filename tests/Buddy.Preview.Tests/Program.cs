using Buddy.Server;
using Buddy.Windows;
using Microsoft.AspNetCore.DataProtection;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;

int count = 0;
void Check(bool value, string label) { if (!value) throw new Exception("FAIL: " + label); count++; Console.WriteLine("PASS: " + label); }
async Task<List<BrainToken>> Tokens(BrainRouter router, BrainRequest request) { var list = new List<BrainToken>(); await foreach (var token in router.CompleteAsync(request, default)) list.Add(token); return list; }
Check(!ShortcutChoice.Choices.Any(c => (c.Modifiers & 8) != 0), "Windows language-switch shortcuts cannot be selected");
Check(ShortcutChoice.Warning("Ctrl + Space").Contains("ChatGPT"), "Ctrl+Space warns about the observed ChatGPT collision");
var registered = new Dictionary<int, uint>(); uint reserved = 6;
bool Register(int id, uint mod) { if ((mod & ~0x4000u) == reserved || registered.Values.Contains(mod)) return false; registered.Add(id, mod); return true; }
using var chat = new ShortcutRegistration(Register, id => registered.Remove(id));
using var voice = new ShortcutRegistration(Register, id => registered.Remove(id), 3, 7);
Check(chat.TrySet(ShortcutChoice.Find("Ctrl + Alt + Space")) && voice.TrySet(ShortcutChoice.Find("Alt + Shift + Space")), "Independent chat and voice bindings coexist");
Check(!voice.TrySet(ShortcutChoice.Find("Ctrl + Shift + Space")) && voice.Active!.Modifiers == 5 && chat.Active!.Modifiers == 3, "A competing app preserves both working shortcuts");
Check(SpeechReview.Required(.45f, []) && SpeechReview.Required(float.NaN, []), "Low or invalid confidence requires transcript review");
Check(SpeechReview.Required(.91f, [("Open Comet", .91f), ("The patch", .85f)]), "Ambiguous alternatives require transcript review even at high confidence");
Check(!SpeechReview.Required(.93f, [("Open Comet", .93f), ("Open Comet", .93f), ("Other", .5f)]), "Duplicate alternatives do not create false ambiguity");
var spoken = ConversationalReply.PlainText("## **Ready.**\n- Visit [the guide](https://example.com/a). [1]\n`Use Comet` *carefully*. https://example.com/source");
Check(!spoken.Contains('*') && !spoken.Contains('#') && !spoken.Contains('`') && !spoken.Contains("https") && !spoken.Contains("[1]"), "Markdown, citation markers and URLs are removed from speech");
Check(ConversationalReply.Sentences("It is 3.14. Ready? Yes!") == 3, "Sentence limit preserves decimal boundaries");
Check(!ConversationalReply.IsConcise("One. Two. Three. Four.") && ConversationalReply.IsConcise("Ready. I'll help."), "Conversation is limited to three sentences");
Check(ConversationalReply.IsConcise(ConversationalReply.Fallback), "Safe composition fallback respects the sentence limit");
int stops = 0; var route = new AudioRouteGuard("headphones", true, () => stops++);
route.DeviceUnavailable("other"); Check(stops == 0, "Unrelated endpoint removal does not stop the selected route");
var watch = System.Diagnostics.Stopwatch.StartNew(); route.DeviceUnavailable("headphones"); watch.Stop();
Check(stops == 1 && watch.ElapsedMilliseconds < 100, "Headphone-disconnect callback stops synchronously within 100 ms in the policy fixture");
route.DefaultRenderChanged(); Check(stops == 2, "Default output changes stop headphones-only speech");
route.PropertiesChanged("headphones"); Check(stops == 3, "Endpoint property changes fail closed without speaker rerouting");

var local = new FakeBrain("local", false); var grok = new FakeBrain("grok", true); var gpt = new FakeBrain("chatgpt", true); var handoff = new FakeBrain("handoff", true, true);
var router = new BrainRouter([local, grok, gpt, handoff]);
var request = new BrainRequest("voice", "Hello", "fixture", [new { role = "user", content = "bank-title screen-private-marker previous-image-marker" }], DefaultBrainId: "grok");
Check(router.Select(request).Id == "grok", "User default selects the authorized provider");
Check(router.Select(request with { SkillBrainId = "chatgpt" }).Id == "chatgpt", "Skill pin takes precedence over user default");
Check(router.Select(request with { BrainId = "grok", SkillBrainId = "chatgpt" }).Id == "grok", "Explicit per-turn choice takes precedence over skill pin");
Check(router.Select(request with { Text = "use ChatGPT for this" }).Id == "chatgpt", "Explicit provider phrase is routed");
Check(router.Select(request with { RestrictedContext = true }).Id == "local", "Bank-title or blocklisted context strips cloud candidates before dispatch");
Check(router.Select(request with { ScreenTitle = "My Bank account", BrainId = "grok" }).Id == "local", "A bank window title forces local routing even with an explicit cloud choice");
Check(router.Select(request with { ScreenApp = "bitwarden", BrainId = "chatgpt" }).Id == "local", "A credential-manager process cannot be routed to cloud");
Check(router.Select(request with { DefaultBrainId = "handoff" }).Id == "local", "Desktop handoff is never automatically selected");
Check(router.Select(request with { BrainId = "unknown" }).Id == "local", "Unconnected providers fall back locally");
var tokens = await Tokens(router, request);
Check(tokens.All(t => t.BrainId == "grok") && tokens.Any(t => t.Text == "Hello."), "Fake Grok stream exposes actual provider identity for the chip");
Check(!JsonSerializer.Serialize(grok.Last!.Messages).Contains("private-marker") && !JsonSerializer.Serialize(grok.Last.Messages).Contains("bank-title"), "Cloud adapter receives no derived UIA, title, image, memory or prior-turn context");
tokens = await Tokens(router, request with { BrainId = "chatgpt" });
Check(tokens.All(t => t.BrainId == "chatgpt"), "Fake ChatGPT cannot spoof another provider identity");
int cloudCalls = grok.Calls + gpt.Calls;
await Tokens(router, request with { RestrictedContext = true, BrainId = "chatgpt" });
Check(grok.Calls + gpt.Calls == cloudCalls, "Restricted turn never invokes a cloud provider");
grok.Fail = true; tokens = await Tokens(router, request); grok.Fail = false;
Check(tokens.Last().BrainId == "local" && tokens.Any(t => t.Notice?.Contains("unavailable") == true), "Provider failure before output falls back with a truthful label");
using (var stopped = new CancellationTokenSource()) {
    stopped.Cancel(); bool cancelled = false;
    try { await foreach (var _ in router.CompleteAsync(request, stopped.Token)) { } } catch (OperationCanceledException) { cancelled = true; }
    Check(cancelled, "Cancelled turns never retry on another provider");
}
var allowed = new HashSet<string> { "uia.type", "uia.snapshot", "unknown" };
Check(!ToolRegistry.IsAllowed("unknown", allowed, allowed, true), "Unknown tools are denied even if a skill names them");
Check(!ToolRegistry.IsAllowed("uia.type", allowed, allowed, false) && ToolRegistry.IsAllowed("uia.type", allowed, allowed, true), "Tool allowlist and grant never replace high-risk approval");
Check(!ToolRegistry.IsAllowed("uia.snapshot", allowed, new HashSet<string>(), true), "A skill cannot manufacture a user grant");
Check(ToolRegistry.CanRunParallel(["uia.snapshot", "files.read"]) && !ToolRegistry.CanRunParallel(["uia.type"]), "Only read-only subtasks may run in parallel");

var folder = Path.Combine(Path.GetTempPath(), "Buddy-preview-tests-" + Guid.NewGuid()); Directory.CreateDirectory(folder);
try {
    var path = Path.Combine(folder, "preferences.json");
    var prefs = new DesktopPreferences { ProtectScreenshots = false, HeadphonesOnly = true, HeadphoneDeviceId = "headset-fixture", RecognitionLanguage = "en-GB", MicrophoneId = "fixture mic", VoiceName = "fixture voice", VoiceRate = -2 };
    prefs.Save(path); Check(DesktopPreferences.Load(path) == prefs, "Voice, headphones and screenshot preferences survive reload");
    using var model = new ReplyModel(); using var client = new HttpClient(model) { BaseAddress = new("http://127.0.0.1:11434") };
    var store = new StateStore(folder, DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(folder, "keys")))); await store.EnsureSaved();
    var service = new BuddyService(store, new(client)) { AgentEnabled = true };
    var conversation = await service.CreateConversation("Preview fixture");
    var events = new List<StreamEvent>();
    await foreach (var e in service.Chat(new(conversation.Id, "Test reply", Guid.NewGuid().ToString()), default)) events.Add(e);
    var answer = string.Concat(events.Where(e => e.Type == "delta").Select(e => e.Text));
    Check(model.Calls == 2 && answer == "Keep the safety qualification. Review the full action plan before approval.", "Overlong model output is recomposed whole, preserving safety rather than cut at sentence three");
    Check(events.Any(e => e.Type == "brain" && e.BrainId == "local"), "Real service announces the local brain before answer output");
    model.AlwaysLong = true; events.Clear();
    await foreach (var e in service.Chat(new(conversation.Id, "Test retry", Guid.NewGuid().ToString()), default)) events.Add(e);
    Check(events.Single(e => e.Type == "delta").Text == ConversationalReply.Fallback, "Repeated model noncompliance produces a safe short fallback");
    service.WebEnabled=true;events.Clear();
    await foreach(var e in service.Chat(new(conversation.Id,"Write a poem on a boat sailing on a lonely sea",Guid.NewGuid().ToString(),UseWeb:true),default))events.Add(e);
    Check(!events.Any(e=>e.Type=="tool_call"||e.Type=="status"&&e.Text?.StartsWith("Researching")==true)&&events.Single(e=>e.Type=="delta").Text==ConversationalReply.Fallback,"Actual chat service skips automatic web research for the reported creative prompt even when web is enabled");
    var plan = await service.PlanAgent(new("Open Comet Browser", new("fixture", "Fixture", [])), default);
    Check(plan.Actions is { Count: 1 } && plan.Actions[0].Value == "comet" && ActionPolicy.LiveRisk(plan.Actions[0], null) == "high", "Comet request yields exactly one approved bounded launch");
    foreach (var value in new[] { "comet --remote-debugging-port=9222", "comet & calc", "cmd.exe", "file:///C:/x.exe" }) {
        bool rejected = false; try { ActionPolicy.Validate(new("bad", [new("open", Value: value)])); } catch (BuddyException) { rejected = true; }
        Check(rejected, "Command and argument injection rejected: " + value);
    }
    Check(!Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Any(p => new[] { ".jpg", ".jpeg", ".png" }.Contains(Path.GetExtension(p))), "Turn processing creates no image files in the isolated store");
} finally { Directory.Delete(folder, true); }
Console.WriteLine($"ALL {count} PREVIEW POLICY CHECKS PASSED");

sealed class FakeBrain(string id, bool cloud, bool explicitOnly = false) : IBrain
{
    public int Calls; public bool Fail; public BrainRequest? Last;
    public BrainDescriptor Descriptor { get; } = new(id, id, cloud, explicitOnly);
    public async IAsyncEnumerable<BrainToken> CompleteAsync(BrainRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); Calls++; Last = request; await Task.Yield();
        if (Fail) throw new HttpRequestException("Fixture provider unavailable");
        yield return new("spoofed", "Hello.");
    }
}
sealed class ReplyModel : HttpMessageHandler
{
    public int Calls; public bool AlwaysLong;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); Calls++;
        string answer = AlwaysLong || Calls == 1 ? "First. Second. Third. Critical safety qualification." : "Keep the safety qualification. Review the full action plan before approval.";
        var json = JsonSerializer.Serialize(new { message = new { content = answer }, done = true });
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json + "\n", Encoding.UTF8, "application/x-ndjson") });
    }
}
