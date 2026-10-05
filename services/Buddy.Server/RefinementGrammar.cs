using System.Text.RegularExpressions;

namespace Buddy.Server;

public sealed record RefinementGrammarEdit(string RuleId, string Source, string Result);

/// <summary>
/// Finite English article repairs in direct task heads. This is not a general grammar
/// or semantic engine. Every change is re-derived from source during exact rendering.
/// </summary>
public static class RefinementGrammar
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);
    private static readonly Regex Protected = new("```[\\s\\S]*?```|`[^`\\r\\n]+`|\"[^\"\\r\\n]*\"|\u201c[^\u201d\\r\\n]*\u201d|(?<![\\p{L}\\p{N}])'[^'\\r\\n]+'(?![\\p{L}\\p{N}])|https?://[^\\s<>\"']+", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex Sensitive = new(@"\b(verbatim|exact|exactly|wording|phrasing|spelling|capitalization|case-sensitive|literally|articles?|determiners?|grammar|unchanged|unmodified|unaltered|untouched|if|unless|when|otherwise|else|whether|only)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex KeepSource = new(@"\b(?:do not|don't|never)\s+(?:change|alter|edit|modify|rewrite|rephrase|correct|fix|adjust)\s+(?:(?:any|a|an|one|single|every|each|all|of|the|this|my|original|supplied|source)\s+)*(?:prompt|text|request|input|output|words?|sentences?|phrasing|characters?|it|anything|everything)\b|\b(?:keep|leave|retain|preserve)\s+(?:(?:any|a|an|one|single|every|each|all|of|the|this|my|original|supplied|source)\s+)*(?:prompt|text|request|input|words?|sentences?|phrasing|characters?)\b|\b(?:keep|leave|retain)\b[^.!?\r\n]{0,80}\bas\s+is\b|\bno\s+(?:edits?|rewriting|rephrasing)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex Artifact = new(@"^(?<verb>[Ww]rite|[Dd]raft|[Cc]ompose|[Pp]repare)(?<gap> +)(?<target>(?:(?:polite|short|brief|formal|informal|clear) +)?(?:poem|email|letter|report|summary|story|outline|recipe))\.?$", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex Explanation = new(@"^(?<verb>[Ee]xplain)(?<gap> +)(?<target>difference(?= +between +\S)|purpose(?= +of +\S))", RegexOptions.CultureInvariant, Timeout);

    public static RefinementGrammarEdit? Propose(string wholeOriginal, string kind, string content)
    {
        if (kind != "task") return null;
        string visible = Protected.Replace(wholeOriginal, match => new string(' ', match.Length));
        if (Sensitive.IsMatch(visible) || KeepSource.IsMatch(visible)) return null;
        var match = Artifact.Match(content);
        string article, rule;
        if (match.Success) {
            string target = match.Groups["target"].Value;
            article = target.StartsWith("email", StringComparison.Ordinal) || target.StartsWith("outline", StringComparison.Ordinal) || target.StartsWith("informal", StringComparison.Ordinal) ? "an" : "a";
            rule = "singular-deliverable-article-v1";
        } else {
            match = Explanation.Match(content);
            if (!match.Success) return null;
            article = "the"; rule = "direct-explanation-article-v1";
        }
        string candidate = content.Insert(match.Groups["target"].Index, article + " ");
        candidate = char.ToUpperInvariant(candidate[0]) + candidate[1..];
        // Existing fidelity is necessary but insufficient on its own: it ignores
        // articles. Only the exact finite source rule above chooses the insertion.
        if (!RefinementCore.Fidelity(content, candidate).Allowed || !RefinementPolicy.PreservesLiterals(content, candidate)) return null;
        return new(rule, content, candidate);
    }
}
