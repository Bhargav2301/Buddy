using Buddy.Server;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace Buddy.Windows;

internal sealed class ExternalRefinementOptionsWindow : Window
{
    private readonly RefinementOptionsPanel options = new();
    private readonly ComboBox mode = new() { ItemsSource = RefinementPolicy.Modes, SelectedItem = "auto", Margin = new(0, 0, 0, 8) };
    private readonly TextBlock status = Note("No source field has been captured. These options are kept only in memory.");
    private bool closed;

    internal ExternalRefinementOptionsWindow(Action<RefinementDraftOptions, string> prepare,
        IReadOnlyList<RefinementContextSource>? sources = null, Func<bool>? contextCurrent = null)
    {
        BuddyTheme.Ensure();
        foreach (var source in sources ?? []) options.AddReviewedResource(source, locked: true);
        Title = "Buddy - Prepare source-field refinement";
        Width = 610; MinWidth = 380; Height = 740; MinHeight = 420;
        MaxHeight = SystemParameters.WorkArea.Height;
        FontFamily = BuddyTheme.Font; Background = BuddyTheme.Surface; Foreground = BuddyTheme.Ink;
        var layout = new DockPanel();
        var footer = new StackPanel { Margin = new(18, 8, 18, 14) };
        var actions = new WrapPanel();
        actions.Children.Add(BuddyTheme.Button("Use for next capture", () => {
            if (closed) return;
            if (options.IsReadingResource || options.HasPendingResource || options.HasUnaddedReference) {
                status.Text = "Finish reviewing references, then add or discard the selected file and add or clear any pasted reference before preparing options."; return;
            }
            try { if(contextCurrent?.Invoke()==false)throw new InvalidOperationException("Selected context changed. Review it again."); prepare(options.Snapshot(), mode.SelectedItem?.ToString() ?? "auto"); Close(); }
            catch (Exception ex) { status.Text = ex.Message; }
        }, true));
        actions.Children.Add(BuddyTheme.Button("Cancel", Close));
        footer.Children.Add(actions); footer.Children.Add(status);
        DockPanel.SetDock(footer, Dock.Bottom); layout.Children.Add(footer);
        var body = new StackPanel { Margin = new(18) };
        body.Children.Add(Note("Prepare options, then focus the exact original prompt field and press Ctrl+Alt+R within five minutes. The next explicit refinement capture consumes these options, even if capture fails. Automatic suggestions pause while these options await capture."));
        body.Children.Add(Note("Changing options discards any current source-field review. Technique prerequisites and the full destination count are checked against the freshly captured prompt. Review and Accept are still required; Buddy never sends the form."));
        body.Children.Add(Note("Refinement mode")); body.Children.Add(mode); body.Children.Add(options);
        AutomationProperties.SetName(mode, "External refinement mode");
        AutomationProperties.SetName(status, "Source-field options status");
        layout.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }); Content = layout;
        SourceInitialized += (_, _) => CaptureProtection.Apply(new WindowInteropHelper(this).Handle);
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        Closed += (_, _) => { closed = true; options.Dispose(); };
    }
    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = BuddyTheme.Muted, Margin = new(0, 0, 0, 8) };
}
