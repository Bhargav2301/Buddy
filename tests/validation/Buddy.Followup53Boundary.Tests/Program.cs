using System.Text.Json;

SourceReceipt.Verify();
string lane = args.SingleOrDefault(a => a.StartsWith("--lane=", StringComparison.Ordinal))?[7..] ?? "teaching";
if (args.Any(a => !a.StartsWith("--lane=", StringComparison.Ordinal))) throw new ArgumentException("Only an explicit pure fixture lane is supported; no live mode exists.");
var checks = new Checks();
switch (lane) {
    case "teaching": TeachingCases.Run(checks); break;
#if HAS_CORE53
    case "core": CoreCases.Run(checks); break;
#endif
#if HAS_REFINE53
    case "refine": RefineCases.Run(checks); break;
#endif
#if HAS_PROVIDER53
    case "provider": await ProviderCases.Run(checks); break;
#endif
#if HAS_NOTCH53
    case "notch": await NotchCases.Run(checks); break;
#endif
    default: throw new ArgumentException("This lane has no linked reviewed checkpoint: " + lane);
}
Console.WriteLine(JsonSerializer.Serialize(new { kind = "summary", lane, checks.Passed, checks.Failed, scope = "pure production boundary checks with canned inputs", realModel = false, native = false, network = false, installedProfile = false, manualReviewRequired = true }));
return checks.Failed == 0 ? 0 : 1;

internal sealed class Checks
{
    internal int Passed { get; private set; }
    internal int Failed { get; private set; }
    internal void Check(bool condition, string id, string expectation)
    {
        if (condition) Passed++; else Failed++;
        Console.WriteLine(JsonSerializer.Serialize(new { kind = "boundary_case", id, expectation, passed = condition }));
    }
    internal void Rejects<T>(Action action, string id, string expectation) where T : Exception
    {
        try { action(); Check(false, id, expectation); }
        catch (T) { Check(true, id, expectation); }
    }
    internal async Task RejectsAsync<T>(Func<Task> action, string id, string expectation) where T : Exception
    {
        try { await action(); Check(false, id, expectation); }
        catch (T) { Check(true, id, expectation); }
    }
}
