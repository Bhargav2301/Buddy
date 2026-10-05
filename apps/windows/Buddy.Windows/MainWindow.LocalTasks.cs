namespace Buddy.Windows;

public sealed partial class MainWindow
{
    // This journal contains generic UI labels and bounded launch reports only;
    // prompts, answers and raw exceptions are excluded. It never triggers work.
    private readonly LocalTaskJournal localTasks = new();
    private LocalTaskToken? homeTask;
    private string? homeTaskConversation;

    private bool OpenLocalTaskSource(LocalTaskToken token)
    {
        if (shuttingDown || !localTasks.Snapshot.Tasks.Any(item => item.Token == token)) return false;
        if (token.Source == "routine") {
            if (!FlushPendingPreferences()) return false;
            return RoutineLaunchReport.OpenSource(localTasks, token, record => {
                ShowChat(); status.Text = record.Title + " — " + record.Detail;
                Show(); WindowState = System.Windows.WindowState.Normal; Activate();
            });
        }
        if (token.Source == "home" && homeTask == token && currentId == homeTaskConversation) {
            // Do not use Summon: refreshing history during streaming would replace
            // the current answer, and navigation must never restart a request.
            if (!FlushPendingPreferences()) return false;
            ShowChat(); Show(); WindowState = System.Windows.WindowState.Normal; Activate();
            return true;
        }
        if (token.Source == "talk") return quick?.OpenTaskSource(token) == true;
        if (token.Source == "refine") return refineWindow?.OpenTaskSource(token) == true;
        if (token.Source == "notch" && notchTask == token && notchSessionId is { } session)
            return island?.OpenChatSession(session) == true;
        if (token.Source.StartsWith("agent-", StringComparison.Ordinal) && localAgentTasks.Values.Any(item => item.Token == token)) { OpenLocalAgentSessions(); return true; }
        if (token.Source == "cloud-text" && cloudTask == token && cloudChat is not null && ReferenceEquals(cloudTaskWindow, cloudChat)) { cloudChat.Activate(); return true; }
        return false;
    }
}
