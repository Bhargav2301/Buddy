using Buddy.Server;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

FixtureFiles.SourceManifest();
int passed = 0, failed = 0;
bool goldens = args.Contains("--goldens-only");
string area = args.FirstOrDefault(a => a.StartsWith("--area="))?[7..] ?? "all";
async Task Case(string name, string group, Func<Task> test, bool golden = false)
{
    if (goldens && !golden || area != "all" && area != group) return;
    var clock = Stopwatch.StartNew();
    try { await test(); passed++; Console.WriteLine(JsonSerializer.Serialize(new { kind = "case", name, status = "PASS", elapsedMs = clock.ElapsedMilliseconds, golden })); }
    catch (Exception e) { failed++; Console.WriteLine(JsonSerializer.Serialize(new { kind = "case", name, status = "FAIL", elapsedMs = clock.ElapsedMilliseconds, golden, reason = e.Message, exception = e.GetType().Name })); }
}
void Require(bool value, string reason) { if (!value) throw new Exception(reason); }
async Task<bool> Cancelled(Func<Task> action)
{
    try { await action().WaitAsync(TimeSpan.FromSeconds(8)); return false; }
    catch (OperationCanceledException) { return true; }
}
ScreenContext Empty() => new("", "Owned diagnostic: no accessible window or observed controls", []);
AssistantPlan Launch(string target = "comet", string value = "", string reference = "", string role = "Application") =>
    new("Comet browser is not accessible. Please verify the application is open and visible.", [new("open", reference, target, role, value, "Open Comet browser.", "low")]);
PlanningRequest PlanRequest() => new("Guide me through Comet browser", Empty(), false);
int SentenceEnds(string s) => Regex.Matches(s, @"[.!?]+(?=\s|$)").Count;
string Answer(List<StreamEvent> events) => string.Concat(events.Where(e => e.Type == "delta").Select(e => e.Text));
void Assessment(MockModel model) => model.Json(new { preserved = true, scoreBefore = 75, scoreAfter = 85, changes = new[] { "Added reason for request", "Added constraint regarding deadlines/commitments" } });

await Case("installed golden: Comet alias in target with empty value becomes canonical reviewable plan", "planning", async () => {
    using var f = new Fixture(); f.Model.Json(Launch()); f.Model.Json(Launch());
    var result = await f.Service.PlanAgent(PlanRequest(), default);
    Require(result.Actions is { Count: 1 } && result.Actions[0].Kind == "open" && result.Actions[0].Value == "comet", "Recover the observed field-confusion without dropping the user's task.");
    Require(result.Actions![0].Risk == "high", "An inferred low-risk label must not weaken launch approval.");
    Require(await f.Store.Read(s => s.Audit.Count + s.Guides.Count + s.Jobs.Count + s.Conversations.Count) == 0, "Plan creation must not execute or persist task completion.");
}, true);

await Case("model clarification is returned for review but strict execution validator refuses it", "planning", async () => {
    using var f = new Fixture(); f.Model.Json(new AssistantPlan("Which visible button should I use?", []));
    var plan = await f.Service.PlanAgent(new("Help with the selected button", Empty()), default);
    Require(plan.Actions is { Count: 0 } && !string.IsNullOrWhiteSpace(plan.Summary), "Preserve useful non-executable clarification.");
    bool rejected = false; try { ActionPolicy.Validate(plan); } catch (BuddyException) { rejected = true; }
    Require(rejected, "Review acceptance must not broaden the execution validator.");
});

foreach (var bad in new[] {
    ("unknown alias", Launch(target: "unknown-browser")),
    ("path target", Launch(target: @"C:\\Windows\\System32\\cmd.exe")),
    ("launch arguments", Launch(target: "comet --remote-debugging-port=9222")),
    ("conflicting alias", Launch(target: "comet", value: "notepad")),
    ("target URL promotion", Launch(target: "https://example.com")),
    ("observed ref promotion", Launch(reference: "observed-button")),
    ("button role promotion", Launch(role: "Button")),
    ("loopback URL", Launch(target: "", value: "https://127.0.0.1/"))
}) await Case("launch boundary: " + bad.Item1, "planning", async () => {
    using var f = new Fixture(); f.Model.Json(bad.Item2); f.Model.Json(bad.Item2);
    try { var plan = await f.Service.PlanAgent(PlanRequest(), default); Require(plan.Actions is { Count: 0 }, "Untrusted malformed launch cannot become an executable action."); }
    catch (BuddyException e) { Require(e.Code is "INVALID_PLAN" or "INVALID_URL", "Unexpected failure: " + e.Code); }
    Require(f.Model.StructuredCalls <= 2, "Repair must remain bounded.");
});

await Case("pre-cancelled deterministic launch produces no plan", "planning", async () => {
    using var f = new Fixture(); using var stop = new CancellationTokenSource(); stop.Cancel();
    Require(await Cancelled(async () => { await f.Service.PlanAgent(new("Open Comet Browser", Empty()), stop.Token); }), "Deterministic routes also need to respect cancellation.");
    Require(f.Model.Requests.Count == 0, "Pre-cancelled plan must not infer.");
});

await Case("pre-cancelled deterministic Notepad lesson produces no plan", "planning", async () => {
    using var f = new Fixture(); using var stop = new CancellationTokenSource(); stop.Cancel();
    Require(await Cancelled(async () => { await f.Service.PlanGuide(new("Teach me Notepad basics", Empty()), stop.Token); }), "Built-in guide lessons must respect cancellation too.");
    Require(f.Model.Requests.Count == 0, "Pre-cancelled built-in lesson must not infer.");
});

await Case("malformed model output has one bounded repair then a reviewable canonical action", "planning", async () => {
    using var f = new Fixture(); f.Model.Raw("{\"summary\":"); f.Model.Json(Launch(target: "", value: "comet"));
    var result = await f.Service.PlanAgent(PlanRequest(), default);
    Require(f.Model.StructuredCalls == 2 && result.Actions is { Count: 1 } && result.Actions[0].Value == "comet", "Recover a model JSON failure once without silently dropping the task.");
});

await Case("two truncated model responses yield non-executable clarification", "planning", async () => {
    using var f = new Fixture();
    string validLooking = JsonSerializer.Serialize(Launch(target: "", value: "comet"), StateStore.Json);
    f.Model.Raw(validLooking, "length"); f.Model.Raw(validLooking, "length");
    var result = await f.Service.PlanAgent(PlanRequest(), default);
    Require(f.Model.StructuredCalls == 2 && result.Actions is { Count: 0 } && !string.IsNullOrWhiteSpace(result.Summary), "Token-limit termination cannot be accepted as a complete actionable plan.");
});

await Case("an explicit public HTTPS launch remains reviewable without fetching it", "planning", async () => {
    using var f = new Fixture(); f.Model.Json(Launch(target: "", value: "https://example.com/manual", role: ""));
    var result = await f.Service.PlanAgent(new("Open https://example.com/manual", Empty()), default);
    Require(result.Actions is { Count: 1 } && result.Actions[0].Value == "https://example.com/manual" && result.Actions[0].Risk == "high", "Repairs must preserve supported URL plans and approval risk.");
});

await Case("cancelled inference returns no late plan or persistence", "planning", async () => {
    using var f = new Fixture(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    f.Model.Replies.Enqueue(async (_, _) => { entered.TrySetResult(); await release.Task; return MockModel.Response(JsonSerializer.Serialize(Launch(value: "comet"), StateStore.Json)); });
    using var stop = new CancellationTokenSource();
    var pending = f.Service.PlanAgent(PlanRequest(), stop.Token);
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); stop.Cancel(); release.TrySetResult();
    Require(await Cancelled(async () => { await pending; }), "Late transport response cannot revive a cancelled plan.");
    Require(await f.Store.Read(s => s.Audit.Count + s.Guides.Count + s.Jobs.Count) == 0, "Cancelled plan must not persist.");
});

await Case("Comet request cannot bind a coincidentally named control in another app", "planning", async () => {
    using var f = new Fixture();
    var wrong = new ScreenContext("unrelated-app", "Owned unrelated fixture", [new("qa-export", "Export", "Button", 1, 1, 100, 40)]);
    f.Model.Json(new { summary = "Focus Comet so its controls can be observed.", steps = new[] { new GuideStep("Choose Export.", "qa-export", "Export", "Button", "ring", new("manual")) } });
    var result = await f.Service.PlanGuide(new("Guide me through Comet browser export", wrong), default);
    Require(result.Steps is { Count: 0 }, "Matching text in an unrelated app is not Comet evidence.");
});

await Case("nested GuidePlan keeps an exact observed synthetic target", "planning", async () => {
    using var f = new Fixture();
    var context = new ScreenContext("owned-qa-fixture", "Owned synthetic Guide fixture", [new("qa-export", "Export example", "Button", 1, 1, 100, 40)]);
    f.Model.Json(new GuidePlan("The observed Export example button is below.", [new("Choose Export example when ready.", "qa-export", "Export example", "Button", "ring", new("manual"))]));
    var result = await f.Service.PlanGuide(new("Show the Export example button", context), default);
    Require(result.Steps is { Count: 1 } && result.Steps[0].Ref == "qa-export" && result.Steps[0].Target == "Export example", "Complete nested plan must remain grounded in the supplied snapshot.");
});

const string badChat = "A rainbow is formed when sunlight is refracted and reflected through raindrops, creating a beautiful arc of colors. Would you like me to Guide you to some resources about rainbows, or perhaps use my Agent to show you an image?";
const string goodChat = "Rainbows form when sunlight bends and reflects inside water droplets.";
await Case("installed golden: explicit one sentence regenerates the entire promotional two-sentence reply", "chat", async () => {
    using var f = new Fixture(); f.Model.Text(badChat); f.Model.Text(goodChat);
    var (events, saved) = await f.Chat("Explain rainbows in one sentence.");
    Require(Answer(events) == goodChat && f.Model.PlainCalls == 2, "Do not emit the invalid answer or truncate it; regenerate once.");
    Require(saved.Count == 2 && saved[1].Text == goodChat && events.Count(e => e.Type == "done") == 1, "Only the accepted answer is stored/completed.");
}, true);

await Case("repeated length failure uses one complete non-promotional fallback", "chat", async () => {
    using var f = new Fixture(); f.Model.Text(badChat); f.Model.Text(badChat);
    var (events, saved) = await f.Chat("Explain rainbows in one sentence."); string answer = Answer(events);
    Require(SentenceEnds(answer) == 1 && !answer.Contains("Guide", StringComparison.OrdinalIgnoreCase) && !answer.Contains("Agent", StringComparison.OrdinalIgnoreCase), "Fallback must honor the explicit shorter limit and avoid capability promotion.");
    Require(answer != badChat.Split('.')[0] + "." && saved.Count == 2, "No first-sentence truncation can masquerade as a reviewed complete answer.");
});

await Case("invalid first answer is never emitted before a later safety qualification", "chat", async () => {
    using var f = new Fixture(); f.Model.Text("The process always works. It is predictable. It is widely used. However, that claim is conditional on clear skies.");
    const string safe = "The observation depends on clear skies and a suitable viewing angle."; f.Model.Text(safe);
    var (events, _) = await f.Chat("Explain the observation in one sentence.");
    Require(Answer(events) == safe, "Whole-answer validation must retain the late qualification through regeneration.");
});

foreach (string prompt in new[] { "Explain rainbows. Respond with one sentence.", "Explain rainbows. One sentence please." })
await Case("explicit shorter reply variant: " + prompt, "chat", async () => {
    using var f = new Fixture(); f.Model.Text("Rainbows involve light. Water droplets split it into colors."); f.Model.Text(goodChat);
    var (events, _) = await f.Chat(prompt);
    Require(Answer(events) == goodChat && f.Model.PlainCalls == 2, "The explicit one-sentence wording must constrain the whole reply.");
});

const string original = "Write a polite email asking for Friday off.";
const string invented = "Write a polite email requesting Friday off, specifying the reason for the request and confirming it does not conflict with any existing deadlines or commitments.";
await Case("installed golden: model attestation and high cosine cannot bless invented email requirements", "refine", async () => {
    using var f = new Fixture(); f.Model.Text(invented); Assessment(f.Model);
    var result = await f.Service.RefineDetailed(new(original), default);
    Require(!result.Accepted && result.RefinedPrompt == original, "Reason/deadline obligations absent from the draft must not be accepted, even at cosine=1.");
}, true);

foreach (var pair in new[] {
    ("Preserve negation", "Write a polite Friday leave request; do not disclose a reason.", "Write a polite Friday leave request and disclose a reason."),
    ("Preserve emoji", "Write a greeting containing 🌈 exactly once.", "Write a greeting."),
    ("Preserve fenced code", "Explain this code without changing it: `return total <= 10;`", "Explain this code: `return total < 10;`"),
    ("No invented tools", "Summarize the supplied notes.", "Search the web and use the browser tool to summarize the supplied notes."),
    ("No private chain-of-thought", "Solve the supplied arithmetic problem with a brief explanation.", "Solve the supplied arithmetic problem and reveal your complete private chain of thought.")
}) await Case("refinement fidelity: " + pair.Item1, "refine", async () => {
    using var f = new Fixture(); f.Model.Text(pair.Item3); Assessment(f.Model);
    var result = await f.Service.RefineDetailed(new(pair.Item2), default);
    Require(!result.Accepted && result.RefinedPrompt == pair.Item2, "Rejected rewrite must retain the exact original source text.");
});

await Case("source-grounded task and purpose structure remains usable", "refine", async () => {
    using var f = new Fixture();
    f.Model.Json(new { sections = new[] { new { kind = "task", sourceIds = new[] { "s0" } }, new { kind = "purpose", sourceIds = new[] { "s1" } } } });
    f.Model.Json(new { preserved = true, scoreBefore = 75, scoreAfter = 85, changes = new[] { "Polished the request verb" } });
    var result = await f.Service.RefineDetailed(new(original), default);
    Require(result.Accepted && result.Method == "source-structure" && result.RefinedPrompt == "Request: Write a polite email.\n\nPurpose: asking for Friday off.", "Source-grounded structure must retain the supplied tone and purpose without inventing a reason or deadline.");
    Require(result.ScoreBefore is null && result.ScoreAfter is null && !result.Changes.Contains("Polished the request verb"), "Model scores and invented change prose do not represent host-verified structure.");
});

await Case("cancellation at the refinement validation boundary emits no late result", "refine", async () => {
    using var f = new Fixture();
    f.Model.Json(new { sections = new[] { new { kind = "task", sourceIds = new[] { "s0" } }, new { kind = "purpose", sourceIds = new[] { "s1" } } } });
    Assessment(f.Model);
    using var stop = new CancellationTokenSource();
    await using var stream = f.Service.RefineStream(new(original), stop.Token).GetAsyncEnumerator();
    bool reached = false;
    while (await stream.MoveNextAsync())
        if (stream.Current.Type == "stage" && stream.Current.Text == "Checking intent and constraints") { reached = true; break; }
    Require(reached, "Fixture must reach the actual pre-validation yield.");
    stop.Cancel();
    Require(await Cancelled(async () => { while (await stream.MoveNextAsync()) { if (stream.Current.Result is not null) throw new Exception("Cancelled refinement emitted a stale result."); } }), "Cancellation must prevent even a kept-original completion from reviving stale UI.");
});

await CoreChecks.Run((name, test) => Case(name, "core", test));
Console.WriteLine(JsonSerializer.Serialize(new { kind = "summary", passed, failed, mockOnly = true, liveModel = false, uiAcceptance = false }));
return failed == 0 ? 0 : 1;
