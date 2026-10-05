using System.Text;
using System.Text.RegularExpressions;

namespace Buddy.Windows;

internal static class AssistantIntent
{
    internal static string Mode(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        var polite = t;
        foreach (var prefix in new[] { "please ", "can you ", "could you ", "would you " })
            if (polite.StartsWith(prefix, StringComparison.Ordinal)) { polite = polite[prefix.Length..].TrimStart(); break; }
        if (polite.StartsWith("please ", StringComparison.Ordinal)) polite = polite[7..].TrimStart();
        if (new[] { "teach ", "help me learn ", "guide ", "show me ", "walk me ", "open " }.Any(prefix => polite.StartsWith(prefix, StringComparison.Ordinal))) t = polite;
        if(Buddy.Server.WorkflowIntent.NeedsSpecialists(text))return "agent";
        if(t.StartsWith("search my app notes ")) return "knowledge";
        if(t.StartsWith("start an agent ") || t.StartsWith("spawn an agent ") || t.StartsWith("start agent "))return "agent";
        if (t.StartsWith("buddy agent") || t.StartsWith("do it") || t.StartsWith("do this") || t.StartsWith("open ") || t.StartsWith("click ") || t.StartsWith("type ")) return "agent";
        if (t.StartsWith("teach ") || t.StartsWith("help me learn ") || t.StartsWith("guide ") || t.StartsWith("show me ") || t.StartsWith("walk me ")) return "guide";
        return "talk";
    }
    internal static string KnowledgeQuery(string text)=>text.Trim()["search my app notes ".Length..].Trim();
    internal static string PlanningMode(string currentMode, string text)
    {
        var intent = Mode(text);
        // Teaching plus a requested action still needs the Agent plan and its approvals.
        bool mixed = intent == "guide" && Regex.IsMatch(text,
            @"\b(and|then|also)\s+(?:(?:please|you)\s+)*(open|click|type|insert|write|create|do|perform|delete|send|submit|save|set up)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        return mixed || intent == "agent" ? "agent" : intent == "guide" ? "guide" : currentMode == "agent" ? "agent" : "guide";
    }
    internal static string ActionQuery(string text) { foreach(var prefix in new[]{"start an agent to ","spawn an agent to ","start agent to ","buddy agent "})if(text.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))return text[prefix.Length..];return text; }
}
internal sealed class SentenceBuffer
{
    private readonly StringBuilder buffer = new();
    internal IEnumerable<string> Add(string text)
    {
        buffer.Append(text); var result = new List<string>();
        while (true) { var s = buffer.ToString(); int end = -1; for (int i = 0; i < s.Length - 1; i++) if ((s[i] is '.' or '!' or '?' or '\n') && char.IsWhiteSpace(s[i+1])) { end = i+1; break; }
            if (end < 0) { if (s.Length < 400) break; end = s.LastIndexOf(' ', 380); if (end < 1) end = 380; }
            var sentence = s[..end].Trim(); buffer.Remove(0, end); if (sentence.Length > 0) result.Add(sentence);
        }
        return result;
    }
    internal IEnumerable<string> Flush() { var text = buffer.ToString().Trim(); buffer.Clear(); return text.Length == 0 ? [] : [text]; }
    internal void Clear() => buffer.Clear();
}
internal sealed class HoldGesture
{
    private long? down;
    internal bool Held { get; private set; }
    internal void Down(long time) { down ??= time; }
    internal bool Tick(long time) { if (down is null || Held || time - down < 250) return false; Held = true; return true; }
    internal bool Up() { bool tap = down is not null && !Held; down = null; Held = false; return tap; }
}
