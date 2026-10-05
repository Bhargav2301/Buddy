using System.Text.RegularExpressions;

namespace Buddy.Server;

// A pure, closed catalog. No model, web, imported notes, actions or pointer targets.
// Full query consumption matters: extra goals/conditions cannot be stripped away
// to turn an unsupported workflow or current-state question into a catalog answer.
public static class KnownControlConcepts
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);
    private const string Name = @"(?:reload|refresh|zoom|mute|word\s+wrap|publish)";
    private const string Subject = @"(?:the |this )?(?:""(?<name>" + Name + @")""|'(?<name>" + Name + @")'|(?<name>" + Name + @"))(?: (?<role>button|slider|checkbox|check box|control|option|setting|menu item))?";
    private static readonly string[] Questions = [
        @"(?:explain|teach me|help me understand) what " + Subject + @" (?:means|does|is for|may do)",
        @"what does " + Subject + @" (?:do|mean)",
        @"what is (?:the (?:meaning|purpose|function) of )?" + Subject + @"(?: for)?",
        @"how does " + Subject + @" work",
        @"(?:explain|describe|help me understand|teach me about|teach me the meaning of|teach me the purpose of) (?:the (?:meaning|purpose|function) of )?" + Subject
    ];
    private static readonly string[] CommonQualifiers = [
        @"(?:without (?:changing|modifying)|(?:do not|don't|never) (?:change|modify)) (?:anything|(?:the |my )?document(?: content)?|(?:stored )?text|(?:stored )?line breaks)",
        @"(?:do not|don't|never) (?:perform any action|act|click|toggle|press|drag|type|change anything)",
        @"(?:do not|don't|never) assume (?:its checked state|whether it is currently checked)(?: or change anything)?",
        @"(?:how to |a |the )?(?:check|verify|compare) (?:the |its )?(?:effect|result|displayed result)(?: manually)?",
        @"(?:include |including )?(?:its |the )?(?:purpose|meaning|function|caution|limitations?|a manual check|a read-only check)",
        @"(?:without acting|without making changes|without changing anything)"
    ];
    private sealed record Topic(string Id, string[] Names, string[] Roles, ConceptualExplanation Text, string[] References);
    private static readonly Topic[] Topics = [
        new("reload", ["reload", "refresh"], ["button"], new(
            "Reload commonly requests the current browser page again.",
            "Unsaved page work may be lost, and this label establishes neither protection nor a successful reload.",
            "Compare the already displayed page and any visible loading indicator with the intended page and expected content, without reloading."), ["reload", "unsaved"]),
        new("zoom", ["zoom"], ["slider"], new(
            "Zoom commonly changes display magnification.",
            "The label does not establish the current factor, affected surface or any change to stored document content.",
            "Compare the displayed size with an available reference while checking that the document text remains the same, without adjusting the slider."), ["zoom"]),
        new("mute", ["mute"], ["checkbox"], new(
            "Mute commonly silences audio within a particular scope.",
            "Neither the current checked state nor whether it affects input, output or one app is established by this label.",
            "Review the documented audio scope and any already visible state indicator to distinguish microphone, speaker or app effects without toggling the setting."), ["mute", "audio-direction"]),
        new("wrap", ["word wrap"], ["checkbox", "menuitem"], new(
            "Word wrap commonly fits displayed text to the available width by moving its visual line endings.",
            "The label does not establish its current state or stored line breaks, and a separate hard-wrap command may edit text.",
            "Compare visible line endings with any already displayed explicit line-break markers to distinguish display wrapping from stored breaks, without editing the text."), ["wrap"])
    ];

    public static GuidePlan? TryExplain(PlanningRequest request)
    {
        if (request?.Context?.Elements is null || request.Context.App is null || request.UseWeb || request.Query is not { Length: > 0 and <= 1000 } || request.Context.Elements.Count > 400)
            return null;
        // The grammar is English-only. Do not silently consume a language request.
        string query = Space(request.Query).Trim();
        Match? intent = null;
        foreach (string form in Questions) {
            var candidate = Regex.Match(query, @"^(?:please )?" + form + @"(?=$|[\s.,;?!])(?<tail>.*)$", Options, Timeout);
            if (candidate.Success) { intent = candidate; break; }
        }
        if (intent is null) return null;
        string name = Space(intent.Groups["name"].Value).ToLowerInvariant();
        var topic = Topics.SingleOrDefault(t => t.Names.Contains(name));
        if (topic is null || !AcceptTail(intent.Groups["tail"].Value, topic.Id, request.Context.App)) return null;
        if (topic.Id == "reload" && !IsBrowser(request.Context.App))
            return Clarify("The Reload meaning needs app context.", "A Reload label alone does not establish that this is a browser page or identify the affected content; which app and documented scope should we examine?");
        string role = Space(intent.Groups["role"].Value).Replace(" ", "").ToLowerInvariant();
        if (role.Length > 0 && role is not ("control" or "option" or "setting") && !topic.Roles.Contains(role)) return null;
        if (GuideSafety.RequestedApp(request.Query) is { } app && !SameApp(app, request.Context.App)) return null;
        // Count aliases and disabled copies, not only the convenient enabled one.
        // Ref and geometry never choose the text; valid geometry is evidence admission.
        var controls = request.Context.Elements.Where(e => e?.Name is { Length: > 0 and <= 200 } && topic.Names.Contains(Space(e.Name).ToLowerInvariant())).ToArray();
        if (controls.Length != 1) return Clarify(controls.Length == 0 ? "The requested control is not established in this view." : "The requested control is ambiguous in this view.",
            "I cannot uniquely establish the requested control from the current observation; which control and role should a fresh view show?");
        var control = controls[0];
        if (!control.Enabled) return Clarify("The requested control is disabled.", "The requested control is disabled; this observation does not establish a prerequisite or permission to change it.");
        if (control.Role is not { Length: > 0 and <= 80 } || !topic.Roles.Contains(Space(control.Role).Replace(" ", "").ToLowerInvariant()) || !GuideSafety.AllowedTarget(control, request.Query) ||
            !double.IsFinite(control.X) || !double.IsFinite(control.Y) || !double.IsFinite(control.Width) || !double.IsFinite(control.Height) || control.Width <= 0 || control.Height <= 0)
            return null;
        var plan = GuideLessons.ComposeExplanation(topic.Text, request);
        return plan with { Summary = ConceptReferences.Origin, Sources = ConceptReferences.For(topic.References) };
    }

    public static GuidePlan? PublishClarification(PlanningRequest request)
    {
        if (request.UseWeb || !GuideLessons.IsExplanationOnly(request.Query)) return null;
        string query = Space(request.Query);
        bool named = Questions.Any(form => {
            var match = Regex.Match(query, @"^(?:please )?" + form + @"(?=$|[\s.,;?!])", Options, Timeout);
            return match.Success && match.Groups["name"].Value.Equals("publish", StringComparison.OrdinalIgnoreCase);
        });
        if (!named) return null;
        return Clarify("Publishing scope needs clarification.", "I cannot establish what Publish does or who can access the result in this app. Any publishing decision requires removal of private content and review of the intended audience and access permissions. Which app and documented publishing destination should we examine?");
    }

    private static GuidePlan Clarify(string heading, string text) => new(heading, [], Lessons: [new(text)]);
    private static string Space(string text) => Regex.Replace(text, @"\s+", " ", Options, Timeout).Trim();
    private static bool IsBrowser(string app) => new[] { "comet", "comet browser", "chrome", "google chrome", "msedge", "edge", "microsoft edge", "firefox", "mozilla firefox", "brave", "brave browser", "opera", "opera browser" }.Contains(app.Trim().ToLowerInvariant());
    private static bool SameApp(string requested, string current)
    {
        static string Alias(string s) => s.Trim().ToLowerInvariant() switch {
            "comet browser" => "comet", "google chrome" => "chrome", "microsoft edge" or "edge" => "msedge", "mozilla firefox" => "firefox", var other => other
        };
        return Alias(requested) == Alias(current);
    }
    private static bool AcceptTail(string tail, string topic, string app)
    {
        // Every clause must be a supported, fully consumed qualifier. No token bag,
        // substring accept, generic instruction stripping or whole-fixture matching.
        string remaining = tail.Trim();
        for (int i = 0; i < 12; i++) {
            remaining = Regex.Replace(remaining, @"^(?:\s|[.,;?!]|\band\b|\bbut\b)+", "", Options, Timeout);
            if (remaining.Length == 0) return true;
            var appClause = Regex.Match(remaining, @"^in (?:the )?(?<app>this app|this browser|browser|comet(?: browser)?|google chrome|chrome|microsoft edge|edge|msedge|mozilla firefox|firefox|notepad)(?=$|[.,;?!])", Options, Timeout);
            if (appClause.Success) {
                string requested = appClause.Groups["app"].Value.ToLowerInvariant();
                if (requested is "this browser" or "browser" && !IsBrowser(app)) return false;
                if (requested is not ("this app" or "this browser" or "browser") && !SameApp(requested, app)) return false;
                remaining = remaining[appClause.Length..]; continue;
            }
            var qualifiers = CommonQualifiers.AsEnumerable();
            if (topic == "reload") qualifiers = qualifiers.Append(@"(?:include|including|with) (?:the )?(?:risk (?:to|of losing) |caution about )?unsaved (?:page |form )?(?:work|content)");
            if (topic == "mute") qualifiers = qualifiers.Append(@"without assuming (?:its |the )?(?:audio scope|channel|checked state)");
            if (topic == "wrap") qualifiers = qualifiers.Append(@"(?:including |and )?(?:the difference between )?(?:display wrapping|soft wrapping) (?:and|versus) (?:stored|inserted|hard) line breaks");
            bool consumed = false;
            foreach (string qualifier in qualifiers) {
                var match = Regex.Match(remaining, "^(?:" + qualifier + @")(?=$|[\s.,;?!])", Options, Timeout);
                if (!match.Success) continue;
                remaining = remaining[match.Length..]; consumed = true; break;
            }
            if (!consumed) return false;
        }
        return false;
    }
}
