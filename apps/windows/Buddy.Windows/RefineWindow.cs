using Buddy.Server;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;

namespace Buddy.Windows;

internal sealed class RefineWindow : Window
{
    private readonly BuddyService service;
    private readonly string original;
    private readonly Func<string, CancellationToken, Task> applyEdit;
    private readonly Func<CancellationToken, Task> undoEdit;
    private readonly TextBlock progress = Label("Choose a mode to refine on this PC.", 12), score = Label("", 20), changes = Label("", 12);
    private readonly TextBox before, after;
    private readonly ComboBox technique = new() { ItemsSource = RefinementPolicy.Techniques, SelectedIndex = 0, Margin = new(0, 0, 0, 8) };
    private readonly Button apply, undo;
    private readonly List<Button> modeButtons = [];
    private CancellationTokenSource? operation;
    private RefinementResult? result;
    private bool applied, closed, applying;
    private int generation;
    private readonly Action<CompanionMood>? mood;
    private readonly Action? starting;

    internal RefineWindow(BuddyService service, string original, Func<string, CancellationToken, Task> applyEdit, Func<CancellationToken, Task> undoEdit, string source, Action<CompanionMood>? mood = null, Action? starting = null)
    {
        BuddyTheme.Ensure(); this.service = service; this.original = original; this.applyEdit = applyEdit; this.undoEdit = undoEdit; this.mood = mood; this.starting = starting;
        Title = "Buddy · Refine"; Width = 400; Height = 620; MinHeight = 420; MaxHeight = SystemParameters.WorkArea.Height; FontFamily = BuddyTheme.Font; Foreground = BuddyTheme.Ink;
        Background = BuddyTheme.Surface; ResizeMode = ResizeMode.CanResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new(20) }; var title = Label("Refine · on this PC", 13); title.Foreground = BuddyTheme.Deep; title.FontWeight = FontWeights.SemiBold; panel.Children.Add(title);
        panel.Children.Add(Label(source, 12));
        var modes = new WrapPanel();
        foreach (var name in new[] { "Quick", "Guided", "Council" }) {
            var button = BuddyTheme.Button(name, () => _ = Refine(name.ToLowerInvariant())); button.Padding = new(10, 6, 10, 6); modeButtons.Add(button); modes.Children.Add(button);
        }
        panel.Children.Add(modes); panel.Children.Add(Label("Technique", 12)); AutomationProperties.SetName(technique, "Refinement technique"); panel.Children.Add(technique);
        score.Foreground = BuddyTheme.Deep; score.FontWeight = FontWeights.Bold; panel.Children.Add(score); panel.Children.Add(changes);
        before = Editor(original); after = Editor(""); AutomationProperties.SetName(before, "Original prompt"); AutomationProperties.SetName(after, "Refined prompt");
        var originalExpander = new Expander { Header = "Original", Content = before, IsExpanded = false, Margin = new(0, 0, 0, 8), Foreground = BuddyTheme.Ink }; panel.Children.Add(originalExpander);
        panel.Children.Add(after); panel.Children.Add(progress); AutomationProperties.SetLiveSetting(progress, AutomationLiveSetting.Polite);
        var actions = new WrapPanel();
        apply = BuddyTheme.Button("Apply", () => _ = Apply(), true); apply.IsEnabled = false; actions.Children.Add(apply);
        actions.Children.Add(BuddyTheme.Button("Copy", () => { if (!string.IsNullOrEmpty(after.Text)) System.Windows.Clipboard.SetText(after.Text); }));
        undo = BuddyTheme.Button("Undo", () => _ = Undo()); undo.IsEnabled = false; actions.Children.Add(undo);
        actions.Children.Add(BuddyTheme.Button("Stop", Cancel)); panel.Children.Add(actions);
        panel.Children.Add(Label("Undo for 30 seconds · Buddy never submits the form", 12));
        panel.Children.Add(BuddyTheme.Button("Copy original", () => System.Windows.Clipboard.SetText(original)));
        Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        SourceInitialized += (_, _) => Native.SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, 0x11);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Cancel(); } };
        Closed += (_, _) => { closed = true; Cancel(); };
    }
    internal async Task Refine(string mode)
    {
        if (applied || applying) return;
        starting?.Invoke();
        Cancel(); int current = ++generation; var cts = new CancellationTokenSource(); operation = cts;
        mood?.Invoke(CompanionMood.Thinking);
        apply.IsEnabled = false; result = null; after.Text = ""; score.Text = ""; changes.Text = "";
        try {
            await foreach (var item in service.RefineStream(new(original, mode, technique.SelectedItem?.ToString() ?? "auto"), cts.Token)) {
                if (generation != current || closed) return;
                if (item.Type is "status" or "stage") progress.Text = item.Text;
                if (item.Type == "delta") after.AppendText(item.Text);
                if (item.Result is { } completed) {
                    result = completed; after.Text = completed.RefinedPrompt;
                    score.Text = completed.ScoreBefore is { } initial && completed.ScoreAfter is { } final ? $"{initial} → {final} · estimated" : "Original retained";
                    changes.Text = string.Join("\n", completed.Changes); progress.Text = completed.Message;
                    apply.IsEnabled = completed.Accepted;
                }
            }
        } catch (Exception e) {
            if (generation == current && !closed) progress.Text = e is OperationCanceledException ? "Stopped. Original unchanged." : e.Message;
        } finally { if (ReferenceEquals(operation, cts)) { operation = null; mood?.Invoke(CompanionMood.Idle); } cts.Dispose(); }
    }
    internal void Cancel()
    {
        generation++; operation?.Cancel(); apply.IsEnabled = false;
        mood?.Invoke(CompanionMood.Idle);
        if (!closed) progress.Text = applied ? "Stopped. Undo remains available for 30 seconds after applying." : "Stopped. Use Copy or choose a mode to try again.";
    }
    private async Task Apply()
    {
        if (result is not { Accepted: true } || applied || applying) return;
        apply.IsEnabled = false; applying = true; foreach (var button in modeButtons) button.IsEnabled = false;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3)); operation = cts;
        try { await applyEdit(result.RefinedPrompt, cts.Token); applied = true; undo.IsEnabled = true; foreach (var button in modeButtons) button.IsEnabled = false; progress.Text = "Applied without sending. Undo is available for 30 seconds."; }
        catch (Exception e) { progress.Text = e.Message + " Use Copy instead."; }
        finally { applying = false; if (!applied) foreach (var button in modeButtons) button.IsEnabled = true; if (ReferenceEquals(operation, cts)) operation = null; }
    }
    private async Task Undo()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3)); operation = cts; undo.IsEnabled = false;
        try { await undoEdit(cts.Token); progress.Text = "Exact original restored."; applied = false; foreach (var button in modeButtons) button.IsEnabled = true; }
        catch (Exception e) { progress.Text = e.Message + " Copy original remains available."; }
        finally { if (ReferenceEquals(operation, cts)) operation = null; }
    }
    private static TextBlock Label(string text, double size) => new() { Text = text, FontSize = size, Foreground = BuddyTheme.Muted, TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 8) };
    private static TextBox Editor(string text) => new() { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(0, 0, 0, 12) };
}
