using System.Security.Cryptography;
using System.Text;

namespace Buddy.Server;

// Developer-reviewed public references, not pages fetched for a user's request.
// Each claim is an original paraphrase with deliberately limited scope. These
// examples explain general concepts; they do not identify the observed app's API.
public sealed record ConceptReference(string Id, string Title, string Url, string SupportedClaim);
public static class ConceptReferences
{
    public const string Reviewed = "2026-10-05";
    public const string Origin = "General concept · local reference";
    public const string TitlePrefix = "Local reference (reviewed " + Reviewed + ")";
    private static readonly ConceptReference[] References = [
        new("reload", "MDN: Location.reload", "https://developer.mozilla.org/en-US/docs/Web/API/Location/reload", "Browser reload requests the current URL again; it does not establish this page's result."),
        new("unsaved", "MDN: beforeunload", "https://developer.mozilla.org/en-US/docs/Web/API/Window/beforeunload_event", "Leaving or reloading can lose unsaved page work; warning delivery is not guaranteed."),
        new("zoom", "Microsoft: DocumentViewer.Zoom", "https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.documentviewer.zoom", "A document-view zoom factor is an example of display magnification, not evidence of this control's implementation."),
        new("mute", "Microsoft: endpoint SetMute", "https://learn.microsoft.com/en-us/windows/win32/api/endpointvolume/nf-endpointvolume-iaudioendpointvolume-setmute", "Muting can silence a stream entering or leaving an audio endpoint; this label does not identify the affected endpoint."),
        new("audio-direction", "Microsoft: default audio endpoint", "https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nf-mmdeviceapi-immdeviceenumerator-getdefaultaudioendpoint", "Audio endpoints distinguish capture from rendering; a generic Mute label does not resolve input, output or application scope."),
        new("wrap", "Unicode: line breaking overview", "https://www.unicode.org/reports/tr14/#Overview", "Display line fitting uses break opportunities and layout width; actual break selection depends on higher-level software.")
    ];
    public static IReadOnlyList<ConceptReference> All => Array.AsReadOnly(References);
    public static string ManifestSha256 => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        Reviewed + "\n" + string.Join("\n", References.Select(r => $"{r.Id}\t{r.Title}\t{r.Url}\t{r.SupportedClaim}"))))).ToLowerInvariant();
    public static List<WebSource> For(params string[] ids) => ids.Select(id => {
        var reference = References.Single(r => r.Id == id);
        return new WebSource(TitlePrefix + " · " + reference.Title, reference.Url, "", "curated local reference");
    }).ToList();
}
