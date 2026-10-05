using Buddy.Server;
using Buddy.Windows;
using Microsoft.AspNetCore.DataProtection;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

// Root dispatches and serializes this owned WPF fixture. Model traffic is a leaf
// handler in memory; picker/reader delegates are explicit, canned test seams.
internal static class RefinementOptionsChecks
{
    private const string Original = "Write a polite email requesting Friday off.";
    private const string Improved = "Please draft a polite email requesting Friday off.";
    private static IEnumerable<DependencyObject> Tree(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var descendant in Tree(child)) yield return descendant;
    }
    private static T Named<T>(DependencyObject root, string name) where T : DependencyObject => Tree(root).OfType<T>().Single(x => AutomationProperties.GetName(x) == name);
    private static Button Button(DependencyObject root, string text) => Tree(root).OfType<Button>().Single(x => Equals(x.Content, text));
    private static Task Call(object target, string name) => (Task)target.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, null)!;
    private static string Labels(DependencyObject root) => string.Join("\n", Tree(root).OfType<TextBlock>().Select(x => x.Text));
    private static void Expand(DependencyObject root) { foreach (var item in Tree(root).OfType<Expander>()) item.IsExpanded = true; }
    private static async Task Wait(Func<bool> predicate, string reason)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        while (!predicate() && clock.Elapsed < TimeSpan.FromSeconds(8)) await Task.Delay(10);
        if (!predicate()) throw new TimeoutException(reason);
    }
    internal static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        BuddyTheme.Apply("Night Mint", true);
        var owner = new Window { Title = "Buddy owned refinement options fixture", Width = 480, Height = 220 };
        var field = new TextBox { Text = Original, AcceptsReturn = true, Margin = new(20) }; owner.Content = field;
        int checks = 0, exit = 1;
        string folder = Path.Combine(Path.GetTempPath(), "Buddy-options-ui45-" + Guid.NewGuid());
        void Check(bool pass, string message) { if (!pass) throw new Exception("FAIL: " + message); checks++; Console.WriteLine("PASS: " + message); }
        owner.Loaded += async (_, _) => {
            var opened = new List<RefineWindow>();
            try {
                using var model = new MockModel(); using var http = new HttpClient(model) { BaseAddress = new("http://127.0.0.1:11434/") };
                var store = new StateStore(folder, new EphemeralDataProtectionProvider());
                var service = new BuddyService(store, new(http)) { WebEnabled = false, AgentEnabled = false };
                (RefineWindow Window, DraftField Adapter) New(RefinementOptionsPanel? options = null)
                {
                    field.Text = Original; var adapter = new DraftField(field); var edit = new GuardedEdit(adapter, Original);
                    var window = new RefineWindow(service, Original, (value, ct) => { edit.Apply(value, DateTimeOffset.UtcNow, ct); return Task.CompletedTask; },
                        ct => { edit.Undo(DateTimeOffset.UtcNow, ct); return Task.CompletedTask; }, "Owned Buddy draft", optionsPanel: options) { Owner = owner };
                    opened.Add(window); window.Show(); Expand(window); return (window, adapter);
                }

                var (review, reviewField) = New();
                await review.Refine("quick");
                Check(review.CanApply && field.Text == Original && reviewField.Writes == 0, "Generation presents an accepted review without changing the owned draft");
                Named<ComboBox>(review.Options, "Refinement technique").SelectedValue = "zero-shot";
                Check(!review.CanApply, "Editing the actual technique control invalidates an accepted result immediately");
                await Call(review, "Apply");
                Check(reviewField.Writes == 0 && field.Text == Original, "Forced Apply cannot use the previous request's accepted result");
                await review.Refine("quick"); review.Cancel(); await Call(review, "Apply");
                Check(!review.CanApply && reviewField.Writes == 0, "Stop invalidates the result at the operation layer as well as disabling its button");
                await review.Refine("quick"); review.Close(); await Call(review, "Apply");
                Check(reviewField.Writes == 0, "A closed review refuses forced Apply of its formerly accepted result");

                var (race, raceField) = New();
                var held = model.HoldNext();
                var old = race.Refine("quick");
                try {
                    await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    int generation = race.RequestGeneration;
                    Named<TextBox>(race.Options, "Confirmed constraints").Text = "Do not send.";
                    Check(race.RequestGeneration > generation && !race.CanApply, "An actual supporting-input edit cancels request A and creates a new review revision");
                    var next = race.Refine("quick"); held.Release.TrySetResult();
                    await Task.WhenAll(old, next).WaitAsync(TimeSpan.FromSeconds(8));
                    Check(race.CanApply && Named<TextBox>(race, "Refined prompt").Text == Improved + "\n\nDo not send.", "Late A completion cannot replace B's complete assembled review");
                    await Call(race, "Apply");
                    Check(field.Text == Improved + "\n\nDo not send." && raceField.Writes == 1 && !race.Options.IsEnabled, "Apply writes only B's reviewed assembly and freezes options while Undo owns the replacement");
                    await Call(race, "Undo");
                    Check(field.Text == Original && raceField.Writes == 2 && race.Options.IsEnabled && !race.CanApply, "Undo restores the exact original and requires fresh review before another Apply");
                } finally { held.Release.TrySetResult(); race.Close(); }

                var (sourceChange, sourceField) = New(); await sourceChange.Refine("quick"); field.Text = "Newer user draft";
                await Call(sourceChange, "Apply");
                Check(field.Text == "Newer user draft" && sourceField.Writes == 0, "An intervening source edit cannot be overwritten by the reviewed old draft"); sourceChange.Close();

                var (budget, budgetField) = New();
                Named<CheckBox>(budget.Options, "Use a destination size limit").IsChecked = true;
                Named<TextBox>(budget.Options, "Destination label").Text = "Owned fixture field";
                Named<TextBox>(budget.Options, "Destination limit").Text = "10";
                int beforeCalls = model.Requests;
                await budget.Refine("quick"); await Call(budget, "Apply");
                Check(budget.CurrentPreparation is { Ready: false } p && p.AssembledText == Original && model.Requests == beforeCalls && budgetField.Writes == 0 && !budget.CanApply,
                    "Impossible required budget remains a complete diagnostic preview with zero inference or mutation");
                Named<TextBox>(budget.Options, "Destination limit").Text = Improved.Length.ToString(System.Globalization.CultureInfo.InvariantCulture);
                Named<TextBox>(budget.Options, "Reference title").Text = "Optional owned memo";
                Named<TextBox>(budget.Options, "Reference text").Text = "Optional reference that does not create a requirement.";
                Button(budget.Options, "Add pasted reference").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                await budget.Refine("quick");
                Check(budget.CanApply && Named<TextBox>(budget, "Refined prompt").Text == Improved && Named<TextBlock>(budget, "Refinement result details").Text.Contains("Omitted optional references: Optional owned memo"),
                    "Actual pasted reference omission is identified by its title while review shows the exact retained result");
                Check(budget.CurrentPreparation!.Budget.Count == Original.Length && budget.CurrentPreparation.Budget.Removed.Count == 1,
                    "Preflight counts the retained original; final candidate growth is separately checked by the service"); budget.Close();

                var (supporting, _) = New();
                Named<ComboBox>(supporting.Options, "Refinement technique").SelectedValue = "few-shot";
                beforeCalls = model.Requests; await supporting.Refine("quick");
                Check(!supporting.CanApply && model.Requests == beforeCalls && Labels(supporting).Contains("example", StringComparison.OrdinalIgnoreCase), "Missing few-shot inputs are visible before any model request");
                supporting.Options.AddExample("Input example", "Output example");
                Named<TextBox>(supporting.Options, "Available tools").Text = "Local calculator";
                Named<TextBox>(supporting.Options, "Ordered stages").Text = "Read draft\nRevise wording";
                Named<CheckBox>(supporting.Options, "Revising an existing draft").IsChecked = true;
                var request = supporting.Options.Snapshot().ToRequest(Original, "quick");
                Check(request.Inputs?.Examples?.Single().Output == "Output example" && request.Inputs.AvailableTools!.Single() == "Local calculator" && request.Inputs.Stages!.SequenceEqual(new[] { "Read draft", "Revise wording" }) && request.Inputs.Revision,
                    "Actual support controls bind complete examples, listed tools, ordered stages and revision without inventing entries");
                supporting.Close();

                int pickerCalls = 0, readerCalls = 0; string? selected = null;
                var snapshot = new RefinementResourceReadResult(new("owned_snapshot", "Owned file.txt", "Reviewed local text with `code` and 👩🏽‍💻.", "document"), 51, "canned-fixture-hash");
                var resourceOptions = new RefinementOptionsPanel(_ => { pickerCalls++; return selected; }, (_, ct) => { ct.ThrowIfCancellationRequested(); readerCalls++; return Task.FromResult(snapshot); });
                var (resources, _) = New(resourceOptions);
                Check(pickerCalls == 0 && readerCalls == 0 && resources.Options.Snapshot().References.Count == 0, "Opening options reads no files and attaches no resources");
                Button(resources.Options, "Choose TXT or Markdown file").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                await Wait(() => !resources.Options.IsReadingResource, "Canceled picker did not settle.");
                Check(pickerCalls == 1 && readerCalls == 0, "Canceling the injected picker performs no resource read");
                selected = @"C:\owned-synthetic-picker\reference.txt";
                Button(resources.Options, "Choose TXT or Markdown file").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                await Wait(() => resources.Options.HasPendingResource, "Selected snapshot did not reach full preview.");
                Check(readerCalls == 1 && resources.Options.Snapshot().References.Count == 0 && Named<TextBox>(resources.Options, "Selected file full text").Text == snapshot.Source.Text,
                    "Explicit selection creates a full pending preview but does not include its text in the request");
                Button(resources.Options, "Add reviewed reference").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Check(!resources.Options.HasPendingResource && resources.Options.Snapshot().References.Single().Text == snapshot.Source.Text && readerCalls == 1,
                    "Only the actual Add reviewed reference action attaches the exact staged snapshot without rereading");
                resources.Close();

                var readEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var readReleased = new TaskCompletionSource<RefinementResourceReadResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                var slowOptions = new RefinementOptionsPanel(_ => selected, (_, _) => { readEntered.TrySetResult(); return readReleased.Task; });
                var (slow, _) = New(slowOptions);
                var pendingRead = slow.Options.LoadSelectedResource(); await readEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                slow.Cancel(); readReleased.TrySetResult(snapshot); await pendingRead;
                Check(!slow.Options.HasPendingResource && slow.Options.Snapshot().References.Count == 0 && !slow.CanApply, "Stop rejects a late resource completion even when the injected reader ignores cancellation"); slow.Close();

                var (closing, closingField) = New(); var closingHold = model.HoldNext(); var closingTask = closing.Refine("quick");
                try { await closingHold.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); closing.Close(); closingHold.Release.TrySetResult(); await closingTask; await Call(closing, "Apply"); Check(!closing.CanApply && closingField.Writes == 0, "Close cancels held inference and rejects later result or mutation"); }
                finally { closingHold.Release.TrySetResult(); }
                Check(await store.Read(s => s.Conversations.Count + s.Knowledge.Count + s.Jobs.Count + s.Guides.Count + s.Audit.Count) == 0,
                    "Owned options and refinement do not persist resource contents, conversations or action receipts");
                Console.WriteLine($"ALL {checks} REFINEMENT OPTIONS OWNED WPF CHECKS PASSED; actual controls, mock model and injected picker; no OS picker acceptance, external editor, installed profile or real model claim."); exit = 0;
            } catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally {
                foreach (var window in opened) window.Close();
                string full = Path.GetFullPath(folder), temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(full).StartsWith("Buddy-options-ui45-", StringComparison.Ordinal) && Directory.Exists(full)) Directory.Delete(full, true);
                owner.Close(); app.Shutdown();
            }
        };
        app.Run(owner); return exit;
    }
    private sealed class DraftField(TextBox field) : IVerifiedTextField
    {
        public string Identity { get; } = Guid.NewGuid().ToString();
        internal int Writes;
        public string Read() => field.Text;
        public void Write(string expected, string value, CancellationToken ct) { ct.ThrowIfCancellationRequested(); if (field.Text != expected || field.IsReadOnly) throw new InvalidOperationException("Owned source draft changed."); Writes++; field.Text = value; }
    }
    private sealed class MockModel : HttpMessageHandler
    {
        internal sealed record Hold(TaskCompletionSource Entered, TaskCompletionSource Release);
        private Hold? next;
        internal int Requests;
        internal Hold HoldNext() => next = new(new(TaskCreationOptions.RunContinuationsAsynchronously), new(TaskCreationOptions.RunContinuationsAsynchronously));
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)).RootElement.Clone(); Requests++;
            if (request.RequestUri!.AbsolutePath == "/api/embed") return Json(new { embeddings = Enumerable.Range(0, payload.GetProperty("input").GetArrayLength()).Select(_ => new[] { 1f, 0f }) });
            if (request.RequestUri.AbsolutePath != "/api/chat") throw new InvalidOperationException("Unexpected mocked route.");
            if (!payload.GetProperty("stream").GetBoolean()) return Json(new { message = new { content = JsonSerializer.Serialize(new { preserved = true, scoreBefore = 70, scoreAfter = 80, changes = new[] { "Polished wording" } }) }, done = true });
            if (next is { } hold) { next = null; hold.Entered.TrySetResult(); await hold.Release.Task; }
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new { message = new { content = Improved }, done = true }) + "\n", Encoding.UTF8, "application/x-ndjson") };
        }
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") };
    }
}
