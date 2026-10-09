# Independent teaching checks

Default execution tests the oracle against deliberately good and bad canned responses. It performs no model, network, native or app operation. These checks validate the evaluator, not Buddy's teaching quality.

`--fixtures` prints ten independent scenarios. Only `recorded-reload` derives one actual observed fact: the nonprivate label **Reload** and role **Button**. Its reference/rectangle are new replay data. Every other scenario is synthetic. `heldout-word-wrap` and `heldout-disabled-download` are separate held-out cases; do not claim they represent recorded Comet controls.

`--evaluate=<file.jsonl>` reads root-produced `teaching_result` rows containing caseId, query, context and plan. It uses the actual row's context for exact reference/name/role checks. Run on the model route with `browserChromeVerified:false`; no rule requires the authored teaching path to pass. Record model-call provenance separately.

Useful teaching must explain a control's function, give an applicable caution/scope limitation, and describe a concrete manual outcome check. Repeating a click card or saying the button remains visible does not suffice. Missing, ambiguous and disabled targets must clarify without pointers. Completion claims, guessed dialogs/transitions, self-scores and unmatched targets fail.

The lexical checks are necessary-condition heuristics, not semantic proof. Root must independently read all actual words for meaning, negation, scope, relevance and omissions; a passing regex cannot replace that review. No fabricated steps, outcome or physical acceptance follows from these fixtures.

Run with `dotnet run --project tests/validation/Buddy.TeachingUsefulness.Tests/Buddy.TeachingUsefulness.Tests.csproj -c Release -r win-x64 --self-contained true`. An explicit `-p:BuddySourceRoot='<reviewed-root>\'` links other production sources into this project's isolated output and logs their hashes.
