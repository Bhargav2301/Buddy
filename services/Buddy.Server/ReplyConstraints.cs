using System.Text.RegularExpressions;

namespace Buddy.Server;

// A narrow explicit-length contract, independent of the model's willingness to
// follow it. Full answers are regenerated; a later qualification is never sliced off.
public sealed record ReplyConstraints(int SentenceLimit)
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    public static ReplyConstraints FromRequest(string request)
    {
        // Quoted examples are data, not reply-length instructions.
        var instructions = Regex.Replace(request, "[\"\u201c][^\"\u201d\r\n]*[\"\u201d]|(?<![\\p{L}\\p{N}])'[^'\r\n]*'(?![\\p{L}\\p{N}])", " ", Options, TimeSpan.FromMilliseconds(100));
        var matches = Regex.Matches(instructions,
            @"\b(?:in|within|using|use|(?:respond|reply|answer) with|at most|no more than|only|just|exactly|limit (?:it|this|the answer) to|keep (?:it|this|the answer) to)\s+(?:a\s+)?(?<count>one|1|single|two|2)[ -]+sentences?\b|\b(?<count>one|1|single|two|2)[ -]+sentence\s+(?:answer|reply|response|explanation|summary)\b|(?:^|[.!?;:]\s*)\s*(?:please\s+)?(?<count>one|1|single|two|2)\s+sentences?(?:\s+please)?\s*(?:[:.!?]|$)",
            Options, TimeSpan.FromMilliseconds(100));
        int limit = 3;
        foreach (Match match in matches) limit = Math.Min(limit, match.Groups["count"].Value.ToLowerInvariant() is "two" or "2" ? 2 : 1);
        return new(limit);
    }

    public string Instruction => "Answer the user's question in at most " +
        (SentenceLimit switch { 1 => "one sentence", 2 => "two sentences", _ => "three sentences" }) + ".";

    public bool Accepts(string answer, string request) => !string.IsNullOrWhiteSpace(answer) &&
        ConversationalReply.IsConcise(answer, SentenceLimit) && !HasUnrequestedPromotion(answer, request) && !HasSourceLabel(answer);

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
