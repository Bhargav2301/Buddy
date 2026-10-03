using System.Text.Json;
using System.Text.RegularExpressions;

namespace Buddy.Server;

public record ScreenElement(string Ref, string Name, string Role, double X, double Y, double Width, double Height, bool Enabled = true);
public record ScreenContext(string App, string Title, List<ScreenElement> Elements);
public record AssistantAction(string Kind = "", string Ref = "", string Target = "", string Role = "", string Value = "", string Description = "", string Risk = "high", bool RequireEmpty = false);
public record AssistantPlan(string Summary = "", List<AssistantAction>? Actions = null);
public record GuideExpectation(string Kind = "manual", string Target = "", string Role = "");
public record GuideStep(string Instruction = "", string Ref = "", string Target = "", string Role = "", string Primitive = "ring", GuideExpectation? Expect = null);
public record GuideLesson(string Instruction, string Target = "", string Role = "");
public record GuidePlan(string Summary = "", List<GuideStep>? Steps = null, List<WebSource>? Sources = null, [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] List<GuideLesson>? Lessons = null);
public record PlanningRequest(string Query, ScreenContext Context, bool UseWeb = false);
public record ActionResult(int Sequence, AssistantAction Action, bool Success, string Observation);
public record AgentContinuation(string Query, ScreenContext Context, List<ActionResult> Results, int RemainingActions);
public record AgentDecision(string Status, string Summary, List<AssistantAction>? Actions);
public record GroundedTarget(ScreenElement Element, string Provenance, double Confidence, bool CanExecute);
public record AuditEntry(DateTimeOffset At, string Kind, string Target, string Result);
public record SavedGuide(string Id, string Query, GuidePlan Plan, int Index, DateTimeOffset UpdatedAt, bool Completed = false);
public record WebSource(string Title, string Url, string Text, string EvidenceKind = "fetched page", List<SourceLink>? Links = null);

public static class ActionPolicy
{
    public static readonly string[] Kinds = ["open", "click", "invoke", "type", "keys", "read", "wait"];
    public static readonly string[] Apps = ["notepad", "calculator", "explorer", "comet"];
    public static readonly string[] Keys = ["Tab", "Shift+Tab", "Enter", "Escape", "Ctrl+A", "Ctrl+C", "Ctrl+Z", "Up", "Down", "Left", "Right"];
    public static string Risk(AssistantAction action, string liveTarget = "") =>
        action.Kind is "type" or "keys" or "open" || Regex.IsMatch(action.Target + " " + liveTarget + " " + action.Description,
            "send|submit|delete|remove|pay|buy|purchase|install|confirm|transfer|publish|share|allow|accept|approve|sign|permission|execute|run", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))
        || action.Kind is "click" or "invoke" ? "high" : "low";

    // The evidence comes from fresh desktop UIA inspection, never from model output or a phone.
    public static string LiveRisk(AssistantAction action, ScreenElement? element, bool reversibleEdit = false, bool navigationPattern = false)
    {
        var text = string.Join(" ", action.Target, element?.Name, action.Description);
        if (Regex.IsMatch(text, @"\b(send|submit|delete|remove|pay|buy|purchase|install|transfer|publish|share|allow|accept|approve|confirm|save|sign|permission|execute|run|password|secret|credential)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) return "high";
        if (action.Kind is "read" or "wait") return "low";
        if (action.Kind is "open" or "type" or "click" or "invoke") return "high";
        if (action.Kind == "keys") return action.Value is "Tab" or "Shift+Tab" or "Escape" or "Up" or "Down" or "Left" or "Right" ? "low" : "high";
        if (element is null || !element.Enabled) return "high";


        return "high";
    }

    public static AssistantPlan Validate(AssistantPlan plan)
    {
        if (plan.Summary is null || plan.Summary.Length > 2000 || plan.Actions is null || plan.Actions.Count is < 1 or > 25) throw new BuddyException("INVALID_PLAN", "The model could not make a bounded action plan. Try one smaller task.");
        var actions = plan.Actions.Select(a => {
            if (a is null || !Kinds.Contains(a.Kind) || a.Value is null || a.Target is null || a.Role is null || a.Ref is null || a.Description is null || a.Value.Length > 4000 || a.Target.Length > 200 || a.Description.Length > 500 || a.Ref.Length > 100 || a.Role.Length > 80)
                throw new BuddyException("INVALID_PLAN", "The model returned an unsupported action. Nothing was executed.");
            if (a.Kind == "open") {
                var value=a.Value.Trim();
                var alias=value.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)?value[..^4]:value;
                if(Apps.Contains(alias.ToLowerInvariant()))a=a with{Value=alias.ToLowerInvariant()};
                else {
                    try { a=a with{Value=WebResearch.ValidateUrl(value).AbsoluteUri}; }
                    catch(BuddyException){throw new BuddyException("INVALID_PLAN","The plan proposed an unsupported app launch. Use a named installed app or an explicit public HTTPS address; no action ran.");}
                }
            }
            if (a.Kind == "keys" && !Keys.Contains(a.Value)) throw new BuddyException("INVALID_PLAN", "The requested keyboard shortcut is not allowed.");
            if (a.Kind is "click" or "invoke" or "type" or "read" && a.Ref.Length == 0 && a.Target.Length == 0 && a.Role.Length == 0)
                throw new BuddyException("INVALID_PLAN", "The action needs a specific visible target.");
            return a with { Risk = Risk(a) }; // Never trust a model-supplied risk label.
        }).ToList();
        return plan with { Actions = actions };
    }
}

public static class GroundingResolver
{
    public static ScreenElement? Resolve(IEnumerable<ScreenElement> elements, string reference, string text, string role)
    {
        var candidates = elements.Where(e => e.Enabled && e.Width > 0 && e.Height > 0 &&
            (string.IsNullOrEmpty(role) || e.Role.Equals(role, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (!string.IsNullOrEmpty(reference)) { var matches = candidates.Where(e => e.Ref == reference).ToArray(); return matches.Length == 1 ? matches[0] : null; }
        if (text.Length == 0) return candidates.Length == 1 ? candidates[0] : null;
        var exact = candidates.Where(e => e.Name.Equals(text, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (exact.Length > 0) return exact.Length == 1 ? exact[0] : null;
        // Fuzzy matching is only for longer names and a matching role; ambiguity fails closed.
        if (text.Length < 5 || role.Length == 0) return null;
        var fuzzy = candidates.Where(e => Distance(e.Name.ToLowerInvariant(), text.ToLowerInvariant()) <= 2).ToArray();
        return fuzzy.Length == 1 ? fuzzy[0] : null;
    }
    private static int Distance(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 2 || a.Length > 200 || b.Length > 200) return 3;
        var row = Enumerable.Range(0, b.Length + 1).ToArray();
        for (int i = 1; i <= a.Length; i++) { int last = row[0]; row[0] = i; for (int j = 1; j <= b.Length; j++) { int old = row[j]; row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), last + (a[i - 1] == b[j - 1] ? 0 : 1)); last = old; } }
        return row[b.Length];
    }
}

public static class AssistantSchemas
{
    private static JsonElement Schema(string item, string array, string fields) => JsonSerializer.SerializeToElement(new {
        type = "object", properties = new Dictionary<string, object> {
            ["summary"] = new { type = "string" }, [array] = new { type = "array", items = new { type = "object",
                properties = fields.Split(',').ToDictionary(k => k, k => (object)(k == "kind" ? new { type = "string", @enum = ActionPolicy.Kinds } : (object)new { type = "string" })),
                required = fields.Split(','), additionalProperties = false } } }, required = new[] { "summary", array }, additionalProperties = false });
    public static readonly JsonElement Agent = Schema("action", "actions", "kind,ref,target,role,value,description,risk");
    public static readonly JsonElement Guide = GuideSchema();
    public static readonly JsonElement Teaching = TeachingSchema();
    private static JsonElement TeachingSchema()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(Guide.GetRawText())!;
        node["properties"]!["steps"]!["maxItems"] = 1;
        node["properties"]!["steps"]!["items"]!["properties"]!["primitive"] = JsonSerializer.SerializeToNode(new { type = "string", @enum = TeachingPolicy.Primitives });
        return JsonSerializer.SerializeToElement(node);
    }
    private static JsonElement GuideSchema()
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(Schema("step", "steps", "instruction,ref,target,role,primitive,expect").GetRawText())!;
        node["properties"]!["steps"]!["items"]!["properties"]!["expect"] = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(new { type = "object", properties = new {
            kind = new { type = "string", @enum = new[] { "manual", "visible", "absent" } }, target = new { type = "string" }, role = new { type = "string" }
        }, required = new[] { "kind", "target", "role" }, additionalProperties = false }));
        return JsonSerializer.SerializeToElement(node);
    }
    public static readonly JsonElement Research = JsonSerializer.SerializeToElement(new { type = "object", properties = new {
        tool = new { type = "string", @enum = new[] { "web.search", "web.fetch", "done" } }, input = new { type = "string" }
    }, required = new[] { "tool", "input" }, additionalProperties = false });
    public static readonly JsonElement Continuation = JsonSerializer.SerializeToElement(new { type = "object", properties = new {
        status = new { type = "string", @enum = new[] { "continue", "done", "clarify" } }, summary = new { type = "string" },
        actions = Agent.GetProperty("properties").GetProperty("actions")
    }, required = new[] { "status", "summary", "actions" }, additionalProperties = false });
}

public static class GuideExpectations
{
    public static bool Matches(GuideExpectation? expected, ScreenContext context) => expected?.Kind switch {
        "visible" => GroundingResolver.Resolve(context.Elements, "", expected.Target, expected.Role) is not null,
        "absent" => expected.Target.Length > 0 && !context.Elements.Any(e => e.Name.Equals(expected.Target, StringComparison.OrdinalIgnoreCase) && (expected.Role.Length == 0 || e.Role.Equals(expected.Role, StringComparison.OrdinalIgnoreCase))),
        _ => false
    };
}
