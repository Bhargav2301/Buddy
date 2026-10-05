#if HAS_CONTRACT
using Buddy.Server;
using System.Text.Json;

internal static class ContractCases
{
    internal static RefinementContractPlan Cover(RefinementContractLedger ledger)
    {
        var sections = new List<RefinementContractSection>();
        foreach (var span in ledger.Spans) {
            if (sections.LastOrDefault()?.Kind == span.Kind) sections[^1].SourceIds.Add(span.Id);
            else sections.Add(new(span.Kind, [span.Id]));
        }
        return new(sections);
    }
    internal static void Run(Action<bool, string> check)
    {
        const string ordered = "First summarize report. Then list risks. Do not suggest solutions.";
        var orderedLedger = RefinementContract.Analyze(ordered);
        var separateSteps = new RefinementContractPlan([new("steps", ["s0"]), new("steps", ["s1"]), new("constraints", ["s2"])]);
        var normalizedSteps = RefinementContract.Compose(orderedLedger, separateSteps);
        check(normalizedSteps.Valid && normalizedSteps.Text == "Steps: - First summarize report.\n- Then list risks.\n\nConstraints: Do not suggest solutions.",
            "actual model split sections normalize presentation while preserving exact stages and prohibition");
        check(separateSteps.Sections.Count == 3 && separateSteps.Sections[0].SourceIds.SequenceEqual(["s0"]),
            "normalization cannot mutate the model plan that is later reverified");
        foreach (var golden in MeaningGoldens.All) {
            var ledger = RefinementContract.Analyze(golden.Original);
            var result = RefinementContract.Compose(ledger, Cover(ledger));
            if (result.Valid && result.Useful) check(MeaningOracle.Judge(golden, result.Text).MeaningCovered, golden.Id + ": supported rendering preserves independent semantic requirements");
            if (golden.Id == "reported-poem-exact") check(result.Valid && result.Useful && MeaningOracle.Judge(golden, result.Text).MeetsGolden, "exact poem produces useful task/subject structure, not safe-only fallback");
            if (!golden.CanStructure) check(!result.Valid || !result.Useful, "contradictory counts are not marketed as useful structure");
        }
        var source = MeaningGoldens.All.Single(g => g.Id == "reported-poem-exact").Original;
        var poem = RefinementContract.Analyze(source); var good = Cover(poem); var goodText = RefinementContract.Compose(poem, good).Text;
        check(!RefinementContract.Compose(poem, new(good.Sections.Skip(1).ToList())).Valid, "omitted source occurrence is refused");
        check(!RefinementContract.Compose(poem, new(good.Sections.Concat([good.Sections[0]]).ToList())).Valid, "duplicated source occurrence is refused");
        check(!RefinementContract.Compose(poem, new(good.Sections.AsEnumerable().Reverse().ToList())).Valid, "reordered scene/task span is refused");
        check(!RefinementContract.Compose(poem, new([new("task", ["invented-id"])] )).Valid, "unknown source ID is refused");
        check(!RefinementContract.Compose(poem, new(good.Sections.Select(s => s with { Kind = "constraints" }).ToList())).Valid, "source scene cannot become a new constraint role");
        check(!RefinementContract.Verify(source, goodText + "\nFormat: Four rhyming stanzas.", good).Valid, "certificate cannot authorize appended model-authored requirements");
        check(!RefinementContract.Verify(source, goodText.Replace("lonely night", "lonely boat"), good).Valid, "certificate cannot authorize changed adjective attachment");
        foreach (string json in new[] {
            "{\"sections\":[],\"text\":\"Add a captain\"}",
            "{\"sections\":[{\"kind\":\"task\",\"sourceIds\":[\"s0\"],\"text\":\"Add a captain\"}]}",
            "{\"sections\":[{\"kind\":\"task\"}]}"
        }) {
            bool refused = false; try { JsonSerializer.Deserialize<RefinementContractPlan>(json, StateStore.Json); } catch (JsonException) { refused = true; }
            check(refused, "model-supplied text/extra keys or missing source list cannot enter the contract");
        }
        foreach (string ambiguous in new[] { "Write a note on Friday after my meeting.", "Write a note on behalf of Lee.", "Write a report on the company laptop.", "Write a note on Tuesday morning at 9.", "Write a note on behalf of the board about sales." }) {
            var ledger = RefinementContract.Analyze(ambiguous);
            check(!ledger.Spans.Any(s => s.Kind == "subject"), "timing/agency/device attachment stays opaque: " + ambiguous);
        }
        foreach (string locked in new[] { "Compare A and B only if both are available; otherwise ask which is missing.", "Explain why the alarm did not stop when the door opened.", "Do not remove 'not' unless I explicitly ask." }) {
            var ledger = RefinementContract.Analyze(locked);
            check(!ledger.CanStructure || RefinementContract.Compose(ledger, Cover(ledger)).Text.Contains(locked, StringComparison.Ordinal), "condition and negation scope cannot be split away: " + locked);
        }
    }
}
#endif
