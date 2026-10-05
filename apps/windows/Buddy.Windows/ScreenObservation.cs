using System.Collections.ObjectModel;
using System.Diagnostics;

namespace Buddy.Windows;

internal enum ScreenObservationMode { LiveProperties, CachedProperties }
// Internal diagnostic controls. Production reaches depth 16 after measured Comet depth-15 omissions; node/time/deadline caps are fixed.
internal sealed record ScreenObservationOptions(ScreenObservationMode Mode = ScreenObservationMode.CachedProperties, int DepthLimit = 16)
{
    internal ScreenObservationOptions Validate()
    {
        if (!Enum.IsDefined(Mode) || DepthLimit is < 1 or > 24) throw new ArgumentOutOfRangeException(nameof(DepthLimit));
        return this;
    }
}

// Safe diagnostic payload: only timings, counts, fixed codes and native identity metadata.
// Never attach exception messages, app/window/control names, values, or captured text.
internal sealed record ScreenObservationIdentity(long Window, uint ThreadId, uint ProcessId, long ProcessStarted);
internal sealed record ScreenObservationMetrics(
    string Outcome, bool Complete, double ElapsedMilliseconds, double TraversalElapsedMilliseconds,
    IReadOnlyDictionary<string, double> StageMilliseconds,
    int Visited, int Enqueued, int Remaining, int MaximumDepth,
    int ElementCount, int PrivateRectangleCount, int OffscreenSkipped, int PasswordSkipped,
    int DepthOmissions, int NodeOmissions, int TimeOmissions,
    int PropertyReads, int NavigationReads, int RuntimeIdReads,
    IReadOnlyList<string> LimitReasons, IReadOnlyDictionary<string, int> ProviderErrors,
    ScreenObservationIdentity? SelectedIdentity, string? FailureCode, int? NativeError,
    int NodeLimit = 400, int DepthLimit = 16, int TraversalLimitMilliseconds = 600, int DeadlineMilliseconds = 2000,
    string AccessStrategy = "live-properties", int CacheRefreshes = 0, int CachedPropertyReads = 0);

internal sealed class ScreenObservationSession(ScreenObservationOptions? options = null)
{
    private readonly ScreenObservationOptions settings = (options ?? new(ScreenObservationMode.LiveProperties)).Validate();
    private readonly object gate = new();
    private readonly long started = Stopwatch.GetTimestamp();
    private long stageStarted = Stopwatch.GetTimestamp();
    private string stage = "queue";
    private readonly Dictionary<string, double> stages = [];
    private readonly Dictionary<string, int> errors = [];
    private readonly HashSet<string> limits = [];
    private int visited, enqueued, remaining, maximumDepth, elements, rectangles, offscreen, passwords;
    private int depthOmissions, nodeOmissions, timeOmissions, properties, navigation, runtimeIds, cacheRefreshes, cachedProperties;
    private ScreenObservationIdentity? identity;
    private long traversalStarted, traversalEnded;
    private static double Milliseconds(long ticks) => ticks * 1000d / Stopwatch.Frequency;
    internal void Stage(string value)
    {
        lock (gate) {
            long now = Stopwatch.GetTimestamp(); stages[stage] = stages.GetValueOrDefault(stage) + Milliseconds(now - stageStarted);
            stage = value; stageStarted = now;
        }
    }
    internal void Selected(WindowSelection selected)
    {
        lock (gate) identity = new(selected.Window.ToInt64(), selected.ThreadId, selected.ProcessId, selected.ProcessStarted);
    }
    internal void BeginTraversal() { lock (gate) traversalStarted = Stopwatch.GetTimestamp(); }
    internal void EndTraversal() { lock (gate) traversalEnded = Stopwatch.GetTimestamp(); }
    internal void Enqueue(int queueCount) { lock (gate) { enqueued++; remaining = queueCount; } }
    internal void Visit(int depth, int queueCount) { lock (gate) { visited++; remaining = queueCount; maximumDepth = Math.Max(maximumDepth, depth); } }
    internal void Offscreen() { lock (gate) offscreen++; }
    internal void Password() { lock (gate) passwords++; }
    internal void Counts(int elementCount, int rectangleCount) { lock (gate) { elements = elementCount; rectangles = rectangleCount; } }
    internal void Omit(string reason)
    {
        lock (gate) {
            limits.Add(reason);
            switch (reason) { case "depth-limit": depthOmissions++; break; case "node-limit": nodeOmissions++; break; case "traversal-time-limit": timeOmissions++; break; }
        }
    }
    internal void ProviderFailure(Exception exception)
    {
        string code = exception switch {
            System.Windows.Automation.ElementNotAvailableException => "element-unavailable",
            System.Runtime.InteropServices.COMException => "com-error",
            InvalidOperationException => "invalid-operation",
            _ => "provider-error"
        };
        lock (gate) { errors[code] = errors.GetValueOrDefault(code) + 1; limits.Add("provider-error"); }
    }
    internal T Property<T>(Func<T> read) { Stage("properties"); lock (gate) properties++; return read(); }
    internal T CachedProperty<T>(Func<T> read) { Stage("cached-properties"); lock (gate) cachedProperties++; return read(); }
    internal T CacheRefresh<T>(Func<T> read) { Stage("cache-refresh"); lock (gate) cacheRefreshes++; return read(); }
    internal T Navigation<T>(Func<T> read) { Stage("navigation"); lock (gate) navigation++; return read(); }
    internal int[] RuntimeId(Func<int[]> read) { Stage("runtime-id"); lock (gate) runtimeIds++; return read(); }
    internal ScreenObservationMetrics Snapshot(string outcome, bool complete, Exception? failure = null)
    {
        lock (gate) {
            long now = Stopwatch.GetTimestamp(); var elapsed = new Dictionary<string, double>(stages);
            elapsed[stage] = elapsed.GetValueOrDefault(stage) + Milliseconds(now - stageStarted);
            var reasons = new HashSet<string>(limits);
            if (outcome == "timeout") reasons.Add("capture-deadline");
            if (outcome == "cancelled") reasons.Add("cancelled");
            var selectionFailure = failure as WindowSelectionException;
            string? failureCode = outcome == "timeout" ? "capture-deadline" : selectionFailure?.Code ?? (failure is null ? null : failure switch {
                OperationCanceledException => "cancelled", TimeoutException => "capture-deadline",
                System.Windows.Automation.ElementNotAvailableException => "element-unavailable",
                System.Runtime.InteropServices.COMException => "com-error",
                InvalidOperationException invalid => invalid.Message switch {
                    "Buddy cannot access a secure desktop." => "secure-desktop",
                    "Buddy blocks this window because it may contain private credentials or financial data." => "private-window",
                    "This application is in your privacy blocklist." => "blocked-app",
                    "Focus the app you want Buddy to help with first." => "own-window",
                    "Computer control is unavailable in terminals and system administration tools." => "restricted-app",
                    _ => "invalid-operation"
                }, _ => "observation-failed"
            });
            double traversalElapsed = traversalStarted == 0 ? 0 : Milliseconds((traversalEnded == 0 ? now : traversalEnded) - traversalStarted);
            var observedIdentity = identity ?? (selectionFailure is null ? null : new ScreenObservationIdentity(
                selectionFailure.Window.ToInt64(), selectionFailure.ThreadId, selectionFailure.ProcessId, 0));
            return new(outcome, complete, Milliseconds(now - started), traversalElapsed, new ReadOnlyDictionary<string, double>(elapsed),
                visited, enqueued, remaining, maximumDepth, elements, rectangles, offscreen, passwords,
                depthOmissions, nodeOmissions, timeOmissions, properties, navigation, runtimeIds,
                Array.AsReadOnly(reasons.OrderBy(x => x, StringComparer.Ordinal).ToArray()),
                new ReadOnlyDictionary<string, int>(new Dictionary<string, int>(errors)), observedIdentity, failureCode, selectionFailure?.NativeError,
                DepthLimit: settings.DepthLimit, AccessStrategy: settings.Mode == ScreenObservationMode.CachedProperties ? "cached-properties" : "live-properties",
                CacheRefreshes: cacheRefreshes, CachedPropertyReads: cachedProperties);
        }
    }
}
