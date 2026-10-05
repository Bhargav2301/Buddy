using Buddy.Server;

namespace Buddy.Windows;

// Memory-only, one-use preparation. No field text, HWND, focus or authority to edit
// is captured here. The user selects the actual field after preparing these inputs.
internal sealed class ExternalRefinementOptionsSlot
{
    internal const long LifetimeMilliseconds = 5 * 60 * 1000;
    private readonly object gate = new();
    private ExternalRefinementPlan? pending;
    private long generation, armedAt;
    internal bool HasPending { get { lock (gate) return pending is not null; } }

    internal void Arm(RefinementDraftOptions options, string mode, long nowMilliseconds)
    {
        lock (gate) {
            // Invalid replacement options must not leave an older preparation armed.
            generation++; pending = null;
            if (nowMilliseconds < 0) throw new InvalidOperationException("The options preparation clock is unavailable.");
            var copy = options with {
                Examples = Array.AsReadOnly(options.Examples.Select(x => x with { }).ToArray()),
                References = Array.AsReadOnly(options.References.Select(x => x with { }).ToArray())
            };
            // Validate input shape now. Task-dependent technique prerequisites and the
            // complete destination count are deliberately checked after fresh capture.
            var request = copy.ToRequest("Source prompt is captured later.", mode);
            RefinementPolicy.Validate(request);
            _ = RefinementContext.Build(request.Inputs?.Context);
            _ = RefinementCore.ApplyBudget([], request.Budget);
            armedAt = nowMilliseconds;
            long selectedGeneration = generation;
            pending = new(copy, mode, () => IsCurrent(selectedGeneration));
        }
    }

    internal ExternalRefinementPlan? Consume(long nowMilliseconds)
    {
        lock (gate) {
            var selected = pending; pending = null;
            if (selected is null) return null;
            if (nowMilliseconds < armedAt || nowMilliseconds - armedAt >= LifetimeMilliseconds) {
                generation++;
                throw new InvalidOperationException("Prepared options expired. Prepare them again, then focus the original field and press Ctrl+Alt+R.");
            }
            return selected;
        }
    }

    internal void Clear() { lock (gate) { generation++; pending = null; } }
    private bool IsCurrent(long selectedGeneration) { lock (gate) return selectedGeneration == generation; }
}

internal sealed class ExternalRefinementPlan(RefinementDraftOptions options, string mode, Func<bool> current)
{
    private int bound;
    internal bool IsCurrent => current();
    internal RefinementPreparationResult Bind(string exactOriginal)
    {
        if (Interlocked.Exchange(ref bound, 1) != 0)
            throw new InvalidOperationException("These options already belong to a capture. Prepare options again for another field.");
        RequireCurrent();
        var result = RefinementPreparation.Prepare(options.ToRequest(exactOriginal, mode));
        RequireCurrent();
        return result;
    }
    private void RequireCurrent()
    {
        if (!current()) throw new InvalidOperationException("Options changed or were cancelled. Prepare them again and capture the original field afresh.");
    }
}
