#if HAS_REFINE53
using Buddy.Server;
using Buddy.Windows;

internal static class RefineCases
{
    private static RefinementDraftOptions Options(IReadOnlyList<RefinementExample>? examples = null, IReadOnlyList<RefinementContextSource>? references = null) => new(
        "auto", "general", false, false, "", "", "", examples ?? [], references ?? [], false, "", "", "utf16-code-units");
    internal static void Run(Checks c)
    {
        const string original = "Write a poem on a boat sailing in a sea on a lonely night";
        var slot = new ExternalRefinementOptionsSlot();
        c.Check(!slot.HasPending && slot.Consume(0) is null, "REFINE-DEFAULT-OFF", "No external field preparation is armed by default.");
        var examples = new List<RefinementExample> { new("Original example", "Reviewed example") };
        slot.Arm(Options(examples), "quick", 1000);
        examples[0] = new("Late unreviewed input", "Late unreviewed output");
        var plan = slot.Consume(1001)!;
        c.Check(plan.IsCurrent && !slot.HasPending && slot.Consume(1002) is null, "REFINE-ONE-CONSUME", "Consuming burns the pending options before any capture can fail.");
        var prepared = plan.Bind(original);
        c.Check(prepared.Request.Prompt == original && prepared.Request.Inputs?.Examples?[0].Input == "Original example", "REFINE-DEEP-SNAPSHOT", "Later caller list edits cannot replace the reviewed options or captured original.");
        c.Rejects<InvalidOperationException>(() => plan.Bind("Another field"), "REFINE-ONE-FIELD", "A consumed plan can bind exactly one original field.");
        slot.Clear();
        c.Check(!plan.IsCurrent, "REFINE-CLEAR-CAPTURED", "Stop or options editing invalidates even a consumed and bound plan.");

        slot.Arm(Options(), "quick", 2000); var stale = slot.Consume(2001)!;
        slot.Arm(Options() with { Constraints = "Keep the exact code." }, "quick", 2002);
        c.Check(!stale.IsCurrent && slot.HasPending, "REFINE-REPLACEMENT", "New preparation expires old capture authority without clearing the newer options.");
        c.Rejects<InvalidOperationException>(() => stale.Bind(original), "REFINE-STALE-BIND", "An older preparation cannot bind after replacement.");
        var current = slot.Consume(2003)!;
        c.Check(current.IsCurrent && current.Bind(original).Request.Inputs?.ConfirmedConstraints?.Single() == "Keep the exact code.", "REFINE-CURRENT-SURVIVES", "Rejected stale work leaves the new option snapshot usable once.");

        slot.Arm(Options(), "quick", 3000);
        c.Rejects<InvalidOperationException>(() => slot.Consume(2999), "REFINE-BACKWARD-CLOCK", "A backwards monotonic timestamp refuses and consumes preparation.");
        c.Check(!slot.HasPending, "REFINE-BACKWARD-BURNS", "Clock refusal cannot silently rearm capture authority.");
        slot.Arm(Options(), "quick", 4000);
        c.Rejects<InvalidOperationException>(() => slot.Consume(304000), "REFINE-EXPIRY", "Exactly five minutes is expired, not an extra grace interval.");
        c.Check(slot.Consume(304001) is null, "REFINE-EXPIRED-NO-RETRY", "Expired preparation cannot be replayed on another field.");

        slot.Arm(Options(), "quick", 5000); var beforeInvalid = slot.Consume(5001)!;
        c.Rejects<InvalidOperationException>(() => slot.Arm(Options() with { HasBudget = true, Destination = "", Limit = "1" }, "quick", 5002), "REFINE-INVALID-REPLACEMENT", "Malformed replacement options fail preflight.");
        c.Check(!slot.HasPending && !beforeInvalid.IsCurrent, "REFINE-INVALID-BURNS-OLD", "Invalid new options cannot leave a previous capture silently armed/current.");

        slot.Arm(Options(), "quick", 6000);
        var consumed = new System.Collections.Concurrent.ConcurrentBag<ExternalRefinementPlan>();
        Parallel.For(0, 16, _ => { if (slot.Consume(6001) is { } value) consumed.Add(value); });
        c.Check(consumed.Count == 1, "REFINE-CONCURRENT-CONSUME", "Competing entry points obtain at most one field preparation.");
        var one = consumed.Single(); int bound = 0, refused = 0;
        Parallel.For(0, 16, _ => { try { one.Bind(original); Interlocked.Increment(ref bound); } catch (InvalidOperationException) { Interlocked.Increment(ref refused); } });
        c.Check(bound == 1 && refused == 15, "REFINE-CONCURRENT-BIND", "One option plan cannot concurrently authorize multiple original fields.");

        slot.Arm(Options() with { HasBudget = true, Destination = "Owned test field", Limit = "1" }, "quick", 7000);
        var overflow = slot.Consume(7001)!.Bind(original);
        c.Check(!overflow.Ready && !overflow.Budget.Fits && overflow.AssembledText.Contains(original, StringComparison.Ordinal), "REFINE-REQUIRED-OVERFLOW", "Required original text exceeding the explicit limit is retained for review and prevents model readiness.");
    }
}
#endif
