using System.Diagnostics;
using System.Text.Json;

SourceReceipt.Verify("independent service mocks and injected window/control policies; no native calls or real model");
int passed = 0, failed = 0;
async Task Case(string name, Func<Task> body)
{
    var watch = Stopwatch.StartNew();
    try { await body().WaitAsync(TimeSpan.FromSeconds(15)); passed++; Console.WriteLine(JsonSerializer.Serialize(new { kind = "case", name, status = "PASS", elapsedMs = watch.ElapsedMilliseconds })); }
    catch (Exception e) { failed++; Console.WriteLine(JsonSerializer.Serialize(new { kind = "case", name, status = "FAIL", elapsedMs = watch.ElapsedMilliseconds, error = e.GetType().Name, message = e.Message })); }
}
await RefinementCases.Run(Case);
await WindowCases.Run(Case);
await ControlCases.Run(Case);
Console.WriteLine(JsonSerializer.Serialize(new { kind = "summary", passed, failed, native = false, realModel = false }));
return failed == 0 ? 0 : 1;

internal static class Require
{
    internal static void True(bool value, string message) { if (!value) throw new Exception(message); }
    internal static async Task Cancelled(Func<Task> action) { try { await action(); } catch (OperationCanceledException) { return; } throw new Exception("Expected cancellation."); }
}
