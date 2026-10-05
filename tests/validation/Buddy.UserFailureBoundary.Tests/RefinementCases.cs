using Buddy.Server;

internal static class RefinementCases
{
    internal static async Task Run(Func<string, Func<Task>, Task> test)
    {
        await test("legacy wording echo has one faithful retry and a changed accepted result", async () => {
            using var f = new RefineFixture(); f.Model.Candidates.Enqueue(RefineFixture.Original); f.Model.Candidates.Enqueue(RefineFixture.Faithful);
            var result = await f.Service.RefineDetailed(new(RefineFixture.Original), default);
            Require.True(result.Accepted && !result.NoChange && result.Method == "wording" && result.Structure is null && result.RefinedPrompt == RefineFixture.Faithful && f.Model.Chats == 2 && f.Model.Assessments == 1 && f.Model.Embeddings == 1, "Recover wording echo once, then pass the changed retry through every verification gate.");
        });
        await test("two legacy wording echoes return explicit no-refinement with no applicable scores", async () => {
            using var f = new RefineFixture(); f.Model.Candidates.Enqueue(RefineFixture.Original); f.Model.Candidates.Enqueue(RefineFixture.Original);
            var result = await f.Service.RefineDetailed(new(RefineFixture.Original), default);
            Require.True(!result.Accepted && result.NoChange && result.RefinedPrompt == RefineFixture.Original && result.ScoreBefore is null && result.ScoreAfter is null && result.Similarity is null && result.Changes.Count == 0, "Do not advertise an unchanged prompt as an accepted improvement.");
            Require.True(f.Model.Chats == 2 && f.Model.Assessments == 0 && f.Model.Embeddings == 0, "Repeated echo is bounded and needs no self-assessment to reject it.");
        });
        await test("whitespace punctuation and case-only retries cannot claim useful improvement", async () => {
            using var f = new RefineFixture(); f.Model.Candidates.Enqueue("  " + RefineFixture.Original + "  "); f.Model.Candidates.Enqueue(RefineFixture.Original.Replace(" ", "\n  "));
            var unchanged = await f.Service.RefineDetailed(new(RefineFixture.Original), default);
            Require.True(!unchanged.Accepted && unchanged.NoChange && unchanged.RefinedPrompt == RefineFixture.Original, "Whitespace folding must not manufacture improvement.");
            f.Model.Candidates.Enqueue(RefineFixture.Original + "."); f.Model.Candidates.Enqueue(RefineFixture.Original.ToUpperInvariant() + "!");
            var punctuation = await f.Service.RefineDetailed(new(RefineFixture.Original), default);
            Require.True(!punctuation.Accepted && punctuation.NoChange && punctuation.RefinedPrompt == RefineFixture.Original && punctuation.Changes.Count == 0 && punctuation.ScoreBefore is null && punctuation.ScoreAfter is null && f.Model.Assessments == 0 && f.Model.Embeddings == 0,
                "Cosmetic output is not a useful refinement even if a model might call it a perfect improvement.");
        });
        await test("a retry inventing requirements is rejected before model assessment", async () => {
            using var f = new RefineFixture(); f.Model.Candidates.Enqueue(RefineFixture.Original); f.Model.Candidates.Enqueue(RefineFixture.Faithful + " Include a captain and a tragic ending.");
            var result = await f.Service.RefineDetailed(new(RefineFixture.Original), default);
            Require.True(!result.Accepted && result.RefinedPrompt == RefineFixture.Original && f.Model.Chats == 2 && f.Model.Assessments == 0, "Repair permission does not permit new creative constraints.");
        });
        foreach (int score in new[] { 70, 65 })
        await test(score == 70 ? "a lexical rewrite with an equal score is truthful no-refinement" : "a lexical rewrite with a lower score is truthful no-refinement", async () => {
            using var f = new RefineFixture(); f.Model.Candidates.Enqueue(RefineFixture.Original); f.Model.Candidates.Enqueue(RefineFixture.Faithful); f.Model.ScoreAfter = score;
            var result = await f.Service.RefineDetailed(new(RefineFixture.Original), default);
            Require.True(!result.Accepted && result.NoChange && result.RefinedPrompt == RefineFixture.Original && result.ScoreBefore is null && result.ScoreAfter is null && result.Similarity is null && result.Changes.Count == 0,
                "A wording-only change without assessed improvement must not advertise an applicable useful result or display misleading scores.");
            Require.True(f.Model.Chats == 2 && f.Model.Assessments == 1, "Equal/lower quality after the one repair does not permit unbounded inference.");
        });
        await test("retry cannot lose negation numeric or code literals", async () => {
            const string original = "Write a 7-line poem; do not mention rain and keep `sea = 7`.";
            using var f = new RefineFixture(); f.Model.Candidates.Enqueue(original); f.Model.Candidates.Enqueue("Write a 9-line poem; mention rain and keep `sea = 9`.");
            var result = await f.Service.RefineDetailed(new(original), default);
            Require.True(!result.Accepted && result.RefinedPrompt == original && f.Model.Assessments == 0, "A more visibly different retry cannot erase exact constraints.");
        });
        foreach (bool assessmentVeto in new[] { true, false })
        await test(assessmentVeto ? "changed retry remains subject to assessment veto" : "changed retry remains subject to embedding veto", async () => {
            using var f = new RefineFixture(); f.Model.Candidates.Enqueue(RefineFixture.Original); f.Model.Candidates.Enqueue(RefineFixture.Faithful); f.Model.AssessmentAllows = !assessmentVeto; f.Model.Similar = assessmentVeto;
            var result = await f.Service.RefineDetailed(new(RefineFixture.Original), default);
            Require.True(!result.Accepted && result.RefinedPrompt == RefineFixture.Original && f.Model.Chats == 2, "Retry must not bypass the original verification veto.");
        });
        await test("required explicit additions count against final assembly rather than draft echo", async () => {
            using var f = new RefineFixture(); f.Model.Candidates.Enqueue(RefineFixture.Original); f.Model.ScoreAfter = f.Model.ScoreBefore;
            var result = await f.Service.RefineDetailed(new(RefineFixture.Original, Inputs: new(ConfirmedConstraints: ["Do not send."])), default);
            Require.True(result.Accepted && !result.NoChange && result.RefinedPrompt == RefineFixture.Original + "\n\nDo not send.", "Explicit required supporting text is a reviewable change to the final destination.");
        });
        await test("pre-cancel and cancellation at retry stage produce no stale acceptance", async () => {
            using var f = new RefineFixture(); using var early = new CancellationTokenSource(); early.Cancel();
            await Require.Cancelled(async () => { await f.Service.RefineDetailed(new(RefineFixture.Original), early.Token); });
            Require.True(f.Model.Payloads.Count == 0, "Pre-cancelled refinement must infer nothing.");
            f.Model.Candidates.Enqueue(RefineFixture.Original); f.Model.Candidates.Enqueue(RefineFixture.Faithful);
            using var stop = new CancellationTokenSource(); bool resultSeen = false, retrySeen = false;
            await Require.Cancelled(async () => {
                await foreach (var item in f.Service.RefineStream(new(RefineFixture.Original), stop.Token)) {
                    if (item.Type == "stage" && item.Text == "Trying one faithful wording revision") { retrySeen = true; stop.Cancel(); }
                    if (item.Result is not null) resultSeen = true;
                }
            });
            Require.True(retrySeen && !resultSeen && f.Model.Chats == 1, "Stop at the published retry stage must be honored before the second inference.");
        });
        await test("canceled late second candidate is discarded and inference can be reused", async () => {
            using var f = new RefineFixture(); f.Model.Candidates.Enqueue(RefineFixture.Original); f.Model.Candidates.Enqueue(RefineFixture.Faithful);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Model.BeforeChat = async (call, _) => { if (call == 2) { entered.TrySetResult(); await release.Task; } };
            using var stop = new CancellationTokenSource(); var pending = f.Service.RefineDetailed(new(RefineFixture.Original), stop.Token);
            try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); stop.Cancel(); release.TrySetResult(); await Require.Cancelled(async () => { await pending; }); }
            finally { release.TrySetResult(); }
            Require.True(f.Model.Assessments == 0 && f.Model.Embeddings == 0, "Late canceled repair cannot be assessed or emitted as done.");
            f.Model.BeforeChat = null; f.Model.Candidates.Enqueue(RefineFixture.Faithful);
            Require.True((await f.Service.RefineDetailed(new(RefineFixture.Original), default)).Accepted, "Canceled repair must release the inference owner.");
            Require.True(await f.Store.Read(s => s.Conversations.Count + s.Jobs.Count + s.Audit.Count + s.Guides.Count) == 0, "Refinement and its cancellation persist no completion or conversation.");
        });
    }
}
