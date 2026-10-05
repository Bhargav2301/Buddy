using Buddy.Server;
using Microsoft.AspNetCore.DataProtection;
using System.Net;
using System.Text;
using System.Text.Json;

// Independent regression probes: actual service/store, in-memory model transport,
// isolated disposable encrypted profile. No microphone, desktop, or live network.
var folder = Path.Combine(Path.GetTempPath(), "Buddy-parity-boundary-" + Guid.NewGuid());
Directory.CreateDirectory(folder);
int checks = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    checks++;
    Console.WriteLine("PASS: " + name);
}
try
{
    var store = new StateStore(folder, DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(folder, "keys"))));
    using var handler = new ModelFixture();
    using var client = new HttpClient(handler) { BaseAddress = new Uri("http://127.0.0.1:11434/") };
    var service = new BuddyService(store, new OllamaEngine(client));
    var conversation = await service.CreateConversation("QA cancellation fixture");
    var original = new ChatRequest(conversation.Id, "Hello Buddy", Guid.NewGuid().ToString(), "voice");
    await foreach (var item in service.Chat(original, CancellationToken.None)) { }
    int originalCount = await store.Read(s => s.Conversations.Single().Messages.Count);
    Check(originalCount == 2, "Completed fixture request stores exactly one user/assistant pair");

    if (!args.Contains("--store-wait-only")) using (var stopped = new CancellationTokenSource())
    {
        stopped.Cancel();
        int observed = 0;
        bool cancelled = false;
        try { await foreach (var item in service.Chat(original, stopped.Token)) observed++; }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && observed == 0, "Pre-cancelled duplicate request emits no cached answer or completion");
    }

    using (var stopped = new CancellationTokenSource())
    {
        var pending = original with { RequestId = Guid.NewGuid().ToString(), Text = "Another hello" };
        await using var stream = service.Chat(pending, stopped.Token).GetAsyncEnumerator();
        bool sawAnswer = false;
        while (await stream.MoveNextAsync())
            if (stream.Current.Type == "delta") { sawAnswer = true; break; }
        Check(sawAnswer, "Pending fixture reaches complete answer before persistence probe");

        using var releaseStore = new ManualResetEventSlim(false);
        var enteredStore = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocker = Task.Run(() => store.Update(s =>
        {
            enteredStore.TrySetResult();
            if (!releaseStore.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("QA store gate was not released");
            return true;
        }));
        await enteredStore.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<bool>? saving = null;
        bool cancelled = false;
        try
        {
            // MoveNext runs synchronously until Store.Update waits on the occupied gate.
            saving = stream.MoveNextAsync().AsTask();
            Check(!saving.IsCompleted, "Response persistence waits for the occupied store gate");
            stopped.Cancel();
        }
        finally { releaseStore.Set(); }
        await blocker.WaitAsync(TimeSpan.FromSeconds(5));
        try { await saving!.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "Stop while waiting for storage cancels response completion");
        Check(await store.Read(s => s.Conversations.Single().Messages.Count) == originalCount,
            "Stop while waiting for storage persists no stale user/assistant pair");
    }
    Console.WriteLine($"ALL {checks} INDEPENDENT CANCELLATION BOUNDARY CHECKS PASSED (mock model; no physical or live acceptance)");
}
finally
{
    // The only deletion target is the test-created directory under the OS temporary root.
    var resolved = Path.GetFullPath(folder);
    var temporaryRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!resolved.StartsWith(temporaryRoot, StringComparison.OrdinalIgnoreCase) ||
        !Path.GetFileName(resolved).StartsWith("Buddy-parity-boundary-", StringComparison.Ordinal))
        throw new InvalidOperationException("Refusing cleanup outside the QA fixture directory");
    Directory.Delete(resolved, true);
}

sealed class ModelFixture : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (request.RequestUri?.AbsolutePath != "/api/chat") throw new InvalidOperationException("Unexpected model route");
        var body = JsonSerializer.Serialize(new { message = new { content = "Hello from the isolated fixture." }, done = true }) + "\n";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/x-ndjson") });
    }
}
