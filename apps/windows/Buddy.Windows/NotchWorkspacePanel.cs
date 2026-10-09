using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;

namespace Buddy.Windows;

// This panel is embedded in the bar, but accepts typing only after the bar's
// explicit Edit button switches out of the passive no-activate presentation.
internal sealed class NotchWorkspacePanel : StackPanel, IDisposable
{
    private readonly NotchWorkspace workspace;
    private readonly TextBox note = Editor("Local note", 20000, 90);
    private readonly TextBlock noteStatus = Label("Notes are saved only when you choose Save note.");
    private readonly TextBox draft = Editor("Notch chat message", 4000, 84);
    private readonly TextBox history = Editor("Notch chat history", 20000, 110);
    private readonly TextBox preview = Editor("Notch file review", 12000, 100);
    private readonly ComboBox models = new() { MinHeight = 40, Margin = new(0, 4, 0, 4), DisplayMemberPath = "" };
    private readonly TextBlock status = Label("");
    private readonly TextBlock modelStatus = Label("");
    private readonly TextBlock fileStatus = Label("");
    private readonly TextBlock fileName = Label("No local file context");
    private readonly TextBlock contextStatus = Label("");
    private readonly Button save, reload, send, choose, attach, remove, newChat;
    private NotchNote? saved;
    private bool editing, painting, disposed;
    private string modelSignature = "";
    private long revision = -1;
    internal NotchWorkspacePanel(NotchWorkspace workspace)
    {
        this.workspace = workspace;
        Children.Add(Heading("Local note")); Children.Add(note);
        var noteActions = new WrapPanel(); save = Add(noteActions, "Save note", SaveNote); reload = Add(noteActions, "Reload saved note", ReloadNote); Children.Add(noteActions);
        Children.Add(noteStatus);
        Children.Add(Heading("Local chat"));
        AutomationProperties.SetName(models, "Notch local model");
        models.ItemContainerStyle = new Style(typeof(ComboBoxItem)) {
            Setters = { new Setter(IsEnabledProperty, new Binding(nameof(NotchModelChoice.CanSelect))), new Setter(ToolTipProperty, new Binding(nameof(NotchModelChoice.UnavailableReason))), new Setter(ToolTipService.ShowOnDisabledProperty, true) }
        };
        Children.Add(models); AutomationProperties.SetName(modelStatus, "Notch model availability"); Children.Add(modelStatus);
        Children.Add(Label(workspace.Chat.RetentionNotice));
        Children.Add(contextStatus);
        history.IsReadOnly = true; history.MaxHeight = 240; Children.Add(history);
        Children.Add(fileName); Children.Add(fileStatus); preview.IsReadOnly = true; preview.Visibility = Visibility.Collapsed; Children.Add(preview);
        var files = new WrapPanel(); choose = Add(files, "Choose text file", ChooseFile); attach = Add(files, "Attach reviewed text", () => {
            if (editing && workspace.Chat.Snapshot.File is { } item) workspace.Chat.AttachReviewed(item.Id);
        }); remove = Add(files, "Remove file", () => { if (editing) workspace.Chat.RemoveFile(); }); Children.Add(files);
        Children.Add(draft);
        var actions = new WrapPanel(); send = Add(actions, "Send local message", () => { if (editing) _ = workspace.Chat.Send(); });
        newChat = Add(actions, "New local chat", () => { if (editing) workspace.Chat.NewChat(); }); Children.Add(actions); Children.Add(status);
        if (workspace.OpenContext is not null) Add(actions, "Context & refine", () => { if (editing && !workspace.Chat.Snapshot.Busy) workspace.OpenContext(); });
        if (workspace.OpenExternalContext is not null) Add(actions, "External chat context", () => { if (editing && !workspace.Chat.Snapshot.Busy) workspace.OpenExternalContext(); });
        AutomationProperties.SetName(status, "Notch chat status"); AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(fileStatus, "Notch file status"); AutomationProperties.SetName(noteStatus, "Notch note status");
        note.TextChanged += (_, _) => { if (!painting) noteStatus.Text = saved is not null && note.Text == saved.Text ? "Saved note unchanged." : "Unsaved local note. Choose Save note to keep it."; };
        draft.TextChanged += (_, _) => { if (!painting) workspace.Chat.SetDraft(draft.Text); };
        draft.PreviewKeyDown += (_, e) => {
            if (editing && e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; _ = workspace.Chat.Send(); }
        };
        models.SelectionChanged += (_, _) => {
            if (painting || !editing || models.SelectedItem is not NotchModelChoice item) return;
            if (!workspace.Chat.SelectModel(item.Id)) { status.Text = item.UnavailableReason ?? "That model is unavailable for local chat."; Refresh(true); }
        };
        AllowDrop = true;
        PreviewDragOver += (_, e) => { e.Effects = CanDrop(e) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += (_, e) => {
            e.Handled = true;
            try {
                if (CanDrop(e) && e.Data.GetDataPresent(DataFormats.FileDrop,false) && e.Data.GetData(DataFormats.FileDrop,false) is string[] { Length: 1 } paths) _ = workspace.StageContextFile?.Invoke(paths[0]) ?? workspace.Chat.StageFile(paths[0]);
                else if (CanDrop(e) && e.Data.GetDataPresent(DataFormats.UnicodeText,false) && e.Data.GetData(DataFormats.UnicodeText,false) is string text && workspace.StageContextText is not null) workspace.StageContextText(text);
                else fileStatus.Text = "Drop one supported local file, browser text or HTTPS link while editing. Review it before use.";
            } catch { fileStatus.Text = "The offered drop could not be read. Save one supported file locally or paste text into Context."; }
        };
        workspace.Chat.Changed += ChatChanged;
        ReloadNote(); SetEditing(false); Refresh(true);
    }
    private bool CanDrop(System.Windows.DragEventArgs e)
    {
        try { return editing && !workspace.Chat.Snapshot.Busy &&
            (e.Data.GetDataPresent(DataFormats.FileDrop,false) && e.Data.GetData(DataFormats.FileDrop,false) is string[] { Length: 1 }
            || workspace.StageContextText is not null && e.Data.GetDataPresent(DataFormats.UnicodeText,false)); }
        catch { return false; }
    }
    private static TextBox Editor(string name, int limit, double height)
    {
        var text = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxLength = limit, MinHeight = height, MaxHeight = height + 80,
            Padding = new(8), VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Foreground = BuddyTheme.Ink, Background = BuddyTheme.Surface, BorderBrush = BuddyTheme.ControlBorder };
        AutomationProperties.SetName(text, name); return text;
    }
    private static TextBlock Label(string text) => new() { Text = text, Foreground = BuddyTheme.Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 4) };
    private static TextBlock Heading(string text) => new() { Text = text, Foreground = BuddyTheme.Ink, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new(0, 10, 0, 6) };
    private static Button Add(Panel panel, string title, Action action)
    {
        var button = new Button { Content = title, MinHeight = 40, Padding = new(8, 5, 8, 5), Margin = new(0, 4, 6, 4),
            Foreground = BuddyTheme.Ink, Background = BuddyTheme.Surface, BorderBrush = BuddyTheme.ControlBorder };
        AutomationProperties.SetName(button, title); button.Click += (_, _) => action(); panel.Children.Add(button); return button;
    }
    private void ChatChanged()
    {
        if (disposed || Dispatcher.HasShutdownStarted) return;
        if (Dispatcher.CheckAccess()) Refresh(); else _ = Dispatcher.BeginInvoke(new Action(() => { if (!disposed) Refresh(); }));
    }
    internal void SetEditing(bool value)
    {
        editing = value; note.IsReadOnly = !value;
        save.IsEnabled = value && saved is not null; reload.IsEnabled = value;
        Refresh(true);
    }
    internal void FocusDraft() { if (editing) draft.Focus(); }
    internal void Refresh(bool force = false)
    {
        if (disposed) return;
        var state = workspace.Chat.Snapshot;
        string signature = string.Join("\n", state.Models.Select(m => $"{m.Id}\t{m.Label}\t{m.Available}\t{m.Local}\t{m.UnavailableReason}"));
        if (!force && state.Revision == revision && signature == modelSignature) return;
        revision = state.Revision; painting = true;
        try {
            if (signature != modelSignature) { models.ItemsSource = state.Models; modelSignature = signature; }
            models.SelectedItem = state.Models.FirstOrDefault(m => m.Id == state.ModelId);
            var selected = state.Models.FirstOrDefault(m => m.Id == state.ModelId);
            modelStatus.Text = selected is { CanSelect: true } ? "Selected local model: " + selected.Label
                : selected?.UnavailableReason ?? "No ready local model is available. Check Buddy Home.";
            if (draft.Text != state.Draft) draft.Text = state.Draft;
            history.Text = state.Messages.Count == 0 ? "No messages in this local chat yet." : string.Join("\n\n", state.Messages.Select(m => m.Role + " · " + m.At.ToLocalTime().ToString("HH:mm") + "\n" + m.Text));
            status.Text = state.Status; fileStatus.Text = state.InboxStatus; contextStatus.Text=state.ContextNotice;
            fileName.Text = state.File is { } item ? (state.InboxPhase == NotchInboxPhase.Attached ? "Attached for next message: " : "Review local text: ") + item.Name : "No local file context";
            preview.Text = state.File?.Text ?? ""; preview.Visibility = state.File is null ? Visibility.Collapsed : Visibility.Visible;
            draft.IsReadOnly = !editing || state.Busy; models.IsEnabled = editing && !state.Busy;
            choose.IsEnabled = editing && !state.Busy; attach.IsEnabled = editing && !state.Busy && state.InboxPhase == NotchInboxPhase.Review;
            remove.IsEnabled = editing && !state.Busy && state.InboxPhase != NotchInboxPhase.Empty;
            send.IsEnabled = editing && !state.Busy && state.InboxPhase is not (NotchInboxPhase.Reading or NotchInboxPhase.Review)
                && state.Models.Any(m => m.Id == state.ModelId && m.Available && m.Local);
            newChat.IsEnabled = editing && !state.Busy && state.InboxPhase != NotchInboxPhase.Reading;
        } finally { painting = false; }
    }
    private void ReloadNote()
    {
        // Reload is an explicit discard of the editor draft; confirm when needed.
        if (saved is not null && note.Text != saved.Text && System.Windows.MessageBox.Show(Window.GetWindow(this), "Replace the unsaved editor text with the saved local note?", "Reload local note", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try {
            saved = workspace.Notes.Load(); painting = true; note.Text = saved.Text; painting = false;
            noteStatus.Text = saved.SavedAt is { } at ? "Saved on this PC: " + at.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "No saved note yet. Choose Save note to keep your text.";
            save.IsEnabled = editing;
        } catch { saved = null; noteStatus.Text = "The saved note could not be read. It has not been changed. Your editor text is kept."; save.IsEnabled = false; }
    }
    private void SaveNote()
    {
        if (!editing || saved is null) return;
        try { saved = workspace.Notes.Save(note.Text, saved.Revision); noteStatus.Text = "Saved on this PC: " + saved.SavedAt!.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm"); }
        catch (Exception error) { noteStatus.Text = "Could not save the note. Your editor text is kept. " + error.Message; }
    }
    private void ChooseFile()
    {
        if (!editing || workspace.Chat.Snapshot.Busy) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Choose text to review locally", Filter = "UTF-8 text or Markdown|*.txt;*.md", Multiselect = false, CheckFileExists = true };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) _ = workspace.Chat.StageFile(dialog.FileName);
    }
    public void Dispose() { if (disposed) return; disposed = true; workspace.Chat.Changed -= ChatChanged; }
}
