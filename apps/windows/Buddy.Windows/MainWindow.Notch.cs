using Buddy.Server;
using System.Text;

namespace Buddy.Windows;

public sealed partial class MainWindow
{
    private NotchChatSession? notchChat;
    private IReadOnlyList<NotchModelChoice> notchModels = [];
    private string? notchSessionId, notchConversationId;
    private LocalTaskToken? notchTask;

    private NotchPlacementPreference ReadIslandPlacement() => NotchPlacementPreference.Normalize(new() {
        Mode = desktop.IslandPlacementMode, MonitorId = desktop.IslandPlacementMonitor,
        XFraction = desktop.IslandPlacementX, YFraction = desktop.IslandPlacementY
    });
    private NotchPlacementPreference StoreIslandPlacement(NotchPlacementPreference preference)
    {
        if (!FlushPendingPreferences()) throw new InvalidOperationException("Finish saving Settings before moving the bar.");
        var placement = NotchPlacementPreference.Normalize(preference);
        SavePreferences(desktop with { IslandPlacementMode = placement.Mode, IslandPlacementMonitor = placement.MonitorId,
            IslandPlacementX = placement.XFraction, IslandPlacementY = placement.YFraction });
        return ReadIslandPlacement();
    }

    private NotchWorkspace CreateNotchWorkspace()
    {
        notchChat = new NotchChatSession(() => notchModels, SendNotchChat,
            "Completed messages are saved in Buddy's local encrypted conversations and can be managed in Home. Reviewed file text accompanies one message; saved answers may quote or describe it.");
        return new(notchChat, new NotchNoteStore(Path.Combine(PreviewEnvironment.DataDirectory, "companion-note.json")));
    }

    private void SetNotchModels(EngineStatus status, string preferred, string embeddingModel)
    {
        notchModels = status.Installed.Where(name => name != embeddingModel &&
                !name.Contains("embed", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("minilm", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name != preferred).ThenBy(name => name, StringComparer.Ordinal)
            .Select(name => new NotchModelChoice(name, name, status.Reachable,
                status.Reachable ? null : "The local engine is unavailable.")).ToArray();
    }

    private async Task<NotchChatReply> SendNotchChat(NotchChatRequest input, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (shuttingDown || host is null) throw new InvalidOperationException("The local Buddy service is unavailable.");
        PrepareDesktopActivity("notch");
        var service = host.Service;
        var activity = localTasks.Begin("notch", "Companion chat", "Preparing a response on this PC.");
        notchTask = activity;
        try {
            if (notchSessionId != input.SessionId || notchConversationId is null) {
                var created = await service.CreateConversation("Companion chat");
                cancellation.ThrowIfCancellationRequested();
                notchSessionId = input.SessionId; notchConversationId = created.Id;
            }
            var answer = new StringBuilder(); bool complete = false;
            await foreach (var item in service.Chat(new(notchConversationId, input.Text, input.RequestId,
                Context: input.Attachment?.Text, UseWeb: false, BrainId: "local", LocalModel: input.ModelId,
                ContextKind: input.Attachment is null ? null : "file"), cancellation)) {
                cancellation.ThrowIfCancellationRequested();
                if (item.Type == "delta") answer.Append(item.Text);
                if (item.Type == "done") complete = true;
                if (item.Type == "status") localTasks.Update(activity, "Waiting for the local model.");
            }
            cancellation.ThrowIfCancellationRequested();
            if (!complete || answer.Length == 0) throw new InvalidOperationException("The local answer did not finish.");
            localTasks.Finish(activity, LocalTaskPhase.Completed, "Answer saved in local conversation history.");
            return new(answer.ToString());
        } catch (OperationCanceledException) {
            localTasks.Finish(activity, LocalTaskPhase.Cancelled, "Stopped; no further response will appear in the bar."); throw;
        } catch {
            localTasks.Finish(activity, LocalTaskPhase.Failed, "The response did not finish. Review the companion chat."); throw;
        }
    }
}
