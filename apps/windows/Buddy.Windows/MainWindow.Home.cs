using Buddy.Server;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using CheckBox = System.Windows.Controls.CheckBox;

namespace Buddy.Windows;

public sealed partial class MainWindow
{
    internal static readonly string[] HomeSections = ["Conversations", "Walkthroughs", "Memories", "Prompts", "Devices", "Settings"];
    private readonly ContentControl homeBody = new(), settingsBody = new();
    private readonly Dictionary<string, Button> navigationButtons = [];
    private Grid chatView = null!;
    private string homeSection = "Conversations", settingsSection = "General", conversationQuery = "";
    private bool showArchived;
    private int pageRevision;
    internal string VisibleHomeSection => homeSection;
    internal string VisibleSettingsSection => settingsSection;

    private void ShowChat()
    {
        homeSection = "Conversations"; pageRevision++; homeBody.Content = chatView; MarkNavigation(); input.Focus();
    }
    internal void NavigateHome(string section)
    {
        if (!HomeSections.Contains(section)) return;
        homeSection = section; int revision = ++pageRevision; MarkNavigation(); homeBody.Content = null;
        if (section == "Settings") { BuildSettings(); return; }
        var p = new StackPanel(); homeBody.Content = new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        if (section == "Conversations") { BuildConversations(p, revision); return; }
        p.Children.Add(Text(section, 28));
        if (host is null) { p.Children.Add(Text("Buddy is starting…", 14, Muted)); return; }
        _ = LoadLibrary(p, section, revision);
    }
    private void MarkNavigation()
    {
        foreach (var item in navigationButtons) {
            item.Value.Background = item.Key == homeSection ? BuddyTheme.Soft : Brushes.Transparent;
            item.Value.Foreground = item.Key == homeSection ? Accent : Muted;
            AutomationProperties.SetHelpText(item.Value, item.Key == homeSection ? "Current section" : "Open " + item.Key);
        }
    }
    private void BuildConversations(StackPanel p, int revision)
    {
        p.Children.Add(Text("Conversations", 28));
        var actions = new WrapPanel(); actions.Children.Add(Btn("New conversation", () => _ = NewChat(), true)); actions.Children.Add(Btn("Guide this screen", () => StartWorkflow("guide", input.Text))); actions.Children.Add(Btn("Agent task", () => StartWorkflow("agent", input.Text))); p.Children.Add(actions);
        var search = new TextBox { Text = conversationQuery, Margin = new(0, 0, 0, 12) }; StyleBox(search); AutomationProperties.SetName(search, "Search conversations"); search.ToolTip = "Search conversation titles and messages"; p.Children.Add(search);
        var archived = new CheckBox { Content = "Show archived conversations", IsChecked = showArchived }; p.Children.Add(archived);
        var cards = new StackPanel(); p.Children.Add(cards); int queryRevision = 0;
        async Task Fill()
        {
            int query = ++queryRevision;
            if (host is null) { cards.Children.Clear(); cards.Children.Add(Text("Buddy is starting…", 14, Muted)); return; }
            var items = await host.Service.Store.Read(s => s.Conversations.Where(c => c.Archived == showArchived && (conversationQuery.Length == 0 || c.Title.Contains(conversationQuery, StringComparison.OrdinalIgnoreCase) || c.Messages.Any(m => m.Text.Contains(conversationQuery, StringComparison.OrdinalIgnoreCase)))).OrderByDescending(c => c.Pinned).ThenByDescending(c => c.UpdatedAt).ToList());
            if (pageRevision != revision || queryRevision != query) return;
            cards.Children.Clear();
            if (items.Count == 0) cards.Children.Add(BuddyTheme.Card(Text(conversationQuery.Length > 0 ? "No conversations match your search." : showArchived ? "No archived conversations." : "Start a conversation. Your history stays on this PC.", 14, Muted)));
            foreach (var c in items) {
                var card = new StackPanel(); var title = Text((c.Pinned ? "Pinned · " : "") + c.Title, 16); title.FontWeight = FontWeights.SemiBold; card.Children.Add(title);
                card.Children.Add(Text(c.UpdatedAt.ToLocalTime().ToString("g") + " · on this PC" + (c.Messages.Any(m => m.Mode == "voice") ? " · voice" : ""), 12, Accent));
                var badges = c.Messages.Select(m => SourceLinks.Badge(m.Evidence)).Where(b => b.Length > 0).Distinct().Take(3);
                card.Children.Add(Text(string.Join(" | ", badges), 12, Muted));
                var preview = c.Messages.LastOrDefault()?.Text ?? "No messages yet";
                card.Children.Add(Text(preview.Length > 220 ? preview[..220] + "…" : preview, 14, Muted));
                var buttons = new WrapPanel();
                buttons.Children.Add(Btn("Open", () => _ = Select(c.Id)));
                buttons.Children.Add(Btn(c.Pinned ? "Unpin" : "Pin", async () => { try { await host.Service.UpdateConversation(c.Id, new(Pinned: !c.Pinned)); await Fill(); } catch (Exception e) { status.Text = e.Message; } }));
                buttons.Children.Add(Btn(c.Archived ? "Restore" : "Archive", async () => { try { await host.Service.UpdateConversation(c.Id, new(Archived: !c.Archived)); await Fill(); } catch (Exception e) { status.Text = e.Message; } }));
                card.Children.Add(buttons); cards.Children.Add(BuddyTheme.Card(card));
            }
        }
        search.TextChanged += (_, _) => { conversationQuery = search.Text; _ = Fill(); };
        archived.Checked += (_, _) => { showArchived = true; _ = Fill(); };
        archived.Unchecked += (_, _) => { showArchived = false; _ = Fill(); };
        _ = Fill();
    }
    private async Task LoadLibrary(StackPanel p, string section, int revision)
    {
        try {
            if (section == "Walkthroughs") {
                p.Children.Add(Btn("Try pointing tutorial", ShowPractice, true));
                var guides = await host!.Service.Store.Read(s => s.Guides.OrderByDescending(g => g.UpdatedAt).ToList());
                if (revision != pageRevision) return;
                if (guides.Count == 0) p.Children.Add(Text("Your walkthroughs will appear here. Try the sample without waiting for a model.", 14, Muted));
                foreach (var guide in guides) {
                    var card = new StackPanel(); card.Children.Add(Text(guide.Query, 16));
                    card.Children.Add(Text(guide.Completed ? "Completed" : $"Step {guide.Index + 1} of {guide.Plan.Steps?.Count ?? 0}", 12, Accent));
                    card.Children.Add(Btn(guide.Completed ? "Start again" : "Resume", () => { if (assistant is not null) _ = assistant.Resume(guide.Id); }));
                    p.Children.Add(BuddyTheme.Card(card));
                }
            } else if (section is "Memories" or "Prompts") {
                bool memory = section == "Memories";
                p.Children.Add(Text(memory ? "Only facts you explicitly save are remembered." : "Save prompts and reuse them in a new draft.", 14, Muted));
                var notes = await host!.Service.Store.Read(s => (memory ? s.Memories : s.Prompts).ToList());
                if (revision != pageRevision) return;
                foreach (var n in notes) {
                    var card = new StackPanel(); card.Children.Add(Text(n.Title, 16, Accent)); card.Children.Add(Text(n.Text, 14));
                    var actions = new WrapPanel();
                    if (!memory) actions.Children.Add(Btn("Use prompt", async () => { await NewChat(); input.Text = n.Text; }));
                    actions.Children.Add(Btn("Delete", async () => { await host.Service.Store.Update(s => (memory ? s.Memories : s.Prompts).RemoveAll(x => x.Id == n.Id)); NavigateHome(section); }));
                    card.Children.Add(actions); p.Children.Add(BuddyTheme.Card(card));
                }
                var title = new TextBox { Margin = new(0, 0, 0, 8) }; var body = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100 };
                AutomationProperties.SetName(title, "Saved item title"); AutomationProperties.SetName(body, memory ? "Memory text" : "Prompt text"); StyleBox(title); StyleBox(body);
                p.Children.Add(Text("Title", 14)); p.Children.Add(title); p.Children.Add(Text(memory ? "Memory" : "Prompt", 14)); p.Children.Add(body);
                var notice = Text("", 12, Accent); p.Children.Add(notice);
                p.Children.Add(Btn("Save", async () => {
                    try { var note = new Note(Guid.NewGuid().ToString(), Security.Text(title.Text, 100, "Title"), Security.Text(body.Text, 2000, "Text")); await host.Service.Store.Update(s => { var list = memory ? s.Memories : s.Prompts; if (list.Count >= 50) throw new BuddyException("LIMIT", "Remove an old item first."); list.Add(note); return true; }); NavigateHome(section); }
                    catch (Exception e) { notice.Text = e.Message; }
                }, true));
            } else if (section == "Devices") {
                var devices = await host!.Service.Store.Read(s => s.Devices.ToList());
                if (revision != pageRevision) return;
                p.Children.Add(Btn("Pair Android phone", Pair, true));
                if (devices.Count == 0) p.Children.Add(Text("No paired phones. Connect both devices to the same Wi-Fi to pair.", 14, Muted));
                foreach (var device in devices) {
                    var card = new StackPanel(); card.Children.Add(Text(device.Name, 16));
                    card.Children.Add(Text("Paired " + device.AddedAt.ToLocalTime().ToString("g"), 12, Muted));
                    card.Children.Add(Btn("Revoke access", async () => { await host.Service.Store.Update(s => s.Devices.RemoveAll(d => d.Id == device.Id)); NavigateHome("Devices"); }));
                    p.Children.Add(BuddyTheme.Card(card));
                }
            }
        } catch (Exception e) { if (revision == pageRevision) p.Children.Add(Text(e.Message, 14, BuddyTheme.Risk)); }
    }
}
