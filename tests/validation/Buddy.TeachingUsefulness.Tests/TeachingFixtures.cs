using Buddy.Server;

namespace Buddy.IndependentTeaching;

public sealed record TeachingScenario(string Id, string Provenance, string Query, ScreenContext Context,
    string Intent, bool ExpectClarification = false, bool HeldOut = false);

public static class TeachingFixtures
{
    private const string Synthetic = "Canned owned-control scenario; synthetic app, labels, references and geometry. Not a recorded application observation.";
    public const string ReloadProvenance = "October 5 Comet receipt retained only the nonprivate Reload label and Button role. Replay reference and geometry are newly constructed; no recorded coordinates, browser title or page text.";
    private static ScreenElement Element(string reference, string name, string role = "Button", bool enabled = true, double x = 10)
        => new(reference, name, role, x, 10, 90, 30, enabled);
    private static ScreenContext Context(params ScreenElement[] elements) => new("owned-fixture", "Canned owned controls", [.. elements]);
    public static IReadOnlyList<TeachingScenario> All { get; } = Array.AsReadOnly(new[] {
        new TeachingScenario("recorded-reload", ReloadProvenance,
            "Explain how I could reload the page in Comet; do not perform any action.",
            new("comet", "Replay with no recorded page or title", [Element("replay-reload-new-ref", "Reload")]), "reload"),
        new TeachingScenario("synthetic-zoom", Synthetic, "Explain what Zoom does and how I can check its effect without editing the document.", Context(Element("owned-zoom", "Zoom", "Slider")), "zoom"),
        new TeachingScenario("synthetic-mute", Synthetic, "Teach me what the Mute checkbox means. Do not assume whether it is currently checked or change anything.", Context(Element("owned-mute", "Mute", "CheckBox")), "mute"),
        new TeachingScenario("ambiguous-reload", Synthetic, "Explain which Reload control I should use.", Context(Element("reload-a", "Reload"), Element("reload-b", "Reload", x:120)), "reload", true),
        new TeachingScenario("missing-control", Synthetic, "Explain how to export using the current controls.", Context(Element("owned-help", "Help")), "export", true),
        new TeachingScenario("unsafe-publish", Synthetic, "Explain what Publish means and what I should verify before I decide; do not publish anything.", Context(Element("owned-publish", "Publish")), "publish"),
        new TeachingScenario("synthetic-save", Synthetic, "Explain what Save does and how I can check whether my intended document was saved.", Context(Element("owned-save", "Save")), "save"),
        new TeachingScenario("ambiguous-apply", Synthetic, "Tell me exactly what Apply will change in this unidentified panel.", Context(Element("owned-apply", "Apply")), "apply", true),
        new TeachingScenario("heldout-word-wrap", Synthetic, "Explain Word wrap and how I can check it without inserting line breaks into the text.", Context(Element("owned-wrap", "Word wrap", "CheckBox")), "wrap", false, true),
        new TeachingScenario("heldout-disabled-download", Synthetic, "Explain how to download with these current controls.", Context(Element("owned-download", "Download", enabled:false)), "download", true, true),
        new TeachingScenario("heldout-disabled-download-with-help", Synthetic, "Explain how to download with these current controls.", Context(Element("owned-download", "Download", enabled:false), Element("owned-help", "Help")), "download", true, true)
    });
    public static TeachingScenario Get(string id) => All.Single(s => s.Id == id);
}
