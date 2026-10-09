using System.Text.RegularExpressions;

internal sealed record MeaningVerdict(bool MeaningCovered, bool UsefulStructure, string[] Failures)
{
    internal bool MeetsGolden => MeaningCovered && UsefulStructure;
}

internal static class MeaningOracle
{
    // This is fixture-specific evidence. It is deliberately independent of model
    // scores, embeddings, production canonicalization and production contract code.
    internal static MeaningVerdict Judge(MeaningGolden golden, string output)
    {
        var failures = new List<string>();
        bool Match(string pattern) => Regex.IsMatch(output, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        foreach (string pattern in golden.RequiredPatterns) if (!Match(pattern)) failures.Add("Missing or changed relation: " + pattern);
        foreach (string literal in golden.ExactLiterals) if (!output.Contains(literal, StringComparison.Ordinal)) failures.Add("Changed literal: " + literal);
        foreach (string pattern in golden.ForbiddenPatterns) if (Match(pattern)) failures.Add("Unsupported content: " + pattern);
        var originalNumbers = Regex.Matches(golden.Original, @"\b\d+(?:[.,]\d+)*\b").Select(m => m.Value).ToHashSet(StringComparer.Ordinal);
        // Numbered list markers are presentation only for an explicitly ordered
        // source fixture; source facts/output counts still need their exact digits.
        string numberedText = golden.OrderedMarkers.Length > 0 ? Regex.Replace(output, @"(?m)^(\s*(?:(?:Steps|Sequence|Order):\s*)?)\d+[.)]\s+", "$1", RegexOptions.IgnoreCase) : output;
        foreach (Match number in Regex.Matches(numberedText, @"\b\d+(?:[.,]\d+)*\b")) if (!originalNumbers.Contains(number.Value)) failures.Add("Invented number: " + number.Value);
        const string formatLimit = @"\b(?:\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+(?:words?|sentences?|paragraphs?|lines?|stanzas?|sections?|examples?|bullets?|steps?|characters?)\b";
        var originalLimits = Regex.Matches(golden.Original, formatLimit, RegexOptions.IgnoreCase).Select(m => m.Value.ToUpperInvariant()).ToHashSet();
        foreach (Match limit in Regex.Matches(output, formatLimit, RegexOptions.IgnoreCase)) if (!originalLimits.Contains(limit.Value.ToUpperInvariant())) failures.Add("Invented output limit: " + limit.Value);
        int previous = -1;
        foreach (string marker in golden.OrderedMarkers) {
            int position = output.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (position < 0 || position <= previous) failures.Add("Changed requested sequence: " + marker);
            previous = position;
        }
        bool meaning = failures.Count == 0;
        var lines = output.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var sections = lines.Select(line => Regex.Match(line, @"^(?:[-*]\s*)?(?:\*\*)?([A-Za-z][A-Za-z /-]{1,35})(?:\*\*)?:\s*(.+)$", RegexOptions.CultureInvariant)).Where(m => m.Success).ToArray();
        string Fold(string text) => Regex.Replace(text, @"[\W_]+", "").ToUpperInvariant();
        bool repeatsWholeInput = sections.Any(s => Fold(s.Groups[2].Value) == Fold(golden.Original));
        bool distinctBodies = sections.Select(s => Fold(s.Groups[2].Value)).Distinct().Count() == sections.Length;
        bool structure = golden.CanStructure && sections.Length >= 2 && sections.Select(s => s.Groups[1].Value.ToUpperInvariant()).Distinct().Count() >= 2 && distinctBodies && !repeatsWholeInput;
        if (!structure) failures.Add("No useful separation of distinct source-grounded task components; cosmetic edits and a header around the full input do not qualify.");
        return new(meaning, structure, failures.ToArray());
    }
}
