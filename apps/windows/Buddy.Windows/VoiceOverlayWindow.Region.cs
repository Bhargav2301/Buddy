using Buddy.Server;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Buddy.Windows;

internal sealed partial class VoiceOverlayWindow
{
    private bool regionSession;
    private readonly StackPanel regionResearch = new() { Visibility = Visibility.Collapsed };
    private readonly System.Windows.Controls.TextBox researchQuery = new() { MaxLength = 300, MinHeight = 72, TextWrapping = TextWrapping.Wrap, AcceptsReturn = false };
    private readonly System.Windows.Controls.Button researchSubmit = new() { Content = "Search this text", Margin = new(0, 6, 0, 6) };
    private readonly System.Windows.Controls.Button researchSuggest = new() { Content = "Suggest a query locally", Margin = new(0, 6, 0, 6) };
    private void EnableRegionResearch(bool enabled) => researchSubmit.IsEnabled = researchQuery.IsEnabled = researchSuggest.IsEnabled = enabled;

    private void SubmitReview()
    {
        if (listening || request is { } active && sending.Contains(active)) return;
        var words = review.Text.Trim();
        if (words.Length == 0) {
            if (!regionSession) return;
            words = "Explain the selected area in the current app.";
        }
        review.Visibility = confirmTranscript.Visibility = Visibility.Collapsed;
        _ = Send(words);
    }
    private void EditRegionQuestion()
    {
        if (listening || request is { } active && sending.Contains(active)) return;
        review.Text = lastText;
        review.Visibility = confirmTranscript.Visibility = Visibility.Visible;
        confirmTranscript.Content = "Explain selected area";
        Show(); Activate(); review.Focus();
        Status("Edit your question - Enter sends locally; Shift+Enter adds a line", CompanionMood.Idle);
    }
    private void AddRegionResearch(Panel parent)
    {
        var content = new StackPanel();
        content.Children.Add(new TextBlock {
            Text = "Optional public web research: enter and review a short query. Only this text goes to web search; selected pixels and local answers stay on your PC. Avoid personal details.",
            TextWrapping = TextWrapping.Wrap, Foreground = BuddyTheme.Muted, FontSize = 12
        });
        AutomationProperties.SetName(researchQuery, "Review exact public web query");
        content.Children.Add(researchQuery); content.Children.Add(researchSuggest); content.Children.Add(researchSubmit);
        researchSuggest.Click += async (_, _) => await SuggestRegionQuery();
        researchSubmit.Click += async (_, _) => await SearchReviewedRegion();
        regionResearch.Children.Add(new Expander { Header = "Review web query", Content = content });
        parent.Children.Add(regionResearch);
    }
    private async Task SuggestRegionQuery()
    {
        if (!regionSession || listening || request is { } active && sending.Contains(active)) return;
        var host = service(); if (host is null) { Status("Buddy is still starting.", CompanionMood.Idle); return; }
        string question = lastText, explanation = answer.Text;
        if (string.IsNullOrWhiteSpace(question) || string.IsNullOrWhiteSpace(explanation)) {
            Status("Explain the selected area locally first, or type your own query.", CompanionMood.Idle); return;
        }
        request?.Dispose(); var source = new CancellationTokenSource(); request = source; sending.Add(source); int token = generation;
        EnableRegionResearch(false); speaker?.Cancel();
        Status("Preparing a query on your PC. Nothing is being sent to web search.", CompanionMood.Thinking);
        try {
            var result = await host.PrepareRegionResearchQuery(question, explanation, source.Token);
            source.Token.ThrowIfCancellationRequested();
            if (generation != token || !ReferenceEquals(request, source) || !regionSession) return;
            if (!string.IsNullOrWhiteSpace(result.Query)) researchQuery.Text = result.Query;
            Status(result.Message, CompanionMood.Idle);
        } catch (OperationCanceledException) {
            if (generation == token && ReferenceEquals(request, source)) Status("Query preparation stopped. Your query is kept.", CompanionMood.Idle);
        } catch (Exception) {
            if (generation == token && ReferenceEquals(request, source)) Status("A query could not be prepared. You can type and review your own.", CompanionMood.Error);
        } finally {
            if (ReferenceEquals(request, source)) request = null;
            sending.Remove(source); source.Dispose();
            if (generation == token) EnableRegionResearch(true);
        }
    }
    private async Task SearchReviewedRegion()
    {
        if (!regionSession || listening || request is { } active && sending.Contains(active)) return;
        if (!preferences().AllowWebResearch) { Status("Enable Internet research in Settings, then review and submit this query.", CompanionMood.Idle); return; }
        var host = service(); if (host is null) { Status("Buddy is still starting.", CompanionMood.Idle); return; }
        string approved = researchQuery.Text;
        if (string.IsNullOrWhiteSpace(approved)) { Status("Enter a short public query to review first.", CompanionMood.Idle); return; }
        request?.Dispose(); var source = new CancellationTokenSource(); request = source; sending.Add(source); int token = generation;
        EnableRegionResearch(false);
        teaching.HideGuidance(); nextTeaching.Visibility = Visibility.Collapsed;
        speaker?.Cancel(); Status("Researching the text you approved.", CompanionMood.Researching);
        try {
            var result = await host.ResearchReviewedRegion(approved, source.Token);
            source.Token.ThrowIfCancellationRequested(); if (generation != token || !ReferenceEquals(request, source)) return;
            answer.Text = result.Speech;
            bool attached = SourceLinks.Fill(sources, new MessageEvidence(Sources: result.Sources));
            await Speak(result.Speech, attached);
            source.Token.ThrowIfCancellationRequested();
            if (generation == token) Status("Research shown - microphone off; sources stay attached", CompanionMood.Idle);
        } catch (OperationCanceledException) { if (generation == token) Status("Research stopped. Your reviewed query is kept.", CompanionMood.Idle); }
        catch (Exception ex) { if (generation == token) Status("Research unavailable: " + ex.Message, CompanionMood.Error); }
        finally {
            if (ReferenceEquals(request, source)) request = null;
            sending.Remove(source); source.Dispose();
            if (generation == token) EnableRegionResearch(true);
        }
    }
}
