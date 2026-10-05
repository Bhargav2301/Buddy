namespace Buddy.Server;

/// <summary>
/// Deterministic request preparation shared by previews and the refinement service.
/// AssembledText includes every retained delimiter and JSON character. When Ready is
/// false it is a diagnostic preview, never an approved rewrite or permission to apply.
/// </summary>
public sealed record RefinementPreparationResult(RefineRequest Request, string Mode,
    RefinementTechniqueChoice Choice, RefinementContextResult Context, RefinementBudgetResult Budget,
    IReadOnlyList<string> Warnings, bool Ready, string AssembledText, IReadOnlyList<RefinementBlock> Blocks);

public static class RefinementPreparation
{
    /// <summary>
    /// Validate and copy a request, choose its technique, and assemble bounded source
    /// context under its explicit destination budget. No model, retrieval, persistence
    /// or destination limit registry is involved. Ready checks these prerequisites;
    /// inference context capacity and the final candidate still need service validation.
    /// </summary>
    public static RefinementPreparationResult Prepare(RefineRequest request, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (request is null) throw new BuddyException("INVALID_REFINE_OPTIONS", "A refinement request is required.");
        // Existing request contracts use mutable lists. Copy them before validation so
        // later edits by a UI/caller cannot change an in-flight service request. Treat
        // the returned snapshot as owned by this preparation; do not mutate its lists.
        request = Snapshot(request);
        RefinementPolicy.Validate(request);
        var mode = RefinementPolicy.Mode(request);
        var choice = RefinementCore.SelectTechnique(request);
        var context = RefinementContext.Build(request.Inputs?.Context);
        var blocks = RefinementContext.RequestBlocks(request, request.Prompt, context);
        var budget = RefinementCore.ApplyBudget(blocks, request.Budget);
        var warnings = choice.Warnings.Concat(context.Warnings).ToList();
        if (budget.Removed.Count > 0) warnings.Add("Optional context was omitted to meet the selected destination limit.");
        // ApplyBudget deliberately returns empty Text for a conflict. Keep the exact
        // surviving required payload available for review without clipping it to fit.
        string assembled = string.Join("\n\n", blocks.Where(b => !budget.Removed.Contains(b.Id) && b.Text.Length > 0).Select(b => b.Text));
        ct.ThrowIfCancellationRequested();
        return new(request, mode, choice, context, budget, warnings.AsReadOnly(),
            choice.Ready && context.Ready && budget.Fits, assembled, blocks.AsReadOnly());
    }

    private static RefineRequest Snapshot(RefineRequest request)
    {
        var input = request.Inputs;
        return request with { Inputs = input is null ? null : input with {
            Examples = input.Examples?.Select(x => x is null ? null! : x with { }).ToList(),
            AvailableTools = input.AvailableTools?.ToList(),
            Stages = input.Stages?.ToList(),
            ConfirmedConstraints = input.ConfirmedConstraints?.ToList(),
            Context = input.Context?.Select(x => x is null ? null! : x with { }).ToList()
        } };
    }
}
