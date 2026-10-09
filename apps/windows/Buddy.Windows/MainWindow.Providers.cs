using Buddy.Server;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Buddy.Windows;

public sealed partial class MainWindow
{
    private ProviderRoutingSession? providerSession;
    private CancellationTokenSource? cloudRequest;
    private ProviderQuestionReviewWindow? cloudReview;
    private Window? providerSetup, cloudChat;
    private LocalTaskToken? cloudTask;
    private Window? cloudTaskWindow;
    private LocalAgentBroker? localAgentBroker;
    private LocalAgentPipeServer? localAgentPipe;
    private LocalAgentSessionWindow? localAgentWindow;
    private readonly DispatcherTimer localAgentRefresh = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Dictionary<Guid, (string Task, LocalTaskToken Token, long Sequence)> localAgentTasks = new();
    private bool localAgentRefreshConfigured;
    private bool openingLocalAgentSessions;

    private void CancelCloudRequest()
    {
        cloudRequest?.Cancel(); providerSession?.CancelReview();
        var review = cloudReview; cloudReview = null; review?.Close();
    }
    private void ReplaceProvider(ProviderRoutingSession? next)
    {
        CancelCloudRequest(); var previous = providerSession; providerSession = next; previous?.Dispose();
    }
    private void OpenProviderSetup()
    {
        if (shuttingDown) return;
        if (providerSetup is not null) { providerSetup.Activate(); return; }
        var window = new ProviderSetupWindow(() => providerSession?.Status, ReplaceProvider) { Owner = this };
        providerSetup = window; window.Closed += (_, _) => { if (providerSetup == window) providerSetup = null; }; window.Show();
    }
    private void OpenCloudText()
    {
        if (shuttingDown) return;
        if (cloudChat is not null) { cloudChat.Activate(); return; }
        var panel = new StackPanel();
        panel.Children.Add(Text("Reviewed cloud text", 24));
        panel.Children.Add(Text("Only the exact text you review is sent, with a fixed concise-reply instruction. Audio, screenshots, files, saved history and local memories are excluded. Provider charges may apply. This separate session does not change local chat or speech.", 14, Muted));
        panel.Children.Add(Btn("Configure or disconnect provider", OpenProviderSetup));
        var draft = new TextBox { MaxLength = 8000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 140, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(draft); var state = Text("Configure a session, then review your typed question.", 14, Muted); panel.Children.Add(state);
        var answer = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 130, MaxHeight = 350, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; panel.Children.Add(answer);
        Button? submit = null;
        submit = Btn("Review text for this provider", async () => {
            if (cloudRequest is not null || shuttingDown) return;
            var session = providerSession;
            if (session is null || !session.Status.Configured) { state.Text = "Configure a text session first. No request was sent."; return; }
            PrepareDesktopActivity("cloud-text");
            using var owner = new CancellationTokenSource(TimeSpan.FromMinutes(3)); cloudRequest = owner;
            submit!.IsEnabled = false; draft.IsEnabled = false;
            LocalTaskToken? activity = null;
            try {
                var review = session.PrepareQuestion(draft.Text);
                var dialog = new ProviderQuestionReviewWindow(review) { Owner = cloudChat };
                cloudReview = dialog;
                bool approved = dialog.ShowDialog() == true;
                if (ReferenceEquals(cloudReview, dialog)) cloudReview = null;
                owner.Token.ThrowIfCancellationRequested();
                if (!approved) { session.CancelReview(); state.Text = "Cancelled. Your draft is kept."; return; }
                if (!ReferenceEquals(session, providerSession)) throw new OperationCanceledException();
                activity = localTasks.Begin("cloud-text", "Reviewed cloud text", "Sending the exact approved text to the selected provider.");
                cloudTask = activity; cloudTaskWindow = cloudChat;
                state.Text = "Waiting for the selected provider. Stop is available.";
                string result = await session.CompleteReviewedAsync(review, owner.Token);
                owner.Token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(session, providerSession) || !ReferenceEquals(cloudRequest, owner)) return;
                answer.Text = ConversationalReply.PlainText(result);
                state.Text = "Provider response received. This separate exchange is not saved in Buddy history.";
                localTasks.Finish(activity.Value, LocalTaskPhase.Completed, "A reviewed provider response completed.");
            } catch (OperationCanceledException) {
                if (activity is { } token) localTasks.Finish(token, LocalTaskPhase.Cancelled, "Stopped; already transmitted text cannot be recalled.");
                if (ReferenceEquals(cloudRequest, owner)) state.Text = "Stopped. Already transmitted text cannot be recalled; your draft is kept.";
            } catch (Exception) {
                if (activity is { } token) localTasks.Finish(token, LocalTaskPhase.Failed, "Provider response unavailable.");
                if (ReferenceEquals(cloudRequest, owner)) state.Text = "The reviewed request did not complete. Check the session and model; your draft is kept.";
            } finally {
                if (ReferenceEquals(cloudRequest, owner)) { cloudRequest = null; submit.IsEnabled = true; draft.IsEnabled = true; }
            }
        }, true);
        panel.Children.Add(Wrap(submit, Btn("Stop", CancelCloudRequest)));
        var window = Dialog("Buddy - reviewed cloud text", panel, 700, 820); cloudChat = window;
        window.Closed += (_, _) => { CancelCloudRequest(); draft.Clear(); answer.Clear(); if (cloudChat == window) cloudChat = null; if (cloudTaskWindow == window) { cloudTask = null; cloudTaskWindow = null; } };
    }
    private void OpenLocalAgentSessions() => _ = OpenLocalAgentSessionsAsync();
    private async Task OpenLocalAgentSessionsAsync()
    {
        if (shuttingDown || openingLocalAgentSessions) return;
        if (localAgentWindow is not null) { localAgentWindow.Activate(); return; }
        openingLocalAgentSessions = true;
        try {
        if (localAgentPipe is { Started: true, Running: false } failed) {
            localAgentPipe = null;
            await failed.DisposeAsync();
            if (shuttingDown) return;
        }
        localAgentBroker ??= new LocalAgentBroker();
        localAgentPipe ??= new LocalAgentPipeServer(localAgentBroker);
        if (!localAgentRefreshConfigured) { localAgentRefresh.Tick += (_, _) => RefreshLocalAgentTasks(); localAgentRefreshConfigured = true; }
        localAgentRefresh.Start();
        var window = new LocalAgentSessionWindow(localAgentBroker, localAgentPipe) { Owner = this };
        localAgentWindow = window;
        window.Closed += (_, _) => { if (localAgentWindow == window) localAgentWindow = null; };
        window.Show();
        } catch (Exception) {
            if (!shuttingDown) status.Text = "Local sessions could not open. No pairing or listener was started by opening this view.";
        } finally { openingLocalAgentSessions = false; }
    }
    private void RefreshLocalAgentTasks()
    {
        if (localAgentBroker is null || shuttingDown) return;
        try {
            var snapshots = localAgentBroker.Snapshot;
            foreach (var item in snapshots) {
                if (item.TaskId.Length == 0) continue;
                if (!localAgentTasks.TryGetValue(item.SessionId, out var bound) || bound.Task != item.TaskId) {
                    if (bound.Token != default) localTasks.Finish(bound.Token, LocalTaskPhase.Cancelled, "The paired client reported a different task; prior results are unverified.");
                    var token = localTasks.Begin("agent-" + item.SessionId.ToString("N")[..24], "Paired local agent", "Status supplied by a paired client; Buddy does not verify its work.");
                    bound = (item.TaskId, token, -1);
                }
                if (!item.Connected) localTasks.Finish(bound.Token, LocalTaskPhase.Cancelled, "Session disconnected; the originating tool may still be running.");
                else if (item.State == "Completed") localTasks.Finish(bound.Token, LocalTaskPhase.Completed, "The paired client reported completion; Buddy has not verified its result.");
                else if (item.State == "Failed") localTasks.Finish(bound.Token, LocalTaskPhase.Failed, "The paired client reported failure; open local sessions for its details.");
                else localTasks.Update(bound.Token, item.State == "Decision waiting for the paired client" ? "A reviewed reply is waiting for the paired client." : item.Request is null ? "The paired client reports work in progress." : "A paired client asks for review in local sessions.", item.Request is null ? LocalTaskPhase.Running : LocalTaskPhase.WaitingForReview);
                localAgentTasks[item.SessionId] = (bound.Task, bound.Token, item.LastSequence);
            }
            foreach (var id in localAgentTasks.Keys.Where(id => !snapshots.Any(s => s.SessionId == id)).ToArray()) {
                localTasks.Finish(localAgentTasks[id].Token, LocalTaskPhase.Cancelled, "The paired session expired or disconnected; external work is unverified."); localAgentTasks.Remove(id);
            }
        } catch (InvalidOperationException) {
            foreach (var bound in localAgentTasks.Values) localTasks.Finish(bound.Token, LocalTaskPhase.Cancelled, "Session timing changed; pair again to receive status.");
            localAgentTasks.Clear();
        }
    }
    private async Task DisposeOptionalSessions()
    {
        localAgentRefresh.Stop();
        CancelCloudRequest(); ReplaceProvider(null); providerSetup?.Close(); cloudChat?.Close(); localAgentWindow?.Close();
        localAgentBroker?.DisconnectAll();
        if (localAgentPipe is { } pipe) { localAgentPipe = null; await pipe.DisposeAsync(); }
    }
}
