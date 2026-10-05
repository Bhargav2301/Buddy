using System.Text.RegularExpressions;

namespace Buddy.Server;

public static class ConversationalReply
{
    private static readonly TimeSpan TextTimeout = TimeSpan.FromMilliseconds(100);
    public const string Policy = "Use a calm, composed, warm conversational voice. Give at most three concise sentences, honoring an explicitly requested tighter limit. Put safety-critical qualifications first. If essential information is missing, ask for it instead of inventing details or requirements. Answer directly without unsolicited feature offers or follow-up tasks. Do not imitate a specific actor. Use plain conversational text. Do not append source labels, citations, links, lists or decorative symbols: the application separately attaches verified evidence from retrieval. Detailed action plans and approval reviews are separate and must retain every action and risk.";

    public static string PlainText(string value)
    {
        // Remove only complete Markdown reference-definition lines before newlines
        // are flattened. Source labels in prose remain visible to reply validation.
        value = Regex.Replace(value, @"(?m)^\s*\[[^\]\r\n]{1,80}\]:\s*<?(?:https?://|www\.)[^\s>]+>?(?:\s+""[^""\r\n]*"")?\s*$", "", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TextTimeout);
        value = Regex.Replace(value, @"(?is)\[(?:circle|arrow|underline|label)\b[^\]]*\]|<(?:circle|arrow|underline|label)\b[^>]*>", " ", RegexOptions.None, TimeSpan.FromMilliseconds(100));
        value = Regex.Replace(value, @"</?[a-zA-Z][^>]{0,500}>", "", RegexOptions.None, TimeSpan.FromMilliseconds(100));
        value = Regex.Replace(value, @"```[\s\S]*?```", " Ask for a separate code review before running code. ");
        value = LinkLabels(value);
        value = Regex.Replace(value, @"https?://[^\s<>]+", m => TrailingPunctuation(m.Value), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TextTimeout);
        value = Regex.Replace(value, @"(?m)^\s*(?:#{1,6}\s*|>\s*|[-*+]\s+|\d+[.)]\s+)", "");
        value = Regex.Replace(value, @"\[\d+\]", "");
        value = value.Replace("**", "").Replace("__", "").Replace("`", "").Replace("*", "").Replace("#", "");
        return Regex.Replace(value, @"\s+", " ").Trim();
    }

    // Keep descriptive labels (including cautions) while removing destinations.
    // Balanced parentheses in a URL must not leave a suffix for speech to read.
    private static string LinkLabels(string value)
    {
        var result = new System.Text.StringBuilder(value.Length);
        for (int at = 0; at < value.Length; at++) {
            int labelStart = value[at] == '!' && at + 1 < value.Length && value[at + 1] == '[' ? at + 1 : at;
            if (value[labelStart] == '[') {
                int labelEnd = value.IndexOf(']', labelStart + 1);
                if (labelEnd < 0) { result.Append(value.AsSpan(at)); break; }
                if (labelEnd >= 0 && labelEnd + 1 < value.Length && value[labelEnd + 1] == '(') {
                    int depth = 1, end = labelEnd + 2;
                    for (; end < value.Length && depth > 0; end++) {
                        if (value[end] == '\\' && end + 1 < value.Length) { end++; continue; }
                        if (value[end] == '(') depth++;
                        if (value[end] == ')') depth--;
                    }
                    if (depth == 0) { result.Append(value.AsSpan(labelStart + 1, labelEnd - labelStart - 1)); at = end - 1; continue; }
                    result.Append(value.AsSpan(at)); break;
                }
            }
            result.Append(value[at]);
        }
        return result.ToString();
    }

    private static string TrailingPunctuation(string text)
    {
        int start = text.Length;
        while (start > 0 && text[start - 1] is '.' or ',' or ';' or ':' or '!' or '?') start--;
        return text[start..];
    }

    // Do not truncate an overlong answer: a later sentence may qualify a dangerous instruction.
    // The caller regenerates once, then uses this safe short fallback if the model still disobeys.
    public static bool IsConcise(string value, int maximumSentences = 3) => Sentences(value) <= Math.Clamp(maximumSentences, 1, 3) && value.Length <= 1600;
    public static int Sentences(string value) => Regex.Matches(value.Trim(), @"[.!?]+(?:[\""'”’)]*)(?=\s|$)|[。！？]").Count
        + (value.Length > 0 && !Regex.IsMatch(value.Trim(), @"[.!?。！？][\""'”’)]*$") ? 1 : 0);
    public const string Fallback = "I couldn't compose a reliable brief answer; which part should I focus on?";
}
