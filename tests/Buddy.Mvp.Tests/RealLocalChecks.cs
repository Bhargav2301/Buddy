using Buddy.Server;
using Microsoft.AspNetCore.DataProtection;
using System.Diagnostics;
using System.Text.Json;

internal static class RealLocalChecks
{
    internal static async Task<int> Run(string output)
    {
        string root = Path.Combine(Path.GetTempPath(), "Buddy.Real.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var report = new List<object>();
        try {
            var store = new StateStore(root, DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(root, "keys"))));
            await store.Update(s => { s.Model = s.VisionModel = "gemma3:4b"; s.EmbeddingModel = "all-minilm:22m"; return true; });
            using var client = new HttpClient { BaseAddress = new("http://127.0.0.1:11434/"), Timeout = Timeout.InfiniteTimeSpan };
            var service = new BuddyService(store, new(client));
            var status = await service.Engine.Status("gemma3:4b", "gemma3:4b");
            if (!status.Installed.Contains("all-minilm:22m") || !status.Installed.Contains("gemma3:4b")) throw new Exception("Required local validation models are unavailable.");
            const string original = "Write a concise 3-item checklist for reviewing a draft. Keep the original dates unchanged and do not invent facts.";
            foreach (var mode in new[] { "quick", "guided", "council" }) {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8));
                var clock = Stopwatch.StartNew(); RefinementResult? result = null;
                await foreach (var item in service.RefineStream(new(original, mode), timeout.Token)) {
                    if (item.Type == "stage") Console.WriteLine(mode + ": " + item.Text);
                    if (item.Result is not null) result = item.Result;
                }
                if (result is null || result.Passes.Count != RefinementPolicy.Roles(mode).Length) throw new Exception(mode + " did not run its complete specialist sequence.");
                if (!result.Accepted && result.RefinedPrompt != original) throw new Exception("Rejected rewrite did not retain the exact original.");
                if (result.Accepted && (result.Similarity < .8 || !RefinementPolicy.PreservesLiterals(original, result.RefinedPrompt))) throw new Exception("Unsafe rewrite passed validation.");
                report.Add(new { mode, durationSeconds = clock.Elapsed.TotalSeconds, original, result });
                Console.WriteLine(mode + ": " + (result.Accepted ? "accepted" : "original retained") + " · similarity " + result.Similarity + " · " + result.Message);
            }
            var before = await store.Read(s => s.Conversations.Count);
            if (before != 0) throw new Exception("Refinement submitted a chat.");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel(); bool stopped = false;
            try { await service.RefineDetailed(new(original), cancelled.Token); } catch (OperationCanceledException) { stopped = true; }
            if (!stopped) throw new Exception("Cancelled real-model request was dispatched.");
            report.Add(new { cancelledBeforeDispatch = true, persistedConversations = before });
            Console.WriteLine("PASS: All real local modes completed safely; this fixture does not validate host fields or task quality.");
            return 0;
        } catch (Exception e) { report.Add(new { error = e.Message }); Console.Error.WriteLine(e); return 1; }
        finally {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { at = DateTimeOffset.UtcNow, fixture = "synthetic prompts only", results = report }, new JsonSerializerOptions { WriteIndented = true }));
            if (Path.GetDirectoryName(Path.GetFullPath(root)) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar) && Path.GetFileName(root).StartsWith("Buddy.Real.Tests.", StringComparison.Ordinal))
                Directory.Delete(root, true);
        }
    }
}
