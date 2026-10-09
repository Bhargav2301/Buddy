namespace Buddy.Windows;

// Hover is a temporary presentation choice. It never rewrites the saved mode or
// infers that a task completed. Pinning lasts for this bar instance.
internal sealed class NotchInteractionState
{
    private readonly Func<DateTimeOffset> clock;
    private DateTimeOffset? leftAt;
    private bool greeted;
    internal string Mode { get; private set; } = "Hidden";
    internal bool Hovered { get; private set; }
    internal bool Pinned { get; private set; }
    internal bool Editing { get; private set; }
    internal string Greeting { get; private set; } = "Ready when you are.";
    internal bool Expanded => Mode != "Hidden" && (Mode == "Expanded" || Hovered || Pinned || Editing);
    internal NotchInteractionState(Func<DateTimeOffset>? clock = null) => this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    internal void SetMode(string? value)
    {
        Mode = CompanionPresentation.Mode(value);
        Hovered = false;
        if (Mode != "Expanded") Pinned = false;
        if (Mode == "Hidden") Editing = false;
    }
    internal void Hover(bool entered)
    {
        if (Mode == "Hidden") return;
        Hovered = entered;
        if (!entered) { leftAt = clock(); return; }
        if (!greeted) { Greeting = "Hi. Ready when you are."; greeted = true; }
        else if (leftAt is { } left && clock() - left >= TimeSpan.FromSeconds(30)) Greeting = "Welcome back. Your local tasks are here.";
    }
    internal void TogglePin() { if (Mode != "Hidden") Pinned = !Pinned; }
    internal bool BeginEditing() { if (Mode == "Hidden") return false; Editing = true; return true; }
    internal void EndEditing() => Editing = false;
    internal static string Ticker(LocalTaskRecord? task)
    {
        if (task is null) return "No task running. Choose an action when you are ready.";
        string detail = task.ObservedSteps.LastOrDefault()?.Detail ?? task.Detail;
        string state = task.Phase == LocalTaskPhase.WaitingForReview ? "Review needed" : task.Phase.ToString();
        return state + ": " + detail;
    }
}
