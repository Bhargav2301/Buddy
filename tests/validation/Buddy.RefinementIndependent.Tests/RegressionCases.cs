using Buddy.Server;
using System.Text.Json;

internal static partial class Program
{
    private static async Task RegressionCases()
    {
        foreach (var pair in new[] {
            ("These reports contains 7 errors.", "These reports contain 7 errors."),
            ("We does not approve the launch.", "We do not approve the launch."),
            ("Draft two mesages to Kim; do not promise a date.", "Draft two messages to Kim; do not promise a date."),
            ("Pleae describe the chart without adding values.", "Please describe the chart without adding values.")
        }) {
            await Case("finite spelling/agreement preserves exact remaining source: " + pair.Item1, async () => {
                using var f = new Fixture(); f.Model.Wording = "This model proposal must not be used.";
                var r = await f.Service.RefineDetailed(new(pair.Item1), default);
                Check(r.Accepted && r.Method == "source-grammar" && r.RefinedPrompt == pair.Item2,
                    "finite edit changed remaining wording, number, negative or relation");
                Check(f.Model.Chats == 0 && f.Model.Plans == 0 && f.Model.Assessments == 1 && f.Model.Embeddings == 1 && r.Grammar is not null,
                    "finite edit lacked source proof or independent verification");
            });
        }
        foreach (var text in new[] {
            "The project status needs review.", "The news is ready.",
            "`These reports contains errors.`", "Write mesage. Keep every word.",
            "Create mesage about the command foo.", "Write mesage only if authorized.",
            "Explain the exact difference between A and B."
        }) {
            SyncCase("finite grammar does not alter protected/unsupported source: " + text, () => {
                Check(RefinementGrammar.Propose(text, "task", text) is null, "finite grammar changed protected or unsupported wording");
            });
        }
        foreach (var request in new[] {
            new RefineRequest("Summarize \uD800."),
            new RefineRequest(Plain, Inputs: new(ConfirmedConstraints: ["Retain \uDC00"])),
            new RefineRequest(Plain, Inputs: new(Examples: [new("\uD800", "valid")]))
        }) {
            await Case("malformed UTF16 rejected before model dispatch", async () => {
                using var f = new Fixture();
                var error = await Throws<BuddyException>(() => f.Service.RefineDetailed(request, default));
                Check(error.Code == "INVALID_REFINE_UNICODE" && f.Model.Calls == 0, "malformed source silently replaced or sent to model");
            });
        }
        await Case("real supplementary scalar and literal surrogate text stay distinct", async () => {
            using var f = new Fixture();
            string text = "Summarize \uD83C\uDF19 and literal `\\uD83C\\uDF19` with quoted \"x\" and line\nending.";
            f.Model.Wording = text; await f.Service.RefineDetailed(new(text), default);
            string payload = f.Model.WordingPayloads.First(); using var json = JsonDocument.Parse(payload);
            Check(json.RootElement.GetProperty("untrustedDraft").GetString() == text, "mixed scalar/literal/control JSON roundtrip changed source");
            Check(payload.Contains("\uD83C\uDF19") && payload.Contains("\\\\uD83C\\\\uDF19"), "real and literal surrogate forms were conflated");
        });
        await Case("Stop at source grammar stage prevents verification", async () => {
            using var f = new Fixture(); using var stop = new CancellationTokenSource(); bool reached = false;
            await Throws<OperationCanceledException>(async () => {
                await foreach (var e in f.Service.RefineStream(new("Prepare outline."), stop.Token)) {
                    if (e.Text == "Source grammar") { reached = true; stop.Cancel(); }
                    Check(e.Result is null, "stopped grammar review published a terminal result");
                }
            });
            Check(reached && f.Model.Calls == 0, "Stop at grammar boundary still dispatched model");
        });
        foreach (var pair in new[] { ("Write poem.", "Write a poem."), ("Explain difference between RAM and storage.", "Explain the difference between RAM and storage."),
            ("Prepare outline.", "Prepare an outline."), ("Compose short letter about Friday's meeting.", "Compose a short letter about Friday's meeting.") }) {
            await Case("source grammar useful exact edit: " + pair.Item1, async () => {
                using var f = new Fixture(); f.Model.Wording = pair.Item1;
                var r = await f.Service.RefineDetailed(new(pair.Item1), default);
                Check(r.Accepted && r.RefinedPrompt == pair.Item2 && r.Method == "source-grammar", "finite grammar was unavailable, formatted differently or expanded beyond source");
                Check(r.ScoreBefore is null && r.ScoreAfter is null && f.Model.Chats == 0 && f.Model.Plans == 0 && f.Model.Assessments == 1 && f.Model.Embeddings == 1,
                    "grammar route skipped independent validation or claimed model quality");
            });
        }
        foreach (var text in new[] {
            "Write exactly 3 sentences about the first proposal and exactly 5 sentences about the second proposal.",
            "Write 2 paragraphs about the old system and 4 paragraphs about the new system.",
            "Write a report about battery life and draft a letter about the warranty.",
            "Write 3 sentences about A and 4 detailed paragraphs about B.",
            "Write 3 sentences about A and then also 4 paragraphs about B.",
            "Write 3 sentences about A and a minimum of 5 paragraphs about B."
        }) {
            SyncCase("compound source keeps complete output obligations: " + text, () => {
                var ledger = RefinementContract.Analyze(text);
                var plan = new RefinementContractPlan(ledger.Spans.Select(s => new RefinementContractSection(s.Kind, [s.Id])).ToList());
                var r = RefinementContract.Compose(ledger, plan);
                Check(!r.Valid || !r.Useful || r.Text.Contains(text, StringComparison.Ordinal),
                    "compound request was split across task/subject in a way that changes obligation scope: " + r.Text);
            });
        }
        foreach (var failure in new[] { "assessment-false", "assessment-http", "embedding-http", "low-similarity" }) {
            await Case("finite grammar still refuses " + failure, async () => {
                using var f = new Fixture(); f.Model.Wording = "Write poem."; f.Model.ValidationFault = failure;
                var r = await f.Service.RefineDetailed(new("Write poem."), default);
                Check(!r.Accepted && r.RefinedPrompt == "Write poem.", "finite source route bypassed verifier failure");
                Check(f.Model.Assessments == 1 && (failure == "assessment-http" || f.Model.Embeddings == 1), "injected validation failure was never reached");
            });
        }
        await Case("reviewed additions without source rewrite have honest provenance", async () => {
            using var f = new Fixture(); f.Model.Wording = Plain;
            var r = await f.Service.RefineDetailed(new(Plain, Inputs: new(ConfirmedConstraints: ["Use short sentences."])), default);
            Check(r.Accepted && r.RefinedPrompt.Contains(Plain) && r.RefinedPrompt.Contains("Use short sentences."), "reviewed required addition missing");
            Check(r.Method == "context-assembly" && r.ScoreBefore is null && r.ScoreAfter is null && r.Changes.Count > 0,
                "context-only assembly presented as model wording improvement");
        });
        foreach (var text in new[] { "Draft a short invitation to Zoë and 李明; retain 🌙.", "Explain the literal `\\u674E` without replacing it." }) {
            await Case("JSON model input preserves source characters: " + text, async () => {
                using var f = new Fixture(); f.Model.Wording = text;
                await f.Service.RefineDetailed(new(text), default);
                var payload = f.Model.WordingPayloads.First(); using var json = JsonDocument.Parse(payload);
                Check(json.RootElement.GetProperty("untrustedDraft").GetString() == text, "JSON roundtrip altered source");
                if (text.Contains('李')) Check(payload.Contains("李明") && payload.Contains("Zoë"), "prompt represents real Unicode as escape-code text for the model to copy");
                else Check(payload.Contains("\\\\u674E") && !payload.Contains('李'), "literal source escape was decoded into a different character");
            });
        }
    }
}
