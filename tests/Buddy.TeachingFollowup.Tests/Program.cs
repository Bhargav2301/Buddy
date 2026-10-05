using Buddy.Server;
using Microsoft.AspNetCore.DataProtection;
using System.Net;
using System.Text.Json;

int checks = 0;
void Check(bool result, string label) { if (!result) throw new Exception("FAIL: " + label); checks++; }
void Reject(ConceptualExplanation answer, PlanningRequest request, string label) {
    try { GuideLessons.ComposeExplanation(answer, request); }
    catch (BuddyException e) when (e.Code == "INVALID_GUIDE") { Check(true, label); return; }
    throw new Exception("FAIL: accepted " + label);
}
PlanningRequest Ask(string query, string name, string role = "Button") => new(query, new("owned-fixture", "Private title not needed", [new("private-ref", name, role, 44, 52, 100, 40)]));
// Non-catalog role deliberately exercises the unchanged bounded model path.
var zoom = Ask("Explain Zoom without changing document content.", "Zoom", "SpinButton");
var useful = new ConceptualExplanation("Zoom commonly adjusts displayed magnification.", "The label does not establish a current value or this app's exact behavior.", "Compare displayed size with the original while checking that document text remains unchanged.");
using (var json = JsonDocument.Parse(GuideLessons.ExplanationInput(zoom))) {
    var raw = json.RootElement.GetRawText();
    Check(!raw.Contains("private-ref") && !raw.Contains("Private title") && !raw.Contains("enabled") && !raw.Contains("width"), "concept input omits pointer, title, availability and geometry");
    Check(json.RootElement.GetProperty("untrustedObservation").GetProperty("controls")[0].GetProperty("name").GetString() == "Zoom", "actual name/role retained as untrusted evidence");
    Check(json.RootElement.GetProperty("question").GetString() == zoom.Query, "user question retained exactly");
}
Check(GuideLessons.ComposeExplanation(useful, zoom).Lessons!.Count == 1, "useful comparison remains accepted whole");
foreach (var bad in new[] {
    "The Zoom control is enabled, showing it can be used.",
    "Observe the behavior of the control.",
    "Verify its functionality.",
    "The control is a Slider with the name Zoom.",
    "Check the presence of the control."
}) Reject(useful with { ManualCheck = bad }, zoom, "availability/tautology does not pass as a result check");
Reject(useful with { Purpose = "The checkbox is currently checked." }, zoom, "unobserved affirmative state");
var mute = Ask("Explain Mute; do not assume its checked state or change anything.", "Mute", "CheckBox");
var audio = new ConceptualExplanation("Mute commonly silences some audio.", "Neither the checked state nor the affected audio channel is established here.", "Review the documented scope to identify whether the setting affects input, output or only one app.");
Check(GuideLessons.ComposeExplanation(audio, mute).Lessons!.Single().Instruction.Contains("input, output"), "unknown channel retained with concrete read-only scope check");
Reject(audio with { Purpose = "Mute silences the audio output." }, mute, "unsupported speaker scope");
Reject(audio with { Limitation = "The checked state is unknown.", ManualCheck = "Inspect the visible checkbox state before deciding whether a change is appropriate." }, mute, "checkbox state alone omits material unknown audio channel");
Check(GuideLessons.ComposeExplanation(audio with { Purpose = "Mute silences the audio output." }, mute with { Query = "Explain the audio output Mute checkbox." }).Lessons!.Count == 1, "user-specified scope remains usable");
var reload = Ask("Explain Reload in the browser without acting.", "Reload");
var refresh = new ConceptualExplanation("Reload commonly requests the current page again.", "Unsaved page work may be lost; this view does not establish that it is protected.", "Compare any independently observed page result with the intended page and check whether expected content is present.");
Check(GuideLessons.ComposeExplanation(refresh, reload).Steps!.Count == 0, "reload concept preserves material caution and no action");
Reject(refresh with { Limitation = "The label only establishes a button exists." }, reload, "reload must retain unsaved-work qualification");
var publish = Ask("Explain Publish for private notes; do not proceed before removing private content and reviewing access.", "Publish");
var privacy = new ConceptualExplanation("Publish commonly makes content available to an audience.", "Remove private content and review access before any publishing decision.", "Compare the intended audience with independently verified access permissions.");
Check(GuideLessons.ComposeExplanation(privacy, publish).Lessons!.Count == 1, "complete privacy qualification retained");
foreach (var caution in new[] { "Review access before any publishing decision.", "Private notes require care before publishing.", "Remove private content before publishing." })
    Reject(privacy with { Limitation = caution, ManualCheck = "Inspect the intended destination and compare its name with the chosen location." }, publish, "partial privacy qualification refused");

using (var f = new Fixture()) {
    f.Model.Replies.Enqueue(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
    f.Model.Replies.Enqueue(_ => Task.FromResult(Model.Json(useful)));
    var result = await f.Service.PlanGuide(zoom, default);
    Check(f.Model.Calls == 2 && result.Lessons!.Single().Instruction.Contains("Compare displayed size"), "model HTTP failure gets only existing bounded repair attempt");
    Check(f.Model.Bodies.All(body => !body.Contains("private-ref") && !body.Contains("Private title")), "production conceptual route uses minimized context on both attempts");
}
foreach (bool spoken in new[] { false, true }) using (var f = new Fixture()) {
    f.Model.Replies.Enqueue(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
    f.Model.Replies.Enqueue(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
    if (spoken) { var result = await f.Service.Teach(new(zoom.Query, zoom.Context), default); Check(result.Targets.Count == 0, "spoken model failure has no target"); }
    else { var result = await f.Service.PlanGuide(zoom, default); Check(result.Steps!.Count == 0 && result.Summary.Contains("could not prepare"), "guide model failure truthfully unavailable"); }
    Check(f.Model.Calls == 2 && f.Model.Replies.Count == 0, "no third request/model substitution after repeated failure");
}
using (var f = new Fixture()) {
    f.Model.Replies.Enqueue(_ => Task.FromResult(Model.Json(useful with { ManualCheck = "The control is enabled and ready." })));
    f.Model.Replies.Enqueue(_ => Task.FromResult(Model.Json(useful)));
    var result = await f.Service.PlanGuide(zoom, default);
    Check(f.Model.Calls == 2 && result.Lessons!.Single().Instruction.EndsWith(useful.ManualCheck), "unsupported effect check is regenerated whole, never trimmed into a pass");
}

// Actual local trace: valid outer HTTP JSON, but a curly closing quote inside
// message.content and repeated word counts until done_reason=length. Never salvage.
foreach (bool reachedLimit in new[] { true, false }) using (var f = new Fixture()) {
    const string malformed = "{\"purpose\":\"A complete sentence.\",\"limitation\":\"Keep the caution.\",\"manualCheck\":\"Compare the intended result.\u201d} 104 words. 104 words.";
    for (int attempt = 0; attempt < 2; attempt++) f.Model.Replies.Enqueue(_ => Task.FromResult(Model.Raw(malformed, reachedLimit ? "length" : "stop")));
    var result = await f.Service.PlanGuide(zoom, default);
    Check(f.Model.Calls == 2 && result.Steps!.Count == 0 && result.Summary.Contains("could not prepare"), "length/incomplete JSON is refused whole with no quote repair or trailing text salvage");
    Check(!string.Join(" ", result.Lessons!.Select(l => l.Instruction)).Contains("104 words"), "invalid model body is never exposed as answer");
}
using (var f = new Fixture()) {
    var curly = useful with { Purpose = "The \u201cZoom\u201d label commonly describes displayed magnification." };
    f.Model.Replies.Enqueue(_ => Task.FromResult(Model.Json(curly)));
    var result = await f.Service.PlanGuide(zoom, default);
    Check(f.Model.Calls == 1 && result.Lessons!.Single().Instruction.StartsWith(curly.Purpose), "valid curly characters within ASCII-delimited JSON survive exactly");
    using var body = JsonDocument.Parse(f.Model.Bodies.Single());
    string system = body.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
    Check(!system.Contains("90 words") && system.Contains("one short complete sentence") && system.Contains("No external sources were supplied"), "reduced conceptual prompt retains sentence/source constraints without exact word-count demand");
    Check(body.RootElement.GetProperty("format").ValueKind == JsonValueKind.Object && !system.Contains("\\u0022"), "wire schema is an object and prompt JSON delimiter quotes are not double serialized");
}
using (var input = JsonDocument.Parse(GuideLessons.ExplanationInput(zoom with { Query = "Explain the user's \"Zoom\" control." })))
    Check(input.RootElement.GetProperty("question").GetString() == "Explain the user's \"Zoom\" control.", "nested apostrophe/quoted-name input roundtrips without global unescaping");

using (var f = new Fixture()) {
    f.Model.Replies.Enqueue(_ => Task.FromResult(Model.Json(new { query = "rainbow formation water droplets" })));
    var result = await f.Service.PrepareRegionResearchQuery("Why does this rainbow form?", "The selected image shows a rainbow near falling water.", default);
    Check(result.Query == "rainbow formation water droplets" && result.Message.Contains("Review and edit") && result.Message.Contains("No search was sent"), "local query suggestion requires distinct user review");
    Check(f.Model.Calls == 1 && !f.Service.WebEnabled, "local preparation works with web disabled and only injected local engine");
    using var payload = JsonDocument.Parse(f.Model.Bodies.Single());
    Check(!payload.RootElement.GetProperty("messages")[1].TryGetProperty("images", out _), "suggestion API carries no image attachment");
    Check(await f.Store.Read(s => s.Conversations.Count + s.Knowledge.Count + s.Audit.Count + s.Jobs.Count) == 0, "local suggestion saves no context/query/history");
}
foreach (var query in new[] { "", " secret", "password=fixture", "rain\nbow", new string('x', 161) }) using (var f = new Fixture()) {
    f.Model.Replies.Enqueue(_ => Task.FromResult(Model.Json(new { query })));
    var result = await f.Service.PrepareRegionResearchQuery("Explain the selected topic.", "A bounded local answer.", default);
    Check(result.Query == "" && result.Message.Contains("No search was sent"), "invalid or sensitive-token suggestion is not silently rewritten/submitted");
}
using (var f = new Fixture()) {
    using var stop = new CancellationTokenSource(); stop.Cancel();
    try { await f.Service.PrepareRegionResearchQuery("A topic", "Local answer", stop.Token); throw new Exception("FAIL cancellation"); }
    catch (OperationCanceledException) { Check(f.Model.Calls == 0, "pre-canceled query suggestion performs no inference"); }
}
using (var f = new Fixture(TimeSpan.FromMilliseconds(80))) {
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var finish = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
    f.Model.Replies.Enqueue(_ => { entered.SetResult(); return finish.Task; });
    var pending = f.Service.PrepareRegionResearchQuery("A public topic", "A local answer", default);
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
    try { await pending.WaitAsync(TimeSpan.FromSeconds(3)); throw new Exception("FAIL timeout"); }
    catch (BuddyException e) { Check(e.Code == "RESEARCH_TIMEOUT", "deadline returns despite cancellation-ignoring inference"); }
    try { await f.Service.PrepareRegionResearchQuery("A public topic", "A local answer", default); throw new Exception("FAIL overlap"); }
    catch (BuddyException e) { Check(e.Code == "RESEARCH_BUSY" && f.Model.Calls == 1, "late inference retains ownership and prevents overlapping suggestion"); }
    finish.SetResult(Model.Json(new { query = "public topic" }));
    for (int i = 0; i < 100; i++) {
        var count = (int)typeof(BuddyService).GetField("reviewedResearchRunning", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(f.Service)!;
        if (count == 0) break;
        await Task.Delay(5);
    }
    Check((int)typeof(BuddyService).GetField("reviewedResearchRunning", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(f.Service)! == 0, "late result discarded and ownership eventually released");
}
Console.WriteLine($"PASS {checks} teaching follow-up checks; injected models only, no desktop, real model, web or audio.");

sealed class Fixture : IDisposable {
    private readonly string path = Path.Combine(Path.GetTempPath(), "Buddy-Teaching53-" + Guid.NewGuid().ToString("N"));
    private readonly HttpClient client;
    internal readonly Model Model = new();
    internal readonly StateStore Store;
    internal readonly BuddyService Service;
    internal Fixture(TimeSpan? total = null) {
        Directory.CreateDirectory(path); Store = new(path, new EphemeralDataProtectionProvider());
        client = new(Model) { BaseAddress = new("http://127.0.0.1:11434/") };
        Service = new(Store, new(client)) { WebEnabled = false, AgentEnabled = false,
            RegionResearchLimits = total is { } t ? new(TimeSpan.FromMilliseconds(20), t) : ReviewedRegionResearchLimits.Default };
    }
    public void Dispose() { client.Dispose(); Directory.Delete(path, true); }
}
sealed class Model : HttpMessageHandler {
    internal int Calls;
    internal readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> Replies = new();
    internal readonly List<string> Bodies = [];
    internal static HttpResponseMessage Json(object answer) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { message = new { content = JsonSerializer.Serialize(answer, StateStore.Json) }, done = true })) };
    internal static HttpResponseMessage Raw(string content, string reason) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { message = new { content }, done = true, done_reason = reason })) };
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
        if (request.RequestUri?.Host != "127.0.0.1") throw new Exception("Unexpected nonlocal request");
        Calls++; Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
        return await Replies.Dequeue()(ct);
    }
}
