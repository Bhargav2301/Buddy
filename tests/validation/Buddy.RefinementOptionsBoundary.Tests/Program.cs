using System.Diagnostics;
using System.Text.Json;

FixtureFiles.VerifySources();
int passed = 0, failed = 0;
async Task Case(string name, Func<Task> body)
{
    var timer = Stopwatch.StartNew();
    try { await body().WaitAsync(TimeSpan.FromSeconds(15)); passed++; Console.WriteLine(JsonSerializer.Serialize(new { kind = "case", name, status = "PASS", elapsedMs = timer.ElapsedMilliseconds })); }
    catch (Exception e) { failed++; Console.WriteLine(JsonSerializer.Serialize(new { kind = "case", name, status = "FAIL", elapsedMs = timer.ElapsedMilliseconds, reason = e.Message, type = e.GetType().Name })); }
}
await PreparationCases.Run(Case);
await ResourceCases.Run(Case);
Console.WriteLine(JsonSerializer.Serialize(new { kind = "summary", passed, failed, scope = "independent pure/service mocks and explicit local resource reader; no native UI or real model" }));
return failed == 0 ? 0 : 1;

internal static class Assert
{
    internal static void That(bool value, string reason) { if (!value) throw new Exception(reason); }
    internal static async Task Cancelled(Func<Task> body) { try { await body(); } catch (OperationCanceledException) { return; } throw new Exception("Expected cancellation."); }
}
