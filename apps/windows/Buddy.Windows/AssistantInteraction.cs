using System.Text;

namespace Buddy.Windows;

internal static class AssistantIntent
{
    internal static string Mode(string text)
    {
        var t = text.Trim().ToLowerInvariant();
        if (t.StartsWith("buddy agent") || t.StartsWith("do it") || t.StartsWith("do this") || t.StartsWith("open ") || t.StartsWith("click ") || t.StartsWith("type ")) return "agent";
        if (t.StartsWith("guide ") || t.StartsWith("show me ") || t.StartsWith("walk me ")) return "guide";
        return "talk";
    }
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
