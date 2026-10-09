using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Buddy.Server;

public sealed record RefinementContractSpan(string Id, string Kind, int Start, int Length, string Text,
    int ContentStart, int ContentLength, int Order);

public sealed class RefinementContractLedger
{
    public string Original { get; }
    public IReadOnlyList<RefinementContractSpan> Spans { get; }
    public bool CanStructure { get; }
    public string Reason { get; }
    public bool RequiresClarification { get; }
    internal RefinementContractLedger(string original, List<RefinementContractSpan> spans, bool canStructure, string reason, bool requiresClarification = false)
        => (Original, Spans, CanStructure, Reason, RequiresClarification) = (original, spans.AsReadOnly(), canStructure, reason, requiresClarification);
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RefinementContractSection([property: JsonRequired] string Kind, [property: JsonRequired] List<string> SourceIds);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RefinementContractPlan([property: JsonRequired] List<RefinementContractSection> Sections);
public sealed record RefinementContractCertificate(IReadOnlyList<string> SourceSpanIds, int CoveredCharacters,
    IReadOnlyList<string> Operations)
{
    public IReadOnlyList<string> GrammarRuleIds { get; init; } = [];
}
public sealed record RefinementContractReview(bool Valid, bool Useful, string Text, string Reason,
    RefinementContractCertificate? Certificate = null);

/// <summary>
/// A bounded English task-structure grammar, not a general semantic parser. Models may
/// arrange only supplied source IDs under host-assigned roles. They cannot author text.
/// Ambiguous conditions and unsupported attachments retain the original wording path.
/// </summary>
public static class RefinementContract
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(150);
    private static readonly string[] Kinds = ["task", "subject", "purpose", "comparison", "constraints", "steps", "context"];
    private static readonly Regex Protected = new("```[\\s\\S]*?```|`[^`\\r\\n]+`|\"[^\"\\r\\n]*\"|\u201c[^\u201d\\r\\n]*\u201d|(?<![\\p{L}\\p{N}])'[^'\\r\\n]+'(?![\\p{L}\\p{N}])|https?://[^\\s<>\"']+", RegexOptions.CultureInvariant, MatchTimeout);

    public static JsonElement PlanSchema { get; } = JsonSerializer.SerializeToElement(new {
        type = "object", additionalProperties = false, required = new[] { "sections" }, properties = new {
            sections = new { type = "array", minItems = 1, maxItems = 24, items = new {
                type = "object", additionalProperties = false, required = new[] { "kind", "sourceIds" }, properties = new {
                    kind = new { type = "string", @enum = Kinds },
                    sourceIds = new { type = "array", minItems = 1, maxItems = 24, items = new { type = "string", maxLength = 16 } }
                }
            } }
        }
    });

    // Presentation metadata only: these visible headings are not proof that a
    // prompt is complete, optimal or safe, and never change refinement routing.
    public static bool HasExplicitOrganization(string original)
    {
        if (string.IsNullOrWhiteSpace(original) || original.Length > 20000) return false;
        try {
            var protectedChars = ProtectedCharacters(original);
            if (original.Select((c, i) => (c is '`' or '"' or '\u201c' or '\u201d') && !protectedChars[i]).Any(x => x)) return false;
            string visible = new(original.Select((c, i) => protectedChars[i] ? ' ' : c).ToArray());
            var headings = Regex.Matches(visible, @"(?m)^[ \t]*(?<kind>request|task|subject|purpose|constraints?|steps|context|output)[ \t]*:[ \t]*(?<body>[^\r\n]*)", Options, MatchTimeout)
                .Where(m => m.Groups["body"].Value.Any(char.IsLetterOrDigit))
                .Select(m => m.Groups["kind"].Value.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
            return headings.Any(h => h is "request" or "task") && headings.Any(h => h is not ("request" or "task"));
        } catch (RegexMatchTimeoutException) { return false; }
    }

    public static RefinementContractLedger Analyze(string original)
    {
        Security.Text(original, 20000, "Prompt");
        var protectedChars = ProtectedCharacters(original);
        string visible = new(original.Select((c, i) => protectedChars[i] ? ' ' : c).ToArray());
        var exactBounds = Regex.Matches(visible, @"\bexactly\s+(\d+)\s+(sentences?|words?|paragraphs?|bullet points?)\s+and\s+exactly\s+(\d+)\s+(sentences?|words?|paragraphs?|bullet points?)\b", Options, MatchTimeout);
        if (exactBounds.Any(m => m.Groups[2].Value.TrimEnd('s').Equals(m.Groups[4].Value.TrimEnd('s'), StringComparison.OrdinalIgnoreCase) && m.Groups[1].Value.TrimStart('0') != m.Groups[3].Value.TrimStart('0')))
            return Unsupported(original, "Conflicting exact output counts need clarification; Buddy did not choose or invent a requirement.", true);
        // Never detach a condition, alternative branch or unresolved role/attachment.
        // Nor pretend re-labelling an already structured prompt is an improvement.
        if (Regex.IsMatch(visible, @"(?m)^\s*(request|task|subject|purpose|constraints?|steps|context|output)\s*:", Options, MatchTimeout) ||
            original.Select((c, i) => (c is '`' or '"' or '\u201c' or '\u201d') && !protectedChars[i]).Any(x => x))
            return Unsupported(original, "The prompt already has structure or contains a condition/ambiguous literal that must remain together.");

        var spans = new List<RefinementContractSpan>();
        // A single explicit constraint after a semicolon can be separated without
        // parsing (or promoting) any subject, count or coordinated task inside it.
        // Keep both complete sides and the source punctuation; this is a finite
        // presentation rule, not a general semicolon/English scope parser.
        if (TrySemicolonConstraint(original, visible, protectedChars, out int separator)) {
            int start = 0, end = original.Length, constraintStart = separator + 1;
            while (char.IsWhiteSpace(original[start])) start++;
            while (char.IsWhiteSpace(original[end - 1])) end--;
            while (char.IsWhiteSpace(original[constraintStart])) constraintStart++;
            Add("task", start, separator + 1 - start, start, separator + 1 - start);
            Add("constraints", constraintStart, end - constraintStart, constraintStart, end - constraintStart);
            return new(original, spans, Covers(original, spans), "Separated an explicit trailing constraint while keeping both complete source clauses intact.");
        }
        bool comparisonScope = !Regex.IsMatch(visible, @"\b(?:first|then|next|finally|verbatim|unchanged)\b|\b(?:keep|preserve|retain)\s+(?:(?:the|this|original|exact)\s+)*(?:format|formatting|layout|wording)\b|\b(?:do not|don't|never)\s+(?:reformat|restructure)\b", Options, MatchTimeout);
        foreach (var (start, length) in Clauses(original, protectedChars))
        {
            var text = original.Substring(start, length);
            string kind = Regex.IsMatch(text, @"^(first|then|next|finally)\b", Options, MatchTimeout) ? "steps" :
                Regex.IsMatch(text, @"^(do not|don't|never|must|keep|preserve|avoid|without|use|return|limit)\b", Options, MatchTimeout) ? "constraints" :
                text.StartsWith("```") || text.StartsWith('`') ? "context" : "task";
            if (kind != "task") { Add(kind, start, length, start, length); continue; }
            // A condition and its alternatives stay in one sentence, including its
            // semicolon branches. Only a separate explicit constraint may be lifted.
            if (HasCondition(text) || HasCoordinatedTask(text)) { Add("task", start, length, start, length); continue; }

            // Lift an explicitly supplied output bound, never infer one from a goal.
            var limit = Regex.Match(text, @"\s+(?:in|within|using)\s+(?:exactly\s+|at most\s+|no more than\s+)?(?:\d+|one|two|three|four|five|six|seven|eight|nine|ten)\s+(?:sentences?|words?|paragraphs?|bullet points?)\s*[.!?]?$", Options, MatchTimeout);
            int taskLength = length;
            if (limit.Success && !protectedChars[start + limit.Index + 1]) taskLength = TrimEnd(original, start, limit.Index);
            var taskText = original.Substring(start, taskLength);
            bool split = false;
            var comparisonHead = Regex.Match(taskText, @"^(?:please\s+)?(?:list|compare)\s+", Options, MatchTimeout);
            if (comparisonScope && comparisonHead.Success && TryComparisonBody(taskText[comparisonHead.Length..], out _, out _, out _))
            {
                int headLength = TrimEnd(original, start, comparisonHead.Length);
                int bodyStart = start + comparisonHead.Length;
                Add("task", start, headLength, start, headLength);
                // Both operands and their literal relation remain one source span.
                // A model cannot relabel or reorder the operands independently.
                Add("comparison", bodyStart, start + taskLength - bodyStart, bodyStart, start + taskLength - bodyStart);
                split = true;
            }
            if (Regex.IsMatch(taskText, @"^(?:please\s+)?(?:write|draft|compose|create|prepare|generate|make|design)\s+", Options, MatchTimeout))
            {
                foreach (Match connector in Regex.Matches(taskText, @"\s+(about|on|regarding|requesting|asking\s+for)\s+", Options, MatchTimeout))
                {
                    int at = start + connector.Index + 1;
                    if (protectedChars[at]) continue;
                    int headLength = TrimEnd(original, start, connector.Index);
                    int bodyStart = start + connector.Index + connector.Length;
                    int bodyLength = start + taskLength - bodyStart;
                    string head = original.Substring(start, headLength), body = original.Substring(bodyStart, bodyLength);
                    string relation = connector.Groups[1].Value.ToLowerInvariant();
                    // 'On' is polysemous. Support a bounded nominal scene (an entity
                    // doing something in/at/etc. a setting), not dates, agency or media.
                    // This remains an English attachment assumption, not semantic proof.
                    if (head.Length > 100 || Regex.IsMatch(head, @"\b(and|or|that|which|who)\b", Options, MatchTimeout) ||
                        bodyLength == 0 || relation == "on" && !SceneSubject(body)) break;
                    Add("task", start, headLength, start, headLength);
                    string role = relation is "requesting" or "asking for" ? "purpose" : "subject";
                    // Purpose keeps its action verb; subject consumes only the verified
                    // topic connector into the Subject relation, never the scene body.
                    int contentStart = role == "purpose" ? at : bodyStart;
                    Add(role, at, start + taskLength - at, contentStart, start + taskLength - contentStart);
                    split = true; break;
                }
            }
            if (!split) Add("task", start, taskLength, start, taskLength);
            if (taskLength != length)
            {
                int constraintStart = start + limit.Index;
                while (char.IsWhiteSpace(original[constraintStart])) constraintStart++;
                Add("constraints", constraintStart, start + length - constraintStart, constraintStart, start + length - constraintStart);
            }
        }
        if (HasCondition(visible) && (spans.Count(s => s.Kind != "constraints") != 1 || spans[0].Kind != "task"))
            return Unsupported(original, "Conditional scope spans multiple task components; the original must remain together.");
        if (spans.Count is < 1 or > 24 || !Covers(original, spans)) return Unsupported(original, "The supplied task cannot be partitioned within the bounded source grammar.");
        var operations = Operations(spans);
        return new(original, spans, operations.Count > 0, operations.Count > 0 ? "Supplied task components can be structured without adding requirements." : "No supported task-level restructuring was identified.");

        void Add(string kind, int start, int length, int contentStart, int contentLength) =>
            spans.Add(new("s" + spans.Count, kind, start, length, original.Substring(start, length), contentStart, contentLength, spans.Count));
    }

    public static RefinementContractReview Compose(RefinementContractLedger ledger, RefinementContractPlan? plan)
    {
        if (!ledger.CanStructure) return new(false, false, "", ledger.Reason);
        if (plan?.Sections is not { Count: > 0 and <= 24 } || plan.Sections.Any(s => s is null || !Kinds.Contains(s.Kind) || s.SourceIds is not { Count: > 0 and <= 24 } || s.SourceIds.Any(id => id is null || id.Length > 16)))
            return Invalid("The local model did not return a bounded source-ID structure plan.");
        var flattened = plan.Sections.SelectMany(s => s.SourceIds).ToArray();
        if (!flattened.SequenceEqual(ledger.Spans.Select(s => s.Id), StringComparer.Ordinal))
            return Invalid("The structure plan omitted, duplicated or reordered supplied content.");
        var map = ledger.Spans.ToDictionary(s => s.Id, StringComparer.Ordinal);
        if (plan.Sections.Any(section => section.SourceIds.Any(id => map[id].Kind != section.Kind)))
            return Invalid("The structure plan changed a supplied task, relation or constraint role.");
        if (!Covers(ledger.Original, ledger.Spans)) return Invalid("The source-span coverage could not be verified.");
        var operations = Operations(ledger.Spans);
        if (operations.Count == 0) return new(true, false, "", "Labels alone do not establish a useful task refinement.");
        var sections = new List<string>();
        var grammarRules = new List<string>();
        // Section boundaries are presentation metadata. Once every source occurrence,
        // role and order is verified, normalize adjacent sections of the same role.
        // This cannot add, drop, relabel or reorder any source content.
        var normalized = new List<RefinementContractSection>();
        foreach (var section in plan.Sections)
            if (normalized.Count > 0 && normalized[^1].Kind == section.Kind) normalized[^1].SourceIds.AddRange(section.SourceIds);
            else normalized.Add(new(section.Kind, section.SourceIds.ToList()));
        for (int sectionIndex = 0; sectionIndex < normalized.Count; sectionIndex++)
        {
            var section = normalized[sectionIndex];
            if (section.Kind == "comparison") {
                foreach (string id in section.SourceIds) {
                    var span = map[id];
                    string content = ledger.Original.Substring(span.ContentStart, span.ContentLength);
                    if (!TryComparisonBody(content, out string left, out string relation, out string right))
                        return Invalid("The supplied comparison relation could not be re-derived.");
                    sections.Add("Comparison:\n" + left + "\n" + relation + "\n" + right);
                }
                continue;
            }
            var values = section.SourceIds.Select(id => map[id]).Select(span => {
                string content = ledger.Original.Substring(span.ContentStart, span.ContentLength);
                if (RefinementGrammar.Propose(ledger.Original, span.Kind, content) is { } edit) {
                    content = edit.Result;
                    grammarRules.Add(edit.RuleId);
                    operations.Add(RefinementGrammar.Describe(edit.RuleId));
                }
                bool comparisonCommand = span.Kind == "task" && sectionIndex + 1 < normalized.Count && normalized[sectionIndex + 1].Kind == "comparison";
                return comparisonCommand ? content : RenderContent(content, span.Kind);
            }).ToArray();
            // Use the same plain heading for every request. Headings are display
            // metadata only; the source roles, coverage and fidelity gates stay fixed.
            string label = section.Kind switch { "task" => "Request", "subject" => "Subject", "purpose" => "Purpose", "constraints" => "Constraints", "steps" => "Steps", _ => "Supplied context" };
            string body = section.Kind == "steps" ? string.Join("\n", values.Select(value => "- " + value)) : string.Join("\n", values);
            sections.Add(label + ": " + body);
        }
        string rendered = string.Join("\n\n", sections);
        if (!RefinementPolicy.PreservesLiterals(ledger.Original, rendered)) return Invalid("A supplied literal could not be preserved in the structure.");
        return new(true, true, rendered, "The supplied task components were separated with complete source coverage.",
            new(ledger.Spans.Select(s => s.Id).ToList().AsReadOnly(), ledger.Original.Length, operations.AsReadOnly()) {
                GrammarRuleIds = grammarRules.Distinct(StringComparer.Ordinal).ToList().AsReadOnly()
            });
    }

    // Re-derive the ledger and rendering; a caller/model-supplied certificate is never
    // accepted as proof. This is distinct from the legacy conservative wording gate.
    public static RefinementContractReview Verify(string original, string candidate, RefinementContractPlan? plan)
    {
        var review = Compose(Analyze(original), plan);
        return review.Valid && !string.Equals(candidate, review.Text, StringComparison.Ordinal)
            ? Invalid("The proposed text differs from the verified source-span rendering.") : review;
    }

    private static RefinementContractReview Invalid(string reason) => new(false, false, "", reason);
    private static bool TrySemicolonConstraint(string original, string visible, bool[] protectedChars, out int separator)
    {
        separator = -1;
        for (int i = 0; i < original.Length; i++) {
            if (original[i] != ';' || protectedChars[i]) continue;
            if (separator >= 0) return false;
            separator = i;
        }
        if (separator < 1 || separator + 1 >= original.Length || !char.IsWhiteSpace(original[separator + 1]) ||
            original.Contains('\n') || original.Contains('\r') || original.Contains('`') ||
            Clauses(original, protectedChars).Count() != 1) return false;
        // Keep conditional, sequential, quoted-as-content, list/math and requested
        // source-layout relationships opaque. Protected strings/URLs themselves
        // remain exact source bytes, and never supply a constraint keyword.
        if (Regex.IsMatch(visible, @"\b(?:if|unless|otherwise|else|provided|when|until|before|after|once|while|whenever|whether|except|first|then|next|finally|second|last|steps?|verbatim|literal|literally|unchanged|unmodified|unaltered|untouched|format|formatting|layout|wording|phrasing|punctuation|indentation|reformat|restructure|semicolon|saying|says|reads|containing|quote|quoted|strings?|code|math|equations?|formulas?|regex|sql|scripts?|commands?|examples?|rules?|instructions?)\b|[()\[\]{}=<>\\]|\$(?!\d)|\bas\s+is\b|\bno\s+(?:edits?|changes?|rewriting)\b|\b(?:do not|don't|never)\s+(?:change|alter|edit|modify|rewrite|rephrase)\s+(?:(?:the|this|my|original|supplied|source)\s+)*(?:prompt|text|request|input|it|anything|everything)\b", Options, MatchTimeout)) return false;
        string task = visible[..separator].Trim(), constraint = visible[(separator + 1)..].Trim();
        if (!Regex.IsMatch(task, @"^(?:please\s+)?(?:compare|contrast|explain|summarize|describe|write|draft|compose|prepare|list|analyze)\s+\S", Options, MatchTimeout)) return false;
        // A trailing imperative can be text requested for a sign/message rather
        // than a rule for its writer. Do not assign a global constraint role when
        // content-introducing syntax or a literal display deliverable is present.
        if (visible.Contains(':') || Regex.IsMatch(task,
            @"\b(?:read|reading|say|state|states|stating|contains|include|including|print|printed|written|bearing|entitled|titled|signs?|labels?|warnings?|notices?|slogans?|mottos?|headlines?|captions?)\b|\bas\s+follows\b|\b(?:with|of|using)\s+(?:(?:the|this|these|following)\s+)*(?:words?|text|content|phrases?|sentences?)\b",
            Options, MatchTimeout)) return false;
        // 'Retain' also has unrelated action senses (e.g. retain a lawyer). Admit
        // only a finite set of supplied content attributes, never arbitrary nouns.
        return Regex.IsMatch(constraint, @"^(?:(?:do not|don't|never|must|keep|preserve|avoid|without|use|return|limit)\s+\S|retain\s+(?:(?:the|all|any|these|those|supplied|provided|original|exact)\s+)*(?:emojis?|names?|numbers?|units?|accents?|diacritics?|spelling|capitalization|dates?|prices?|symbols?)\b)", Options, MatchTimeout);
    }

    private static bool HasCoordinatedTask(string text)
    {
        // A second requested output is not part of the first output's subject.
        // Keep the complete clause intact instead of lifting a prefix count into a
        // global Request heading. Protected literal/code contents stay opaque.
        string visible = Protected.Replace(text, match => new string(' ', match.Length));
        // For a generated deliverable, even an unfamiliar second verb or adjective
        // can introduce another obligation. This bounded parser cannot prove that a
        // coordinated phrase belongs only to the first subject; preserve it whole.
        if (Regex.IsMatch(visible, @"^(?:please\s+)?(?:write|draft|compose|create|prepare|generate|make|design)\b", Options, MatchTimeout) &&
            Regex.IsMatch(visible, @"\b(?:and|or|plus|as\s+well\s+as)\b|[&+]", Options, MatchTimeout)) return true;
        const string coordinator = @"\b(?:and|or|plus|as\s+well\s+as)\s+(?:(?:also|then|please)\s+)*";
        const string action = @"(?:write|draft|compose|prepare|create|generate|make|design|explain|summarize|describe|list|compare|contrast|show|include|add|remove|return|give|calculate|analyze|find|build|evaluate|recommend|review|check|tell|state|report|outline|translate|rewrite|answer|provide)\b";
        const string amount = @"(?:(?:exactly|at\s+most|at\s+least|no\s+more\s+than|no\s+fewer\s+than|(?:a\s+)?(?:minimum|maximum)\s+of)\s+)?(?:\d+|one|two|three|four|five|six|seven|eight|nine|ten|a|an|another|the)\s+";
        return Regex.IsMatch(visible, coordinator + "(?:" + action + "|" + amount + ")", Options, MatchTimeout);
    }
    private static RefinementContractLedger Unsupported(string original, string reason, bool requiresClarification = false) =>
        new(original, [new("s0", "task", 0, original.Length, original, 0, original.Length, 0)], false, reason, requiresClarification);

    private static List<string> Operations(IReadOnlyList<RefinementContractSpan> spans)
    {
        var result = new List<string>();
        if (spans.Any(s => s.Kind == "task") && spans.Any(s => s.Kind == "subject")) result.Add("Separated the requested task from its supplied subject; kept the subject relation together.");
        if (spans.Any(s => s.Kind == "task") && spans.Any(s => s.Kind == "purpose")) result.Add("Separated the requested deliverable from its supplied purpose.");
        if (spans.Any(s => s.Kind == "task") && spans.Any(s => s.Kind == "comparison")) result.Add("Separated the two supplied comparison sides while preserving their order, complete wording and comparison relation.");
        if (spans.Any(s => s.Kind is "task" or "steps") && spans.Any(s => s.Kind == "constraints")) result.Add("Separated explicit constraints from the requested task without adding requirements.");
        if (spans.Count(s => s.Kind == "steps") > 1) result.Add("Enumerated the explicitly ordered stages without changing their order.");
        if (spans.Any(s => s.Kind == "task") && spans.Any(s => s.Kind == "context")) result.Add("Separated the requested task from supplied literal or code context.");
        return result;
    }

    private static string RenderContent(string text, string kind)
    {
        if (kind == "context") return text;
        return text.Length > 0 && ".!?;:".Contains(text[^1]) ? text : text + ".";
    }

    private static bool TryComparisonBody(string text, out string left, out string relation, out string right)
    {
        left = relation = right = "";
        string visible = Protected.Replace(text, match => new string(' ', match.Length));
        // Refuse ambiguous scope rather than assigning a condition, alternative or
        // later operation to one side. Conjunctions inside each side stay intact.
        if (HasCondition(visible) || Regex.IsMatch(visible,
            @"[;:\r\n]|\b(?:first|then|next|finally|before|after|until|while|whether|only|except|instead|rather|or|vs|against)\b|\bcompared\s+(?:with|to)\b",
            Options, MatchTimeout)) return false;
        if (Regex.IsMatch(visible, @"\band\s+(?:(?:also|then)\s+)?(?:please\s+)?(?:explain|summarize|list|compare|contrast|describe|write|draft|compose|prepare|generate|make|design|send|delete|open|run|execute|show|include|add|remove|return|give|calculate|analyze|find|build|create|evaluate|recommend|review|check|tell|state|report|outline|translate|rewrite|answer|provide|do)\b", Options, MatchTimeout))
            return false;
        var separators = Regex.Matches(visible, @"(?<!\S)versus(?!\S)", Options, MatchTimeout);
        if (separators.Count != 1 || Regex.Matches(visible, @"\bversus\b", Options, MatchTimeout).Count != 1) return false;
        var separator = separators[0];
        string lhs = text[..separator.Index].TrimEnd(), rhs = text[(separator.Index + separator.Length)..].TrimStart();
        if (!lhs.Any(char.IsLetterOrDigit) || !rhs.Any(char.IsLetterOrDigit)) return false;
        string visibleLeft = visible[..separator.Index].Trim(), visibleRight = visible[(separator.Index + separator.Length)..].Trim();
        if (Regex.IsMatch(visibleLeft, @"^(?:not|never|no)\b|\b(?:not|never|no|and|but)$", Options, MatchTimeout) ||
            Regex.IsMatch(visibleRight, @"^(?:not|never|no|and|but)\b", Options, MatchTimeout)) return false;
        left = lhs; relation = text.Substring(separator.Index, separator.Length); right = rhs;
        return true;
    }

    private static bool SceneSubject(string body) =>
        Regex.IsMatch(body, @"^an?\s+[\p{L}\p{N}\s-]+?\s+[\p{L}]+ing\s+(?:in|on|at|through|across|under|over|near|beside|toward|towards|from|with)\s+\S", Options, MatchTimeout) &&
        !Regex.IsMatch(body, @"\b(behalf|monday|tuesday|wednesday|thursday|friday|saturday|sunday|morning|afternoon|evening|paper|sheet|page|canvas|laptop|computer|tablet|phone|screen|platform|device)\b", Options, MatchTimeout);

    private static bool HasCondition(string text) => Regex.IsMatch(text, @"\b(if|unless|otherwise|else|provided that|when)\b", Options, MatchTimeout);

    private static bool[] ProtectedCharacters(string text)
    {
        var result = new bool[text.Length];
        foreach (Match match in Protected.Matches(text)) for (int i = match.Index; i < match.Index + match.Length; i++) result[i] = true;
        return result;
    }

    private static IEnumerable<(int Start, int Length)> Clauses(string text, bool[] protectedChars)
    {
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (protectedChars[i]) continue;
            bool boundary = (text[i] is '\r' or '\n') || (text[i] is '.' or '!' or '?') && (i + 1 == text.Length || char.IsWhiteSpace(text[i + 1]));
            if (!boundary) continue;
            int end = char.IsWhiteSpace(text[i]) ? i : i + 1;
            while (start < end && char.IsWhiteSpace(text[start])) start++;
            while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
            if (end > start) yield return (start, end - start);
            start = i + 1;
        }
        while (start < text.Length && char.IsWhiteSpace(text[start])) start++;
        int last = text.Length; while (last > start && char.IsWhiteSpace(text[last - 1])) last--;
        if (last > start) yield return (start, last - start);
    }

    private static int TrimEnd(string text, int start, int length)
    {
        while (length > 0 && char.IsWhiteSpace(text[start + length - 1])) length--;
        return length;
    }

    private static bool Covers(string original, IReadOnlyList<RefinementContractSpan> spans)
    {
        int previous = 0;
        foreach (var span in spans)
        {
            if (span.Start < previous || span.Length < 1 || span.Start + span.Length > original.Length || span.ContentStart < span.Start || span.ContentLength < 1 ||
                span.ContentStart + span.ContentLength > span.Start + span.Length || span.Text != original.Substring(span.Start, span.Length) ||
                !original.AsSpan(previous, span.Start - previous).ToString().All(char.IsWhiteSpace)) return false;
            previous = span.Start + span.Length;
        }
        return original.AsSpan(previous).ToString().All(char.IsWhiteSpace);
    }
}
