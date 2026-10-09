using Buddy.Server;
using Microsoft.AspNetCore.DataProtection;
using System.Diagnostics;

namespace Buddy.RegionCompletion;

public sealed record CompletionCase(bool Passed, object Result, CompletionVerdict Verdict, long ElapsedMs);
public static class ProbeRunner
{
    public static async Task<CompletionCase> Region(BuddyService service, RegionFixture fixture, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        var turn = await service.Teach(new(fixture.Expectation.Question, new("owned-region-fixture", "Owned selected area", []), RegionImageBase64: Convert.ToBase64String(fixture.Png), ImageScope: "region"), ct).WaitAsync(ct);
        var verdict = RegionOracle.Image(turn, fixture.Expectation);
        return new(verdict.Passed, turn, verdict, clock.ElapsedMilliseconds);
    }
    public static async Task<CompletionCase> Research(BuddyService service, ObservedResearch observed, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        var result = await service.ResearchReviewedRegion(RegionFixture.ReviewedQuery, ct).WaitAsync(ct);
        var verdict = RegionOracle.Research(result, observed.Fetched);
        if (!observed.Queries.SequenceEqual([RegionFixture.ReviewedQuery])) verdict = new(false, verdict.Failures.Append("Exact query was not searched once.").ToArray());
        return new(verdict.Passed, result, verdict, clock.ElapsedMilliseconds);
    }
}

public sealed class OwnedState : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "Buddy-region-completion-" + Guid.NewGuid());
    public StateStore Store { get; }
    public OwnedState() => Store = new(folder, new EphemeralDataProtectionProvider());
    public Task Select(string model) => Store.Update(s => { s.Model = model; s.VisionModel = model; return true; });
    public void Dispose()
    {
        string full = Path.GetFullPath(folder), temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("Buddy-region-completion-", StringComparison.Ordinal) || (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("Unsafe owned-state cleanup path.");
        // The service writes only direct encrypted state files in this owned folder.
        foreach (var entry in Directory.EnumerateFileSystemEntries(full)) {
            if (Directory.Exists(entry) || (File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Unexpected owned-state cleanup entry.");
            File.Delete(entry);
        }
        Directory.Delete(full);
    }
}
