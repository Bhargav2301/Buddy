using Buddy.Server;
using Buddy.Windows;
using System.Runtime.CompilerServices;
using System.Text;

int checks = 0;
void Check(bool okay, string label) { if (!okay) throw new Exception("FAIL: " + label); checks++; Console.WriteLine("PASS: " + label); }
bool Refused(Action action) { try { action(); return false; } catch (InvalidOperationException) { return true; } }
TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
async Task Wait(Task task) => await task.WaitAsync(TimeSpan.FromSeconds(8));
async Task<bool> Cancelled(Task task) { try { await Wait(task); return false; } catch (OperationCanceledException) { return true; } }

// Fixed user-visible failure goldens: different address presentations reach speech.
foreach (var address in new[] {
    "https://example.com/help?q=one#part", "HTTP://EXAMPLE.COM/HELP", "HtTpS://example.com/(help)",
    "www.example.com/help", "WWW.EXAMPLE.COM/help", "example.com/help", "docs.example.org",
    "example.co.uk/guide", "reference.test/guide", "[https://example.com](https://example.com)",
    "[www.example.com](HTTPS://example.com)", "<https://example.com/a>", "&lt;HTTPS://example.com/a&gt;",
    "h\u200Bttps://example.com/a", "ftp://example.com/file", "file://localhost/example.txt",
    "[manual][ref]\n[ref]: https://example.com/help \"Documentation\""
}) {
    string cleaned = SpeechText.Prepare("Review the answer. " + address + "\nDo not run anything without approval.");
    Check(!cleaned.Contains("example", StringComparison.OrdinalIgnoreCase) && !cleaned.Contains("reference.test") && !cleaned.Contains("://") && !cleaned.Contains("www.", StringComparison.OrdinalIgnoreCase), "Address is not spoken: " + address);
    Check(cleaned.Contains("Do not run anything without approval.") && !cleaned.Contains(SpeechText.SourceCue), "Late approval caution retained; raw address does not fabricate source attachment");
}
Check(SpeechText.Prepare("## **Ready.** [Open Comet Browser](https://example.com) only after you approve. Use `Ctrl+Shift+Space` for voice. [1]") == "Ready. Open Comet Browser only after you approve. Use Control plus Shift plus Space for voice.", "Existing approval and shortcut speech contract is retained");
Check(SpeechText.Prepare("J.A.R.V.I.S. &amp; [[words]]") == "Jarvis & words", "Selected pronunciation cleanup and entity decoding are retained");
Check(SpeechText.Prepare("[Do not run this](https://example.com/a_(b_(c))). Keep your original.") == "Do not run this. Keep your original.", "Balanced Markdown destinations retain complete warning labels and trailing sentences");
Check(SpeechText.Prepare("[Do not proceed][warning].\n[warning]: https://example.com\nCheck first.") == "Do not proceed. Check first.", "Reference links retain warning labels while metadata definition is silent");
Check(SpeechText.Prepare(ConversationalReply.PlainText("[Do not proceed][warning].\n[warning]: HTTPS://example.com \"Documentation\"\nCheck first.")) == "Do not proceed. Check first.", "Server-to-speech path removes reference definitions before whitespace flattening");
foreach (var citation in new[] { "[1]", "[1, 2]", "[1-3]", "[^note]", "[citation:2]", "【1†source】", "\uE200cite\uE202turn0search1\uE201" })
    Check(SpeechText.Prepare("It bends light. " + citation) == "It bends light.", "Citation marker is silent: " + citation);
Check(SpeechText.Prepare("It is 3.14, version 1.2, and Dr. Lane agrees.") == "It is 3.14, version 1.2, and Dr. Lane agrees.", "Decimals, version numbers and abbreviations remain intact");
Check(SpeechText.Prepare("Only after approval [not before] may you proceed.").Contains("not before"), "Bracketed prose qualifications are preserved");
Check(SpeechText.Prepare("No sources are attached.") == "No sources are attached.", "A negative provenance statement is not changed to an affirmative cue");
Check(SpeechText.Prepare("Sources are attached. Review the answer.") == "Review the answer.", "Model-authored attachment claim alone does not reach speech");
Check(SpeechText.Prepare("Sources are attached; Review the answer. Sources are attached.", true) == "Sources are attached; Review the answer.", "Host cue is included exactly once despite duplicated answer claims");
Check(SpeechText.Prepare("https://example.com") == "" && SpeechText.Prepare("[1]") == "", "URL/citation-only material has no speech or invented explanation");
Check(SpeechText.CompleteAnswer("https://example.com", true).Length == 0, "Evidence does not turn an empty answer into a fabricated spoken response");
foreach (var answer in new[] { "Only proceed after approval.", "Keep the original. Verify the result.", "Keep the original. Verify the result. Stop if unsure." }) {
    var withSources = SpeechText.CompleteAnswer(answer, true).Single();
    Check(withSources == SpeechText.SourceCue + answer && ConversationalReply.Sentences(withSources) == ConversationalReply.Sentences(answer), "One evidence cue does not add a sentence or remove a qualification");
}
Check(SpeechText.CompleteAnswer("A. B. C. D.").Single() == "A. B. C. D.", "Speech presentation never truncates a separately reviewed long action/risk statement");
Check(!ConversationalReply.IsConcise("A. B. C. D.") && ConversationalReply.IsConcise("A. B. C."), "Ordinary server ceiling remains three sentences, separately from complete review speech");
Check(new ReplyConstraints(1).Accepts("A cautious answer.", "Answer in one sentence") && !new ReplyConstraints(1).Accepts("A cautious answer. Another sentence.", "Answer in one sentence"), "Explicit one-sentence server validation remains unchanged");
Check(ReplyConstraints.HasSourceLabel(ConversationalReply.PlainText("Light bends. [Source: NASA](HTTPS://example.com/rainbows)")), "Server keeps attribution labels detectable so unverified sources still trigger whole-answer repair");
Check(ConversationalReply.PlainText("Keep this caution. HTTPS://example.com/help. Do not proceed.").Contains("Do not proceed."), "Shared address cleanup never discards text after the URL");
Check(Refused(() => SpeechText.CompleteAnswer(new string('x', 1601))) && Refused(() => SpeechText.CompleteAnswer(new string('x', 1600), true)), "Over-limit answer or added cue refuses speech instead of clipping later content");
Check(Refused(() => SpeechText.Prepare(new string('x', 20001))), "Input preparation is bounded before text parsing");

// Headless pipeline tests call the same entry preparation and render/play pipeline
// as LocalVoiceOutput. No NAudio endpoint, synthesizer, native window or model runs.
{
    const string answer = "Dr. Lane says it is 3.14. Keep the original. Do not proceed without approval.";
    var rendered = new List<string>(); var buffers = new List<byte[]>(); int played = 0;
    await SentencePlayback.Run(SpeechText.CompleteAnswer(answer, true), (text, ct) => {
        rendered.Add(text); var bytes = Encoding.UTF8.GetBytes(text); buffers.Add(bytes); return Task.FromResult(bytes);
    }, (bytes, ct) => { played++; return Task.CompletedTask; }, default);
    Check(rendered.SequenceEqual([SpeechText.SourceCue + answer]) && played == 1, "Complete reviewed answer uses one synthesis and playback, preserving Piper's within-request pause opportunity");
    Check(buffers.All(b => b.All(value => value == 0)), "Completed answer audio memory is cleared after playback");
}
{
    using var stop = new CancellationTokenSource(); stop.Cancel(); int rendered = 0;
    var task = SentencePlayback.Run(SpeechText.CompleteAnswer("Keep the original."), (text, ct) => { rendered++; return Task.FromResult(new byte[] { 1 }); }, (bytes, ct) => Task.CompletedTask, stop.Token);
    Check(await Cancelled(task) && rendered == 0, "Pre-cancelled answer cannot enter rendering or playback");
}
{
    using var stop = new CancellationTokenSource(); var started = Signal(); int played = 0;
    var task = SentencePlayback.Run(SpeechText.CompleteAnswer("Keep the original."), async (text, ct) => { started.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); return new byte[] { 1 }; }, (bytes, ct) => { played++; return Task.CompletedTask; }, stop.Token);
    await Wait(started.Task); stop.Cancel();
    Check(await Cancelled(task) && played == 0, "Stop while preparing the complete answer prevents playback");
}
{
    using var stop = new CancellationTokenSource(); var started = Signal(); byte[] audio = [1, 2, 3]; int played = 0;
    var task = SentencePlayback.Run(SpeechText.CompleteAnswer("Keep the original. Verify the result."), (text, ct) => Task.FromResult(audio), async (bytes, ct) => { played++; started.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); }, stop.Token);
    await Wait(started.Task); stop.Cancel();
    Check(await Cancelled(task) && played == 1 && audio.All(x => x == 0), "Stop during whole-answer playback clears memory and never replays");
}
{
    using var stop = new CancellationTokenSource(); var playing = Signal(); var next = Signal(); var release = Signal(); int played = 0;
    async IAsyncEnumerable<string> Parts([EnumeratorCancellation] CancellationToken ct = default) {
        yield return "Light bends."; next.TrySetResult(); await release.Task.WaitAsync(ct); yield return "HTTPS://example.com";
    }
    var task = SentencePlayback.Run(SpeechText.PrepareSentences(Parts(), stop.Token), (text, ct) => Task.FromResult(new byte[] { 1 }), async (bytes, ct) => { played++; playing.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); }, stop.Token);
    await Wait(playing.Task); await Wait(next.Task); release.TrySetResult();
    Check(await Cancelled(task) && played == 1, "A later staged sentence that sanitizes to empty stops current audio without fallback or replay");
}
{
    using var stop = new CancellationTokenSource(); var playing = Signal(); var generationGap = Signal(); int rendered = 0, played = 0;
    async IAsyncEnumerable<string> Parts([EnumeratorCancellation] CancellationToken ct = default) {
        yield return "Light bends."; generationGap.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); yield return "Colors separate.";
    }
    var task = SentencePlayback.Run(SpeechText.PrepareSentences(Parts(), stop.Token), (text, ct) => { rendered++; return Task.FromResult(new byte[] { 1 }); }, async (bytes, ct) => { played++; playing.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); }, stop.Token);
    await Wait(playing.Task); await Wait(generationGap.Task);
    var route = new AudioRouteGuard("headphones", true, stop.Cancel); route.DeviceUnavailable("unrelated");
    Check(!stop.IsCancellationRequested, "An unrelated endpoint event cannot change the selected route");
    route.DeviceUnavailable("headphones");
    Check(await Cancelled(task) && rendered == 1 && played == 1, "Headphone loss cancels rendering/playback and a generation gap with no replacement endpoint");
}
{
    int stopped = 0; var route = new AudioRouteGuard("headphones", true, () => stopped++);
    route.DefaultRenderChanged(); route.PropertiesChanged("headphones");
    Check(stopped == 2, "Selected headphone/default changes still require cancellation, never fallback");
}
Console.WriteLine($"SPEECH FEEDBACK CHECKS PASSED: {checks}");
