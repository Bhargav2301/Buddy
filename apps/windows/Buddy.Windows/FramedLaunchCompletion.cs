using Buddy.Server;

namespace Buddy.Windows;

internal sealed record FramedLaunchCompletion(bool Completed, string Message)
{
    internal static async Task AfterAudit(Func<Task> audit, Func<bool> isCurrent, Action complete, CancellationToken ct)
    {
        await audit();
        ct.ThrowIfCancellationRequested();
        if (!isCurrent()) throw new OperationCanceledException("The framed launch task changed during audit.", ct);
        complete();
    }
    // Host-owned completion policy. A broader query or remaining action never
    // becomes complete merely because its Calculator launch was verified.
    internal static FramedLaunchCompletion Decide(string query, IReadOnlyList<AssistantAction> approved,
        ActionResult receipt, int completedCount, int pendingCount)
    {
        bool complete = receipt.Success && receipt.Sequence == 1 && completedCount == 1 && pendingCount == 0 &&
            approved.Count == 1 && approved[0] == receipt.Action && receipt.Action.Kind == "open" &&
            receipt.Action.Value == "calculator" &&
            AppLaunchIntent.Exact(query) is { IsApp: true, Supported: true, Destination: "calculator" };
        return new(complete, complete
            ? "Calculator opened and its application window was verified."
            : "The app launch was recorded. Further work was not performed; select the app and review a fresh plan.");
    }
}
