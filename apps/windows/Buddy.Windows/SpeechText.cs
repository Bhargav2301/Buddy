using Buddy.Server;
using System.Net;
using System.Text.RegularExpressions;

namespace Buddy.Windows;

internal static class SpeechText
{
    internal static string Prepare(string value)
    {
        value = ConversationalReply.PlainText(WebUtility.HtmlDecode(value));
        value = Regex.Replace(value, @"\bJ\.?A\.?R\.?V\.?I\.?S\.?", "Jarvis", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\bCtrl\b", "Control", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\bWin\s*\+", "Windows plus ", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"\b(Control|Alt|Shift)\s*\+\s*", "$1 plus ", RegexOptions.IgnoreCase);
        // Treat model-generated phoneme markup as ordinary words, never engine instructions.
        return value.Replace("[[", "").Replace("]]", "");
    }
}
