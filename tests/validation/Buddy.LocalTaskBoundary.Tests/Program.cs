using Buddy.Windows;
using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

// Reject execution against changed source, including the exact external linked journal.
using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("QaReviewedInputs") ?? throw new InvalidOperationException("Missing compiled-source receipt."))
using (var reader = new StreamReader(stream))
    while (reader.ReadLine() is { } line) {
        var pair = line.Split('|');
        if (pair.Length != 2 || !File.Exists(pair[0]) || !Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pair[0]))).Equals(pair[1], StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Compile input changed; rebuild: " + pair[0]);
        Console.WriteLine(JsonSerializer.Serialize(new { kind = "source", path = pair[0], sha256 = pair[1] }));
    }
int checks = 0;
void Check(bool pass, string label) { if (!pass) throw new InvalidOperationException(label); checks++; Console.WriteLine("PASS: " + label); }
void Refuses(Action action, string label) { try { action(); } catch (ArgumentException) { Check(true, label); return; } throw new InvalidOperationException(label); }
var time = new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
var journal = new LocalTaskJournal(() => time);
var initial = journal.Snapshot;
Check(initial.Revision == 0 && initial.Tasks.Count == 0, "New journal has no fabricated task history");
var a = journal.Begin("home", "Home chat", "Preparing an answer.");
var first = journal.Snapshot;
Check(a.Id != Guid.Empty && a.Generation > 0 && first.Tasks.Single().Token == a, "Begin returns a unique full task identity");
Check(first.Tasks[0].Phase == LocalTaskPhase.Running && !first.Tasks[0].IsTerminal && first.Tasks[0].ObservedSteps.Count == 0, "Beginning is running, without invented observed stages");
time = time.AddSeconds(3);
Check(journal.Update(a, "Answer received.", observedStep: true), "Actual event can add an observed stage");
var observed = journal.Snapshot;
Check(observed.Tasks[0].ObservedSteps.Single().At == time && observed.Tasks[0].UpdatedAt == time, "Observed stage uses event time, not simulated progress");
Check(first.Tasks[0].ObservedSteps.Count == 0 && first.Tasks[0].Detail == "Preparing an answer.", "Previously captured snapshot remains unchanged");
Check(initial.Tasks.Count == 0, "Initial snapshot cannot acquire later records");
long revision = observed.Revision;
Check(journal.Update(a, "Answer received.", observedStep: true) && journal.Snapshot.Revision == revision && journal.Snapshot.Tasks[0].ObservedSteps.Count == 1, "Duplicate event does not duplicate stage or revision");
Check(journal.Update(a, "Proposal ready; no edit applied.", LocalTaskPhase.WaitingForReview), "Review readiness is a nonterminal explicit phase");
Check(!journal.Snapshot.Tasks[0].IsTerminal && !journal.Dismiss(a), "A proposal cannot be dismissed as an already completed task");
Check(journal.Finish(a, LocalTaskPhase.Completed, "Answer displayed."), "Explicit successful completion is recorded");
revision = journal.Snapshot.Revision;
Check(!journal.Finish(a, LocalTaskPhase.Cancelled, "Late cancellation.") && !journal.Update(a, "Late callback."), "Late terminal/update callbacks cannot rewrite completed outcome");
Check(journal.Snapshot.Revision == revision && journal.Snapshot.Tasks[0].Phase == LocalTaskPhase.Completed, "Ignored callbacks leave revision and outcome unchanged");
Check(journal.Dismiss(a) && !journal.Dismiss(a) && !journal.Update(a, "Stale."), "Dismiss removes only the exact terminal record and expires its token");

var old = journal.Begin("refine", "Draft refinement");
var beforeReplacement = journal.Snapshot;
var current = journal.Begin("refine", "New draft refinement");
Check(old.Id != current.Id && current.Generation > old.Generation && journal.Snapshot.Tasks[0].Token == current, "Replacement allocates a new generation and stays newest first");
var replaced = journal.Snapshot.Tasks.Single(t => t.Token == old);
Check(replaced.Phase == LocalTaskPhase.Cancelled && replaced.Detail.Contains("earlier dispatched effect", StringComparison.Ordinal), "Replacement does not claim that an earlier dispatched effect was undone");
Check(beforeReplacement.Tasks[0].Phase == LocalTaskPhase.Running, "Replacement does not mutate an earlier snapshot");
Check(!journal.Finish(old, LocalTaskPhase.Completed, "Late result.") && !journal.Update(old, "Stale stage."), "Replaced source cannot complete or update a newer task");
foreach (var forged in new[] { current with { Id = Guid.NewGuid() }, current with { Source = "home" }, current with { Generation = current.Generation + 1 } })
    Check(!journal.Update(forged, "Forged update.") && !journal.Finish(forged, LocalTaskPhase.Completed, "Forged completion.") && !journal.Dismiss(forged), "Every token component participates in ownership validation");
Check(journal.Snapshot.Tasks[0].Token == current && journal.Snapshot.Tasks[0].Phase == LocalTaskPhase.Running, "Rejected forgeries preserve current request");
Refuses(() => journal.Update(current, "Cannot complete through Update.", LocalTaskPhase.Completed), "Update refuses terminal phases");
Refuses(() => journal.Finish(current, LocalTaskPhase.WaitingForReview, "Not completed."), "Finish refuses readiness masquerading as completion");
Refuses(() => journal.Update(current, "", observedStep: true), "An observed event requires a real nonempty description");

time = time.AddMinutes(-10);
journal.Update(current, "Clock moved backwards.", observedStep: true);
Check(journal.Snapshot.Tasks[0].UpdatedAt >= journal.Snapshot.Tasks[0].StartedAt, "Wall-clock reversal cannot put an event before its task start");
for (int i = 0; i < 12; i++) journal.Update(current, "Observed event " + i, observedStep: true);
var bounded = journal.Snapshot.Tasks[0];
Check(bounded.ObservedSteps.Count == 8 && bounded.ObservedSteps[0].Detail == "Observed event 4" && bounded.ObservedSteps[^1].Detail == "Observed event 11", "Stage retention is exactly the last eight real events in order");
Check(bounded.ObservedSteps is IList steps && steps.IsReadOnly && journal.Snapshot.Tasks is IList records && records.IsReadOnly, "Returned record and step collections are read-only");
bool immutable = false;
try { ((IList)bounded.ObservedSteps)[0] = new LocalTaskStep(time, "Injected."); } catch (NotSupportedException) { immutable = true; }
Check(immutable && journal.Snapshot.Tasks[0].ObservedSteps[0].Detail == "Observed event 4", "Caller cannot inject a fabricated observed event through a snapshot");

var capacity = new LocalTaskJournal(() => time);
var oldestActive = capacity.Begin("first", "First active");
var terminal = capacity.Begin("finished", "Finished"); capacity.Finish(terminal, LocalTaskPhase.Failed, "Failed explicitly.");
for (int i = 0; i < 7; i++) capacity.Begin("source-" + i, "Active " + i);
Check(capacity.Snapshot.Tasks.Count == 8 && capacity.Snapshot.Tasks.Any(t => t.Token == oldestActive) && !capacity.Snapshot.Tasks.Any(t => t.Token == terminal), "Capacity removes oldest terminal before any still-active task");
var capturedActive = capacity.Snapshot.Tasks.Single(t => t.Token == oldestActive);
capacity.Begin("last", "Latest");
Check(capacity.Snapshot.Tasks.Count == 8 && !capacity.Snapshot.Tasks.Any(t => t.Token == oldestActive) && capturedActive.Phase == LocalTaskPhase.Running, "All-active expiry forgets display without inventing cancellation or completion");
Check(!capacity.Finish(oldestActive, LocalTaskPhase.Completed, "Expired callback."), "An evicted active token cannot resurrect its record");

foreach (string invalid in new[] { "", "Home", "bad source", "../home", "a\nb", "9source", new string('a', 33) })
    Refuses(() => capacity.Begin(invalid, "Title"), "Source rejects noncanonical/path-like/unbounded identity");
Refuses(() => capacity.Begin("valid", new string('x', 81)), "Titles cannot exceed 80 UTF-16 units");
Refuses(() => capacity.Begin("valid", "Title", new string('x', 241)), "Details cannot exceed 240 UTF-16 units");
Refuses(() => capacity.Begin("valid", "Multiline\nTitle"), "Control characters cannot enter display labels");
Refuses(() => capacity.Begin("valid", "   "), "Whitespace-only title is refused");
var unicode = capacity.Begin("unicode", "Owned Δ review 😀", "Canned detail only.");
Check(capacity.Snapshot.Tasks[0].Title == "Owned Δ review 😀", "Valid Unicode display labels are preserved");

var racing = new LocalTaskJournal(() => time);
var raceToken = racing.Begin("race", "Race");
int wins = 0;
Parallel.For(0, 32, index => { if (racing.Finish(raceToken, index % 2 == 0 ? LocalTaskPhase.Completed : LocalTaskPhase.Cancelled, "Terminal callback " + index)) Interlocked.Increment(ref wins); });
Check(wins == 1 && racing.Snapshot.Tasks.Count == 1 && racing.Snapshot.Tasks[0].IsTerminal && racing.Snapshot.Revision == 2, "Concurrent terminal callbacks have exactly one winner");
var parallel = new LocalTaskJournal(() => time);
var tokens = new System.Collections.Concurrent.ConcurrentBag<LocalTaskToken>();
Parallel.For(0, 32, _ => tokens.Add(parallel.Begin("same-source", "Replacement")));
Check(tokens.Select(t => t.Id).Distinct().Count() == 32 && tokens.Select(t => t.Generation).Distinct().Count() == 32, "Concurrent replacements cannot reuse IDs or generations");
Check(parallel.Snapshot.Tasks.Count == 8 && parallel.Snapshot.Tasks.Count(t => !t.IsTerminal) == 1, "Concurrent same-source tasks retain only one current running owner");
Console.WriteLine(JsonSerializer.Serialize(new { kind = "summary", checks, passed = checks, scope = "Pure journal; no UI, task execution, model, network, profile or persistence", native = "NOT_RUN" }));
