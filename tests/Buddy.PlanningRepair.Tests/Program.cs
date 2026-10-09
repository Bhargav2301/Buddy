using Buddy.Server;
using Microsoft.AspNetCore.DataProtection;
using System.Net;
using System.Text;
using System.Text.Json;

int checks = 0;
void Check(bool value, string label) { if (!value) throw new Exception("FAIL: " + label); checks++; Console.WriteLine("PASS: " + label); }
void Reject(Action action, string label) { try { action(); } catch (BuddyException ex) when (ex.Code == "INVALID_PLAN" || ex.Code == "INVALID_GUIDE") { Check(true, label); return; } throw new Exception("FAIL: " + label); }
async Task<bool> Cancelled(Task task) { try { await task.WaitAsync(TimeSpan.FromSeconds(5)); return false; } catch (OperationCanceledException) { return true; } }
AssistantPlan Plan(AssistantAction action) => new("Review the proposed action; nothing has run.", [action]);
var observedFailure = new AssistantAction("open", Target: "comet", Role: "Application", Value: "", Description: "Open Comet browser.", Risk: "low");
var reviewed = ActionPolicy.ValidateForReview(Plan(observedFailure));
Check(reviewed.Actions is { Count: 1 } && reviewed.Actions[0] is { Value: "comet", Target: "", Role: "", Ref: "", Risk: "high" }, "Exact measured target/value swap becomes one canonical high-risk Comet review action");
Reject(() => ActionPolicy.Validate(Plan(observedFailure)), "Execution validation still rejects the unnormalized malformed launch");
foreach (var alias in new[] { "comet", "Comet Browser", "COMET.EXE", "Notepad.exe", "calculator", "File Explorer" }) {
    var action = ActionPolicy.ValidateForReview(Plan(new("open", Value: alias))).Actions!.Single();
    Check(ActionPolicy.Apps.Contains(action.Value) && action.Risk == "high", "Exact audited alias canonicalized for review: " + alias);
}
Check(ActionPolicy.ValidateForReview(Plan(new("open", Value: "COMET.EXE", Target: "Comet Browser", Role: "Application"))).Actions![0].Value == "comet", "Matching known fields can be normalized without changing app intent");
foreach (var value in new[] { "comet --flag", "comet & calc", "C:\\Apps\\comet.exe", "../comet.exe", "cmd.exe", "Comet Browser.exe", "some installed app", "https://127.0.0.1/", "http://example.com", "https://user:secret@example.com", "https://example.com:444" }) {
    Reject(() => ActionPolicy.ValidateForReview(Plan(new("open", Value: value))), "Unsafe/unknown launch remains rejected: " + value);
    Reject(() => ActionPolicy.ValidateForReview(Plan(new("open", Target: value))), "Unsafe/unknown target cannot migrate into launch value: " + value);
}
foreach (var action in new[] {
    new AssistantAction("open", Value: "notepad", Target: "comet"), new AssistantAction("open", Value: "comet", Target: "browser"),
    observedFailure with { Ref = "screen-ref" }, observedFailure with { Role = "Button" }, observedFailure with { Target = null! },
    new AssistantAction("shell", Value: "comet"), new AssistantAction("keys", Value: "Win+R")
}) Reject(() => ActionPolicy.ValidateForReview(Plan(action)), "Conflicting or incompatible fields cannot become an executable review plan");
Check(ActionPolicy.ValidateForReview(Plan(new("open", Value: "https://example.com/page"))).Actions![0].Value == "https://example.com/page", "Existing explicit public HTTPS value remains supported");
var clarification = new AssistantPlan("Which visible control do you want to use?", []);
Check(ActionPolicy.ValidateForReview(clarification).Actions!.Count == 0, "Actionless clarification is a review result");
Reject(() => ActionPolicy.Validate(clarification), "Actionless clarification is still rejected by the execution validator");
Reject(() => ActionPolicy.ValidateForReview(new("", [])), "Empty explanation cannot masquerade as clarification");
Reject(() => ActionPolicy.ValidateForReview(new("Question?", null)), "Missing action array is not a clarification");
Reject(() => ActionPolicy.ValidateForReview(new("Too many", Enumerable.Repeat(new AssistantAction("wait"), 26).ToList())), "Review remains bounded to 25 actions");
var schemaActions = AssistantSchemas.Agent.GetProperty("properties").GetProperty("actions");
var openSchema = schemaActions.GetProperty("items").GetProperty("oneOf").EnumerateArray().Single(x => x.GetProperty("properties").GetProperty("kind").GetProperty("enum")[0].GetString() == "open");
Check(schemaActions.GetProperty("minItems").GetInt32() == 0 && schemaActions.GetProperty("maxItems").GetInt32() == 25, "Planning schema explicitly supports bounded non-executable clarification");
Check(openSchema.GetProperty("properties").GetProperty("value").GetProperty("anyOf")[0].GetProperty("enum").EnumerateArray().Select(x => x.GetString()).SequenceEqual(ActionPolicy.Apps), "Open schema restricts canonical app values instead of unconstrained free text");
Check(new[] { "ref", "target", "role" }.All(k => openSchema.GetProperty("properties").GetProperty(k).GetProperty("enum")[0].GetString() == ""), "Open schema puts the launch destination exclusively in value");

var noWindow = new ScreenContext("", "No accessible window. No current controls were observed.", []);
await using (var f = new Fixture()) {
    f.Model.Structured(Plan(observedFailure));
    var plan = await f.Service.PlanAgent(new("Guide me through Comet browser", noWindow), default);
    Check(plan.Actions!.Single().Value == "comet" && f.Model.Calls == 1, "Service repairs the captured installed-model failure without a second inference or execution");
    var pure = await f.Service.PlanAgent(new("Open Comet Browser", noWindow), default);
    Check(pure.Actions!.Single().Value == "comet" && f.Model.Calls == 1, "Existing deterministic exact Comet command still requires no inference");
}
await using (var f = new Fixture()) {
    f.Model.Structured(clarification);
    var plan = await f.Service.PlanAgent(new("Help with the selected app", noWindow), default);
    Check(plan.Summary == clarification.Summary && plan.Actions is { Count: 0 } && f.Model.Calls == 1, "Model clarification reaches review without an INVALID_PLAN error");
}
await using (var f = new Fixture()) {
    f.Model.InvalidJson(); f.Model.Structured(Plan(new("open", Value: "comet")));
    var plan = await f.Service.PlanAgent(new("Please launch Comet", noWindow), default);
    Check(plan.Actions!.Single().Value == "comet" && f.Model.Calls == 2, "Malformed first model response gets exactly one bounded correction");
    Check(f.Model.Payloads[1].Contains("previous response was invalid") && f.Model.Payloads[1].Contains("Do not add actions"), "Correction preserves original goal and prohibits substitute tasks");
}
await using (var f = new Fixture()) {
    f.Model.Structured(Plan(new("open", Value: "cmd.exe /c whoami"))); f.Model.Structured(Plan(new("open", Value: "unlisted app")));
    var plan = await f.Service.PlanAgent(new("Please launch Comet", noWindow), default);
    Check(plan.Actions is { Count: 0 } && plan.Summary.EndsWith('?') && f.Model.Calls == 2, "Two unsafe model plans end in a question without executable fallback");
    Reject(() => ActionPolicy.Validate(plan), "Exhausted-repair result cannot be executed");
}
await using (var f = new Fixture()) {
    using var stopped = new CancellationTokenSource(); stopped.Cancel();
    Check(await Cancelled(f.Service.PlanAgent(new("Open Comet Browser", noWindow), stopped.Token)) && f.Model.Calls == 0, "Pre-cancelled deterministic request cannot return a launch plan");
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    f.Model.Replies.Enqueue(async ct => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); return ""; });
    var pending = f.Service.PlanAgent(new("Please launch Comet", noWindow), default); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); f.Service.StopAll();
    Check(await Cancelled(pending) && f.Model.Calls == 1, "StopAll cancels inference and prevents repair calls");
}
await using (var f = new Fixture()) {
    f.Model.Replies.Enqueue(ct => Task.FromException<string>(new HttpRequestException("fixture model disconnected")));
    bool failed = false; try { await f.Service.PlanAgent(new("Please launch Comet", noWindow), default); } catch (HttpRequestException) { failed = true; }
    Check(failed && f.Model.Calls == 1, "Transport outage is reported rather than converted to a successful clarification");
}

var controls = new List<ScreenElement> { new("export", "Export", "Button", 0, 0, 80, 25) };
var wrongApp = new ScreenContext("notepad", "Other application", controls);
var guide = new GuidePlan("Choose Export.", [new("Choose Export.", "export", "Export", "Button")]);
Check(GuideSafety.RequestedApp("Guide me through Comet browser") == "comet", "Comet Guide requests bind to process comet");
Reject(() => GuideSafety.Validate(guide, new("Guide me through Comet browser", wrongApp)), "Comet guidance cannot bind an identically named control in Notepad");
Check(GuideSafety.Validate(guide, new("Guide me through Comet browser", wrongApp with { App = "comet" })).Steps!.Count == 1, "Current Comet control still passes ordinary grounding validation");
Check(GuideLessons.Resolve(new("Choose Export.", "Export", "Button"), wrongApp, "Guide me through Comet browser") is null, "Future lesson resolution also refuses wrong-app Comet pointers");
foreach (var unobserved in new[] { noWindow, wrongApp, noWindow with { App = "comet" } }) {
    await using var f = new Fixture();
    f.Model.Structured(new { summary = "Comet is not responding." });
    var result = await f.Service.PlanGuide(new("Guide me through Comet browser", unobserved), default);
    bool missingTargetInCorrectApp = unobserved.App == "comet";
    Check(result.Steps is { Count: 0 } && result.Lessons?.Count == (missingTargetInCorrectApp ? 1 : 2) && f.Model.Calls == 0, "Missing Comet controls use deterministic observation guidance without model health guesses");
    Check(missingTargetInCorrectApp
        ? result.Lessons![0].Instruction.Contains("No enabled controls") && result.Lessons[0].Instruction.Contains("fresh view")
        : result.Lessons![0].Instruction.Contains("do not have a verified view") && result.Lessons[1].Instruction.Contains("Focus Comet"), "Missing-view guidance states only the evidence gap and the next observation step");
}
await using (var f = new Fixture()) {
    f.Model.Structured(guide);
    var result = await f.Service.PlanGuide(new("Guide me through Comet browser", wrongApp with { App = "comet" }), default);
    Check(result.Steps is { Count: 1 } && result.Steps[0].Ref == "export" && f.Model.Calls == 1, "Observed Comet controls retain real model planning and exact grounding");
}
await using (var f = new Fixture()) {
    f.Model.InvalidJson(); f.Model.InvalidJson();
    var result = await f.Service.PlanGuide(new("Explain this tool without changing anything.", wrongApp), default);
    Check(result.Steps is { Count: 0 } && result.Lessons is { Count: 1 } && f.Model.Calls == 2 && result.Summary.Contains("could not prepare a reliable explanation"), "Malformed conceptual orientation stops after two attempts with a nonexecuting limitation");
}
var editorContext = new ScreenContext("owned-fixture", "Owned editor", [new("current-editor", "Example text", "Edit", 0, 0, 120, 40)]);
var malformedEditor = new GuidePlan("Select the editor.", [new("Select Example text.", "current-editor", "Edit", "Edit")]);
var exactEditor = new GuidePlan("Select the editor.", [new("Select Example text.", "current-editor", "Example text", "Edit")]);
await using (var f = new Fixture()) {
    f.Model.Structured(malformedEditor); f.Model.Structured(exactEditor);
    var result = await f.Service.PlanGuide(new("Guide me to Example text", editorContext), default);
    Check(result.Steps is { Count: 1 } && result.Steps[0].Target == "Example text" && f.Model.Calls == 2, "Captured role-as-target Guide failure gets one bounded exact-control correction");
    Check(f.Model.Payloads[1].Contains("role such as Edit is not the target name"), "Guide correction explains the malformed current-control contract without guessing a name");
}
await using (var f = new Fixture()) {
    f.Model.Structured(malformedEditor); f.Model.Structured(malformedEditor);
    var result = await f.Service.PlanGuide(new("Guide me to Example text", editorContext), default);
    Check(result.Steps is { Count: 0 } && result.Lessons is { Count: 1 } && f.Model.Calls == 2 && GuideLessons.Resolve(result.Lessons[0], editorContext, "Guide me to Example text") is null, "Repeated malformed Guide targets retain text but never manufacture a pointer or a third attempt");
}
await using (var f = new Fixture()) {
    f.Model.Structured(malformedEditor); f.Model.InvalidJson();
    var result = await f.Service.PlanGuide(new("Guide me to Example text", editorContext), default);
    Check(result.Steps is { Count: 0 } && result.Lessons is { Count: 1 } && f.Model.Calls == 2, "Invalid correction preserves the previous bounded explanation without another model call");
}
await using (var f = new Fixture()) {
    f.Model.Structured(new GuidePlan("Current and later lesson.", [exactEditor.Steps![0], new("Later choose Format.", "future-format", "Format", "ComboBox")]));
    var result = await f.Service.PlanGuide(new("Guide me through editing", editorContext), default);
    Check(result.Steps is { Count: 1 } && result.Lessons is { Count: 2 } && f.Model.Calls == 1, "A valid current pointer plus future explanatory section stays a single-call lesson");
}
await using (var f = new Fixture()) {
    f.Model.Structured(new GuidePlan("Which edit do you want?", []));
    var result = await f.Service.PlanGuide(new("Guide me through editing", editorContext), default);
    Check(result.Steps is { Count: 0 } && f.Model.Calls == 1, "Deliberate empty-step Guide clarification does not trigger pointer repair");
}
await using (var f = new Fixture()) {
    f.Model.Structured(new GuidePlan("Review both suggestions.", [exactEditor.Steps![0], malformedEditor.Steps![0]])); f.Model.Structured(exactEditor);
    var result = await f.Service.PlanGuide(new("Guide me to Example text", editorContext), default);
    Check(result.Steps is { Count: 1 } && result.Lessons is { Count: 1 } && f.Model.Calls == 2, "A contradictory current reference is repaired even when another step has a valid pointer");
}
var twoEditors = editorContext with { Elements = [..editorContext.Elements, new("other-editor", "Other text", "Edit", 150, 0, 120, 40)] };
foreach (var reference in new[] { "current-editor", "unknown-reference" }) foreach (bool invalidCorrection in new[] { false, true }) await using (var f = new Fixture()) {
    var contradictory = new GuidePlan("Review the text.", [new("Review Other text.", reference, "Other text", "Edit")]);
    f.Model.Structured(contradictory);
    if (invalidCorrection) f.Model.InvalidJson(); else f.Model.Structured(contradictory);
    var result = await f.Service.PlanGuide(new("Guide me to Example text", twoEditors), default);
    Check(f.Model.Calls == 2 && result.Steps is { Count: 0 } && result.Lessons is { Count: 1 }, "Rejected current-reference/name mismatch remains a bounded explanation after failed correction");
    Check(result.Lessons![0].Instruction == "Review Other text." && result.Lessons[0].Target == "" && result.Lessons[0].Role == "" && GuideLessons.Resolve(result.Lessons[0], twoEditors, "Guide me to Example text") is null,
        "A rejected pointer cannot regain ink through the other observed control's matching lesson name");
}
await using (var f = new Fixture()) {
    var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var releaseSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    f.Model.Replies.Enqueue(async ct => { first.TrySetResult(); await releaseFirst.Task; return JsonSerializer.Serialize(malformedEditor, StateStore.Json); });
    f.Model.Replies.Enqueue(async ct => { second.TrySetResult(); await releaseSecond.Task; return JsonSerializer.Serialize(exactEditor, StateStore.Json); });
    var pending = f.Service.PlanGuide(new("Guide me to Example text", editorContext), default);
    await first.Task.WaitAsync(TimeSpan.FromSeconds(5));
    var active = (System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource>)typeof(BuddyService).GetField("active", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(f.Service)!;
    var owner = active.Single(pair => pair.Key.StartsWith("guide-review:", StringComparison.Ordinal));
    releaseFirst.TrySetResult(); await second.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Check(active.TryGetValue(owner.Key, out var currentOwner) && ReferenceEquals(owner.Value, currentOwner) && !owner.Value.IsCancellationRequested,
        "One Guide cancellation owner spans the first inference and correction without being replaced between attempts");
    f.Service.StopAll(); releaseSecond.TrySetResult();
    Check(await Cancelled(pending) && f.Model.Calls == 2 && !active.ContainsKey(owner.Key), "Stop cancels the entire Guide repair and rejects a late corrected pointer, then removes its owner");
}
await using (var f = new Fixture()) {
    using var stopped = new CancellationTokenSource(); stopped.Cancel();
    Check(await Cancelled(f.Service.PlanGuide(new("Guide me through Notepad", noWindow), stopped.Token)) && f.Model.Calls == 0, "Pre-cancelled deterministic Guide request returns no lesson or model call");
    var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    f.Model.Replies.Enqueue(async ct => { entered.TrySetResult(); await release.Task; return "{\"summary\":\"Which part would you like to learn?\"}"; });
    var pending = f.Service.PlanGuide(new("Guide me through this tool", wrongApp), default);
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); f.Service.StopAll(); release.TrySetResult();
    Check(await Cancelled(pending) && f.Model.Calls == 1, "Guide rejects a late model response after StopAll even when transport ignores cancellation");
}

foreach (var pair in new[] {
    ("Explain rainbows in one sentence.", 1), ("In 2 sentences, explain rainbows.", 2), ("Use a single sentence.", 1),
    ("Give a one-sentence answer about rainbows.", 1), ("Two sentences: explain rainbows.", 2),
    ("Explain rainbows.", 3), ("Explain rainbows in five sentences.", 5),
    ("Explain the phrase \"in one sentence\" in two sentences.", 2), ("Discuss 'one sentence' as a phrase.", 3),
    ("Respond with one sentence.", 1), ("Explain rainbows. One sentence please.", 1), ("Explain rainbows. Two sentences please", 2)
}) Check(ReplyConstraints.FromRequest(pair.Item1).SentenceLimit == pair.Item2, "Explicit reply limit parsed: " + pair.Item1);
Check(ConversationalReply.Sentences(ConversationalReply.Fallback) == 1, "Safe fallback also satisfies an explicit single-sentence limit");
Check(!OllamaEngine.Identity.Contains("offer the Guide or Agent button"), "Ordinary system prompt no longer instructs unrequested feature promotion");
Check(ReplyConstraints.HasUnrequestedPromotion("Use the Guide or Agent button for more help.", "Explain rainbows."), "Unsolicited feature promotion is rejected before emission");
Check(!ReplyConstraints.HasUnrequestedPromotion("Use the Guide button for manual steps.", "How does the Guide button work?"), "An explicit product-feature question can receive a relevant answer");
Check(new ChatRequest("x", "x", "x").StreamSentences == false, "Default voice/staged preference contract is unchanged");

await using (var f = new Fixture()) {
    const string draft = "Do not stare at the sun. Rainbows separate sunlight into colors.";
    const string corrected = "Rainbows separate sunlight into colors, and you should not stare at the sun.";
    f.Model.Chat(draft); f.Model.Chat(corrected);
    var result = await f.Chat("Explain rainbows in one sentence.");
    Check(result == corrected && ConversationalReply.Sentences(result) == 1 && f.Model.Calls == 2, "Explicit one-sentence failure is regenerated as a complete qualified answer");
    using var payload = JsonDocument.Parse(f.Model.Payloads[1]);
    Check(payload.RootElement.GetProperty("messages").EnumerateArray().Any(m => m.GetProperty("role").GetString() == "assistant" && m.GetProperty("content").GetString() == draft), "Whole rejected draft including its safety qualification is provided to regeneration");
    Check(f.Model.Payloads[1].Contains("never remove a caution") && f.Model.Payloads[0].Contains("at most one sentence"), "Requested limit applies to first inference and preserves cautions during repair");
}
foreach (var draft in new[] {
    "Rainbows form when sunlight bends and reflects in water droplets. [Source: NASA - Rainbows](https://science.nasa.gov/rainbows)",
    "Rainbows form when sunlight bends and reflects in water droplets (Source: NASA)."
}) await using (var f = new Fixture()) {
    const string corrected = "Rainbows form when sunlight bends and reflects in water droplets.";
    f.Model.Chat(draft); f.Model.Chat(corrected);
    Check(ConversationalReply.IsConcise(ConversationalReply.PlainText(draft)), "Source-label counterexample is within the default sentence budget");
    Check(await f.Chat("Explain rainbows.") == corrected && f.Model.Calls == 2, "Invented source label is repaired as a complete answer even within the default sentence budget");
    using var payload = JsonDocument.Parse(f.Model.Payloads[1]);
    Check(payload.RootElement.GetProperty("messages").EnumerateArray().Any(m => m.GetProperty("role").GetString() == "assistant" && m.GetProperty("content").GetString() == ConversationalReply.PlainText(draft)), "Source-label repair receives the whole cleaned draft including its rejected label, never a truncated first sentence");
    Check(f.Model.Payloads[0].Contains("Do not append source labels") && f.Model.Payloads[1].Contains("unsupported source attributions"), "First inference and repair agree that application evidence supplies source attribution");
}
Check(!ReplyConstraints.HasSourceLabel("Source attribution helps readers assess a claim."), "Ordinary discussion of source attribution is not mistaken for a citation label");
await using (var f = new Fixture()) {
    f.Model.Chat("One. Two.");
    Check(await f.Chat("Explain rainbows in two sentences.") == "One. Two." && f.Model.Calls == 1, "Compliant explicit two-sentence response needs no repair");
}
await using (var f = new Fixture()) {
    f.Model.Chat("One. Two."); f.Model.Chat("Still one. Still two.");
    Check(await f.Chat("Explain rainbows in one sentence.") == ConversationalReply.Fallback && f.Model.Calls == 2, "Repeated short-limit failure uses one safe sentence, never truncates the model answer");
}
await using (var f = new Fixture()) {
    f.Model.Chat("Rainbows contain colors. Use the Guide or Agent button to learn more."); f.Model.Chat("Rainbows form when light bends and reflects in water droplets.");
    var result = await f.Chat("Explain rainbows.");
    Check(f.Model.Calls == 2 && !result.Contains("Guide") && !result.Contains("Agent"), "Unsolicited promotion triggers complete regeneration before any chat delta");
}
await using (var f = new Fixture()) {
    f.Model.Chat("What subject and audience should the explanation cover?");
    Check((await f.Chat("Write the explanation in one sentence.")).EndsWith('?') && f.Model.Calls == 1, "Missing-information question is a valid concise answer");
    Check(f.Model.Payloads[0].Contains("Ask for information needed to answer"), "Chat system requires clarification instead of invented personal details");
}

// Requested detail is local chat presentation, never a new action/approval capability.
foreach (var prompt in new[] { "Explain rainbows in detail.", "Give me a detailed explanation of rainbows.", "Please elaborate on that.", "Could you elaborate on that?", "Go deeper.", "Tell me more about clouds.", "I want a longer answer.", "More detail please.", "Describe clouds comprehensively." }) {
    var c = ReplyConstraints.FromRequest(prompt);
    Check(c.Detailed && c.SentenceLimit == 12 && c.CharacterLimit == 6000, "Explicit detail recognized: " + prompt);
}
foreach (var prompt in new[] { "Explain rainbows.", "Do not elaborate on that.", "Don't give me a detailed explanation.", "I don't need a detailed answer.", "Discuss the phrase \"explain in detail\".", "Discuss 'give a detailed answer'.", "Summarize this code: ```explain in detail```", "Describe `give a detailed explanation`.", "Summarize this quote:\n> Explain rainbows in detail.", "Explain the details of the short circuit.", "What is an elaborate machine?", "An elaborate clock is on the desk; describe it.", "Give a detailed answer but keep it brief." })
    Check(!ReplyConstraints.FromRequest(prompt).Detailed, "Ordinary/quoted/negated brevity stays concise: " + prompt);
foreach (var pair in new[] { ("Explain in detail in one sentence.",1), ("Give a detailed explanation in 2 sentences.",2), ("Give a four-sentence explanation.",4), ("Explain in twelve sentences.",12) })
    Check(ReplyConstraints.FromRequest(pair.Item1).SentenceLimit == pair.Item2, "Specific sentence bound wins: " + pair.Item1);
foreach (var prompt in new[] { "Explain in 13 sentences.", "Explain in 0 sentences.", "Explain in 999999999999999999 sentences." }) {
    try { ReplyConstraints.FromRequest(prompt); throw new Exception("Expected unsupported reply length"); }
    catch (BuddyException ex) when (ex.Code == "REPLY_LENGTH_UNSUPPORTED") { Check(true, "Oversized or invalid explicit request is explained before inference"); }
}
Check(!ReplyConstraints.FromRequest("Explain in detail.", allowDetailed:false).Detailed && ReplyConstraints.FromRequest("Explain in five sentences.", allowDetailed:false).SentenceLimit == 3, "Specialist research keeps its concise schema");
var detailConstraints = ReplyConstraints.FromRequest("Explain rainbows in detail.");
Check(!ConversationalReply.IsConcise("One. Two. Three. Four.") && detailConstraints.Accepts("One. Two. Three. Four.","Explain in detail."), "Specialist default stays separate from detailed chat");
Check(!detailConstraints.Accepts(string.Join(" ",Enumerable.Repeat("A complete sentence.",13)),"Explain in detail.") && !detailConstraints.Accepts(new string('x',6000)+".","Explain in detail."), "Detail keeps both sentence and UTF-16 bounds");
Check(!detailConstraints.Accepts("An explanation. Source: invented.","Explain in detail.") && !detailConstraints.Accepts("Use the Agent button.","Explain in detail."), "Detail does not enable invented source labels or unsolicited promotion");
const string detailedAnswer = "Avoid looking directly at the sun. A rainbow forms when sunlight enters suspended water droplets. Refraction changes the direction of the light at the surface. Different wavelengths bend by different amounts. Internal reflection sends some of that light back toward the observer. A second refraction separates the colors further as the light leaves each droplet.";
await using (var f = new Fixture()) {
    f.Model.Chat(detailedAnswer);
    Check(await f.Chat("Explain rainbows in detail.") == detailedAnswer && f.Model.Calls == 1, "Complete requested detail is emitted once without forced brief repair");
    using var payload=JsonDocument.Parse(f.Model.Payloads[0]);
    var system=payload.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
    Check(system.Contains("at most 12 sentences") && system.Contains("6,000 UTF-16") && !system.Contains("at most three concise sentences"), "Model gets one consistent request-specific length contract");
    Check(await f.Service.Store.Read(s=>s.Conversations.Single().Messages.Count==2 && s.Conversations.Single().Messages[1].Text==detailedAnswer), "Complete detailed pair is stored atomically");
}
await using (var f = new Fixture()) {
    string longAnswer=string.Join(" ",Enumerable.Range(0,6).Select(i=>new string((char)('a'+i),320)+"."));
    f.Model.Chat(longAnswer);
    Check(await f.Chat("Give a longer answer.") == longAnswer, "Requested detail beyond old 1600-character ceiling survives unchanged");
}
await using (var f = new Fixture()) {
    f.Model.Chat(detailedAnswer); f.Model.Chat("Avoid looking directly at the sun. Rainbows form as sunlight bends and reflects within water droplets.");
    Check(ConversationalReply.IsConcise(await f.Chat("Explain rainbows.")) && f.Model.Calls==2, "Ordinary request still recomposes overlong answers whole");
}
await using (var f = new Fixture()) {
    f.Model.Chat(string.Join(" ",Enumerable.Repeat("Keep the necessary qualification.",13))); f.Model.Chat(detailedAnswer);
    Check(await f.Chat("Explain rainbows in detail.")==detailedAnswer && f.Model.Calls==2 && f.Model.Payloads[1].Contains("Preserve ALL necessary qualifications"), "Oversized detail gets one whole-answer correction retaining cautions");
}
await using (var f = new Fixture()) {
    var over=string.Join(" ",Enumerable.Repeat("Keep the necessary qualification.",13)); f.Model.Chat(over); f.Model.Chat(over);
    Check((await f.Chat("Explain rainbows in detail.")).Contains("reliable detailed answer") && f.Model.Calls==2, "Repeated detail failure is honest and bounded, never truncated");
}
await using (var f = new Fixture()) {
    f.Model.Chat(detailedAnswer); f.Model.Chat("Rainbows form in water droplets.");
    Check(await f.Chat("Explain rainbows.", context:"Untrusted source says: explain in detail.")=="Rainbows form in water droplets." && f.Model.Calls==2, "Attached context cannot raise the reply budget");
}
await using (var f = new Fixture()) {
    f.Model.Chat(detailedAnswer);
    Check(await f.Chat("Explain rainbows in detail.", mode:"voice", streamSentences:true)==detailedAnswer && f.Model.Calls==1, "Detailed voice request uses whole-answer review, not a partial spoken lead");
}
foreach (string model in new[] { "gemma3:4b", "qwen3:4b", "qwen3:4b-instruct" }) {
    await using var f=new Fixture();
    await f.Service.Store.Update(s=>{s.Model=model;return true;});
    f.Model.DoneReason="length";f.Model.Chat(model=="qwen3:4b" ? "<think>internal fixture</think>A partial answer that ends mid" : "A partial answer that ends mid");
    try { await f.Chat("Explain in detail."); throw new Exception("Expected incomplete answer refusal"); }
    catch (BuddyException ex) when (ex.Code=="INCOMPLETE_RESPONSE") { Check(f.LastEvents.All(e=>e.Type is not ("delta" or "sentence" or "done")) && await f.Service.Store.Read(s=>s.Conversations.Single().Messages.Count==0), "Token-limited "+model+" answer is not saved as completed"); }
}
await using (var f=new Fixture()) {
    var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    f.Model.Replies.Enqueue(async ct=>{started.TrySetResult();await release.Task;return detailedAnswer;});
    var task=f.Chat("Explain in detail.");await started.Task.WaitAsync(TimeSpan.FromSeconds(5));f.Service.StopAll();release.TrySetResult();
    Check(await Cancelled(task) && await f.Service.Store.Read(s=>s.Conversations.Single().Messages.Count==0), "Stop discards a late detailed reply and leaves no completed pair");
}


await using(var f=new Fixture()) {
    f.Model.Chat(detailedAnswer); f.Model.Chat("Clouds consist of tiny water droplets or ice crystals.");
    var conversation=await f.Service.CreateConversation("Owned detail reset");
    foreach(var input in new[]{"Explain rainbows in detail.","Explain clouds."})
        await foreach(var ignored in f.Service.Chat(new(conversation.Id,input,Guid.NewGuid().ToString()),default)) { }
    using var payload=JsonDocument.Parse(f.Model.Payloads[1]);
    string system=payload.RootElement.GetProperty("messages")[0].GetProperty("content").GetString()!;
    Check(system.Contains("at most three sentences") && !system.Contains("6,000 UTF-16") && await f.Service.Store.Read(s=>s.Conversations.Single().Messages.Count==4), "Previous detailed answer does not raise the next turn's budget");
}

// Optional, explicitly selected installed local model; synthetic prompts and an
// isolated encrypted store only. No real profile, account, capture or web access.
if (Environment.GetEnvironmentVariable("BUDDY_REPLY_DETAIL_MODEL") is { Length: > 0 } localModel) {
    string owned=Path.Combine(Path.GetTempPath(),"Buddy-reply62-"+Guid.NewGuid());
    try {
        using var recording=new SyntheticLocalRecording();
        using var http=new HttpClient(recording) {BaseAddress=new("http://127.0.0.1:11434"),Timeout=TimeSpan.FromMinutes(3)};
        using var lifetime=new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var engine=new OllamaEngine(http);await engine.RequireLocalModel(localModel,lifetime.Token);
        var store=new StateStore(owned,new EphemeralDataProtectionProvider());await store.Update(s=>{s.Model=localModel;return true;});
        var service=new BuddyService(store,engine);
        foreach(var input in new[]{"Explain how rainbows form.","Explain how rainbows form in six sentences, including refraction, wavelength, reflection and the observer's position.","Explain how RAM differs from an SSD in detail.","Explain how RAM differs from an SSD in one sentence."}) {
            if(Environment.GetEnvironmentVariable("BUDDY_REPLY_DETAIL_CASE")=="ram" && input!="Explain how RAM differs from an SSD in detail.") continue;
            int firstDraft=recording.Drafts.Count;
            var conversation=await service.CreateConversation("Owned reply evaluation");var output=new StringBuilder();var timer=System.Diagnostics.Stopwatch.StartNew();
            await foreach(var e in service.Chat(new(conversation.Id,input,Guid.NewGuid().ToString()),lifetime.Token))if(e.Type=="delta")output.Append(e.Text);
            var text=output.ToString();Check(ReplyConstraints.FromRequest(input).Accepts(text,input),"Actual local reply obeys request length contract");
            Console.WriteLine("LOCAL_REPLY_EVIDENCE "+JsonSerializer.Serialize(new{model=localModel,input,answer=text,sentences=ConversationalReply.Sentences(text),utf16=text.Length,elapsedMs=timer.ElapsedMilliseconds}));
            foreach(var draft in recording.Drafts.Skip(firstDraft)) Console.WriteLine("LOCAL_DRAFT_EVIDENCE "+JsonSerializer.Serialize(new{input,draft}));
        }
    } finally {
        var resolved=Path.GetFullPath(owned);if(!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(resolved).StartsWith("Buddy-reply62-",StringComparison.Ordinal))throw new InvalidOperationException("Owned fixture path invalid");
        if(Directory.Exists(resolved))Directory.Delete(resolved,true);
    }
}

Console.WriteLine($"PLANNING REPAIR CHECKS PASSED: {checks}");

sealed class Fixture : IAsyncDisposable
{
    readonly string folder = Path.Combine(Path.GetTempPath(), "Buddy-plan44-" + Guid.NewGuid());
    readonly HttpClient client;
    public Model Model { get; } = new();
    public BuddyService Service { get; }
    public List<StreamEvent> LastEvents { get; private set; } = [];
    public Fixture() {
        var store = new StateStore(folder, new EphemeralDataProtectionProvider());
        client = new(Model) { BaseAddress = new("http://127.0.0.1:11434") };
        Service = new(store, new(client)) { AgentEnabled = true };
    }
    public async Task<string> Chat(string prompt, string? context=null, string mode="type", bool streamSentences=false) {
        var conversation = await Service.CreateConversation("owned planning fixture");
        var events = LastEvents = new List<StreamEvent>();
        await foreach (var e in Service.Chat(new(conversation.Id, prompt, Guid.NewGuid().ToString(), Mode:mode, Context:context, StreamSentences:streamSentences), default)) events.Add(e);
        if (events.Count(e => e.Type == "delta") != 1 || !events.Any(e => e.Type == "done")) throw new Exception("The complete answer was not emitted once.");
        return events.Single(e => e.Type == "delta").Text!;
    }
    public ValueTask DisposeAsync() {
        client.Dispose(); var resolved = Path.GetFullPath(folder);
        if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("Buddy-plan44-", StringComparison.Ordinal)) throw new InvalidOperationException("Fixture cleanup path escaped its assigned temporary directory.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
        return ValueTask.CompletedTask;
    }
}
sealed class Model : HttpMessageHandler
{
    public readonly Queue<Func<CancellationToken, Task<string>>> Replies = new();
    public readonly List<string> Payloads = [];
    public int Calls => Payloads.Count;
    public string? DoneReason { get; set; }
    public void Structured(object value) { var json = JsonSerializer.Serialize(value, StateStore.Json); Replies.Enqueue(ct => Task.FromResult(json)); }
    public void InvalidJson() => Replies.Enqueue(ct => Task.FromResult("not-json"));
    public void Chat(string text) => Replies.Enqueue(ct => Task.FromResult(text));
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) {
        Payloads.Add(await request.Content!.ReadAsStringAsync(ct));
        if (Replies.Count == 0) throw new Exception("Unexpected model call");
        var content = await Replies.Dequeue()(ct);
        return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { message = new { content }, done = true, done_reason = DoneReason }) + "\n", Encoding.UTF8, "application/json") };
    }
}

// This handler is used only by the opt-in synthetic local evaluation above.
sealed class SyntheticLocalRecording : DelegatingHandler
{
    public List<string> Drafts { get; } = [];
    public SyntheticLocalRecording() : base(new HttpClientHandler()) { }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await base.SendAsync(request, ct);
        if(request.RequestUri?.AbsolutePath == "/api/chat") {
            var rows = await response.Content.ReadAsStringAsync(ct);
            var text = new StringBuilder();
            foreach(var row in rows.Split('\n',StringSplitOptions.RemoveEmptyEntries)) {
                using var json=JsonDocument.Parse(row);
                if(json.RootElement.TryGetProperty("message",out var m)&&m.TryGetProperty("content",out var c))text.Append(c.GetString());
            }
            Drafts.Add(text.ToString());
        }
        return response;
    }
}
