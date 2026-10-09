using Buddy.Server;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;

namespace Buddy.Windows;

internal static class SourceLinks
{
    internal static string Badge(MessageEvidence? evidence)
    {
        if (evidence is null) return "";
        var parts = new List<string>();
        if (evidence.Screen) parts.Add("Screen" + (string.IsNullOrWhiteSpace(evidence.App) ? "" : " · " + evidence.App) + (evidence.Image ? " · local vision" : ""));
        if (evidence.File) parts.Add("Reviewed file text");
        if (evidence.Sources?.Count > 0) parts.Add(evidence.Sources.Count + " sources");
        return string.Join(" · ", parts);
    }
    internal static bool Fill(Panel panel, MessageEvidence? evidence)
    {
        bool attached = false;
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
            AutomationProperties.SetName(button, "Open source: " + source.Title); panel.Children.Add(button); attached = true;
        }
        return attached;
    }
}
