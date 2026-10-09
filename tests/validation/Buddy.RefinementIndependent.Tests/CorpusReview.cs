using System.Text.Json;
using System.Text.RegularExpressions;

internal static class CorpusReview
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    internal sealed record Golden(string Id, string Category, string Prompt, string Utility, string[] Preserve,
        string[] Literals, string[] Forbid, string[]? Ordered = null, bool AllowUnchanged = false, bool RequireOriginal = false);
    internal sealed record Verdict(string Outcome, string[] HardFailures, bool ManualUtilityReviewRequired, string UtilityCriterion);
    private static Golden[] ReadGoldens() => new[] { "Corpus.json", "KnownRegressions.json" }.SelectMany(name => {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, name)));
        return doc.RootElement.GetProperty("cases").Deserialize<Golden[]>(Json)!;
    }).ToArray();
    internal static Verdict Judge(Golden g, string candidate, bool accepted, bool noChange)
    {
        var failures = new List<string>(); bool exact = candidate == g.Prompt;
        if (!accepted && !exact) failures.Add("A rejected result changed the exact original.");
        if (accepted && (exact || noChange)) failures.Add("Accepted output is unchanged or also marked NoChange.");
        if (g.RequireOriginal && !exact) failures.Add("Fixture requires exact original/clarification, not a selected rewrite.");
        if (accepted) {
            if (g.Id == "distinct-counts" && (!Regex.IsMatch(candidate, @"3\s+sentences\s+about\s+the\s+first\s+proposal", RegexOptions.IgnoreCase) ||
                !Regex.IsMatch(candidate, @"5\s+sentences\s+about\s+the\s+second\s+proposal", RegexOptions.IgnoreCase)))
                failures.Add("The two output counts lost their separately stated proposal bindings; human review required, not a token-coverage pass.");
            foreach (string literal in g.Literals) if (!candidate.Contains(literal, StringComparison.Ordinal)) failures.Add("Literal missing/changed: " + literal);
            foreach (string fact in g.Preserve) if (!candidate.Contains(fact, StringComparison.OrdinalIgnoreCase)) failures.Add("Supplied fact/qualification needs manual investigation: " + fact);
            foreach (string addition in g.Forbid) if (!g.Prompt.Contains(addition, StringComparison.OrdinalIgnoreCase) && candidate.Contains(addition, StringComparison.OrdinalIgnoreCase)) failures.Add("Forbidden addition: " + addition);
            int offset = 0;
            foreach (string item in g.Ordered ?? []) {
                int next = candidate.IndexOf(item, offset, StringComparison.OrdinalIgnoreCase);
                if (next < 0) { failures.Add("Order/relationship anchor missing: " + item); break; }
                offset = next + item.Length;
            }
        }
        string outcome = failures.Count > 0 ? "hard-check-failure" : !accepted ? g.AllowUnchanged ? "faithful-original-allowed" : "unmet-utility-original-kept" : "candidate-needs-independent-manual-review";
        return new(outcome, failures.ToArray(), accepted, g.Utility);
    }
    internal static int Assess(string path)
    {
        var info = new FileInfo(path); if (info.Length > 8 * 1024 * 1024) throw new ArgumentException("Canned receipt exceeds eight MiB.");
        var goldens = ReadGoldens().ToDictionary(g => g.Id, StringComparer.Ordinal); int reviewed = 0, failures = 0, original = 0, pending = 0, errors = 0;
        string text = File.ReadAllText(path); IEnumerable<JsonElement> rows;
        if (text.TrimStart().StartsWith('[')) { using var doc = JsonDocument.Parse(text); rows = doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToArray(); }
        else rows = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => { using var doc = JsonDocument.Parse(line); return doc.RootElement.Clone(); }).ToArray();
        foreach (var row in rows) {
            string? id = Value(row, "caseId")?.GetString() ?? Value(row, "id")?.GetString();
            if (id is null || !goldens.TryGetValue(id, out var golden)) continue;
            var result = Value(row, "result") ?? Value(row, "refinementResult");
            if (result is { ValueKind: JsonValueKind.Object } outer && Value(outer, "result") is { ValueKind: JsonValueKind.Object } nested) result = nested;
            if (result is not { ValueKind: JsonValueKind.Object } r || Value(r, "refinedPrompt") is not { ValueKind: JsonValueKind.String } candidate) {
                errors++; Console.WriteLine(JsonSerializer.Serialize(new { kind = "independent_assessment", id, outcome = "error-or-missing-terminal-result", actualUtilityPass = false })); continue;
            }
            var verdict = Judge(golden, candidate.GetString()!, Value(r, "accepted")?.GetBoolean() ?? false, Value(r, "noChange")?.GetBoolean() ?? false);
            reviewed++; failures += verdict.HardFailures.Length > 0 ? 1 : 0; original += verdict.Outcome.Contains("original") ? 1 : 0; pending += verdict.ManualUtilityReviewRequired ? 1 : 0;
            Console.WriteLine(JsonSerializer.Serialize(new { kind = "independent_assessment", id, lane = Value(row, "lane")?.GetString(), verdict, actualUtilityPass = false, modelScoresUsed = false }));
        }
        Console.WriteLine(JsonSerializer.Serialize(new { kind = "independent_summary", reviewed, hardFailureCases = failures, originalKept = original, errors, pendingManualCandidateReview = pending,
            actualUtilityAccepted = false, meaningChecksAreNecessaryNotSufficient = true, source = "frozen independent canned corpus" }));
        return reviewed == 0 || failures > 0 || errors > 0 ? 1 : 0;
    }
    private static JsonElement? Value(JsonElement element, string key) => element.ValueKind == JsonValueKind.Object
        ? element.EnumerateObject().Where(p => p.Name.Equals(key, StringComparison.OrdinalIgnoreCase)).Select(p => (JsonElement?)p.Value).FirstOrDefault() : null;
    internal static void SelfTests(Action<bool, string> check)
    {
        var all = ReadGoldens(); check(all.Length == 38 && all.Select(g => g.Id).Distinct().Count() == 38, "corpus count/identity changed");
        var ordered = all.Single(g => g.Id == "ordered-steps");
        check(Judge(ordered, ordered.Prompt.Replace("First", "Finally").Replace("Then", "First"), true, false).HardFailures.Length > 0, "oracle missed reordered anchors");
        var code = all.Single(g => g.Id == "code-literal");
        check(Judge(code, code.Prompt.Replace("<=", ">="), true, false).HardFailures.Length > 0, "oracle missed code mutation");
        var email = all.Single(g => g.Id == "rough-email");
        check(Judge(email, email.Prompt + " Give the reason for the request.", true, false).HardFailures.Length > 0, "oracle missed invented requirement");
        check(Judge(email, email.Prompt, false, true).Outcome == "unmet-utility-original-kept", "safe no-change counted useful");
        check(Judge(email, "Write a polite email asking for Friday off.", true, false).ManualUtilityReviewRequired, "automated checks bypassed manual utility review");
        check(Judge(email, "Write a polite email asking for Friday off.", false, false).HardFailures.Length > 0, "rejected changed result permitted");
        var counts = all.Single(g => g.Id == "distinct-counts");
        check(Judge(counts, "Request: Write exactly 3 sentences.\n\nSubject: the first proposal and exactly 5 sentences about the second proposal.", true, false).HardFailures.Length > 0,
            "oracle missed demonstrated globally scoped first count despite ordered literal coverage");
    }
}
