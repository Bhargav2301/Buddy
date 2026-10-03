namespace Buddy.Windows;

internal static class SpeechReview
{
    internal static bool Required(float confidence, IEnumerable<(string Text, float Confidence)> alternatives)
    {
        if (!float.IsFinite(confidence) || confidence < .80f) return true;
        var distinct = alternatives.Where(a => !string.IsNullOrWhiteSpace(a.Text)).GroupBy(a => a.Text, StringComparer.OrdinalIgnoreCase).Select(g => g.Max(a => a.Confidence)).OrderDescending().Take(2).ToArray();
        return distinct.Length > 1 && distinct[0] - distinct[1] < .12f;
    }
}
