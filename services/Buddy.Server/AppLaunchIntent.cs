using System.Text.RegularExpressions;

namespace Buddy.Server;

// This is a bounded request grammar, not a model classification. A missing or
// unknown destination cannot authorize a replacement application or website.
public sealed record AppLaunchRequest(string Destination, string RequestedName, bool IsApp, bool Supported);

public static class AppLaunchIntent
{
    private static readonly Dictionary<string, string> Names = new(StringComparer.OrdinalIgnoreCase) {
        ["notepad"] = "notepad", ["notepad app"] = "notepad", ["calculator"] = "calculator", ["calculator app"] = "calculator",
        ["explorer"] = "explorer", ["file explorer"] = "explorer", ["comet"] = "comet", ["comet browser"] = "comet",
        ["camera"] = "camera", ["camera app"] = "camera", ["windows camera"] = "camera", ["windows camera app"] = "camera",
        ["spotify"] = "spotify", ["spotify app"] = "spotify"
    };
    public static string? CanonicalApp(string name) => Names.TryGetValue(name.Trim(), out var alias) ? alias : null;

    public static AppLaunchRequest? Exact(string? query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length > 2200 || query.Any(char.IsControl)) return null;
        string text = query.Trim();
        if (text.StartsWith("please ", StringComparison.OrdinalIgnoreCase)) text = text[7..];
        var prefix = new[] { "open ", "launch ", "start " }.FirstOrDefault(p => text.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        if (prefix is null) return null;
        string name = text[prefix.Length..].Trim();
        // URLs keep their exact path/query/fragment: punctuation may be data.
        if (name.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !name.Any(char.IsWhiteSpace)) {
            try { return new(WebResearch.ValidateUrl(name).AbsoluteUri, name, false, true); }
            catch (BuddyException) { return new("", name, false, false); }
        }
        name = name.TrimEnd('.', '!').TrimEnd();
        if (CanonicalApp(name) is { } alias) return new(alias, name, true, true);
        if (name.Length is < 1 or > 100 || !Regex.IsMatch(name, @"^[\p{L}\p{N}][\p{L}\p{N} ._-]*$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) return null;
        if (Regex.IsMatch(name, @"\b(and|then|using|with|to|in|on|after|before|please)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) return null;
        return new("", name, true, false);
    }

    public static AssistantPlan Bind(string query, AssistantPlan validated)
    {
        Security.Text(query, 4000, "Task");
        if (validated.Actions is not { Count: > 0 }) return validated;
        var exact = Exact(query);
        if (exact is not null) {
            if (!exact.Supported || validated.Actions.Count != 1 || validated.Actions[0] is not { Kind: "open" } action ||
                !action.Value.Equals(exact.Destination, StringComparison.Ordinal))
                throw Mismatch();
            // Model-authored prose cannot mislabel a correctly bound action card.
            string label = exact.IsApp ? DisplayName(exact.Destination) : exact.Destination;
            return validated with { Summary = "Open " + label + " after approval; no action has run.",
                Actions = [action with { Description = "Open " + label + "." }] };
        }
        foreach (var action in validated.Actions.Where(a => a.Kind == "open")) {
            if (Regex.IsMatch(query, @"\b(do not|don't|never|without|instead of|rather than|not)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100))) throw Mismatch();
            if (ActionPolicy.Apps.Contains(action.Value)) {
                // Broader tasks retain review, but a launch requires an explicit
                // app name in the task. Ambiguous/negated requests fail closed.
                var matching = Names.Where(pair => pair.Value == action.Value).Select(pair => pair.Key);
                if (!matching.Any(name => Regex.IsMatch(query, @"(?<![\p{L}\p{N}_])" + Regex.Escape(name) + @"(?![\p{L}\p{N}_])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))) throw Mismatch();
            } else {
                var urls = Regex.Matches(query, @"https://[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                if (!urls.Any(match => SamePublicUrl(match.Value, action.Value))) throw Mismatch();
            }
        }
        return validated;
    }
    private static bool SamePublicUrl(string requested, string planned)
    {
        try { return WebResearch.ValidateUrl(requested).AbsoluteUri.Equals(planned, StringComparison.Ordinal); }
        catch (BuddyException) { return false; }
    }
    public static string DisplayName(string alias) => alias switch { "notepad" => "Notepad", "calculator" => "Calculator", "explorer" => "File Explorer", "comet" => "Comet Browser", "camera" => "Camera", "spotify" => "Spotify", _ => alias };
    private static BuddyException Mismatch() => new("INVALID_PLAN", "The proposed launch does not match the app or exact address you requested. No action ran; request one app by name.");
}
