# Planning and reply reliability checks

Run from this checkout:

```powershell
dotnet run --project tests/Buddy.PlanningRepair.Tests -c Release -r win-x64 --self-contained true
```

These checks inject an HTTP model handler and use isolated encrypted temporary state. They do not contact Ollama, inspect the user's profile, launch applications, access the foreground desktop, record/play audio, or run an execution plan.

## Review versus execution

`ActionPolicy.ValidateForReview` accepts a bounded, nonempty explanation with `Actions=[]` as clarification. The desktop must display it with Run disabled and without an awaiting-approval job. `ActionPolicy.Validate` continues to require 1–25 actions before execution; it also rejects conflicting launch fields. No wire fields were added to `AssistantPlan`.

Review normalization maps only existing canonical app names, their existing `.exe` aliases, and the exact display names `Comet Browser` and `File Explorer`. The measured empty-value/target-comet error is repaired only when the target is such a known alias, the ref is empty, and role is empty or Application. URLs, paths, commands, arbitrary names and conflicting destinations are never migrated from target into value. Public HTTPS destinations remain subject to the existing runtime URL checks. This does not resolve, install or launch an app; actual Comet resolution still verifies its fixed path/product/signature/publisher at approved execution.

The Agent schema separates action kinds and puts a nonempty allowed app/HTTPS destination exclusively in an open action's value. `PlanAgent` allows one correction inference after invalid JSON/semantics, under one cancellation/deadline owner, then returns a non-executable clarification. Transport failures and cancellation still propagate. Guide output remains explanatory and mechanically binds Comet targets to process `comet`; malformed Guide explanations become clarification without fabricated controls or documentation.

## Reply constraints

The default ordinary-chat ceiling remains three sentences. Explicit English one/two/single-sentence forms lower it. Quoted examples do not set the limit. The limit is applied to initial inference and validated before a delta is emitted. Violations or unsolicited Guide/Agent promotion trigger one whole-answer regeneration carrying the complete rejected draft and qualification-preservation instructions. Persistent failure uses a one-sentence clarification; no answer is sliced at sentence N. Tighter requests bypass the experimental staged path.

Sentence counting and promotion detection are conservative text heuristics, not semantic proofs or multilingual instruction parsing. A full draft and preservation instructions reduce qualification loss but cannot prove that a paraphrase retained every implication. Likewise a model's clarification text is not evidence that any observation or action happened; UI execution gating and real window evidence remain separate. The legacy off-by-default staging implementation and paused SP-44 drafts are unchanged.

The captured failure is reproduced as a fixed test input. Mock/schema checks are not acceptance of Gemma's schema adherence, an actual Comet guide, foreground targeting, approvals, or execution. Root owns serialized real-model/native checks and final integrated evidence.
