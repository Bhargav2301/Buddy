using System.Text.RegularExpressions;

namespace Buddy.Server;

public sealed record RefinementGrammarEdit(string RuleId, string Source, string Result);

/// <summary>
/// Finite English repairs in direct task heads. This is not a general grammar
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
    private static readonly Regex Naming = new(@"\b(?:names?|named|called|identifiers?|variables?|commands?|labels?|titles?|tokens?|spelled)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex PleaseTypo = new(@"^[Pp]leae(?= +(?:write|draft|compose|prepare|create|explain|summarize|describe|list|compare)\b)", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex ArtifactTypo = new(@"^(?:(?:[Pp]lease) +)?(?:[Ww]rite|[Dd]raft|[Cc]ompose|[Pp]repare|[Cc]reate) +(?:(?:a|an|the|my|our|your|his|her|their|\d+|one|two|three|four|five|six|seven|eight|nine|ten) +)?(?:(?:short|brief|polite|formal|informal|clear) +)?(?<word>mesage|mesages|paragaph|paragaphs|sentance|sentances|sumary|sumaries|invitaton|invitatons)(?= +(?:saying|that|to|about|regarding|for|asking|requesting)\b|[.!?]?$)", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex PluralAgreement = new(@"^(?:(?:[Tt]he|[Tt]hese|[Tt]hose|[Oo]ur|[Yy]our|[Tt]heir) +(?:(?:release|test|project|meeting|draft|final|new|old|recent) +)?(?:notes|results|reports|files|users|reviewers|messages|documents|tests|records|examples|instructions|changes|requests)|[Tt]hey|[Ww]e|[Yy]ou) +(?<verb>needs|requires|contains|includes|shows|provides|uses|has|is|does)(?= +\S)", RegexOptions.CultureInvariant, Timeout);

    public static RefinementGrammarEdit? Propose(string wholeOriginal, string kind, string content)
    {
        if (kind != "task") return null;
        string visible = Protected.Replace(wholeOriginal, match => new string(' ', match.Length));
        if (Sensitive.IsMatch(visible) || KeepSource.IsMatch(visible)) return null;
        // A closed vocabulary plus direct syntax is intentional. Do not infer noun
        // plurality from an arbitrary trailing 's', spell-check names, or approve
        // arbitrary small edit distances. These exact edits are source-derived and
        // separately re-derived at the service boundary, not canonical synonyms.
        if (!Naming.IsMatch(visible)) {
            string spelling = PleaseTypo.Replace(content, m => char.IsUpper(m.Value[0]) ? "Please" : "please");
            var typo = ArtifactTypo.Match(spelling);
            if (typo.Success) {
                var word = typo.Groups["word"];
                string repaired = word.Value switch {
                    "mesage" => "message", "mesages" => "messages", "paragaph" => "paragraph", "paragaphs" => "paragraphs",
                    "sentance" => "sentence", "sentances" => "sentences", "sumary" => "summary", "sumaries" => "summaries",
                    "invitaton" => "invitation", "invitatons" => "invitations", _ => throw new InvalidOperationException("Unknown finite spelling rule.")
                };
                spelling = spelling[..word.Index] + repaired + spelling[(word.Index + word.Length)..];
            }
            if (spelling != content && RefinementPolicy.PreservesLiterals(content, spelling))
                return new("task-spelling-v1", content, spelling);
            var agreement = PluralAgreement.Match(content);
            if (agreement.Success) {
                var verb = agreement.Groups["verb"];
                string repaired = verb.Value switch { "has" => "have", "is" => "are", "does" => "do", _ => verb.Value[..^1] };
                string result = content[..verb.Index] + repaired + content[(verb.Index + verb.Length)..];
                if (RefinementPolicy.PreservesLiterals(content, result)) return new("explicit-plural-agreement-v1", content, result);
            }
        }
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

    public static string Describe(string ruleId) => ruleId switch {
        "singular-deliverable-article-v1" or "direct-explanation-article-v1" => "Added a missing article in the requested task.",
        "task-spelling-v1" => "Corrected supported spelling in the requested task.",
        "explicit-plural-agreement-v1" => "Corrected verb agreement with the explicit plural subject.",
        _ => throw new ArgumentException("Unknown finite grammar rule.", nameof(ruleId))
    };
}
