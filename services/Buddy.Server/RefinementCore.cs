using System.Text;
using System.Text.RegularExpressions;

namespace Buddy.Server;

public sealed record RefinementExample(string Input, string Output);
public sealed record RefinementInputs(List<RefinementExample>? Examples = null, List<string>? AvailableTools = null,
    List<string>? Stages = null, bool Revision = false, List<string>? ConfirmedConstraints = null,
    List<RefinementContextSource>? Context = null);
public sealed record RefinementTechniqueChoice(string Technique, string Rationale, bool Ready, List<string> Warnings);
public sealed record RefinementFidelity(bool Allowed, string Reason, List<string> Ledger);
public sealed record RefinementBudget(string Destination, int? Limit, string Unit, string Authority = "explicit");
// Verified entries come from a reviewed host registry, never from the refinement request.
// No destination registry ships with Buddy. Callers must not treat a claimed limit as verified.
public sealed record RefinementVerifiedLimit(string Destination, int Limit, string Unit, string Evidence);
public sealed record RefinementBlock(string Id, string Text, bool Required = false, string Kind = "context");
public sealed record RefinementBudgetResult(bool Fits, string Text, int Count, int? Limit, string? Unit,
    List<string> Removed, string? Conflict, string? Evidence = null);

/// <summary>Pure, local selection and loss-aware assembly. No model, retrieval, actions or persistence.</summary>
public static class RefinementCore
{
    public static readonly string[] BudgetUnits = ["utf16-code-units", "unicode-scalars", "utf8-bytes"];

    public static RefinementTechniqueChoice SelectTechnique(RefineRequest request)
    {
        var input = request.Inputs;
        bool examples = input?.Examples is { Count: > 0 } && input.Examples.All(x => x is not null && !string.IsNullOrWhiteSpace(x.Input) && !string.IsNullOrWhiteSpace(x.Output));
        bool tools = input?.AvailableTools is { Count: > 0 } && input.AvailableTools.All(x => !string.IsNullOrWhiteSpace(x));
        bool multistage = input?.Stages is { Count: > 1 } || Regex.IsMatch(request.Prompt, @"\bfirst\b[\s\S]+\bthen\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        bool revision = input?.Revision == true || request.Domain == "review" || Regex.IsMatch(request.Prompt, @"^\s*(?:please\s+)?(?:revise|rewrite|critique)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        string selected;
        string reason;
        if (request.Technique != "auto") { selected = request.Technique; reason = "Uses your selected technique."; }
        else if (multistage) { selected = "chaining"; reason = "The supplied stages form a multistage task."; }
        else if (revision) { selected = "meta"; reason = "Revision benefits from a concise check and revision pass."; }
        else if (request.Domain == "agent-ide" && tools) { selected = "react"; reason = "The supplied tools support a tool-aware plan; this does not execute them."; }
        else if (examples) { selected = "few-shot"; reason = "Uses the examples you supplied without fabricating new ones."; }
        else { selected = "zero-shot"; reason = "A direct instruction is sufficient for the supplied task."; }

        var warnings = new List<string>();
        if (selected == "few-shot" && !examples) warnings.Add("Supply real input/output examples before using few-shot.");
        if (selected == "react" && !tools) warnings.Add("No available tools were supplied. ReAct cannot be prepared.");
        if (selected == "chaining" && !multistage) warnings.Add("Supply at least two task stages before using chaining.");
        if (!RefinementPolicy.Techniques.Contains(selected) || selected == "auto") warnings.Add("Choose a supported technique.");
        return new(selected, reason, warnings.Count == 0, warnings);
    }

    public static string TechniqueInstruction(string technique) => technique switch
    {
        "few-shot" => "Preserve the supplied examples as reference data. Do not invent examples or requirements.",
        "react" => "Use only the listed available tools when describing the task. Do not execute tools or invent capabilities.",
        "chaining" => "Keep the supplied stages in their original order. Do not invent extra stages.",
        "meta" => "Check the draft and revise wording only. Give a brief rationale, never private chain-of-thought.",
        "chain-of-thought" => "If an explanation is requested, ask for a concise answer explanation, never hidden reasoning or a private thought transcript.",
        "tree-of-thoughts" => "Preserve any requested comparison of alternatives; ask only for a brief conclusion and concise rationale, never private reasoning.",
        "role" => "Preserve an explicitly supplied role; do not invent a persona, audience or credentials.",
        _ => "Improve the direct instruction without adding substantive content."
    };

    // Conservative independent gate: a fluent model and a high embedding score cannot
    // authorize new obligations. This supports bounded polishing, not arbitrary paraphrase.
    // The original remains the authoritative ledger, including content inside quotations/code.
    public static RefinementFidelity Fidelity(string original, string candidate, IReadOnlyList<string>? confirmedConstraints = null)
    {
        Security.Text(original, 20000, "Original prompt");
        var ledger = new List<string> { original };
        if (confirmedConstraints is not null) ledger.AddRange(confirmedConstraints);
        if (string.IsNullOrWhiteSpace(candidate) || candidate.Length > 30000 || !RefinementPolicy.PreservesLiterals(original, candidate))
            return new(false, "A supplied literal, fact or code fragment was changed or omitted.", ledger);
        if (PrivateReasoning.IsMatch(candidate) && !PrivateReasoning.IsMatch(original))
            return new(false, "The rewrite added a request for private reasoning.", ledger);
        if (!Canonical(original).SequenceEqual(Canonical(candidate), StringComparer.Ordinal))
            return new(false, "The rewrite added, removed or reordered substantive content. Missing details must be confirmed separately.", ledger);
        // Additional confirmed constraints are assembled as required blocks, not silently
        // inferred from references or suggestions. They are never dropped for a budget.
        return new(true, "The ordered intent and constraint ledger is preserved within the supported wording edits.", ledger);
    }

    private static readonly Regex PrivateReasoning = new(@"\b(?:hidden|private|internal)\s+(?:reasoning|thoughts?|chain[- ]of[- ]thought)|\bshow\s+(?:all\s+)?(?:your\s+)?chain[- ]of[- ]thought", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static readonly Regex Tokens = new("```[\\s\\S]*?```|`[^`]*`|https?://[^\\s<>\"']+|\"[^\"]*\"|[\\p{L}\\p{M}\\p{N}_]+|[^\\s\\p{L}\\p{M}\\p{N}_]", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    private static IEnumerable<string> Canonical(string text)
    {
        // These equivalences are intentionally small. Negations, comparisons, temporal
        // relations, conjunctions, numbers and domain vocabulary are not stop words.
        text = Regex.Replace(text, @"\basking\s+for\b|\brequesting\b", "request", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        bool firstWord = true;
        foreach (Match match in Tokens.Matches(text))
        {
            var token = match.Value;
            if (token.StartsWith('`') || token.StartsWith('"') || token.StartsWith("http", StringComparison.Ordinal)) { yield return token; firstWord = false; continue; }
            token = token.ToLowerInvariant();
            if (token is "." or "," or ";" or ":" || token is "a" or "an" or "the") continue;
            if (firstWord && token == "please") continue;
            if (firstWord && token is "draft" or "compose") token = "write";
            // "clear" is only optional wording about a prompt, never an operation such as clear a file.
            if (token == "clear" && Regex.IsMatch(text, @"\b(?:write|draft|compose)\s+(?:a\s+)?clear,?\s+(?:useful\s+)?prompt\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) continue;
            firstWord = false;
            yield return token;
        }
    }

    public static int Count(string text, string unit) => unit switch
    {
        "utf16-code-units" => text.Length,
        "unicode-scalars" => text.EnumerateRunes().Count(),
        "utf8-bytes" => Encoding.UTF8.GetByteCount(text),
        _ => throw new BuddyException("INVALID_REFINE_BUDGET", "Choose UTF-16 code units, Unicode scalars or UTF-8 bytes. Token limits need a verified tokenizer.")
    };

    public static RefinementBudgetResult ApplyBudget(IReadOnlyList<RefinementBlock> blocks, RefinementBudget? budget,
        IReadOnlyList<RefinementVerifiedLimit>? verifiedLimits = null)
    {
        if (blocks is null || blocks.Count > 64 || blocks.Any(x => x is null || string.IsNullOrEmpty(x.Id) || x.Id.Length > 80 || x.Text is null || x.Text.Length > 200000 || x.Kind is null) || blocks.Sum(x => (long)x.Text.Length) > 1000000 || blocks.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != blocks.Count)
            throw new BuddyException("INVALID_REFINE_CONTEXT", "Refinement blocks are invalid or too large.");
        string Assemble(IEnumerable<RefinementBlock> selected) => string.Join("\n\n", selected.Where(x => x.Text.Length > 0).Select(x => x.Text));
        string all = Assemble(blocks);
        if (budget is null) return new(true, all, Count(all, "utf16-code-units"), null, null, [], null);
        if (string.IsNullOrWhiteSpace(budget.Destination) || budget.Destination.Length > 120 || !BudgetUnits.Contains(budget.Unit))
            throw new BuddyException("INVALID_REFINE_BUDGET", "A destination, explicit count unit and valid limit are required.");
        int? limit = budget.Limit; string? evidence = null;
        if (budget.Authority == "verified")
        {
            var matches = verifiedLimits?.Where(x => x is not null && x.Destination == budget.Destination && x.Unit == budget.Unit).ToArray() ?? [];
            if (matches.Length != 1 || string.IsNullOrWhiteSpace(matches[0].Evidence) || limit is not null && limit != matches[0].Limit)
                return new(false, "", Count(all, budget.Unit), null, budget.Unit, [], "No matching verified limit exists for this destination and unit.");
            limit = matches[0].Limit; evidence = matches[0].Evidence;
        }
        else if (budget.Authority != "explicit") throw new BuddyException("INVALID_REFINE_BUDGET", "A limit must be explicit or come from a verified host registry.");
        if (limit is null or < 1 or > 20000) throw new BuddyException("INVALID_REFINE_BUDGET", "The destination limit must be between 1 and 20000 counted units.");
        var selected = blocks.ToList(); var removed = new List<string>();
        foreach (var optional in blocks.Where(x => !x.Required).OrderBy(x => x.Kind == "context" ? 0 : 1))
        {
            if (Count(Assemble(selected), budget.Unit) <= limit) break;
            selected.Remove(optional); removed.Add(optional.Id);
        }
        string result = Assemble(selected); int count = Count(result, budget.Unit);
        if (count > limit) return new(false, "", count, limit, budget.Unit, removed,
            "The required intent and constraints exceed the destination limit. Increase the limit or explicitly revise those requirements.", evidence);
        return new(true, result, count, limit, budget.Unit, removed, null, evidence);
    }

    internal static void ValidateInputs(RefineRequest request)
    {
        var input = request.Inputs;
        if (input is null) return;
        if (input.Examples is { Count: > 8 } || input.AvailableTools is { Count: > 20 } || input.Stages is { Count: > 12 } || input.ConfirmedConstraints is { Count: > 20 } || input.Context is { Count: > 8 })
            throw new BuddyException("INVALID_REFINE_CONTEXT", "Too many refinement inputs were supplied.");
        foreach (var value in (input.AvailableTools ?? []).Concat(input.Stages ?? []).Concat(input.ConfirmedConstraints ?? [])) Security.Text(value, 2000, "Refinement input");
        foreach (var example in input.Examples ?? []) {
            if (example is null) throw new BuddyException("INVALID_REFINE_CONTEXT", "An example is missing.");
            Security.Text(example.Input, 2000, "Example input"); Security.Text(example.Output, 2000, "Example output");
        }
    }
}
