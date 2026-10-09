using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Buddy.Windows;

public sealed partial class MainWindow
{
    private Func<DesktopPreferences>? pendingPreferenceReader;
    private TextBlock? preferenceNotice;
    private DesktopPreferences? preferenceEditorBaseline;
    private bool preferencesPending, savingPreferences;
    private string preferenceRuntimeWarning = "";
    private readonly DispatcherTimer preferenceSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };

    private void ClearPreferenceEditor()
    {
        preferenceSaveTimer.Stop(); pendingPreferenceReader = null; preferenceEditorBaseline = null; preferenceNotice = null; preferencesPending = false;
    }
    private void BindPreferenceEditor(Panel panel, TextBlock notice, Func<DesktopPreferences> next)
    {
        pendingPreferenceReader = next; preferenceEditorBaseline = next(); preferenceNotice = notice;
        notice.Text = preferenceRuntimeWarning.Length == 0 ? "Changes save on this PC automatically." : "Saved on this PC. " + preferenceRuntimeWarning;
        preferenceSaveTimer.Tick -= SavePendingPreferenceTick;
        preferenceSaveTimer.Tick += SavePendingPreferenceTick;
        foreach (var child in PreferenceControls(panel)) {
            if (child is CheckBox check) {
                RoutedEventHandler changed = (_, _) => { MarkPreferencesPending(); FlushPendingPreferences(); };
                check.Checked += changed; check.Unchecked += changed;
            }
            else if (child is ComboBox combo) combo.SelectionChanged += (_, _) => { MarkPreferencesPending(); FlushPendingPreferences(); };
            else if (child is System.Windows.Controls.TextBox text) text.TextChanged += (_, _) => MarkPreferencesPending();
        }
    }
    private static IEnumerable<DependencyObject> PreferenceControls(DependencyObject parent)
    {
        foreach (object child in LogicalTreeHelper.GetChildren(parent)) if (child is DependencyObject value) {
            yield return value;
            if (value is not (CheckBox or ComboBox or System.Windows.Controls.TextBox))
                foreach (var descendant in PreferenceControls(value)) yield return descendant;
        }
    }
    private void SavePendingPreferenceTick(object? sender, EventArgs e) => FlushPendingPreferences();
    private void MarkPreferencesPending()
    {
        if (pendingPreferenceReader is null || savingPreferences) return;
        preferencesPending = true;
        if (preferenceNotice is not null) preferenceNotice.Text = "Saving changes on this PC...";
        preferenceSaveTimer.Stop(); preferenceSaveTimer.Start();
    }
    internal bool FlushPendingPreferences()
    {
        preferenceSaveTimer.Stop();
        if (!preferencesPending || pendingPreferenceReader is null || savingPreferences) return true;
        savingPreferences = true;
        try {
            var displayed = pendingPreferenceReader();
            var next = DesktopPreferences.ApplyChanges(desktop, preferenceEditorBaseline ?? desktop, displayed);
            // Clear before applying because a display-mode change can rebuild Settings.
            preferencesPending = false;
            SavePreferences(next);
            if (pendingPreferenceReader is not null) preferenceEditorBaseline = pendingPreferenceReader();
            if (preferenceNotice is not null) preferenceNotice.Text = "Saved on this PC." + (preferenceRuntimeWarning.Length == 0 ? "" : " " + preferenceRuntimeWarning);
            return true;
        } catch (Exception ex) {
            preferencesPending = true;
            if (preferenceNotice is not null) preferenceNotice.Text = "Not saved. Your edits are still here. " + ex.Message;
            return false;
        } finally { savingPreferences = false; }
    }
}
