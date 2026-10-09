using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using Buddy.Server;

namespace Buddy.Windows;

internal sealed class LocalAgentSessionWindow : Window
{
    private readonly LocalAgentBroker broker;
    private readonly LocalAgentPipeServer pipe;
    private readonly StackPanel cards = new();
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    internal LocalAgentSessionWindow(LocalAgentBroker broker, LocalAgentPipeServer pipe)
    {
        this.broker = broker; this.pipe = pipe; BuddyTheme.Ensure();
        Title = "Buddy · Local agent sessions"; Width = 660; Height = 700; MinWidth = 420; MinHeight = 400;
        Background = BuddyTheme.Canvas; Foreground = BuddyTheme.Ink; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SourceInitialized += (_, _) => CaptureProtection.Apply(new WindowInteropHelper(this).Handle);
        var panel = new StackPanel { Margin = new Thickness(22) };
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(Text("Local agent sessions", 24));
        panel.Children.Add(Text("An adapter you choose can send status and questions over a private Windows pipe. Buddy installs no hooks and cannot approve or execute commands. Possession of a pairing token does not prove the identity of a coding product."));
        var consent = new CheckBox { Content = Text("Enable local status and question exchange for sessions I pair."), IsChecked = false }; panel.Children.Add(consent);
        var name = new TextBox { MaxLength = 80, Text = "My local agent", Margin = new Thickness(0, 8, 0, 8) }; panel.Children.Add(name);
        var pairing = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, MaxHeight = 140, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; panel.Children.Add(pairing);
        panel.Children.Add(BuddyTheme.Button("Pair one local session", () =>
        {
            try
            {
                if (consent.IsChecked != true) throw new InvalidOperationException();
                if (pipe.Started && !pipe.Running) {
                    pairing.Clear();
                    status.Text = "The local listener stopped. Close and reopen this window, then pair again with consent.";
                    return;
                }
                if (!pipe.Started) pipe.Start(true);
                var lease = broker.Pair(name.Text, true);
                pairing.Text = "Pipe: " + pipe.PipeName + "\nSession: " + lease.SessionId + "\nToken: " + lease.Token + "\nExpires: " + lease.Expires.ToString("u");
                status.Text = "Share these details only with the local adapter you chose. No adapter is connected merely by pairing."; Refresh();
            }
            catch (InvalidOperationException) { status.Text = "Enable consent, check the session label, or disconnect a session before pairing."; }
        }, true));
        panel.Children.Add(BuddyTheme.Button("Disconnect all sessions", () => { broker.DisconnectAll(); pairing.Clear(); Refresh(); }));
        panel.Children.Add(status); panel.Children.Add(cards);
        timer.Tick += (_, _) => Refresh(); Loaded += (_, _) => { Refresh(); timer.Start(); }; Closed += (_, _) => { timer.Stop(); pairing.Clear(); };
    }
    private string revision = "";
    private void Refresh()
    {
        System.Collections.Generic.IReadOnlyList<LocalAgentSnapshot> snapshots;
        try { snapshots = broker.Snapshot; }
        catch (InvalidOperationException) { cards.Children.Clear(); status.Text = "Local session timing changed. Disconnect and pair again."; return; }
        if (pipe.Started && !pipe.Running) status.Text = pipe.Status;
        // Preserve an in-progress answer while the event/state is unchanged.
        string next = string.Join("|", snapshots.Select(s => s.SessionId + ":" + s.LastSequence + ":" + s.State));
        if (next == revision) return; revision = next; cards.Children.Clear();
        foreach (var s in snapshots)
        {
            var card = new StackPanel { Margin = new Thickness(0, 16, 0, 8) };
            card.Children.Add(Text(s.Label + " · " + s.State, 18)); card.Children.Add(Text(s.Detail));
            if (s.Request is { } request)
            {
                card.Children.Add(Text(request.Kind == "approval" ? "Approve commands in the originating tool. Buddy can decline this request only." : "Review your exact reply before sending it to this paired session."));
                var reply = new TextBox { MaxLength = 2000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70 };
                if (request.Kind == "question")
                {
                    card.Children.Add(reply);
                    card.Children.Add(BuddyTheme.Button("Send this reply", () => Decide(s, request, "answer", reply.Text), true));
                }
                card.Children.Add(BuddyTheme.Button("Decline", () => Decide(s, request, "deny", null)));
            }
            card.Children.Add(BuddyTheme.Button("Disconnect session", () => { broker.Disconnect(s.SessionId); Refresh(); })); cards.Children.Add(card);
        }
    }
    private void Decide(LocalAgentSnapshot session, LocalAgentRequest request, string kind, string? text)
    {
        if (!pipe.Running) { status.Text = "The local listener is unavailable; no reply was queued. Close and reopen this window before pairing again."; return; }
        try { broker.Decide(session.SessionId, session.TaskId, request.Id, request.Digest, kind, text); status.Text = "Decision queued for the exact paired request."; }
        catch (InvalidOperationException) { status.Text = "This request changed or expired; nothing was relayed."; }
        Refresh();
    }
    private static TextBlock Text(string text, double size = 14) => new() { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), Foreground = BuddyTheme.Ink };
}
