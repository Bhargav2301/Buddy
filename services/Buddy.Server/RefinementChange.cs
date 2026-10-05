using System.Text;

namespace Buddy.Server;

/// <summary>Conservative wording-change detection; intent preservation still needs the refinement gates.</summary>
public static class RefinementChange
{
    public const string NoChangeMessage = "No refinement was produced. Your original prompt is unchanged.";
    public const string EchoMessage = "The local model returned only formatting changes or the original wording after two attempts. Your original prompt is unchanged.";
    public const string NoImprovementMessage = "The local check could not establish a useful wording improvement. Your original prompt is unchanged.";

    // A period, capital letter or whitespace change alone is not a wording refinement.
    // Compare the ordered remaining Unicode characters without changing the proposal.
    // This conservative filter is not a semantic or quality proof. It cannot approve
    // joined/split words, altered literals or code: the separate fidelity gate owns that.
    // Call this on the final assembled proposal, including reviewed supporting blocks.
    public static bool HasMeaningfulChange(string original, string proposed)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(proposed);
        return !Wording(original).SequenceEqual(Wording(proposed));
    }

    private static IEnumerable<Rune> Wording(string text) => text.EnumerateRunes()
        .Where(r => !Rune.IsWhiteSpace(r) && !Rune.IsPunctuation(r)).Select(Rune.ToLowerInvariant);
}
