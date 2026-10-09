using Buddy.Server;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Buddy.Windows;

// Local, explicit editing of a retained extraction. Saving only stages text;
// the context surface requires a separate review before preparing any draft.
internal sealed class OcrCorrectionWindow : Window
{
    private readonly TextBox editor;
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new(0, 8, 0, 8) };
    private readonly Image original;
    private readonly RefinementWorkspace workspace;
    private readonly RefinementChatScope scope;
    private readonly RefinementSourceSnapshot source;
    private readonly long revision;
    private readonly Action saved;
    private bool closed;

    internal OcrCorrectionWindow(RefinementWorkspace workspace, RefinementChatScope scope,
        RefinementSourceSnapshot source, long revision, Action saved)
    {
        this.workspace = workspace; this.scope = scope; this.source = source; this.revision = revision; this.saved = saved;
        BuddyTheme.Ensure(); Title = "Buddy — Correct extracted text"; Width = 860; Height = 640; MinWidth = 600; MinHeight = 420;
        MaxHeight = SystemParameters.WorkArea.Height; Background = BuddyTheme.Surface; Foreground = BuddyTheme.Ink; FontFamily = BuddyTheme.Font;
        using var asset = workspace.RetainOcrOriginalForReview(scope, source.Id, source.ReviewDigest, revision);
        byte[] encoded = asset.CopyBytes(); ContextImageInfo info;
        try { info = ContextSourceReader.ParseImage(encoded); }
        finally { CryptographicOperations.ZeroMemory(encoded); }
        using var snapshot = new ContextImageSnapshot(asset, info);
        original = new Image { Source = ContextSourceImagePreview.Decode(snapshot), Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        AutomationProperties.SetName(original, "Original selected image");
        var scale = new ScaleTransform(Math.Min(1, 340d / info.Width), Math.Min(1, 340d / info.Width)); original.LayoutTransform = scale;
        var zoom = new Slider { Minimum = .1, Maximum = 3, Value = scale.ScaleX, Margin = new(0, 8, 0, 8), SmallChange = .1, LargeChange = .5 };
        AutomationProperties.SetName(zoom, "Original image zoom"); zoom.ValueChanged += (_, _) => { scale.ScaleX = zoom.Value; scale.ScaleY = zoom.Value; };
        editor = new TextBox { Text = source.Text, MaxLength = RefinementWorkspace.MaximumTextCharacters, AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = BuddyTheme.Surface, Foreground = BuddyTheme.Ink, Padding = new(8) };
        AutomationProperties.SetName(editor, "Corrected extracted text");
        var root = new DockPanel { Margin = new(18) };
        var heading = new TextBlock { Text = "Compare with the original image and correct any misread letters or numbers. Saving changes only the extracted text; review it again before use.", TextWrapping = TextWrapping.Wrap, Margin = new(0, 0, 0, 12) };
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var footer = new StackPanel(); status.Foreground = BuddyTheme.Muted; footer.Children.Add(status);
        var actions = new WrapPanel();
        actions.Children.Add(BuddyTheme.Button("Reset edits", () => { editor.Text = source.Text; status.Text = "Edits reset to the text shown when this window opened."; }));
        actions.Children.Add(BuddyTheme.Button("Save corrections for review", Save));
        var cancel = BuddyTheme.Button("Cancel", Close); cancel.IsCancel = true; actions.Children.Add(cancel);
        footer.Children.Add(actions); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var columns = new Grid(); columns.ColumnDefinitions.Add(new()); columns.ColumnDefinitions.Add(new());
        var left = new DockPanel { Margin = new(0, 0, 12, 0) }; DockPanel.SetDock(zoom, Dock.Top); left.Children.Add(zoom);
        left.Children.Add(new ScrollViewer { Content = original, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        columns.Children.Add(left); Grid.SetColumn(editor, 1); columns.Children.Add(editor); root.Children.Add(columns); Content = root;
        SourceInitialized += (_, _) => CaptureProtection.Apply(new WindowInteropHelper(this).Handle);
        Closed += (_, _) => { closed = true; original.Source = null; editor.Clear(); };
    }

    private void Save()
    {
        if (closed) return;
        try { workspace.CorrectOcrText(scope, source.Id, source.ReviewDigest, editor.Text, revision); saved(); Close(); }
        catch (Exception error) { status.Text = error.Message; }
    }
}
