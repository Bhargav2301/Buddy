namespace Buddy.Windows;

// A drag preview is transient. Only an explicit release/button commits through
// the owner's persistence callback. Lost capture/Hide/Stop never commits.
internal sealed class NotchPlacementState
{
    private sealed record Drag(PixelPosition Origin, PixelPosition GrabDip, double Threshold, PixelPosition Pointer, bool Moved);
    private Drag? drag;
    private NotchPlacementPreference? preview;
    internal NotchPlacementPreference Saved { get; private set; }
    internal bool IsDragging => drag is not null;
    internal string? Error { get; private set; }
    internal NotchPlacementState(NotchPlacementPreference? initial = null) => Saved = NotchPlacementPreference.Normalize(initial);
    internal bool Begin(PixelPosition pointer, PixelBounds current, double scale)
    {
        if (drag is not null || !double.IsFinite(pointer.X) || !double.IsFinite(pointer.Y) || !double.IsFinite(current.Left) || !double.IsFinite(current.Top)) return false;
        scale = NotchPlacementPolicy.Scale(scale);
        drag = new(pointer, new((pointer.X - current.Left) / scale, (pointer.Y - current.Top) / scale), 4 * scale, pointer, false);
        preview = null; Error = null; return true;
    }
    internal void Move(PixelPosition pointer)
    {
        if (drag is null || !double.IsFinite(pointer.X) || !double.IsFinite(pointer.Y)) return;
        bool moved = drag.Moved || Math.Max(Math.Abs(pointer.X - drag.Origin.X), Math.Abs(pointer.Y - drag.Origin.Y)) >= drag.Threshold;
        drag = drag with { Pointer = pointer, Moved = moved };
    }
    internal NotchPlacementResult Resolve(IReadOnlyList<NotchPlacementMonitor> monitors, PixelPosition fallback, double width, double height, double scale)
    {
        if (drag is not { Moved: true } current) return NotchPlacementPolicy.Resolve(Saved, monitors, fallback, width, height, scale);
        scale = NotchPlacementPolicy.Scale(scale);
        var desired = new PixelPosition(current.Pointer.X - current.GrabDip.X * scale, current.Pointer.Y - current.GrabDip.Y * scale);
        var result = NotchPlacementPolicy.At(desired, current.Pointer, monitors, width, height, scale);
        preview = result.Preference; return result;
    }
    internal bool Finish(Func<NotchPlacementPreference, NotchPlacementPreference>? persist)
    {
        var next = preview; bool moved = drag?.Moved == true; drag = null; preview = null;
        return !moved || next is null || Commit(next, persist);
    }
    internal void Cancel() { drag = null; preview = null; }
    internal void Refuse(string reason) { Cancel(); Error = reason; }
    internal bool Commit(NotchPlacementPreference preference, Func<NotchPlacementPreference, NotchPlacementPreference>? persist)
    {
        Cancel(); var next = NotchPlacementPreference.Normalize(preference);
        try {
            // A merged profile save may preserve concurrently changed fields.
            // Reflect the owner's effective saved value, not our original draft.
            var effective = persist is null ? next : persist(next) ?? throw new InvalidOperationException("The saved placement was not returned.");
            Saved = NotchPlacementPreference.Normalize(effective); Error = null; return true;
        }
        catch { Error = "Could not confirm the saved bar position. The previous placement is kept here."; return false; }
    }
}
