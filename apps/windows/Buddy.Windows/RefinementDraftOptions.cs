using Buddy.Server;
using System.Globalization;
using System.Text.Json;

namespace Buddy.Windows;

// One window's explicitly supplied data. Never persisted; external use requires a
// copied, one-use ExternalRefinementOptionsSlot followed by a fresh field capture.
internal sealed record RefinementDraftOptions(string Technique, string Domain, bool Important, bool Revision,
    string Tools, string Stages, string Constraints, IReadOnlyList<RefinementExample> Examples,
    IReadOnlyList<RefinementContextSource> References, bool HasBudget, string Destination, string Limit, string Unit)
{
    internal RefineRequest ToRequest(string original, string mode)
    {
        static List<string> Lines(string text, int maximum, string name)
        {
            var values = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).ToList();
            if (values.Count > maximum) throw new InvalidOperationException($"Use at most {maximum} {name}, one per line.");
            return values;
        }
        var tools = Lines(Tools, 20, "tools"); var stages = Lines(Stages, 12, "stages"); var constraints = Lines(Constraints, 20, "confirmed constraints");
        var examples = Examples.Select(x => new RefinementExample(x.Input, x.Output)).ToList();
        var references = References.Select(x => x with { }).ToList();
        if (examples.Count > 8 || references.Count > 8) throw new InvalidOperationException("Use at most eight examples and eight references.");
        RefinementBudget? budget = null;
        if (HasBudget) {
            if (string.IsNullOrWhiteSpace(Destination)) throw new InvalidOperationException("Name the destination for your size limit.");
            if (!int.TryParse(Limit, NumberStyles.None, CultureInfo.InvariantCulture, out int limit) || limit is < 1 or > 20000)
                throw new InvalidOperationException("Enter a destination limit from 1 to 20,000 in the selected unit.");
            budget = new(Destination.Trim(), limit, Unit, "explicit");
        }
        bool hasInputs = tools.Count + stages.Count + constraints.Count + examples.Count + references.Count > 0 || Revision;
        return new(original, mode, Technique, Domain, Important,
            hasInputs ? new(examples, tools, stages, Revision, constraints, references) : null, budget);
    }
    internal static string Fingerprint(RefineRequest request) => JsonSerializer.Serialize(request, StateStore.Json);
    internal string DescribeBlock(string id)
    {
        if (id.StartsWith("source-", StringComparison.Ordinal)) return References.FirstOrDefault(x => "source-" + x.Id == id)?.Title ?? id;
        return id switch { "intent" => "Original draft", "examples" => "Supplied examples", "tools" => "Available tools", _ => id };
    }
}
