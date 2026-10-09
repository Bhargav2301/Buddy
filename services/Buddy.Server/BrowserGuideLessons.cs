using System.Text.RegularExpressions;

namespace Buddy.Server;

// Authored, bounded browser teaching. Only recognized simple goals use this path;
// arbitrary tasks still use the planner. Labels/roles must match a unique observed
// enabled control. No title, address value, page text, network or action is needed.
// ScreenContext has no ancestry metadata: the desktop host must first qualify
// these controls as browser chrome; a matching webpage label is not that proof.
public static class BrowserGuideLessons
{
    // Provenance hook for host diagnostics: true means the bounded authored local
    // path can answer (including a missing-target explanation), not model inference.
    public static bool IsSupported(PlanningRequest request) => TryCompose(request) is not null;
    // Asking how the user could act is distinct from asking what a control means.
    // This is only an intent gate: TryCompose must still consume the whole goal,
    // including its single supported manual-only qualifier, and the host must
    // independently qualify browser chrome before exposing any pointer.
    public static bool IsProceduralRequest(string query) => Regex.IsMatch(query.TrimStart(),
        @"^(?:please\s+)?(?:(?:explain|show me|teach me|guide me through)\s+how(?:\s+i\s+(?:could|can|should))?(?:\s+to)?|how\s+(?:do|can|should)\s+i)\s+(?:reload|refresh|open|create|use)\b",
        Options, MatchTimeout);
    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private sealed record Topic(string Id, string Title, string Role, string[] Labels, string Function, string Instruction, string Check);
    private static readonly Topic[] Topics = [
        new("reload", "reload the current page", "Button", ["Reload", "Reload this page", "Refresh"],
            "Reload asks the browser to load the current page again in the same tab. It can help with stale or incomplete page content; save unfinished form work first because a reload can reset it.",
            "When you are ready to reload, select \"{0}\" once.",
            "Let loading settle, then check the page for the content you expected. The continued presence of this button does not prove that fresh content loaded."),
        new("tab", "open a separate tab", "Button", ["New tab", "Open a new tab"],
            "A new tab gives you a separate place to browse while keeping the current tab available. It does not duplicate the current page or choose a destination for you.",
            "To open a new tab yourself, select \"{0}\".",
            "Check the tab strip for an additional selected tab and confirm your previous tab is still available. The New tab button remaining visible does not verify that a tab was created."),
        new("address", "use the address and search bar", "Edit", ["Address and search bar", "Search or enter address", "Search with Google or enter address"],
            "The address and search bar accepts a website address or a search query. Selecting it only prepares the field; pressing Enter requests navigation or sends the query to the browser's configured search service.",
            "Select \"{0}\" and enter the destination or query you choose; press Enter only when you intend to navigate or search.",
            "Check the address and displayed page yourself afterward. This lesson has not read the field's value or verified a destination.")
    ];
    private static string? Browser(string? name) => name?.ToLowerInvariant() switch {
        "comet" or "comet browser" => "comet", "chrome" or "google chrome" => "chrome",
        "edge" or "microsoft edge" or "msedge" => "msedge", "firefox" or "mozilla firefox" => "firefox",
        "brave" or "brave browser" => "brave", "opera" or "opera browser" => "opera", _ => null
    };

    public static GuidePlan? TryCompose(PlanningRequest request)
    {
        if (request?.Context?.Elements is null || request.Context.Elements.Count > 400 || request.Query is null || request.Query.Length > 4000 || Browser(request.Context.App) is not { } app) return null;
        var goal = Goal(request.Query, app);
        if (goal is null || GuideSafety.RequestedText(request.Query) is not null) return null;
        var selected = goal == "intro" ? Topics : Topics.Where(t => t.Id == goal).ToArray();
        List<GuideStep> steps = []; List<GuideLesson> lessons = [];
        foreach (var topic in selected) {
            var matches = request.Context.Elements.Where(e => e is not null && IsTarget(e, topic) && GuideSafety.AllowedTarget(e, request.Query)).ToArray();
            if (matches.Length != 1 || request.Context.Elements.Count(e => e is not null && e.Ref == matches[0].Ref) != 1) {
                if (goal != "intro") return new("Learn how to " + topic.Title + "; a unique pointer target is not available in this view.", [], Lessons: [
                    new(topic.Function), new("I cannot uniquely verify the relevant browser control here. Focus the browser toolbar and choose Make plan for a fresh observation. No action has run.")]);
                continue;
            }
            var target = matches[0];
            string instruction = string.Format(System.Globalization.CultureInfo.InvariantCulture, topic.Instruction, target.Name);
            steps.Add(new(instruction, target.Ref, target.Name, target.Role, "ring", new("manual")));
            // The actual lesson UI uses Lessons, not Steps. Give it complementary
            // purpose and action/check sections rather than copying one click twice.
            lessons.Add(new(topic.Function, target.Name, target.Role));
            lessons.Add(new(instruction + " " + topic.Check, target.Name, target.Role));
        }
        if (steps.Count == 0) return null;
        string scope = goal == "intro" ? "Browser basics for the controls exposed in this view" : "Learn how to " + selected[0].Title;
        var plan = new GuidePlan(scope + ". You carry out any action yourself; reviewing this lesson does not verify its outcome.", steps, Lessons: lessons);
        return GuideSafety.Validate(plan, request);
    }

    // Reload/new-tab/address effects cannot be established by the continued presence
    // of a chrome control. The current expectation schema has no page-load/tab-count
    // transition proof, so these steps require a manual result check.
    public static GuideStep ManualExpectation(GuideStep step, PlanningRequest request) =>
        Browser(request.Context.App) is not null && Topics.Any(t => t.Role.Equals(step.Role, StringComparison.OrdinalIgnoreCase) && t.Labels.Contains(step.Target, StringComparer.OrdinalIgnoreCase))
            ? step with { Expect = new("manual") } : step;

    private static bool IsTarget(ScreenElement element, Topic topic) => element.Enabled &&
        element.Ref is { Length: > 0 and <= 200 } && string.Equals(element.Role, topic.Role, StringComparison.OrdinalIgnoreCase) &&
        topic.Labels.Contains(element.Name, StringComparer.OrdinalIgnoreCase) &&
        double.IsFinite(element.X + element.Y + element.Width + element.Height) && element.Width > 0 && element.Height > 0;

    private static string? Goal(string query, string app)
    {
        string text = Regex.Replace(query.Trim().TrimEnd('.', '!', '?'), @"\s+", " ", Options, MatchTimeout).ToLowerInvariant();
        // This exact manual qualifier changes no goal; other suffixes/conditions stay
        // outside the authored grammar instead of being silently discarded.
        text = Regex.Replace(text, @"[;.]\s*do not perform any action$", "", Options, MatchTimeout).Trim();
        var named = Regex.Match(text, @"\s+(?:in|with|using)\s+(?:the\s+)?(comet(?: browser)?|google chrome|chrome|microsoft edge|edge|firefox|brave(?: browser)?|opera(?: browser)?|this browser|browser)$", Options, MatchTimeout);
        if (named.Success) {
            string browser = named.Groups[1].Value;
            if (Browser(browser) is { } requested && requested != app) return null;
            text = text[..named.Index];
        }
        string beforePrefix = text;
        text = Regex.Replace(text, @"^(?:please\s+)?(?:(?:explain|show me|teach me|guide me through)(?: how)?(?: i (?:could|can|should))?(?: to)?|how (?:do|can|should) i)\s+", "", Options, MatchTimeout);
        if (Regex.IsMatch(text, @"^(?:reload|refresh) (?:(?:the|this|my) )?(?:current )?page$|^what does (?:the )?(?:reload|refresh)(?: button)? do$", Options, MatchTimeout)) return "reload";
        if (Regex.IsMatch(text, @"^(?:open|create|use)(?: a)? new tab$|^what does (?:the )?new tab button do$", Options, MatchTimeout)) return "tab";
        if (Regex.IsMatch(text, @"^(?:use |understand )?(?:the )?(?:address and search|address|search) bar$|^what does (?:the )?(?:address and search|address|search) bar do$", Options, MatchTimeout)) return "address";
        string intro = Regex.Replace(text, @"^(?:use|the basics of|basics of|getting started with)\s+", "", Options, MatchTimeout);
        return beforePrefix != text && (Browser(intro) == app || intro is "this browser" or "browser") ? "intro" : null;
    }
}
