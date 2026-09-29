using Buddy.Server;
using Microsoft.AspNetCore.DataProtection;
using System.Net;
using System.Text.Json;

int count = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); count++; Console.WriteLine("PASS: " + name); }
var folder = Path.Combine(Path.GetTempPath(), "buddy-mvp-" + Guid.NewGuid());
Directory.CreateDirectory(folder);
try {
    var store = new StateStore(folder, DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(folder, "keys"))));
    using var handler = new LocalModel(); using var client = new HttpClient(handler) { BaseAddress = new("http://localhost:11434/") };
    var service = new BuddyService(store, new(client));
    Check(RefinementPolicy.Mode(new("short", "auto")) == "quick", "Short prompts use Quick");
    Check(RefinementPolicy.Mode(new("short", "auto", Domain: "coding")) == "guided", "Coding uses Guided");
    Check(RefinementPolicy.Mode(new(new string('a', 801), "auto")) == "council", "Long prompts use Council");
    Check(RefinementPolicy.Mode(new(new string('a', 900), "quick", Important: true)) == "quick", "Explicit mode beats auto selection");
    Check(RefinementPolicy.Technique(new("x", Domain: "planning")) == "tree-of-thoughts", "Planning technique selected");
    Check(!RefinementPolicy.PreservesLiterals("Return 3 items at https://example.com", "Return 4 items at https://example.com"), "Changed numeric facts fail preservation");
    Check(!RefinementPolicy.PreservesLiterals("Use `theExactName`", "Use another name"), "Literal identifiers must survive");
    Check(RefinementPolicy.Cosine([0,0], [0,0]) == 0 && RefinementPolicy.Cosine([float.NaN], [1]) == 0, "Invalid embeddings cannot validate a rewrite");
    foreach (var (mode, expected) in new[] { ("quick", 1), ("guided", 4), ("council", 6) }) {
        handler.ChatCalls = 0;
        var events = new List<RefinementEvent>(); await foreach (var item in service.RefineStream(new("Write a useful prompt", mode), default)) events.Add(item);
        var result = events.Last().Result!;
        Check(result.Accepted && result.RefinedPrompt == "Write a clear, useful prompt.", mode + " completes a checked local rewrite");
        Check(handler.ChatCalls == expected && result.Passes.Count == (mode == "guided" ? 3 : mode == "council" ? 5 : 1), mode + " runs actual specialist/synthesis passes");
        Check(events.Any(x => x.Type == "delta") && result.Engine == "buddy_local", mode + " streams text and reports the local engine");
    }
    handler.Preserved = false;
    Check(!(await service.RefineDetailed(new("Do not mention my name"), default)).Accepted, "Negation or intent failure rejects even semantically similar text");
    handler.Preserved = true; handler.EmbeddingAvailable = false;
    var original = "Keep this draft"; var rejected = await service.RefineDetailed(new(original), default);
    Check(rejected.RefinedPrompt == original && !rejected.Accepted && rejected.Similarity is null, "Missing embeddings keep the byte-equal original");
    handler.EmbeddingAvailable = true; handler.Similar = false;
    Check(!(await service.RefineDetailed(new(original), default)).Accepted, "Unrelated rewrite fails similarity gate"); handler.Similar = true;
    Check((await store.Read(s => s.Conversations.Count)) == 0, "Refinement never submits a chat or persists field contents");
    handler.Delay = true; handler.Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    var work = service.RefineDetailed(new(original), default);
    await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); service.StopAll();
    bool stopped = false; try { await work; } catch (OperationCanceledException) { stopped = true; }
    Check(stopped, "Global stop cancels refinement"); handler.Delay = false;
    Check((await service.RefineDetailed(new(original), default)).Accepted, "Cancelled refinement releases the inference lock");
    var conversation = await service.CreateConversation("Original title");
    await service.UpdateConversation(conversation.Id, new(Pinned:true, Archived:true));
    var changed = await store.Read(s => s.Conversations.Single());
    Check(changed.Pinned && changed.Archived && changed.Title == "Original title", "Partial conversation updates preserve unspecified values");
    var provider = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(folder, "keys")));
    Check((await new StateStore(folder, provider).Read(s => s.Conversations.Single())).Pinned, "Conversation organization survives encrypted reload");
    var editor = new ScreenElement("edit", "Draft", "Edit", 0,0,100,30);
    Check(ActionPolicy.LiveRisk(new("type", Value:"Hello"), editor, reversibleEdit:true) == "low", "Verified reversible edits can run after plan approval");
    Check(ActionPolicy.LiveRisk(new("type", Value:"Hello"), editor) == "high", "Unverified edits require confirmation");
    Check(ActionPolicy.LiveRisk(new("click", Description:"Send message"), editor, navigationPattern:true) == "high", "Consequential effects override navigation evidence");
    Check(ActionPolicy.LiveRisk(new("click"), editor) == "high", "Unknown click effects remain high risk");
    Check(ActionPolicy.LiveRisk(new("open", Value:"notepad"), null) == "low" && ActionPolicy.LiveRisk(new("open", Value:"https://example.com"), null) == "high", "App allowlist and external navigation have distinct approval needs");
    Check(ActionPolicy.LiveRisk(new("keys", Value:"Enter"), null) == "high", "Enter may submit and requires confirmation");
    var context = new ScreenContext("sample", "Sample", [editor]);
    Check(GuideExpectations.Matches(new("visible", "Draft", "Edit"), context), "Guide advances only for a matching observable condition");
    Check(!GuideExpectations.Matches(new("manual"), context) && !GuideExpectations.Matches(new("absent"), context), "Empty and manual expectations never auto-advance");
    Check(!GuideExpectations.Matches(new("visible", "Draft", "Edit"), context with { Elements = [editor,editor with { Ref="duplicate" }] }), "Ambiguous expected controls cannot advance a guide");
    service.AgentEnabled = true;
    var exhausted = await service.ContinueAgent(new("Task",context,[new(1,new("read", Ref:"edit"),true,"Read")],0),default);
    Check(exhausted.Status == "clarify" && exhausted.Actions?.Count == 0, "Exhausted runs never ask the model for more actions");
    handler.ChatCalls = 0;
    var large = new string('\u4e00', 6000);
    var bounded = await service.RefineDetailed(new(large, "council"), default);
    Check(!bounded.Accepted && bounded.RefinedPrompt == large && handler.ChatCalls == 0, "Oversized refinement keeps every original character without silent context truncation");
    Console.WriteLine($"ALL {count} MVP CHECKS PASSED");
} finally { Directory.Delete(folder, true); }

sealed class LocalModel : HttpMessageHandler
{
    public int ChatCalls; public bool Preserved = true, EmbeddingAvailable = true, Similar = true, Delay;
    public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (Delay) { Started.TrySetResult(); await Task.Delay(Timeout.Infinite,ct); }
        using var data = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
        if (request.RequestUri!.AbsolutePath == "/api/embed") {
            if (!EmbeddingAvailable) return new(HttpStatusCode.NotFound);
            var length = data.RootElement.GetProperty("input").GetArrayLength();
            return Json(new { embeddings = Enumerable.Range(0,length).Select(i => Similar || i == 0 ? new[] {1f,0f} : new[] {0f,1f}) });
        }
        if (!data.RootElement.GetProperty("stream").GetBoolean()) return Json(new { message = new { content = JsonSerializer.Serialize(new { preserved = Preserved, scoreBefore = 40, scoreAfter = 75, changes = new[] { "Clarified output" } }) }, done = true });
        ChatCalls++;
        return new(HttpStatusCode.OK) { Content = new StringContent("{\"message\":{\"content\":\"Write a clear, useful prompt.\"},\"done\":true}\n") };
    }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value)) };
}
