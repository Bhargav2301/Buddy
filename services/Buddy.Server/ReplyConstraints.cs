using System.Text.RegularExpressions;

namespace Buddy.Server;

// A narrow explicit-length contract, independent of the model's willingness to
// follow it. Full answers are regenerated; a later qualification is never sliced off.
public sealed record ReplyConstraints(int SentenceLimit, bool Detailed = false)
{
    public const int MaximumDetailedSentences = 12;
    public const int MaximumDetailedCharacters = 6000; // UTF-16 code units, like string.Length.
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);
    private const string Count = @"(?<count>single|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|[0-9]+)";
    public int CharacterLimit => Detailed ? MaximumDetailedCharacters : 1600;

    public static ReplyConstraints FromRequest(string request, bool allowDetailed = true)
    {
        // Only the current request controls length. Quoted text/code/examples are
        // data; separately attached context and previous turns never enter here.
        var instructions = Regex.Replace(request, @"```[\s\S]*?(?:```|$)|~~~[\s\S]*?(?:~~~|$)|`[^`\r\n]*(?:`|$)|(?m:^\s*>[^\r\n]*$)", " ", Options, Timeout);
        instructions = Regex.Replace(instructions, "[\"\u201c][^\"\u201d]*[\"\u201d]|(?<![\\p{L}\\p{N}])'[^'\r\n]*'(?![\\p{L}\\p{N}])", " ", Options, Timeout);
        var matches = Regex.Matches(instructions,
            @"\b(?:in|within|using|use|(?:respond|reply|answer) with|at most|no more than|only|just|exactly|limit (?:it|this|the answer) to|keep (?:it|this|the answer) to)\s+(?:a\s+)?" + Count + @"[ -]+sentences?\b|\b" + Count + @"[ -]+sentence\s+(?:answer|reply|response|explanation|summary)\b|(?:^|[.!?;:]\s*)\s*(?:please\s+)?" + Count + @"\s+sentences?(?:\s+please)?\s*(?:[:.!?]|$)", Options, Timeout);
        int? limit = null;
        string[] words = ["one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "twelve"];
        foreach (Match match in matches) {
            if (Negated(instructions, match.Index)) continue;
            string count = match.Groups["count"].Value.ToLowerInvariant();
            int parsed = count == "single" ? 1 : Array.IndexOf(words, count) + 1;
            if (parsed == 0 && !int.TryParse(count, out parsed)) parsed = int.MaxValue;
            limit = Math.Min(limit ?? int.MaxValue, parsed);
        }
        if (limit is { } requested) {
            if (allowDetailed && (requested < 1 || requested > MaximumDetailedSentences))
                throw new BuddyException("REPLY_LENGTH_UNSUPPORTED", "Ask for one to twelve sentences, or split the explanation into smaller questions.");
            int accepted = Math.Clamp(requested, 1, allowDetailed ? MaximumDetailedSentences : 3);
            return new(accepted, accepted > 3);
        }
        if (!allowDetailed) return new(3);
        // A direct brevity request wins over a vague request for more detail.
        if (Positive(instructions, @"\b(?:keep|make)\s+(?:it|this|the (?:answer|reply|response|explanation))\s+(?:brief|short|concise)\b|\b(?:answer|reply|respond|explain)\s+(?:briefly|concisely)\b|\b(?:give|provide|write)\s+(?:me\s+)?(?:a\s+)?(?:brief|short|concise)\s+(?:answer|reply|response|explanation|summary)\b")) return new(3);
        bool detail = Positive(instructions,
            @"\b(?:explain|describe|discuss|answer|respond|reply)\b[^.!?;\r\n]{0,100}\b(?:in\s+(?:(?:more|greater|full)\s+)?detail|in[- ]depth|thoroughly|comprehensively)\b|" +
            @"\b(?:give|provide|write|want|need|would like)\s+(?:(?:me|us)\s+)?(?:(?:a|an|the)\s+)?(?:more\s+)?(?:detailed|in[- ]depth|thorough|comprehensive|longer|expanded)\s+(?:answer|reply|response|explanation|summary|overview|analysis)\b|" +
            @"(?:^|[.!?;:]\s*)\s*(?:please\s+|(?:can|could|would|will)\s+you\s+(?:please\s+)?)?(?:elaborate(?:\s+on)?|expand\s+(?:on\s+(?:that|this)|(?:the|your|that)\s+(?:answer|explanation))|go\s+(?:deeper|into\s+(?:more\s+)?detail)|tell\s+me\s+more\s+about)\b|(?:^|[.!?;:]\s*)\s*(?:please\s+)?(?:more|greater)\s+detail(?:\s+please)?\s*[.!?]?$" );
        return detail ? new(MaximumDetailedSentences, true) : new(3);
    }

    private static bool Positive(string text, string pattern) => Regex.Matches(text, pattern, Options, Timeout)
        .Cast<Match>().Any(match => !Negated(text, match.Index));
    private static bool Negated(string text, int at) => Regex.IsMatch(text[..at],
        @"\b(?:do not|don['’]t|not|never|avoid|without|no need to)\s+(?:[\p{L}]+\s+){0,4}$", Options, Timeout);

    public string Instruction => "Answer the user's question in at most " +
        (SentenceLimit switch { 1 => "one sentence", 2 => "two sentences", 3 => "three sentences", _ => SentenceLimit + " sentences" }) +
        (Detailed ? " and 6,000 UTF-16 code units. The user explicitly requested detail. Aim for " + Math.Min(SentenceLimit, 6) + " complete sentences, using the remaining budget only for necessary explanation or qualifications. Start directly with the explanation; omit a greeting, preamble and unnecessary follow-up question. Do not pad the answer to reach the maximum." : ".");

    public bool Accepts(string answer, string request) => !string.IsNullOrWhiteSpace(answer) &&
        ConversationalReply.Sentences(answer) <= Math.Clamp(SentenceLimit, 1, Detailed ? MaximumDetailedSentences : 3) &&
        answer.Length <= CharacterLimit && !HasUnrequestedPromotion(answer, request) && !HasSourceLabel(answer);

    // Model-generated citation labels are not retrieval provenance. The app renders
    // verified links from its separate evidence object; regenerate the whole answer.
    public static bool HasSourceLabel(string answer) => Regex.IsMatch(answer,
        @"(?:^|[.!?]\s+|[(\[])\s*(?:sources?|references?|citations?)\s*:",
        Options, TimeSpan.FromMilliseconds(100));

    public static bool HasUnrequestedPromotion(string answer, string request)
    {
        bool asked = Regex.IsMatch(request,
            @"\b(?:guide|agent)\s+(?:mode|button|feature|plan)\b|\bwhat can (?:you|buddy) do\b|\b(?:buddy|your)\s+(?:features|capabilities)\b",
            Options, TimeSpan.FromMilliseconds(100));
        return !asked && Regex.IsMatch(answer,
            @"\b(?:open|use|try|choose|click|select)\s+(?:(?:the|my|our)\s+)?(?:Guide|Agent)\b|\b(?:Guide|Agent)\s+(?:button|mode|features?)\b|\b(?:I|Buddy)\s+can\b.{0,100}\b(?:Guide|Agent)\b",
            Options, TimeSpan.FromMilliseconds(100));
    }
}
