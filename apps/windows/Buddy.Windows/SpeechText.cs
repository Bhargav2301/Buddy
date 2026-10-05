using Buddy.Server;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Buddy.Windows;

internal static class SpeechText
{
    internal const int MaximumCharacters = 1600;
    internal const string SourceCue = "Sources are attached; ";
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);
    // This is speech presentation, not URL validation or retrieval. A raw address
    // never establishes provenance or enables the source cue.
    private const string Address = @"(?<![\p{L}\p{N}_])(?:(?:https?|ftp|file)\s*:\s*(?:/\s*){2}|www\.)[^\s<>\[\]""']+|(?<![\p{L}\p{N}_])(?:[\p{L}\p{N}](?:[\p{L}\p{N}-]*[\p{L}\p{N}])?\.)+(?:com|org|net|edu|gov|mil|io|ai|dev|app|co|uk|de|fr|jp|ca|au|info|biz|me|us|eu|in|tv|xyz|online|site|tech|cloud|store|museum|example|test|invalid|local)(?![\p{L}\p{N}_-])(?::\d+)?(?:[/#?][^\s<>\[\]""']*)?";

    internal static string Prepare(string value, bool sourcesAttached = false)
    {
        if (value.Length > 20000) throw new InvalidOperationException("This text is too long to read aloud. Review the full answer on screen.");
        value = WebUtility.HtmlDecode(value);
        value = Replace(value, @"[\u200B-\u200F\u202A-\u202E\u2060-\u2069\uFEFF\u00AD]", "");
        // Reference definitions are metadata only when the entire line is a
        // destination and optional title. Do not remove prose after a URL.
        value = Replace(value, @"(?m)^\s*\[[^\]\r\n]{1,80}\]:\s*<?(?:https?://|www\.)[^\s>]+>?(?:\s+""[^""\r\n]*"")?\s*$", "");
        value = Replace(value, @"!?\[([^\]\r\n]+)\]\[[^\]\r\n]*\]", "$1");
        value = Replace(value, @"\[(?:\d+(?:\s*[,;\u2013-]\s*\d+)*|\^[-\w]+|(?:citation|cite)\s*:?\s*\d+)\]|\u3010[^\u3011\r\n]*[\u2020\u2021][^\u3011\r\n]*\u3011|\uE200[^\uE201\r\n]*\uE201", "");
        value = ConversationalReply.PlainText(value);
        // Labels that are themselves addresses survive Markdown label extraction.
        // Keep terminal punctuation, so removing an address never joins two cautions.
        value = Regex.Replace(value, Address, m => {
            int end = m.Value.Length;
            while (end > 0 && m.Value[end - 1] is '.' or ',' or ';' or ':' or '!' or '?') end--;
            return m.Value[end..];
        }, Options, Timeout);
        // Model-authored claims of an attachment are not evidence. The host adds
        // this acknowledgement once, only after displaying actual source records.
        value = Replace(value, @"(?:^|(?<=[.!?;])\s+)(?:the\s+)?sources?\s+(?:are|is)\s+attached(?:\s*[.;:!?]\s*|$)", " ");
        value = Replace(value, @"\[(?:sources?|references?|citations?)\s*:\s*([^\]]*)\]", "$1");
        value = Replace(value, @"\bJ\.?A\.?R\.?V\.?I\.?S\.?", "Jarvis");
        value = Replace(value, @"\bCtrl\b", "Control");
        value = Replace(value, @"\bWin\s*\+", "Windows plus ");
        value = Replace(value, @"\b(Control|Alt|Shift)\s*\+\s*", "$1 plus ");
        // Treat model-generated phoneme markup as ordinary words, never engine instructions.
        value = value.Replace("[[", "").Replace("]]", "");
        value = Replace(value, @"[\[\]]", "");
        value = Replace(value, @"\s+([,.;:!?])", "$1");
        value = Replace(value, @"\s+", " ").Trim();
        if (!value.Any(char.IsLetterOrDigit)) return "";
        return sourcesAttached ? SourceCue + value : value;
    }

    private static string Replace(string value, string pattern, string replacement) => Regex.Replace(value, pattern, replacement, Options, Timeout);

    // Keep a complete reviewed answer in one synthesis request. The local Piper
    // worker supplies its own inter-sentence pauses; separate WAVs omit them.
    internal static string[] CompleteAnswer(string value, bool sourcesAttached = false)
    {
        var text = Prepare(value, sourcesAttached);
        if (text.Length > MaximumCharacters) throw new InvalidOperationException("This text is too long to read aloud. Review the full answer on screen.");
        return text.Length == 0 ? [] : [text];
    }

    internal static async IAsyncEnumerable<string> PrepareSentences(IAsyncEnumerable<string> values,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var value in values.WithCancellation(ct)) {
            ct.ThrowIfCancellationRequested();
            var text = Prepare(value);
            if (text.Length is 0 or > MaximumCharacters) throw new InvalidOperationException("Sentence speech review failed; speech stopped.");
            yield return text;
        }
    }
}
