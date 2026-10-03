using System.Text.RegularExpressions;

namespace Buddy.Server;

public static class ConversationalReply
{
    public const string Policy = "Use a calm, composed, warm conversational voice. Give at most three concise sentences. Put safety-critical qualifications first. Do not imitate a specific actor. Avoid Markdown, lists, raw URLs and decorative symbols in ordinary chat; sources are displayed separately. Detailed action plans and approval reviews are separate and must retain every action and risk.";

    public static string PlainText(string value)
    {
        value = Regex.Replace(value, @"(?is)\[(?:circle|arrow|underline|label)\b[^\]]*\]|<(?:circle|arrow|underline|label)\b[^>]*>", " ", RegexOptions.None, TimeSpan.FromMilliseconds(100));
        value = Regex.Replace(value, @"</?[a-zA-Z][^>]{0,500}>", "", RegexOptions.None, TimeSpan.FromMilliseconds(100));
        value = Regex.Replace(value, @"```[\s\S]*?```", " Ask for a separate code review before running code. ");
        value = Regex.Replace(value, @"!?\[([^\]]*)\]\([^)]*\)", "$1");
        value = Regex.Replace(value, @"https?://\S+", "");
        value = Regex.Replace(value, @"(?m)^\s*(?:#{1,6}\s*|>\s*|[-*+]\s+|\d+[.)]\s+)", "");
        value = Regex.Replace(value, @"\[\d+\]", "");
        value = value.Replace("**", "").Replace("__", "").Replace("`", "").Replace("*", "").Replace("#", "");
        return Regex.Replace(value, @"\s+", " ").Trim();
    }

    // Do not truncate an overlong answer: a later sentence may qualify a dangerous instruction.
    // The caller regenerates once, then uses this safe short fallback if the model still disobeys.
    public static bool IsConcise(string value) => Sentences(value) <= 3 && value.Length <= 1600;
    public static int Sentences(string value) => Regex.Matches(value.Trim(), @"[.!?]+(?:[\""'”’)]*)(?=\s|$)|[。！？]").Count
        + (value.Length > 0 && !Regex.IsMatch(value.Trim(), @"[.!?。！？][\""'”’)]*$") ? 1 : 0);
    public const string Fallback = "I couldn't compose a reliable short answer. Please narrow the question; for an action, open Guide or review the full Agent plan before approving.";
}
