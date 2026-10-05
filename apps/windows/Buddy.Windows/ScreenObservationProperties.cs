using System.Windows;
using System.Windows.Automation;

namespace Buddy.Windows;

// Per-capture caches only. Full mode keeps the original live nodes usable for later revalidation.
// The navigation cache contains privacy/visibility metadata, never Name, Value or TextPattern.
internal sealed class ScreenObservationProperties(ScreenObservationMode mode, ScreenObservationSession metrics)
{
    private readonly CacheRequest privacy = Request(false);
    private readonly CacheRequest visible = Request(true);
    private bool Cached => mode == ScreenObservationMode.CachedProperties;
    private static CacheRequest Request(bool includeVisible)
    {
        var request = new CacheRequest { AutomationElementMode = AutomationElementMode.Full, TreeScope = TreeScope.Element, TreeFilter = Automation.ControlViewCondition };
        request.Add(AutomationElement.IsOffscreenProperty); request.Add(AutomationElement.IsPasswordProperty); request.Add(AutomationElement.BoundingRectangleProperty);
        if (includeVisible) {
            request.Add(AutomationElement.NameProperty); request.Add(AutomationElement.ControlTypeProperty);
            request.Add(AutomationElement.IsEnabledProperty); request.Add(AutomationElement.RuntimeIdProperty);
        }
        return request;
    }
    internal AutomationElement Root(IntPtr window)
    {
        var root = AutomationElement.FromHandle(window);
        return Cached ? metrics.CacheRefresh(() => root.GetUpdatedCache(privacy)) : root;
    }
    internal AutomationElement? FirstChild(AutomationElement node) => metrics.Navigation(() => Cached
        ? TreeWalker.ControlViewWalker.GetFirstChild(node, privacy) : TreeWalker.ControlViewWalker.GetFirstChild(node));
    internal AutomationElement? NextSibling(AutomationElement node) => metrics.Navigation(() => Cached
        ? TreeWalker.ControlViewWalker.GetNextSibling(node, privacy) : TreeWalker.ControlViewWalker.GetNextSibling(node));
    internal ScreenObservationNode Read(AutomationElement node)
    {
        if (!Cached) return ReadLive(node);
        var initial = node.Cached;
        if (metrics.CachedProperty(() => initial.IsOffscreen)) return new(true, false, Rect.Empty);
        if (metrics.CachedProperty(() => initial.IsPassword)) return new(false, true, metrics.CachedProperty(() => initial.BoundingRectangle));
        // Only a visible, non-password node may request Name and the other public metadata.
        // Recheck the refreshed privacy flags before using any fetched public property.
        var updated = metrics.CacheRefresh(() => node.GetUpdatedCache(visible)); var data = updated.Cached;
        if (metrics.CachedProperty(() => data.IsOffscreen)) return new(true, false, Rect.Empty);
        if (metrics.CachedProperty(() => data.IsPassword)) return new(false, true, metrics.CachedProperty(() => data.BoundingRectangle));
        var bounds = metrics.CachedProperty(() => data.BoundingRectangle);
        if (!ValidBounds(bounds)) return new(false, false, bounds);
        var runtime = metrics.CachedProperty(() => updated.GetCachedPropertyValue(AutomationElement.RuntimeIdProperty)) as int[]
            ?? throw new InvalidOperationException("The accessibility cache did not contain an element identity.");
        if (!runtime.SequenceEqual(metrics.RuntimeId(node.GetRuntimeId)))
            throw new InvalidOperationException("The accessibility element changed during observation.");
        return new(false, false, bounds, metrics.CachedProperty(() => data.Name), metrics.CachedProperty(() => data.ControlType),
            metrics.CachedProperty(() => data.IsEnabled), runtime);
    }
    private ScreenObservationNode ReadLive(AutomationElement node)
    {
        var data = node.Current;
        if (metrics.Property(() => data.IsOffscreen)) return new(true, false, Rect.Empty);
        if (metrics.Property(() => data.IsPassword)) {
            var bounds = metrics.Property(() => data.BoundingRectangle);
            return new(false, true, bounds.IsEmpty ? bounds : metrics.Property(() => data.BoundingRectangle));
        }
        var rectangle = metrics.Property(() => data.BoundingRectangle);
        if (!ValidBounds(rectangle)) return new(false, false, rectangle);
        var runtime = metrics.RuntimeId(node.GetRuntimeId);
        string? name = metrics.Property(() => data.Name);
        // Preserve the baseline's second live Name read for an honest diagnostic A/B.
        string? comparison = metrics.Property(() => data.Name);
        return new(false, false, rectangle, name, metrics.Property(() => data.ControlType), metrics.Property(() => data.IsEnabled), runtime, comparison, true);
    }
    internal static bool ValidBounds(Rect rect) => !rect.IsEmpty && rect.Width > 0 && rect.Height > 0 && double.IsFinite(rect.X) && double.IsFinite(rect.Y);
}

internal sealed record ScreenObservationNode(bool Offscreen, bool Password, Rect Bounds, string? Name = "", ControlType? Type = null,
    bool Enabled = false, int[]? RuntimeId = null, string? ComparisonName = null, bool UsesLiveComparison = false);
