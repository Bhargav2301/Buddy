using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Buddy.Server;

public sealed record BrainDescriptor(string Id, string Label, bool IsCloud, bool RequiresExplicitSelection = false);
public sealed record BrainRequest(string Mode, string Text, string Model, IReadOnlyList<object> Messages,
    string? BrainId = null, string? SkillBrainId = null, string? DefaultBrainId = null, bool RestrictedContext = false,
    string? ScreenApp = null, string? ScreenTitle = null);
public sealed record BrainToken(string BrainId, string? Text = null, AssistantAction? ProposedAction = null, string? Notice = null);
public interface IBrain
{
    BrainDescriptor Descriptor { get; }
    IAsyncEnumerable<BrainToken> CompleteAsync(BrainRequest request, CancellationToken ct);
}
public sealed class OllamaBrain(OllamaEngine engine) : IBrain
{
    public BrainDescriptor Descriptor { get; } = new("local", "on this PC", false);
    public async IAsyncEnumerable<BrainToken> CompleteAsync(BrainRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var text in engine.Chat(request.Model, request.Messages.ToList(), ct)) yield return new("local", text);
    }
}

// Providers are server-owned. Production registers only Ollama in this preview.
// Adding an account or accepting a skill file does not grant a tool or upload consent.
public sealed class BrainRouter
{
    private readonly IReadOnlyDictionary<string, IBrain> brains;
    public BrainRouter(IEnumerable<IBrain> providers)
    {
        brains = providers.ToDictionary(p => p.Descriptor.Id, StringComparer.OrdinalIgnoreCase);
        if (!brains.TryGetValue("local", out var local) || local.Descriptor.IsCloud) throw new ArgumentException("A local fallback is required.");
    }
    public IReadOnlyList<BrainDescriptor> Available => brains.Values.Select(b => b.Descriptor).ToArray();
    public static string? ExplicitPhrase(string text)
    {
        var match = Regex.Match(text, @"^\s*use\s+(Grok|ChatGPT|Claude|Gemini|Codex|local)\b", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }
    public BrainDescriptor Select(BrainRequest request)
    {
        string? explicitId = request.BrainId ?? ExplicitPhrase(request.Text);
        string id = explicitId ?? request.SkillBrainId ?? request.DefaultBrainId ?? "local";
        bool restricted = request.RestrictedContext || BrainPrivacy.Restricted(request.ScreenApp, request.ScreenTitle);
        if (!brains.TryGetValue(id, out var provider) || (restricted && provider.Descriptor.IsCloud)
            || (provider.Descriptor.RequiresExplicitSelection && explicitId is null)) return brains["local"].Descriptor;
        return provider.Descriptor;
    }
    public async IAsyncEnumerable<BrainToken> CompleteAsync(BrainRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        var selected = Select(request); var provider = brains[selected.Id];
        // Until a future explicit screen-sharing grant contract exists, cloud adapters can
        // receive only the user's current text, never prior turns, memories, UIA, titles or images.
        var effective = selected.IsCloud ? request with { Messages = new object[] { new { role = "system", content = OllamaEngine.Identity }, new { role = "user", content = request.Text } } } : request;
        yield return new(selected.Id, Notice: "Answering " + selected.Label);
        bool emitted = false, failed = false;
        await using (var stream = provider.CompleteAsync(effective, ct).GetAsyncEnumerator(ct)) {
            while (true) {
                bool more;
                try { more = await stream.MoveNextAsync(); }
                catch (Exception ex) when (selected.IsCloud && !emitted && !ct.IsCancellationRequested && ex is not OperationCanceledException) { failed = true; break; }
                if (!more) break;
                ct.ThrowIfCancellationRequested(); emitted = true;
                yield return stream.Current with { BrainId = selected.Id }; // Never trust a provider-supplied identity.
            }
        }
        if (failed) {
            yield return new("local", Notice: "Provider unavailable; answering on this PC");
            await foreach (var token in brains["local"].CompleteAsync(request, ct)) yield return token with { BrainId = "local" };
        }
    }
}

public static class BrainPrivacy
{
    public static bool Restricted(string? app, string? title)
    {
        string process = (app ?? "").ToLowerInvariant(), caption = (title ?? "").ToLowerInvariant();
        return new[] { "keepass", "1password", "bitwarden", "lastpass", "credential", "authenticator", "logonui", "consent" }.Any(process.Contains)
            || new[] { "bank", "password", "payment", "paytm", "phonepe", "incognito", "inprivate", "sign in", "log in" }.Any(caption.Contains);
    }
}

public static class ToolRegistry
{
    private static readonly IReadOnlyDictionary<string, bool> tools = new Dictionary<string, bool> {
        ["screen.capture"] = false, ["uia.snapshot"] = false, ["overlay.draw"] = false,
        ["voice.speak"] = false, ["refine.apply"] = true, ["files.read"] = false,
        ["browser.open"] = true, ["mail.search"] = false, ["calendar.list"] = false,
        ["mail.send"] = true, ["calendar.create"] = true, ["uia.click"] = true, ["uia.type"] = true
    };
    public static bool IsAllowed(string tool, IReadOnlySet<string> skillAllowlist, IReadOnlySet<string> grants, bool approved)
        => tools.TryGetValue(tool, out bool highRisk) && skillAllowlist.Contains(tool) && grants.Contains(tool) && (!highRisk || approved);
    public static bool CanRunParallel(IEnumerable<string> requested) => requested.All(t => tools.TryGetValue(t, out bool highRisk) && !highRisk && t is "uia.snapshot" or "files.read" or "mail.search" or "calendar.list");
}
