using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Buddy.Server;

// The model supplies content only. Heading, paragraph structure and pointer absence
// are host-owned, so a decoder length bound cannot turn a heading into clipped prose.
public sealed record ConceptualExplanation(string Purpose = "", string Limitation = "", string ManualCheck = "");

// A lesson is explanatory content, never an action or evidence of a visible control.
// The desktop independently resolves one exact live target before drawing any ink.
public static class GuideLessons
{
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);
    private const RegexOptions MatchOptions = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    public const string ExplanationHeading = "Explanation";
    public const string ExplanationInstructions = " Return only the JSON object with purpose, limitation and manualCheck, each containing one short complete sentence in the user's language. " +
        "Use ASCII double quotes as JSON delimiters; no word counts, character counts, headings, commentary or internal field names in the answer. " +
        "purpose: explain the useful common concept without inventing this app's behavior. limitation: keep the material risk and specifically unknown state or scope, not generic filler. " +
        "manualCheck: describe a specific read-only comparison between a relevant result and the intended result, or verification of the named unknown scope; control presence or availability is not a result. " +
        "Preserve all user prohibitions and necessary qualifications. Do not direct any change, even conditionally, claim an action happened, or predict an unseen transition. " +
        "Labels identify controls, not products, checked states, slider values, permissions or affected resources. Mute may concern input, output or one app; keep unknown scope explicit. " +
        "Reload can lose unsaved page work. Private publishing requires removal of private content and review of audience/access before any decision.";

    // Concept explanations do not need pointer identity, geometry, window titles or
    // availability flags. Keep those in host validation, where available != checked.
    // This is input minimization, not proof that a model's explanation is correct.
    public static string ExplanationInput(PlanningRequest request) => JsonSerializer.Serialize(new {
        question = request.Query,
        untrustedObservation = new {
            app = request.Context.App is { Length: <= 200 } ? request.Context.App : "",
            controls = request.Context.Elements.Where(e => e is not null && e.Name is { Length: <= 200 } && e.Role is { Length: <= 80 }).Take(40)
                .Select(e => new { name = e.Name, role = e.Role }).Distinct().ToArray()
        },
        observationLimits = "Labels and roles only; checked states, values, affected resources, permissions, document contents and action results were not observed. No action ran.",
        answerRequirements = "Explain the concept, retain the question's material cautions, then give a specific read-only result comparison or scope check; never substitute control availability for a result."
    }, StateStore.Json);
    // Classification reads only the user's main request. Quoted labels are data;
    // "don't explain" is not a request for an explanation. A procedural "teach me
    // to..." keeps the ordinary guide unless the user expressly prohibits changes.
    public static bool IsExplanationOnly(string query)
    {
        var main = Regex.Replace(query, "\"[^\"\\r\\n]*\"|(?<![\\p{L}])'[^'\\r\\n]*'(?![\\p{L}])", " ", MatchOptions, MatchTimeout).Trim();
        return Regex.IsMatch(main, @"^(?:please\s+)?(?:explain\b|describe\b|(?:help me\s+)?understand\b|what\b|why\b|how does\b|teach me\s+(?:about|what|why|the meaning|the purpose)\b)", MatchOptions, MatchTimeout) ||
            Regex.IsMatch(main, @"\b(?:do not|don't|never)\s+(?:perform|change|click|press|toggle|drag|type|publish|send|delete|act|modify|save|overwrite)\b|\bwithout\s+(?:changing|modifying|publishing|sending|deleting|saving)\b", MatchOptions, MatchTimeout);
    }

    // Missing observations are an evidence gap, never a reason to invent an app
    // state or next action. Procedural requests without an active target use the
    // same bounded explanatory schema until a new observation supplies one.
    public static bool HasCurrentTarget(PlanningRequest request) => request.Context.Elements.Any(e =>
        e is not null && e.Enabled && e.Width > 0 && e.Height > 0 && GuideSafety.AllowedTarget(e, request.Query));

    public static GuidePlan? EvidenceClarification(PlanningRequest request)
    {
        var named = request.Context.Elements.Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Name) && e.Name.Length <= 200 && GuideSafety.AllowedTarget(e,request.Query))
            .GroupBy(e => e.Name + "\n" + e.Role, StringComparer.OrdinalIgnoreCase).Where(g => RequestedName(request.Query,g.First().Name)).ToArray();
        if (named.Any(g => g.All(e => !e.Enabled))) return new("The requested control is disabled.", [], Lessons: [new(
            "The requested control is present but disabled; unrelated enabled controls do not establish why it is unavailable. What app state or documented prerequisite can you verify before continuing?")]);
        if (!HasCurrentTarget(request)) return new("Current control evidence is missing.", [], Lessons: [new(
            "No enabled controls are available in this observation, so a current target and its behavior cannot be verified. Which app and control should we examine in a fresh view?")]);
        var duplicate = request.Context.Elements.Where(e => e is not null && e.Enabled && e.Width > 0 && e.Height > 0 && GuideSafety.AllowedTarget(e,request.Query))
            .GroupBy(e => e.Name + "\n" + e.Role, StringComparer.OrdinalIgnoreCase)
            .Any(g => g.Count() > 1 && g.First().Name.Length > 0 && RequestedName(request.Query,g.First().Name));
        return duplicate ? new("The requested control is ambiguous in this view.", [], Lessons: [new(
            "More than one enabled control has the requested name and role; this observation does not establish which one you mean. Which part of the interface contains the intended control?")]) : null;
    }

    private static bool RequestedName(string query, string name) => Regex.Split(query, @"[;.!?]\s+").Any(clause =>
        !Regex.IsMatch(clause.Trim(), @"^(?:do not|don't|never|avoid|ignore)\b", MatchOptions, MatchTimeout) &&
        Regex.IsMatch(clause, @"(?<![\p{L}\p{N}])" + Regex.Escape(name) + @"(?![\p{L}\p{N}])", MatchOptions, MatchTimeout) &&
        !Regex.IsMatch(clause, @"\b(?:not|never|ignore|avoid)\s+(?:use\s+)?['""\u201c]?" + Regex.Escape(name) + @"(?![\p{L}\p{N}])", MatchOptions, MatchTimeout));

    public static JsonElement ModelSchema(bool explanationOnly)
    {
        if (explanationOnly) return JsonSerializer.SerializeToElement(new {
            type = "object", properties = new {
                purpose = new { type = "string", description = "Complete sentence explaining the concept." },
                limitation = new { type = "string", description = "Complete sentence preserving material caution and uncertainty." },
                manualCheck = new { type = "string", description = "Complete sentence giving a specific read-only result or scope check." }
            }, required = new[] { "purpose", "limitation", "manualCheck" }, additionalProperties = false
        });
        var node = JsonNode.Parse(AssistantSchemas.Guide.GetRawText())!;
        node["properties"]!["summary"]!["maxLength"] = 2400;
        node["properties"]!["summary"]!["description"] = "Short orientation answering the user's question, not a repeated click instruction or claim of action.";
        node["properties"]!["steps"]!["maxItems"] = explanationOnly ? 0 : 3;
        node["properties"]!["steps"]!["items"]!["properties"]!["primitive"] = JsonSerializer.SerializeToNode(new { type = "string", @enum = new[] { "ring" } });
        node["properties"]!["lessons"] = JsonSerializer.SerializeToNode(new { type = "array", minItems = 1, maxItems = 6, items = new {
            type = "object", properties = new Dictionary<string, object> {
                ["instruction"] = new { type = "string", minLength = 1, maxLength = 600, description = "A useful explanation paragraph: purpose, relevant caution or evidence limit, and a concrete manual check. Never just repeat a click command. Never predict an unseen dialog or claim the task completed." },
                ["target"] = explanationOnly ? (object)new { type = "string", @enum = new[] { "" } } : new { type = "string", maxLength = 200 },
                ["role"] = explanationOnly ? (object)new { type = "string", @enum = new[] { "" } } : new { type = "string", maxLength = 80 }
            }, required = new[] { "instruction", "target", "role" }, additionalProperties = false
        } });
        node["required"] = JsonSerializer.SerializeToNode(new[] { "summary", "steps", "lessons" });
        return JsonSerializer.SerializeToElement(node);
    }

    public static GuidePlan ComposeExplanation(ConceptualExplanation draft, PlanningRequest request)
    {
        string[] fields = [draft.Purpose, draft.Limitation, draft.ManualCheck];
        for (int index = 0; index < fields.Length; index++) {
            string? field = fields[index];
            if (string.IsNullOrWhiteSpace(field) || field.Length > 600)
                throw new BuddyException("INVALID_GUIDE", "Each explanation field must contain one complete bounded sentence; regenerate the whole explanation without clipping.");
            field = field.Trim();
            if (!field.Any(char.IsLetter) || field.Contains('\n') || field.Contains('\r') || ConversationalReply.Sentences(field) != 1 ||
                !Regex.IsMatch(field, @"[.!?]['""\u2019\u201d)]*$", RegexOptions.CultureInvariant, MatchTimeout) ||
                field.Count(c => c == '(') != field.Count(c => c == ')') || field.Count(c => c == '[') != field.Count(c => c == ']'))
                throw new BuddyException("INVALID_GUIDE", "Use one complete sentence per explanation field, with final punctuation and closed parentheses; do not return fragments, lists or extra sentences.");
            if (Regex.IsMatch(field, @"\buntrusted[A-Z][A-Za-z]*\b|\b(?:browserChromeVerified|invalidPreviousReply|manualCheck)\b", RegexOptions.CultureInvariant, MatchTimeout))
                throw new BuddyException("INVALID_GUIDE", "Use user-facing prose, not internal input or schema field names.");
            ValidateModelProse(field, true, request);
            fields[index] = field;
        }
        string paragraph = string.Join(" ", fields);
        if (paragraph.Length > 600 || fields.Distinct(StringComparer.OrdinalIgnoreCase).Count() != fields.Length)
            throw new BuddyException("INVALID_GUIDE", "Use three distinct complete sentences totaling at most 600 characters; regenerate instead of removing a qualification.");
        ValidateExplanationMeaning(fields, request);
        return new(ExplanationHeading, [], Lessons: [new(paragraph)]);
    }

    private static void ValidateExplanationMeaning(string[] fields, PlanningRequest request)
    {
        static bool Has(string text, string pattern) => Regex.IsMatch(text, pattern, MatchOptions, MatchTimeout);
        string paragraph = string.Join(" ", fields), check = fields[2], query = request.Query;
        // These are rejection boundaries for demonstrated unsupported claims, not
        // an automatic semantic-quality score or a substitute for independent QA.
        // A result comparison can ask whether wrapping is enabled without using
        // the control's UIA Enabled metadata as proof of its checked state.
        bool wrappingComparison = Has(query, @"\bword\s+wrap\b") &&
            Has(check, @"\bcompar\w*\b") && Has(check, @"\b(?:displayed|visible)\s+(?:lines?|text|line endings)\b") &&
            Has(check, @"\b(?:original|stored|explicit)\s+(?:text|line[ -]?breaks?)\b") &&
            Has(check, @"\b(?:whether|if)\s+(?:word\s+)?wrapping\s+is\s+enabled\b") &&
            !Has(check, @"\b(?:control|button|checkbox|slider|menu item)\b[^.!?]{0,40}\benabled\b");
        if (Has(check, @"\b(?:availability|available for interaction|accessible|presence)\b") ||
            Has(check, @"\benabled\b") && !wrappingComparison ||
            !Has(check, @"\b(?:compar\w*|check\w*|verif\w*|inspect\w*|read\w*|observ\w*|look for|review\w*)\b") ||
            Has(check, @"\b(?:observe|check|verify|confirm)\s+(?:its |the )?(?:behavior|functionality|function|presence)\b"))
            throw new BuddyException("INVALID_GUIDE", "The result check only restates availability or generic behavior. Compare a relevant visible result with the intended result, or check specifically missing scope without changing anything.");
        if (Has(paragraph, @"\b(?:enabled|available)\b[^.!?]{0,100}\b(?:indicat\w*|mean\w*|signif\w*|therefore|so that)\b[^.!?]{0,70}\b(?:checked|active|on|wrap\w*|muted)\b") &&
            !Has(paragraph, @"\b(?:does not|doesn't|cannot|can't|not reveal|not establish)\b"))
            throw new BuddyException("INVALID_GUIDE", "Availability does not establish checked state, a value or an effect. State that these were not observed.");
        if ((Has(paragraph, @"\b(?:checkbox|control|setting)\s+is\s+(?:currently\s+)?(?:checked|unchecked|selected|active|on|off)\b") ||
             Has(paragraph, @"\b(?:the|this)\s+(?:checked|unchecked|selected)\s+(?!state\b)(?:[\p{L}-]+\s+){0,3}(?:box|checkbox|control|option|setting)\b")) &&
            !Has(query, @"\b(?:checkbox|control|setting)\s+is\s+(?:currently\s+)?(?:checked|unchecked|selected|active|on|off)\b"))
            throw new BuddyException("INVALID_GUIDE", "The observation does not contain a checked state. Do not assert one unless the user explicitly supplies it.");
        if (Has(query, @"\bmute\b") && !Has(query, @"\b(?:microphone|speaker|headphone|audio output|audio input|this tab|this app)\b")) {
            if (Has(fields[0], @"\b(?:audio output|microphone|speakers?|headphones?)\b") && !Has(fields[0], @"\b(?:may|might|could|or|depending|unknown|unspecified)\b"))
                throw new BuddyException("INVALID_GUIDE", "The Mute label does not identify audio input, output or app scope. Do not specialize the purpose to an unobserved channel.");
            if (!Has(fields[1] + " " + check, @"\b(?:scope|channel|which audio|input or output|input, output|affects|affected|applies to)\b"))
                throw new BuddyException("INVALID_GUIDE", "Retain the unknown audio scope as well as the unknown checkbox state; the check must identify which audio is affected.");
        }
        if (Has(query, @"\b(?:reload|refresh)\b") && !Has(paragraph, @"\b(?:unsaved|unsubmitted|uncommitted|form entries)\b"))
            throw new BuddyException("INVALID_GUIDE", "Retain the material possibility of losing unsaved page work before any later reload decision; do not imply a reload has occurred.");
        if (Has(query, @"\b(?:private|sensitive|confidential)\b") && Has(query, @"\bpublish\w*\b") &&
            (!Has(paragraph, @"\b(?:private|sensitive|confidential)\b") ||
             !Has(paragraph, @"\b(?:remov\w*|exclud\w*|omit\w*|redact\w*)\b") ||
             !Has(fields[1] + " " + check, @"\b(?:review\w*|check\w*|verif\w*|inspect\w*|compar\w*)\b[^.!?]{0,110}\b(?:audience|access|permission|visibility|recipient)\w*\b")))
            throw new BuddyException("INVALID_GUIDE", "Preserve both removal of private content and review of the intended audience/access before any publishing decision.");
    }

    private static void ValidateModelProse(string text, bool explanationOnly, PlanningRequest request)
    {
        try { TeachingPolicy.ValidateProse(text); }
        catch (BuddyException ex) { throw new BuddyException("INVALID_GUIDE", ex.Message); }
        // Operation euphemisms are still instructions, even when softened by try,
        // could or proceed. Anchoring preserves prohibitions and conceptual uses.
        if (explanationOnly && Regex.IsMatch(text,
            @"(?:^|[.!?:;,\n]\s*)\s*['""\u201c]?(?:please\s+)?(?:try\s+(?:to\s+)?(?:click\w*|press\w*|select\w*|interact\w*|activat\w*|toggl\w*|reload\w*|refresh\w*)\b|use\s+(?:the |this |your )?[^.!?]{1,100}\s+to\s+(?:click|press|select|interact|activate|toggle|reload|refresh|publish|send|delete|save)\b)|\byou\s+(?:can|could|may|might|should|must)\s+(?:(?:try|attempt)\s+to\s+(?:click|press|select|interact|activate|toggle|reload|refresh|publish|send|delete|save)\b|proceed\s+by\s+(?:clicking|pressing|selecting|interacting|activating|toggling|reloading|refreshing|publishing|sending|deleting|saving)\b)",
            MatchOptions, MatchTimeout))
            throw new BuddyException("INVALID_GUIDE", "This is an explanation-only request. An attempt, interaction or suggested use is still an operation; describe the concept and read-only result check without directing it.");
        if (explanationOnly && Regex.IsMatch(text,
            @"(?:^|[.!?:;,\n]\s*)\s*['""\u201c]?(?:please\s+)?(?:click|press|select|toggle|drag|type|enter|activate|tap|choose)\b|\b(?:you\s+(?:should|must|can|may|could|might|need to|have to)|go ahead and|then)\s+(?:(?:[\p{L}]+ly|now|also|just|still)\s+){0,3}(?:click|press|select|toggle|drag|type|enter|activate|tap|choose|publish|send|delete|submit|save|overwrite)\b|(?:^|[.!?:;,\n]\s*)\s*(?:publish|send|delete|submit|save|overwrite)\s+(?:it|this|the|your)\b",
            MatchOptions, MatchTimeout))
            throw new BuddyException("INVALID_GUIDE", "This is an explanation-only request. Remove instructions to change anything; preserve cautions and describe a manual check without telling the user to perform an operation.");
        foreach (var name in request.Context.Elements.Where(e => e is not null).Select(e => e.Name).Where(n => !string.IsNullOrWhiteSpace(n) && n.Length <= 200).Distinct(StringComparer.OrdinalIgnoreCase)) {
            if (!name.Equals(request.Context.App, StringComparison.OrdinalIgnoreCase) && Regex.IsMatch(text, @"(?<![\p{L}\p{N}])" + Regex.Escape(name) + @"\s+(?:app|application|meeting)\b", MatchOptions, MatchTimeout))
                throw new BuddyException("INVALID_GUIDE", "An observed control label was incorrectly used as an app identity. Explain its general role without inventing the application's name or features.");
        }
    }

    public static GuidePlan Compose(GuidePlan draft, PlanningRequest request, bool explanationOnly = false)
    {
        if (draft.Summary is null || draft.Summary.Length > 2400 || draft.Steps is null || draft.Steps.Count > 8)
            throw new BuddyException("INVALID_GUIDE", "The lesson must have a bounded explanation and at most eight sections.");
        if (explanationOnly && draft.Steps.Count > 0)
            throw new BuddyException("INVALID_GUIDE", "An explanation-only request needs empty steps and text-only lessons, not action pointers.");
        if (explanationOnly || draft.Lessons is { Count: > 0 }) ValidateModelProse(draft.Summary, explanationOnly, request);
        List<GuideStep> pointers = []; List<GuideLesson> lessons = [];
        foreach (var step in draft.Steps) {
            if (step is null || string.IsNullOrWhiteSpace(step.Instruction) || step.Instruction.Length > 600 || step.Target is null || step.Target.Length > 200 || step.Role is null || step.Role.Length > 80)
                throw new BuddyException("INVALID_GUIDE", "A lesson section was incomplete or too long.");
            if (draft.Lessons is { Count: > 0 } && step.Primitive != "ring")
                throw new BuddyException("INVALID_GUIDE", "Teaching pointers use ring, never an executable action primitive.");
            // Irrelevant chrome must not survive merely by becoming a text-only lesson.
            if (!GuideSafety.AllowedTarget(new("", step.Target, step.Role, 0, 0, 1, 1), request.Query)) continue;
            try {
                GuideSafety.Validate(new("", [step]), request); pointers.Add(draft.Lessons is { Count: > 0 } ? step with { Expect = new("manual") } : BrowserGuideLessons.ManualExpectation(step, request));
                lessons.Add(new(step.Instruction, step.Target, step.Role));
            }
            catch (BuddyException e) when (e.Code == "INVALID_GUIDE") {
                // A rejected current reference must not regain a pointer by resolving
                // another control's name. Future targets can still be resolved later.
                bool referencesCurrent = request.Context.Elements.Any(element => element.Ref == step.Ref ||
                    element.Name.Equals(step.Target, StringComparison.OrdinalIgnoreCase) && element.Role.Equals(step.Role, StringComparison.OrdinalIgnoreCase));
                lessons.Add(referencesCurrent ? new(step.Instruction) : new(step.Instruction, step.Target, step.Role));
            }
        }
        if(draft.Steps.Count>0&&lessons.Count==0) throw new BuddyException("INVALID_GUIDE","All suggested sections were irrelevant window controls. Give a useful explanation of the requested task.");
        if (draft.Lessons is { Count: > 0 } supplied) {
            if (supplied.Count > 6) throw new BuddyException("INVALID_GUIDE", "Use at most six bounded teaching sections.");
            var complete = new List<GuideLesson>();
            foreach (var lesson in supplied) {
                if (lesson is null || string.IsNullOrWhiteSpace(lesson.Instruction) || lesson.Instruction.Length > 600 || lesson.Target is null || lesson.Role is null || lesson.Target.Length > 200 || lesson.Role.Length > 80)
                    throw new BuddyException("INVALID_GUIDE", "A teaching section is missing or too long.");
                ValidateModelProse(lesson.Instruction, explanationOnly, request);
                bool named = lesson.Target.Length > 0 || lesson.Role.Length > 0;
                if (named && (explanationOnly || lesson.Target.Length == 0 || lesson.Role.Length == 0 ||
                    pointers.Count(p => p.Target.Equals(lesson.Target, StringComparison.OrdinalIgnoreCase) && p.Role.Equals(lesson.Role, StringComparison.OrdinalIgnoreCase)) != 1 ||
                    Resolve(lesson, request.Context, request.Query) is null))
                    throw new BuddyException("INVALID_GUIDE", "A lesson pointer must belong to one already validated current step; rejected or ambiguous references cannot be recovered by name.");
                if (!complete.Any(l => l.Instruction.Equals(lesson.Instruction, StringComparison.OrdinalIgnoreCase))) complete.Add(lesson);
            }
            lessons = complete;
            foreach (var pointer in pointers) ValidateModelProse(pointer.Instruction, false, request);
        } else if (explanationOnly) {
            // Legacy shape remains readable, but never fabricate missing teaching.
            lessons = string.IsNullOrWhiteSpace(draft.Summary) ? [] : [new(draft.Summary)];
        }
        return draft with { Steps = pointers, Lessons = lessons };
    }
    public static ScreenElement? Resolve(GuideLesson lesson, ScreenContext context, string query)
    {
        if (GuideSafety.RequestedApp(query) is {} app && !app.Equals(context.App, StringComparison.OrdinalIgnoreCase)) return null;
        if (lesson.Role.Length == 0) return null;
        var exact = context.Elements.Where(e => e.Enabled && e.Width > 0 && e.Height > 0 && e.Name.Equals(lesson.Target, StringComparison.OrdinalIgnoreCase) && e.Role.Equals(lesson.Role, StringComparison.OrdinalIgnoreCase) && GuideSafety.AllowedTarget(e, query)).ToArray();
        return exact.Length == 1 ? exact[0] : null;
    }
    public static bool IsNotepadIntroduction(string query) => GuideSafety.RequestedApp(query) == "notepad" &&
        Regex.IsMatch(query, @"\b(teach|learn|how to use|getting started|basics)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public static GuidePlan Notepad(PlanningRequest request)
    {
        var editors = request.Context.App.Equals("notepad", StringComparison.OrdinalIgnoreCase) ? request.Context.Elements.Where(e => e.Enabled && e.Role is "Edit" or "Document").ToArray() : [];
        var editor = editors.Length == 1 ? editors[0] : null;
        return new("Notepad is a plain-text editor: enter text, make changes, then save a text file. This introductory lesson leaves the work to you; reviewing a section does not change or save a document.", [], Lessons: [
            new("Start with an empty document, or keep your existing text. Select the editor and type a short practice sentence if you want to try it.", editor?.Name ?? "", editor?.Role ?? ""),
            new("Select a word to replace it, or use the Edit menu to find text and undo a change. Review the result before continuing.", "Edit", "MenuItem"),
            new("When you want to keep your work, use File > Save or Ctrl+S, choose a name and location, and check the saved file. Saving is your choice; Buddy has not saved anything.", "File", "MenuItem")]);
    }
}
