using Buddy.Server;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;

namespace Buddy.Windows;

internal static class SourceLinks
{
    internal static string Badge(MessageEvidence? evidence) => evidence is null ? "" :
        (evidence.Screen ? "Screen" + (string.IsNullOrWhiteSpace(evidence.App) ? "" : " · " + evidence.App) + (evidence.Image ? " · local vision" : "") : "") +
        (evidence.Sources?.Count > 0 ? (evidence.Screen ? " · " : "") + evidence.Sources.Count + " sources" : "");
    internal static void Fill(Panel panel, MessageEvidence? evidence)
    {
        panel.Children.Clear();
        var badge = Badge(evidence);
        if (badge.Length > 0) panel.Children.Add(new TextBlock { Text = badge, Foreground = BuddyTheme.Deep, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new(0, 4, 0, 4) });
        foreach (var source in evidence?.Sources?.DistinctBy(s => s.Url).Take(8) ?? []) {
            try { WebResearch.ValidateUrl(source.Url); } catch { continue; }
            var button = BuddyTheme.Button(source.Title.Length > 65 ? source.Title[..65] + "…" : source.Title, () => {
                try { var url = WebResearch.ValidateUrl(source.Url); Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }); }
                catch (Exception e) { panel.Children.Add(new TextBlock { Text = e.Message, Foreground = BuddyTheme.Risk, TextWrapping = TextWrapping.Wrap }); }
            });
            button.HorizontalContentAlignment = HorizontalAlignment.Left; button.ToolTip = source.Url;
            AutomationProperties.SetName(button, "Open source: " + source.Title); panel.Children.Add(button);
        }
    }
}
