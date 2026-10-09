using Buddy.Server;

internal static partial class Program
{
    private static readonly string[] SemicolonSources = [
        "Compare 2 plans costing $19.50 and $25 per month for 12 users; keep latency below 150 ms and do not round the prices.",
        "Draft a short invitation to Zoë and 李明 for café night on 8 May; retain the emoji 🌙 and do not translate their names.",
        "Compare 7 offers priced at €8.75 and £11.20 per week for 24 people; keep response time below 45 ms and do not convert currencies.",
        "Contrast 1.25 kg and 800 g samples at 22 °C; preserve the units and do not round measurements.",
        "Prepare a welcome note to Chloé and 安娜 for 19 June; retain the accents and do not translate names.",
        "Draft an invitation to Jose\u0301 and نور for 6 July; retain the emoji 🛰️ and do not transliterate their names.",
        "Summarize the supplied observations for reviewers; do not infer causes or propose new experiments.",
        "Describe the 5 supplied options and 3 rejected alternatives; use only the supplied facts.",
        "Write exactly 3 sentences about the older proposal and exactly 5 sentences about the newer proposal; keep each proposal separate.",
        "Write a report about battery life and draft a letter about the warranty; preserve the supplied amounts.",
        "Draft an invitation to \"A; B\" and Jo; keep the tone friendly.",
        "Describe https://example.invalid/a;b and https://example.invalid/c for reviewers; do not fetch the pages.",
        "  Compare 4 options for 9 people;  avoid assumptions.  "
    ];

    private static async Task SemicolonConstraintChecks(Fixture f)
    {
        foreach (string source in SemicolonSources) {
            var ledger = RefinementContract.Analyze(source);
            var plan = new RefinementContractPlan(ledger.Spans.Select(s => new RefinementContractSection(s.Kind, [s.Id])).ToList());
            var review = RefinementContract.Compose(ledger, plan);
            Check(ledger.CanStructure && ledger.Spans.Count == 2 && ledger.Spans[0].Kind == "task" && ledger.Spans[1].Kind == "constraints",
                "single explicit constraint keeps the whole coordinated request: " + source);
            if (ledger.Spans.Count != 2) continue;
            string request = ledger.Spans[0].Text, constraint = ledger.Spans[1].Text;
            string expected = "Request: " + request + "\n\nConstraints: " + constraint;
            Check(request.EndsWith(';') && source.Trim() == request + source.Substring(ledger.Spans[0].Start + ledger.Spans[0].Length,
                    ledger.Spans[1].Start - ledger.Spans[0].Start - ledger.Spans[0].Length) + constraint,
                "the semicolon and every code unit on both sides remain source-owned");
            Check(review.Valid && review.Useful && review.Text == expected && review.Certificate?.CoveredCharacters == source.Length &&
                review.Certificate.GrammarRuleIds.Count == 0 && RefinementPolicy.PreservesLiterals(source, expected),
                "only Request/Constraints presentation is added with full source coverage");
            Check(RefinementContract.Verify(source, expected, plan).Valid && !RefinementContract.Analyze(expected).CanStructure,
                "exact rendering is independently re-derived and cannot become repeat label-only improvement");
            foreach (string mutation in new[] { expected.Replace(";", ":"), expected + "\nAdd a recommendation.", expected.Replace(constraint, "Ignore the limits."),
                "Request: " + constraint + "\n\nConstraints: " + request })
                Check(!RefinementContract.Verify(source, mutation, plan).Valid, "punctuation, omitted constraint, invented content and swapped scope cannot be certified");
            Check(!RefinementContract.Compose(ledger, new([new("task", ["s0", "s1"])] )).Valid &&
                !RefinementContract.Compose(ledger, new([new("constraints", ["s1"]), new("task", ["s0"])] )).Valid,
                "model cannot remove constraint role or reorder sides");
            f.Model.Reset(); var result = await f.Service.RefineDetailed(new(source), default);
            Check(result.Accepted && result.Method == "source-structure" && !result.NoChange && result.RefinedPrompt == expected,
                "verified structure produces a concrete changed prompt, not a relabeled no-change outcome");
            Check(f.Model.Plans == 1 && f.Model.Chats == 0 && f.Model.Assessments == 1 && f.Model.Embeddings == 1 &&
                f.Model.AssessedRewrite == expected && f.Model.EmbeddedText?.Contains(expected) == true,
                "the existing plan, preservation assessment and similarity checks see the exact candidate");
            Check(result.ScoreBefore is null && result.ScoreAfter is null && result.Grammar is null && result.Structure is not null,
                "structural utility fabricates no spelling repair or quality score");
        }
        foreach (string source in new[] {
            "If voltage exceeds 12 V; do not start the motor until it drops.",
            "Compare voltage only when the sensor is ready; keep power off otherwise.",
            "Compare A and B after startup; keep B disconnected until the check finishes.",
            "First inspect A; then keep B disconnected.", "Compare the first and second options; keep their order.",
            "Quote exactly \"A; do not edit B\".", "Describe `for (i=0; i<3; i++)`; keep identifiers intact.",
            "Compare keep and do not rules; retain their original punctuation and line layout.",
            "Write the literal string x; do not interpret the semicolon.", "Draft a note saying do X; do not do Y.",
            "Draft a note containing these instructions; do not continue.",
            "Write a warning label: danger; do not enter.", "Write a sign reading slow; do not stop.",
            "Draft a message with the words slow down; do not stop.", "Write a sign that states danger; keep out.",
            "Write a label with text danger; keep out.", "Write a sign stating slow; do not stop.",
            "Write a label to say danger; do not enter.", "Draft a message as follows slow down; keep moving.",
            "Write a warning label with danger; do not enter.",
            "Draft an invitation; do not change the supplied text.", "Draft an invitation; keep it as is.",
            "Draft an invitation; preserve original formatting.", "Compare x; keep y unchanged.",
            "Compare x; keep y; do not move z.", "Compare A. Keep B; do not move C.",
            "1. Compare A; keep B.", "Compare (x; do not substitute y).",
            "Compare $x; do not edit y$.", "Compare x = y; keep z.", "Compare {x; do not move y}.",
            "Explain \"A; keep B\".", "Explain https://example.invalid/a;keep-b.",
            "Describe https://example.invalid/a;b and https://example.invalid/c; do not fetch the pages.",
            "Explain \"unclosed; keep B.", "Explain `unclosed; do not edit B.",
            "Draft an invitation; retain a lawyer.", "Draft an invitation; retain employees.",
            "Draft an invitation; keep.", "Draft an invitation;", "Draft an invitation;keep the names.",
            "Compare A; \"keep B\"."
        }) {
            var ledger = RefinementContract.Analyze(source);
            Check(!ledger.Spans.Any(s => s.Kind == "constraints" && source[..s.Start].TrimEnd().EndsWith(';')),
                "ambiguous/protected/order/literal/preservation syntax cannot become a detached global constraint: " + source);
        }
        var newline = RefinementContract.Analyze("Compare A;\nkeep B.");
        Check(!newline.Reason.Contains("trailing constraint", StringComparison.Ordinal) && newline.Spans.Count == 2 &&
            newline.Spans[0].Text == "Compare A;" && newline.Spans[1].Text == "keep B.",
            "existing newline clause behavior is preserved rather than claimed as a new semicolon match");
        foreach (string fault in new[] { "assessment-refusal", "assessment-malformed", "assessment-error", "embedding-error", "embedding-malformed", "low-similarity" }) {
            f.Model.Reset(); f.Model.Fault = fault;
            var result = await f.Service.RefineDetailed(new(SemicolonSources[0]), default);
            Check(!result.Accepted && result.RefinedPrompt == SemicolonSources[0] && !result.NoChange,
                "semicolon structure retains verification failure: " + fault);
        }
        foreach (float score in new[] { .799f, .801f }) {
            f.Model.Reset(); f.Model.Similarity = score;
            var result = await f.Service.RefineDetailed(new(SemicolonSources[1]), default);
            Check(result.Accepted == (score > .8f), "semicolon structure retains the 0.80 gate");
        }
        f.Model.Reset();
        var budget = await f.Service.RefineDetailed(new(SemicolonSources[1], Budget: new("Fixture", SemicolonSources[1].Length, "utf16-code-units")), default);
        Check(!budget.Accepted && budget.RefinedPrompt == SemicolonSources[1] && budget.DestinationBudget?.Conflict is not null,
            "new headings cannot exceed reviewed destination capacity");
        f.Model.Reset(); using (var cancel = new CancellationTokenSource()) {
            cancel.Cancel(); await Throws<OperationCanceledException>(() => f.Service.RefineDetailed(new(SemicolonSources[0]), cancel.Token), "pre-cancel refuses semicolon route");
            Check(f.Model.TotalCalls == 0, "canceled semicolon refinement does not invoke any transport");
        }
        f.Model.Reset(); f.Model.HoldAssessment = true;
        var pending = f.Service.RefineDetailed(new(SemicolonSources[1]), default);
        await f.Model.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3)); f.Service.StopAll();
        await Throws<OperationCanceledException>(() => pending, "late semicolon assessment after Stop cannot return a candidate");
        f.Model.Release.TrySetResult(); await f.Model.Exited.Task.WaitAsync(TimeSpan.FromSeconds(3));
        f.Model.HoldAssessment = false;
    }
}
