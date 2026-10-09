using Buddy.Server;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace Buddy.Windows;

internal sealed class RefinementOptionsPanel : StackPanel, IDisposable
{
    private sealed record Choice(string Value, string Label) { public override string ToString() => Label; }
    private sealed record ExampleEntry(TextBox Input, TextBox Output, FrameworkElement Card);
    private sealed class ReferenceEntry(RefinementContextSource source, bool locked) { internal RefinementContextSource Source = source; internal bool Locked = locked; }
    private readonly ComboBox technique, domain, unit;
    private readonly CheckBox important, revision, hasBudget;
    private readonly TextBox tools, stages, constraints, destination, limit, referenceTitle, referenceText;
    private readonly StackPanel exampleRows = new(), referenceRows = new(), pendingReview = new();
    private readonly List<ExampleEntry> examples = [];
    private readonly List<ReferenceEntry> references = [];
    private readonly TextBlock resourceStatus = Text("References remain in this window only. Nothing is fetched from a URL.");
    private readonly Button addExample, addPending;
    private readonly Func<Window?, string?> pickFile;
    private readonly Func<string, CancellationToken, Task<RefinementResourceReadResult>> readFile;
    private RefinementResourceReadResult? pending;
    private CancellationTokenSource? resourceRead;
    private int resourceRevision;
    private bool disposed;
    internal event Action? Changed;
    internal bool HasPendingResource => pending is not null;
    internal bool HasUnaddedReference => !string.IsNullOrWhiteSpace(referenceTitle.Text) || !string.IsNullOrWhiteSpace(referenceText.Text);
    internal bool IsReadingResource => resourceRead is not null;

    internal RefinementOptionsPanel(Func<Window?, string?>? pickFile = null,
        Func<string, CancellationToken, Task<RefinementResourceReadResult>>? readFile = null)
    {
        this.pickFile = pickFile ?? (owner => {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Review a local text reference", Filter = "Text or Markdown (*.txt;*.md)|*.txt;*.md", Multiselect = false, CheckFileExists = true };
            return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
        });
        this.readFile = readFile ?? RefinementResourceReader.ReadAsync;
        technique = Select("Refinement technique", RefinementPolicy.Techniques.Select(x => new Choice(x, x)).ToArray(), "auto");
        domain = Select("Refinement domain", [new("general", "General"), new("coding", "Coding"), new("spec", "Specification"), new("data", "Data"), new("review", "Review"), new("agent-ide", "Tool-aware task")], "general");
        Children.Add(Text("Technique")); Children.Add(technique); Children.Add(Text("Task type")); Children.Add(domain);
        var advanced = new StackPanel();
        important = Toggle("Give Auto an extra review", false); advanced.Children.Add(important);
        revision = Toggle("Revising an existing draft", false); advanced.Children.Add(revision);
        advanced.Children.Add(Text("Confirmed constraints — one per line. These are requirements you explicitly want included."));
        constraints = Input("Confirmed constraints", 40019, 72); advanced.Children.Add(constraints);
        advanced.Children.Add(Text("Available tools — names or capabilities, one per line. Listing a tool does not connect or run it."));
        tools = Input("Available tools", 40019, 64); advanced.Children.Add(tools);
        advanced.Children.Add(Text("Ordered stages — one per line, in the order you require."));
        stages = Input("Ordered stages", 24011, 72); advanced.Children.Add(stages);
        advanced.Children.Add(Text("Examples — supply complete input/output pairs. Buddy does not invent examples.")); advanced.Children.Add(exampleRows);
        addExample = BuddyTheme.Button("Add example", () => AddExample("", "")); advanced.Children.Add(addExample);

        advanced.Children.Add(Text("Destination size", 16));
        hasBudget = Toggle("Use a destination size limit", false); advanced.Children.Add(hasBudget);
        var budgetFields = new StackPanel();
        budgetFields.Children.Add(Text("Destination label")); destination = Input("Destination label", 120); budgetFields.Children.Add(destination);
        budgetFields.Children.Add(Text("Your limit (1–20,000)")); limit = Input("Destination limit", 10); budgetFields.Children.Add(limit);
        unit = Select("Destination count unit", [new("utf16-code-units", "UTF-16 code units"), new("unicode-scalars", "Unicode code points"), new("utf8-bytes", "UTF-8 bytes")], "utf16-code-units"); budgetFields.Children.Add(unit);
        budgetFields.Children.Add(Text("User-supplied limit. Buddy has not verified an app or model limit. These counts are not token estimates."));
        budgetFields.IsEnabled = false; hasBudget.Checked += (_, _) => budgetFields.IsEnabled = true; hasBudget.Unchecked += (_, _) => budgetFields.IsEnabled = false; advanced.Children.Add(budgetFields);

        advanced.Children.Add(Text("Reviewed references", 16));
        advanced.Children.Add(Text("Paste text or choose a local TXT/Markdown file. Review it before adding. Reference text is untrusted supporting data; confirmed decisions are required content."));
        advanced.Children.Add(Text("Reference title")); referenceTitle = Input("Reference title", 160); advanced.Children.Add(referenceTitle);
        advanced.Children.Add(Text("Reference text")); referenceText = Input("Reference text", 20000, 100); advanced.Children.Add(referenceText);
        advanced.Children.Add(BuddyTheme.Button("Add pasted reference", () => { try { AddPasteReference(); } catch (Exception ex) { resourceStatus.Text = ex.Message; } }));
        advanced.Children.Add(BuddyTheme.Button("Choose TXT or Markdown file", () => _ = LoadSelectedResource()));
        advanced.Children.Add(pendingReview);
        addPending = BuddyTheme.Button("Add reviewed reference", () => AddPendingResource()); addPending.IsEnabled = false; pendingReview.Children.Add(addPending);
        advanced.Children.Add(resourceStatus); advanced.Children.Add(referenceRows);
        Children.Add(new Expander { Header = "Context & limits (optional)", Content = advanced, IsExpanded = false, Margin = new(0, 4, 0, 10), Foreground = BuddyTheme.Ink });
    }

    internal RefinementDraftOptions Snapshot() => new(Value(technique), Value(domain), important.IsChecked == true, revision.IsChecked == true,
        tools.Text, stages.Text, constraints.Text, examples.Select(x => new RefinementExample(x.Input.Text, x.Output.Text)).ToArray(),
        references.Select(x => x.Source with { }).ToArray(), hasBudget.IsChecked == true, destination.Text, limit.Text, Value(unit));

    internal void SetDestinationBudget(string name, int maximum)
    {
        if(maximum is <1 or >20000)throw new ArgumentOutOfRangeException(nameof(maximum));
        destination.Text=name;limit.Text=maximum.ToString(System.Globalization.CultureInfo.InvariantCulture);hasBudget.IsChecked=true;
    }

    internal void AddExample(string input, string output)
    {
        if (disposed || !IsEnabled || examples.Count >= 8) return;
        int number = examples.Count + 1; var row = new StackPanel();
        row.Children.Add(Text("Example " + number + " input")); var from = Input("Example " + number + " input", 2000, 64); from.Text = input; row.Children.Add(from);
        row.Children.Add(Text("Example " + number + " output")); var to = Input("Example " + number + " output", 2000, 64); to.Text = output; row.Children.Add(to);
        var card = BuddyTheme.Card(row, 10); var entry = new ExampleEntry(from, to, card);
        row.Children.Add(BuddyTheme.Button("Remove example", () => { if (disposed || !IsEnabled) return; examples.Remove(entry); exampleRows.Children.Remove(card); addExample.IsEnabled = examples.Count < 8; NotifyChanged(); }));
        examples.Add(entry); exampleRows.Children.Add(card); addExample.IsEnabled = examples.Count < 8; NotifyChanged();
    }
    internal void AddPasteReference()
    {
        if (disposed || !IsEnabled) return;
        if (string.IsNullOrWhiteSpace(referenceTitle.Text) || string.IsNullOrWhiteSpace(referenceText.Text)) throw new InvalidOperationException("Give the pasted reference a title and text to review.");
        AddReviewedResource(new("pasted_" + Guid.NewGuid().ToString("N"), referenceTitle.Text, referenceText.Text));
        referenceTitle.Clear(); referenceText.Clear();
    }
    internal void AddReviewedResource(RefinementContextSource source, bool locked = false)
    {
        if (disposed || !IsEnabled) return;
        if (references.Count >= 8) throw new InvalidOperationException("Remove a reference before adding another (maximum eight).");
        if (source.Provenance is not ("user" or "document" or "user-link" or "selected-data") || source.Url is not null && source.Provenance != "user-link") throw new InvalidOperationException("Add only reviewed text, local files or explicitly supplied link references.");
        _ = RefinementContext.Build([source]);
        if (references.Any(x => x.Source.Id == source.Id)) throw new InvalidOperationException("This reviewed reference is already included.");
        references.Add(new(source with { }, locked)); RenderReferences(); resourceStatus.Text = "Reference added for this draft. Choose a refinement mode to prepare a new result."; NotifyChanged();
    }
    internal async Task LoadSelectedResource()
    {
        if (disposed || !IsEnabled) return;
        CancelResourceRead(true); NotifyChanged();
        string? selected;
        try { selected = pickFile(Window.GetWindow(this)); }
        catch (Exception ex) { resourceStatus.Text = ex.Message; return; }
        if (selected is null || disposed || !IsEnabled) { resourceStatus.Text = "No file selected. Nothing was added."; return; }
        int current = ++resourceRevision; var cts = new CancellationTokenSource(); resourceRead = cts; resourceStatus.Text = "Reading the selected local file for review.";
        try {
            var result = await readFile(selected, cts.Token); cts.Token.ThrowIfCancellationRequested();
            if (disposed || !IsEnabled || current != resourceRevision) return;
            pending = result; RenderPending(); resourceStatus.Text = "Full selected-file snapshot shown below. It is not included until you choose Add reviewed reference."; NotifyChanged();
        } catch (Exception ex) { if (!disposed && current == resourceRevision) resourceStatus.Text = ex is OperationCanceledException ? "File reading stopped. Nothing was added." : ex.Message; }
        finally { if (ReferenceEquals(resourceRead, cts)) resourceRead = null; cts.Dispose(); }
    }
    internal void AddPendingResource()
    {
        if (disposed || !IsEnabled || pending is not { } selected) return;
        try { AddReviewedResource(selected.Source); pending = null; RenderPending(); }
        catch (Exception ex) { resourceStatus.Text = ex.Message; }
    }
    internal void CancelResourceRead(bool discardPreview = false)
    {
        resourceRevision++; var current = resourceRead; resourceRead = null; current?.Cancel();
        if (discardPreview) { pending = null; RenderPending(); }
    }
    private void RenderPending()
    {
        pendingReview.Children.Clear();
        if (pending is { } item) {
            pendingReview.Children.Add(Text(item.Source.Title + " · local file · unverified", 14));
            pendingReview.Children.Add(Text($"Selected snapshot: {item.ByteCount:N0} bytes. Content fingerprint: {item.Sha256}"));
            pendingReview.Children.Add(Preview(item.Source.Text, "Selected file full text"));
            pendingReview.Children.Add(BuddyTheme.Button("Discard selected file", () => { if (disposed || !IsEnabled) return; pending = null; RenderPending(); NotifyChanged(); }));
        }
        addPending.IsEnabled = pending is not null; pendingReview.Children.Add(addPending);
    }
    private void RenderReferences()
    {
        referenceRows.Children.Clear();
        foreach (var entry in references) {
            var source = entry.Source; var row = new StackPanel();
            row.Children.Add(Text(source.Title + (source.Provenance == "document" ? " · selected local file · unverified" : " · pasted text · unverified"), 14));
            row.Children.Add(Preview(source.Text, "Full reference " + source.Title));
            if(entry.Locked){row.Children.Add(Text("Frozen from your reviewed chat context. Reopen Context to change this selection."));referenceRows.Children.Add(BuddyTheme.Card(row,10));continue;}
            var disposition = new ComboBox { ItemsSource = new[] { new Choice("reference", "Reference data"), new Choice("suggestion", "Suggestion"), new Choice("confirmed-decision", "Confirmed decision (required)") }, SelectedValuePath = nameof(Choice.Value), SelectedValue = source.Disposition, Margin = new(0, 0, 0, 6) };
            AutomationProperties.SetName(disposition, "Reference use " + source.Title); row.Children.Add(disposition);
            var required = new CheckBox { Content = "Keep this reference in full", IsChecked = source.Required || source.Disposition == "confirmed-decision", IsEnabled = source.Disposition != "confirmed-decision", Margin = new(0, 0, 0, 6) };
            AutomationProperties.SetName(required, "Required reference " + source.Title); row.Children.Add(required);
            disposition.SelectionChanged += (_, _) => {
                entry.Source = entry.Source with { Disposition = Value(disposition) };
                required.IsEnabled = entry.Source.Disposition != "confirmed-decision";
                if (!required.IsEnabled) required.IsChecked = true;
                NotifyChanged();
            };
            required.Checked += (_, _) => { entry.Source = entry.Source with { Required = true }; NotifyChanged(); };
            required.Unchecked += (_, _) => { entry.Source = entry.Source with { Required = false }; NotifyChanged(); };
            row.Children.Add(Text("Optional reference data may be excerpted or omitted to fit your limit. Confirmed decisions stay required."));
            row.Children.Add(BuddyTheme.Button("Remove reference", () => { if (disposed || !IsEnabled) return; references.Remove(entry); RenderReferences(); NotifyChanged(); }));
            referenceRows.Children.Add(BuddyTheme.Card(row, 10));
        }
    }
    private void NotifyChanged() { if (disposed) return; CancelResourceRead(); Changed?.Invoke(); }
    private TextBox Input(string name, int maxLength, double height = 44)
    {
        var box = new TextBox { MaxLength = maxLength, MinHeight = height, MaxHeight = Math.Max(160, height), AcceptsReturn = height > 44, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(0, 0, 0, 8) };
        AutomationProperties.SetName(box, name); box.TextChanged += (_, _) => NotifyChanged(); return box;
    }
    private CheckBox Toggle(string name, bool value)
    {
        var box = new CheckBox { Content = new TextBlock { Text = name, TextWrapping = TextWrapping.Wrap }, IsChecked = value, Margin = new(0, 0, 0, 5) };
        AutomationProperties.SetName(box, name); box.Checked += (_, _) => NotifyChanged(); box.Unchecked += (_, _) => NotifyChanged(); return box;
    }
    private ComboBox Select(string name, Choice[] choices, string selected)
    {
        var box = new ComboBox { ItemsSource = choices, SelectedValuePath = nameof(Choice.Value), SelectedValue = selected, Margin = new(0, 0, 0, 8) };
        AutomationProperties.SetName(box, name); box.SelectionChanged += (_, _) => NotifyChanged(); return box;
    }
    private static string Value(ComboBox box) => box.SelectedValue?.ToString() ?? "";
    private static TextBlock Text(string text, double size = 12) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = BuddyTheme.Muted, FontSize = size, Margin = new(0, 0, 0, 6) };
    private static TextBox Preview(string text, string name)
    {
        var box = new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70, MaxHeight = 200, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(0, 0, 0, 8) };
        AutomationProperties.SetName(box, name); return box;
    }
    public void Dispose() { if (disposed) return; CancelResourceRead(true); disposed = true; Changed = null; }
}
