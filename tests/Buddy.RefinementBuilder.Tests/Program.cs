using System.Net;
using System.Text.Json;
using Buddy.Server;
using Microsoft.AspNetCore.DataProtection;

internal static partial class Program
{
    private static int checks, failures;
    private static readonly (string Id, string Source, string Expected)[] GrammarCases = [
        ("standalone-poem", "Write poem.", "Write a poem."),
        ("standalone-email", "Draft email", "Draft an email"),
        ("standalone-outline", "Prepare outline.", "Prepare an outline."),
        ("standalone-letter", "compose informal letter", "Compose an informal letter"),
        ("standalone-summary", "Write brief summary.", "Write a brief summary."),
        ("standalone-explanation", "Explain difference between RAM and storage.", "Explain the difference between RAM and storage."),
        ("standalone-purpose", "Explain purpose of `cache_size=64`.", "Explain the purpose of `cache_size=64`."),
        ("standalone-unicode", "Explain difference between café and cafe\u0301.", "Explain the difference between café and cafe\u0301.")
    ];

    private static async Task<int> Main(string[] args)
    {
        if (args.SequenceEqual(new[] { "--cases" })) {
            Console.WriteLine(JsonSerializer.Serialize(GrammarCases.Select(c => new { c.Id, prompt = c.Source, expected = c.Expected, contract = "finite-source-grammar" })));
            return 0;
        }
        bool baselineOnly = args.SequenceEqual(new[] { "--baseline-grammar" });
        if (args.Length != 0 && !baselineOnly) throw new ArgumentException("Only --cases, --baseline-grammar or the default injected suite is supported.");
        using var fixture = new Fixture();
        await GrammarChecks(fixture);
        if (baselineOnly) { Console.WriteLine($"BASELINE: {checks} finite-route checks, {failures} failed; synthetic echo transport."); return failures == 0 ? 0 : 1; }
        await GateChecks(fixture);
        await FiniteWordingChecks(fixture);
        await CompoundChecks(fixture);
        await SemicolonConstraintChecks(fixture);
        await UnicodeChecks(fixture);
        await RefusalChecks(fixture);
        await ContextChecks(fixture);
        await NoChangeReasonChecks(fixture);
        await LifecycleChecks(fixture);
        Check(await fixture.Store.Read(s => s.Conversations.Count + s.Jobs.Count + s.Audit.Count + s.Prompts.Count) == 0,
            "no prompt, conversation, job or audit persistence");
        Console.WriteLine($"{(failures == 0 ? "PASS" : "FAIL")}: {checks} builder checks, {failures} failed; injected transport only, no actual model/native/profile.");
        return failures == 0 ? 0 : 1;
    }

    private static void Check(bool condition, string label)
    {
        checks++;
        if (!condition) { failures++; Console.WriteLine("FAIL: " + label); }
    }

    private static async Task GrammarChecks(Fixture f)
    {
        foreach (var sample in GrammarCases) {
            Check(!RefinementContract.Analyze(sample.Source).CanStructure &&
                RefinementGrammar.Propose(sample.Source, "task", sample.Source)?.Result == sample.Expected,
                sample.Id + " is an existing finite rule outside the structure path");
            f.Model.Reset();
            var result = await f.Service.RefineDetailed(new(sample.Source), default);
            Check(result.Accepted && !result.NoChange && result.RefinedPrompt == sample.Expected && result.Method == "source-grammar", sample.Id + " reaches verified finite correction");
            Check(f.Model.Chats == 0 && f.Model.Plans == 0 && f.Model.Assessments == 1 && f.Model.Embeddings == 1,
                sample.Id + " uses existing validation without speculative wording attempts");
            Check(result.ScoreBefore is null && result.ScoreAfter is null && result.Changes.Count == 1 &&
                result.Changes.All(x => x.Contains("article", StringComparison.Ordinal)), sample.Id + " reports the host correction, not model quality claims");
            Check(f.Model.AssessedRewrite == sample.Expected && f.Model.EmbeddedText?.Contains(sample.Expected) == true,
                sample.Id + " checks the actual corrected proposal");
        }
        foreach (string mode in new[] { "guided", "council" }) {
            f.Model.Reset(); var result = await f.Service.RefineDetailed(new(GrammarCases[0].Source, mode), default);
            Check(result.Accepted && result.Mode == mode && f.Model.Chats == 0 && result.Warnings.Any(w => w.Contains("wording passes")),
                mode + " truthfully reports finite grammar instead of claimed council passes");
        }
    }

    private static async Task GateChecks(Fixture f)
    {
        foreach (string fault in new[] { "assessment-refusal", "assessment-malformed", "assessment-error", "embedding-error", "embedding-malformed", "low-similarity" }) {
            f.Model.Reset(); f.Model.Fault = fault;
            var result = await f.Service.RefineDetailed(new(GrammarCases[0].Source), default);
            Check(!result.Accepted && result.RefinedPrompt == GrammarCases[0].Source && !result.NoChange, fault + " cannot accept or claim a harmless echo");
        }
        foreach (float score in new[] { .799f, .801f }) {
            f.Model.Reset(); f.Model.Similarity = score;
            var result = await f.Service.RefineDetailed(new(GrammarCases[0].Source), default);
            Check(result.Accepted == (score > .8f), "finite correction retains the 0.80 similarity gate");
        }
        f.Model.Reset();
        var small = await f.Service.RefineDetailed(new(GrammarCases[0].Source,
            Budget: new("Fixture", GrammarCases[0].Source.Length, "utf16-code-units")), default);
        Check(!small.Accepted && small.RefinedPrompt == GrammarCases[0].Source && small.DestinationBudget?.Conflict is not null,
            "new article cannot exceed a confirmed destination budget");
    }

    private static async Task FiniteWordingChecks(Fixture f)
    {
        foreach (var (source, expected) in new[] {
            ("The release notes needs a short summary for reviewers.", "The release notes need a short summary for reviewers."),
            ("These results shows 2 failures, not 3.", "These results show 2 failures, not 3."),
            ("Our files has no changes.", "Our files have no changes."),
            ("They does not need another report.", "They do not need another report."),
            ("Your messages is ready for review.", "Your messages are ready for review."),
            ("Pleae write a short mesage saying we cannot attend on Tuesday, not that we refuse the invitation.", "Please write a short message saying we cannot attend on Tuesday, not that we refuse the invitation."),
            ("Draft a brief sumary for reviewers.", "Draft a brief summary for reviewers."),
            ("Write 2 mesages.", "Write 2 messages."),
            ("Prepare a paragaph about the supplied result.", "Prepare a paragraph about the supplied result."),
            ("Write an invitaton to the meeting.", "Write an invitation to the meeting."),
            ("Prepare brief summary about last week's test results.", "Prepare a brief summary about last week's test results.")
        }) {
            f.Model.Reset(); var result = await f.Service.RefineDetailed(new(source), default);
            bool changed = source != expected;
            Check(changed ? result.Accepted && result.RefinedPrompt == expected && result.Method == "source-grammar" && result.Grammar?.Source == source && result.Grammar.Result == expected : result.Method != "source-grammar",
                "finite source proof: " + source);
            if (changed) {
                Check(f.Model.Chats == 0 && f.Model.Plans == 0 && f.Model.Assessments == 1 && f.Model.Embeddings == 1,
                    "finite source spelling/agreement retains actual candidate assessment and embedding");
                Check(result.ScoreBefore is null && result.ScoreAfter is null && result.Changes.Single() != "Untrusted model claim",
                    "finite source spelling/agreement uses host operations");
            }
        }
        foreach (string source in new[] {
            "The news needs a summary.", "The number of notes needs a summary.", "James needs a summary.", "The series needs a summary.",
            "The Notes needs a summary.", "The notes named Needs are useful.", "The notes needs a title.",
            "`The notes needs a summary.`", "\"The notes needs a summary.\"", "The notes needs a summary. Keep every word unchanged.",
            "The notes needs a summary if requested.", "Explain why the notes needs a summary.", "Write mesage variable.",
            "Write Mesage to the meeting.", "Write \"mesage\" to the meeting.", "Write `mesage` to the meeting.",
            "Write a mesage about the command named mesage.", "Use mesage for the label.", "Write a form about reports.",
            "Pleae is the project name.", "The notes needs must be copied verbatim."
        }) Check(RefinementGrammar.Propose(source, "task", source) is null,
            "closed source rules reject names, protected wording and ambiguous plurality: " + source);
        foreach (string fault in new[] { "assessment-refusal", "low-similarity", "embedding-error" }) {
            f.Model.Reset(); f.Model.Fault = fault;
            var result = await f.Service.RefineDetailed(new("The reports needs a summary."), default);
            Check(!result.Accepted && result.Grammar is null && result.RefinedPrompt == "The reports needs a summary.",
                "finite lexical proof never bypasses " + fault);
        }
    }

    private static async Task CompoundChecks(Fixture f)
    {
        foreach (string source in new[] {
            "Write exactly 3 sentences about the first proposal and exactly 5 sentences about the second proposal.",
            "Write 3 sentences about A and 5 sentences on B.",
            "Write a poem about the first proposal and an email about the second proposal.",
            "Draft one report about the result or another summary about the review.",
            "Write a poem about the sea and explain its setting.",
            "Write 3 sentences about A and 4 detailed paragraphs about B.",
            "Write 3 sentences about A and then also 4 paragraphs about B.",
            "Write 3 sentences about A and a minimum of 5 paragraphs about B.",
            "Write 3 sentences about A and warmly greet the reviewer.",
            "Compose a report about A and contextualize the history of B.",
            "Prepare a summary about A & 4 paragraphs about B."
        }) {
            var ledger = RefinementContract.Analyze(source);
            Check(!ledger.CanStructure && ledger.Spans.Count == 1 && ledger.Spans[0].Text == source,
                "coordinated request remains one exact task; no global count extraction");
            f.Model.Reset(); var result = await f.Service.RefineDetailed(new(source), default);
            Check(!result.Accepted && result.RefinedPrompt == source && f.Model.Plans == 0,
                "an echo of a coordinated request cannot become an unsafe structure");
        }
        const string compoundWithConstraint = "Write exactly 3 sentences about A and exactly 5 sentences about B. Do not combine the proposals.";
        var mixed = RefinementContract.Analyze(compoundWithConstraint);
        Check(mixed.Spans[0].Text == "Write exactly 3 sentences about A and exactly 5 sentences about B." && mixed.Spans[0].Kind == "task",
            "separate constraints never detach compound output counts from their operands");
        Check(!RefinementContract.Analyze("Write a poem about a boat and its captain.").CanStructure,
            "ambiguous coordination conservatively retains wording even when a joint subject is plausible");
        Check(RefinementContract.Analyze("Write a poem about \"and exactly 5 sentences\".").CanStructure,
            "quoted task-looking data is not treated as an actual coordination boundary");
    }

    private static async Task UnicodeChecks(Fixture f)
    {
        foreach (string source in new[] {
            "Describe Zoë and 李明 beside café 🌙 using `\\u0041` and \"<user>\".",
            "Write a poem about Zoë and 李明 beside café 🌙.",
            "Explain purpose of `李明 🌙 \\u0041`."
        }) {
            f.Model.Reset(); await f.Service.RefineDetailed(new(source), default);
            Check(f.Model.RawInputs.All(x => x.Contains("李明", StringComparison.Ordinal) && x.Contains("🌙", StringComparison.Ordinal)),
                "model data contains actual Unicode in wording, plan and assessment protocols");
            foreach (string raw in f.Model.RawInputs) {
                using var parsed = JsonDocument.Parse(raw);
                Check(parsed.RootElement.ValueKind == JsonValueKind.Object, "Unicode transport preserves JSON structural boundaries");
            }
            Check(!source.Contains("\\u0041") || f.Model.RawInputs.All(x => x.Contains("\\\\u0041", StringComparison.Ordinal)),
                "literal code backslash-u remains escaped data rather than decoded text");
        }
        foreach (string source in new[] {
            "Describe `\\uD83C\\uDF19` literally, not 🌙.",
            "Describe café cafe\u0301 and 👩🏽‍💻 without changing `\\u674E`.",
            "Describe \"quotes\", \\backslashes\\, and `\\uD800` as supplied.",
            "Describe `\\\\uD83C\\uDF19` and `\\uDF19\\uD83C` as supplied."
        }) {
            f.Model.Reset(); var result = await f.Service.RefineDetailed(new(source), default);
            Check(result.RefinedPrompt == source, "source escapes, combining characters and supplementary sequences are not normalized");
            Check(f.Model.RawInputs.Count > 0, "source-encoding roundtrip exercised model data");
            foreach (string raw in f.Model.RawInputs) {
                using var parsed = JsonDocument.Parse(raw);
                Check(parsed.RootElement.GetProperty("untrustedDraft").GetString() == source &&
                    parsed.RootElement.GetProperty("orderedConstraintLedger")[0].GetString() == source,
                    "every decoded model input equals the exact source code units");
            }
        }
        foreach (string invalid in new[] { "bad \uD800 text", "bad \uDC00 text", "bad \uD800x\uDC00 text" }) {
            f.Model.Reset(); bool refused = false;
            try { await f.Service.RefineDetailed(new(invalid), default); }
            catch (BuddyException e) { refused = e.Code == "INVALID_REFINE_UNICODE"; }
            Check(refused && f.Model.TotalCalls == 0, "unpaired source surrogates are refused before lossy serialization");
        }
        f.Model.Reset(); bool contextRefused = false;
        try { await f.Service.RefineDetailed(new("Make that shorter.", Inputs: new(Context: [new("pair", "Source", "bad \uD800") ])), default); }
        catch (BuddyException e) { contextRefused = e.Code == "INVALID_REFINE_UNICODE"; }
        Check(contextRefused && f.Model.TotalCalls == 0, "malformed Unicode in supporting context cannot be silently replaced");
    }

    private static async Task RefusalChecks(Fixture f)
    {
        foreach (string source in new[] {
            "Write a poem.", "Write poems.", "Write Poem.", "Write `poem`.", "Write \"poem\".",
            "Do not write poem.", "Write poem only when requested.", "Write poem. Keep wording verbatim.",
            "Explain difference between RAM and storage. Do not add articles.",
            "Write poem. Don't change it.", "Explain purpose of grammar.",
            "Explain why the alarm did not stop when the door opened.",
            "Please explain the relationship between x and y without changing `x != y`."
        }) {
            f.Model.Reset(); var result = await f.Service.RefineDetailed(new(source), default);
            Check(result.Method != "source-grammar" && result.Grammar is null &&
                (result.Structure?.GrammarRuleIds.Count ?? 0) == 0 && (result.Accepted || result.RefinedPrompt == source),
                "finite path does not expand to unsupported or source-sensitive wording: " + source);
        }
        f.Model.Reset();
        var contradictory = await f.Service.RefineDetailed(new("Write exactly 2 sentences and exactly 3 sentences."), default);
        Check(!contradictory.Accepted && f.Model.TotalCalls == 0, "contradictory requirements still clarify before inference");
        foreach (string source in new[] { "", " \r\n\t" }) {
            f.Model.Reset(); await Throws<BuddyException>(() => f.Service.RefineDetailed(new(source), default), "empty input is explicitly rejected");
            Check(f.Model.TotalCalls == 0, "empty input never reaches transport");
        }
        foreach (string fault in new[] { "wording-empty", "wording-error", "wording-malformed" }) {
            f.Model.Reset(); f.Model.Fault = fault;
            await Throws<Exception>(() => f.Service.RefineDetailed(new("Explain the difference."), default), fault + " cannot become usable refinement");
            Check(f.Model.Assessments == 0 && f.Model.Embeddings == 0, fault + " performs no validation of nonexistent proposal");
        }
    }

    private static async Task ContextChecks(Fixture f)
    {
        f.Model.Reset();
        var longDraft = "Explain purpose of " + string.Join(" ", Enumerable.Repeat("supplied subject", 420)) + ".";
        var oversized = await f.Service.RefineDetailed(new(longDraft), default);
        Check(!oversized.Accepted && oversized.RefinedPrompt == longDraft && f.Model.TotalCalls == 0,
            "finite route checks verification context before any model call");
        f.Model.Reset();
        var request = new RefineRequest(GrammarCases[0].Source, Inputs: new(ConfirmedConstraints: ["Use the supplied source only."]));
        var supported = await f.Service.RefineDetailed(request, default);
        Check(supported.Accepted && supported.RefinedPrompt == GrammarCases[0].Expected + "\n\nUse the supplied source only.",
            "confirmed additions remain separate required blocks");
        Check(f.Model.AssessedRewrite == GrammarCases[0].Expected, "assessment does not treat supplied constraints as invented source wording");
        const string followup = "Make that shorter.";
        var history = new RefinementInputs(Context: [new("selected_pair", "Selected completed pair", "User: Draft a welcome note.\nAssistant: Welcome to the team.", Required: true)]);
        f.Model.Reset(); var contextual = await f.Service.RefineDetailed(new(followup, Inputs: history), default);
        Check(contextual.Accepted && contextual.Method == "context-assembly" && contextual.RefinedPrompt == RefinementPreparation.Prepare(new(followup, Inputs: history)).AssembledText,
            "context-only result delivers the exact reviewed assembly without guessing a referent");
        Check(contextual.ScoreBefore is null && contextual.ScoreAfter is null && contextual.Changes.Single().Contains("supporting inputs") && !contextual.NoChange,
            "context inclusion never claims a model wording improvement or unchanged final payload");
        Check(f.Model.AssessedRewrite == followup && f.Model.Chats == 1,
            "history remains supporting data, not an invented rewrite or unnecessary retry");
        f.Model.Reset(); var omitted = await f.Service.RefineDetailed(new(followup,
            Inputs: new(Context: [history.Context![0] with { Required = false }]),
            Budget: new("Fixture", followup.Length, "utf16-code-units")), default);
        Check(!omitted.Accepted && omitted.NoChange && omitted.Method != "context-assembly" && omitted.RefinedPrompt == followup,
            "omitted context cannot masquerade as an assembled contextual improvement");
        f.Model.Reset();
        var structure = await f.Service.RefineDetailed(new("Write a poem about a boat at sea."), default);
        Check(structure.Accepted && structure.Method == "source-structure" && f.Model.Plans == 1 && f.Model.Chats == 0,
            "existing multi-part structure path remains distinct");
    }

    private static async Task NoChangeReasonChecks(Fixture f)
    {
        const string organized = "Request: Draft a short email.\n\nSubject: A request for Friday off.\n\nConstraint: Do not give a reason for the request.";
        foreach (string source in new[] { organized, "Task: Summarize the supplied report.\nOutput: 2 bullets.", "request: Explain the result.\r\nconstraints: Do not guess." }) {
            Check(RefinementContract.HasExplicitOrganization(source), "nonempty request and supporting headings are observed, not quality-rated");
            f.Model.Reset(); var result = await f.Service.RefineDetailed(new(source), default);
            Check(!result.Accepted && result.NoChange && result.NoChangeReason == "already-organized" && result.RefinedPrompt == source && result.Message == RefinementChange.AlreadyOrganizedMessage,
                "organized echo result explains existing sections without calling the prompt optimal");
            Check(f.Model.Chats == 2 && f.Model.Assessments == 0 && f.Model.Embeddings == 0 && result.Passes.Count == 2,
                "organization classification does not bypass the same two attempts");
            Check(result.ScoreBefore is null && result.ScoreAfter is null && result.Changes.Count == 0 && result.Grammar is null && result.Structure is null,
                "organization observation fabricates no quality score or certified refinement");
        }
        foreach (string source in new[] {
            "Explain the supplied facts.", "Request: Explain the supplied facts.", "Request: Explain.\nTask: Summarize.",
            "Subject: Supplied facts.\nConstraints: Do not guess.", "Request:\nSubject:\nConstraints:",
            "Request: .\nSubject: ---", "```text\nRequest: Explain.\nSubject: Supplied facts.\n```",
            "\"Request: Explain.\"\n\"Subject: Supplied facts.\"", "Prefix Request: Explain.\nPrefix Subject: Facts."
        }) {
            Check(!RefinementContract.HasExplicitOrganization(source), "single, empty, repeated-task, quoted or code headings do not claim organized request");
            f.Model.Reset(); var result = await f.Service.RefineDetailed(new(source), default);
            Check(!result.Accepted && result.NoChange && result.NoChangeReason == "no-verified-improvement" && result.RefinedPrompt == source && result.Message == RefinementChange.EchoMessage,
                "unverified improvement is separate from already-organized presentation");
            Check(f.Model.Chats == 2 && result.ScoreAfter is null && result.Changes.Count == 0,
                "generic unchanged outcome retains retry and no-score behavior");
        }
        f.Model.Reset(); f.Model.CandidateOverride = "Please " + organized;
        var scoreVeto = await f.Service.RefineDetailed(new(organized), default);
        Check(scoreVeto.NoChange && !scoreVeto.Accepted && scoreVeto.NoChangeReason == "already-organized" && scoreVeto.RefinedPrompt == organized &&
            f.Model.Chats == 1 && f.Model.Assessments == 1 && f.Model.Embeddings == 1,
            "assessment-veto presentation changes neither validation calls nor outcome");
        f.Model.Reset(); f.Model.Fault = "assessment-refusal";
        var refusal = await f.Service.RefineDetailed(new("Write poem."), default);
        Check(!refusal.NoChange && !refusal.Accepted && refusal.NoChangeReason is null,
            "failed intent verification is never relabeled already organized or a no-change success");
        bool limited = false;
        for (int size = 1300; size <= 2100 && !limited; size += 50) {
            f.Model.Reset(); string source = "Explain " + new string('x', size) + ".";
            var result = await f.Service.RefineDetailed(new(source), default);
            if (result.NoChangeReason != "context-limit") continue;
            limited = true;
            Check(result.NoChange && !result.Accepted && result.RefinedPrompt == source && result.Message.Contains("context limit") && f.Model.Chats == 1,
                "context-limited retry keeps its actual limiting reason and exact original");
        }
        Check(limited, "context-limited no-change category is exercised within the fixed inference budget");
    }

    private static async Task LifecycleChecks(Fixture f)
    {
        foreach (string stage in new[] { "Source grammar", "Checking intent and constraints" }) {
            f.Model.Reset(); bool cancelled = false, completed = false;
            try {
                await foreach (var item in f.Service.RefineStream(new(GrammarCases[0].Source), default)) {
                    if (item.Type == "stage" && item.Text == stage) f.Service.StopAll();
                    if (item.Result is not null) completed = true;
                }
            } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled && !completed && f.Model.TotalCalls == 0, "Stop at " + stage + " prevents stale result/model work");
        }
        f.Model.Reset(); using (var cancel = new CancellationTokenSource()) {
            cancel.Cancel(); await Throws<OperationCanceledException>(() => f.Service.RefineDetailed(new(GrammarCases[0].Source), cancel.Token), "pre-cancel refuses finite route");
            Check(f.Model.TotalCalls == 0, "pre-cancel has no transport effect");
        }
        f.Model.Reset(); f.Model.HoldAssessment = true;
        var old = f.Service.RefineDetailed(new(GrammarCases[0].Source), default);
        await f.Model.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); f.Service.StopAll();
        await Throws<OperationCanceledException>(() => old, "held late assessment cannot become accepted after Stop");
        f.Model.Release.TrySetResult(); await f.Model.Exited.Task.WaitAsync(TimeSpan.FromSeconds(3));
        // The semaphore retains ownership until the late call settles. A fresh request
        // may queue briefly, but must never observe old acceptance or old candidate text.
        f.Model.HoldAssessment = false;
        var fresh = await f.Service.RefineDetailed(new(GrammarCases[1].Source), default).WaitAsync(TimeSpan.FromSeconds(3));
        Check(fresh.Accepted && fresh.RefinedPrompt == GrammarCases[1].Expected, "fresh turn after Stop uses its own draft");
        for (int i = 0; i < 3; i++) {
            f.Model.Reset(); var sample = GrammarCases[i];
            var result = await f.Service.RefineDetailed(new(sample.Source), default);
            Check(result.Accepted && result.RefinedPrompt == sample.Expected && f.Model.Assessments == 1,
                "repeated turns do not carry prior draft, result or model state");
        }
    }

    private static async Task Throws<T>(Func<Task<RefinementResult>> action, string label) where T : Exception
    {
        try { await action().WaitAsync(TimeSpan.FromSeconds(4)); Check(false, label); }
        catch (T) { Check(true, label); }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "Buddy.RefinementBuilder.Tests." + Guid.NewGuid().ToString("N"));
        private readonly HttpClient client;
        internal StateStore Store { get; }
        internal Model Model { get; } = new();
        internal BuddyService Service { get; }
        internal Fixture()
        {
            Directory.CreateDirectory(directory);
            Store = new(directory, DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(directory, "keys"))));
            client = new(Model) { BaseAddress = new("http://127.0.0.1:11434/") };
            Service = new(Store, new(client));
        }
        public void Dispose() { client.Dispose(); Directory.Delete(directory, true); }
    }

    private sealed class Model : HttpMessageHandler
    {
        internal int Chats, Plans, Assessments, Embeddings;
        internal int TotalCalls => Chats + Plans + Assessments + Embeddings;
        internal string Fault = "", AssessedRewrite = "";
        internal string? CandidateOverride;
        internal string[]? EmbeddedText;
        internal List<string> RawInputs = [];
        internal float Similarity = 1;
        internal bool HoldAssessment;
        internal TaskCompletionSource Entered = Signal(), Release = Signal(), Exited = Signal();
        internal void Reset()
        {
            Chats = Plans = Assessments = Embeddings = 0; Fault = AssessedRewrite = "";
            EmbeddedText = null; CandidateOverride = null; RawInputs.Clear(); Similarity = 1; HoldAssessment = false;
            Entered = Signal(); Release = Signal(); Exited = Signal();
        }
        private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct)); var payload = body.RootElement;
            if (request.RequestUri!.AbsolutePath == "/api/embed") {
                Embeddings++; EmbeddedText = payload.GetProperty("input").EnumerateArray().Select(x => x.GetString()!).ToArray();
                if (Fault == "embedding-error") return new(HttpStatusCode.NotFound);
                if (Fault == "embedding-malformed") return Json(new { embeddings = new[] { new[] { 0f, 0f } } });
                float score = Fault == "low-similarity" ? 0 : Similarity;
                return Json(new { embeddings = EmbeddedText.Select((_, i) => i == 0 ? new[] { 1f, 0f } : new[] { score, (float)Math.Sqrt(1 - score * score) }) });
            }
            string input = payload.GetProperty("messages")[1].GetProperty("content").GetString()!;
            RawInputs.Add(input);
            using var data = JsonDocument.Parse(input);
            if (payload.GetProperty("stream").GetBoolean()) {
                Chats++;
                if (Fault == "wording-error") return new(HttpStatusCode.InternalServerError);
                if (Fault == "wording-malformed") return new(HttpStatusCode.OK) { Content = new StringContent("{invalid}\n") };
                string echo = Fault == "wording-empty" ? "" : CandidateOverride ?? data.RootElement.GetProperty("untrustedDraft").GetString()!;
                return Json(new { message = new { content = echo }, done = true });
            }
            if (data.RootElement.TryGetProperty("untrustedSpans", out var spans)) {
                Plans++;
                return Structured(new { sections = spans.EnumerateArray().Select(s => new { kind = s.GetProperty("kind").GetString(), sourceIds = new[] { s.GetProperty("id").GetString() } }) });
            }
            Assessments++; AssessedRewrite = data.RootElement.GetProperty("rewrite").GetString()!;
            if (HoldAssessment) { Entered.TrySetResult(); await Release.Task; Exited.TrySetResult(); } // Hostile transport ignores cancellation.
            if (Fault == "assessment-error") return new(HttpStatusCode.InternalServerError);
            if (Fault == "assessment-malformed") return Json(new { message = new { content = "{invalid}" }, done = true });
            return Structured(new { preserved = Fault != "assessment-refusal", scoreBefore = 85, scoreAfter = 85, changes = new[] { "Untrusted model claim" } });
        }
        private static HttpResponseMessage Structured(object value) => Json(new { message = new { content = JsonSerializer.Serialize(value) }, done = true });
        private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value) + "\n") };
    }
}
