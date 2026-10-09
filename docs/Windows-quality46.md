# Local quality investigation: refinement and Comet observation

This uncommitted investigation is based on `ced111d78a4e4230e53d8d359ca167b231e29e56`, in `Buddy-quality46` / `local/quality46`. VERSION remains 0.4.5. It is not a qualified replacement package. Installed 0.4.4 and the clean 0.4.5 checkout, source archive and separate preview are preserved; no installation or publication occurred.

## Refinement result

The exact reported prompt now produces this reviewable proposal through the actual local Gemma3:4b service:

```text
Task: Write a poem.

Subject: a boat sailing in a sea on a lonely night.
```

This separates the requested artifact from its supplied subject, preserving the complete scene without inventing a length, rhyme, speaker or additional mood. It is a modest task-specification improvement, not a claim of universal prompt optimization. Apply/Undo review remains required; refinement does not send the prompt.

The old identity instruction allowed only grammar/small wording changes, and ordered-token fidelity rejected structural rewrites. Character changes and self-assigned numeric scores did not establish usefulness. The new bounded English source-span contract classifies supplied task, subject, purpose, constraint, ordered-step and context spans. The model may reference these immutable IDs only; the host checks complete occurrence coverage, order and roles before rendering the proposal. Unsupported constructions retain the conservative wording path. Contradictory exact output counts request clarification.

Structure results show verified operations instead of model-authored change claims or numeric quality scores. Live model assessment explanations demonstrably claimed retained words were missing, so those claims are not presented as facts. Intent-preservation assessment, literal checks, the existing 0.80 embedding threshold, budgets, cancellation and review remain in force.

One integration amendment normalizes adjacent sections with the same role only after complete ID/order/role checks. It copies the input lists and cannot add or reorder content. Independent checks cover exact output and nonmutation. Three otherwise valid first-round model plans had been rejected solely for this section grouping. Their failed evidence is retained.

## Actual local model evidence

The expanded 14-case set ran twice against existing local `gemma3:4b` and `all-minilm:22m`, using canned text and an ephemeral service store. No model was installed, no audio was used, no browser field was written and no installed profile was read by the model fixture.

| Outcome | Unique cases | Across two rounds |
|---|---:|---:|
| Useful source-grounded structure, independently reviewed | 12 | 24 |
| Expected clarification for contradictory counts | 1 | 2 |
| Original retained but utility unmet | 1 | 2 |
| Unsafe/invalid accepted output found by this fixture | 0 | 0 |

All eight original comparison prompts now have useful structure in both rounds, compared with two modest grammar improvements in the prior eight-case run. Exact poem, email purpose, ordered steps, output limits, prohibitions, code, number bindings, ambiguity and Unicode checks retain their supplied meaning. The rough-input examples still contain rough grammar and conversational scaffolding; they establish useful separation, not polished prose or substantial rewriting.

The conditional request `Compare A and B only if both are available; otherwise ask which is missing. Do not guess.` remains unchanged in both rounds: its plan and preservation assessment pass, but similarity is 0.7505545693567622, below 0.80. The threshold was not lowered. The live harness correctly exits 1 for this unmet utility requirement; the 14-case matrix is not an all-pass result. The incompatible-count case is an expected safe refusal, not another useful rewrite.

Private receipts: `actual-14-goldens-final-two-rounds.jsonl` and `final-root-quality-review.json` in `validation/quality46`. The earlier single exact-poem live pass and deterministic 8/8 mock result are separate evidence, not substituted for this matrix. Root review found the same final candidate in each corresponding round. This remains fixture-specific agent review, not user acceptance or a general semantic guarantee.

## Comet observation and exact desktop gate

The original read-only baseline explains the incomplete 153-control observation. A cold run hit the 600 ms traversal limit. Two warm runs visited all 280 queued nodes and returned 153 controls, but both omitted seven branches at depth 12. No provider error was recorded. Faster property access alone cannot make that tree complete.

The integrated change adds immutable, content-free diagnostics for selected process lifetime, node counts, timing, limits, omissions and fixed provider-error categories. A privacy-first property cache reads public metadata only after visibility/password/bounds checks and refreshes privacy and runtime identity before use; it retains live nodes for later guarded operations. It does not cache Value/Text data. Production bounds remain depth 12, 400 nodes, 600 ms traversal and a 2-second deadline. Incomplete snapshots still refuse images. Guide displays a durable notice that some controls could not be read and clears stale notices on Stop/invalidation.

The final code compiles, and 13 pure diagnostic checks pass. **Actual cached UIA performance, owned-window cache parity and actual Comet Guide usefulness remain unverified.** The last actual probe stopped at the secure-desktop guard before UIA/property reads. No alternate route, unlock attempt or guard bypass was used. Continue only after the user unlocks Satonara, leaves an ordinary Comet tab visible and confirms readiness. The prepared Guide fixture restricts model context to verified public browser chrome; no page contents, screenshots, focus change or input are required.

## Verification and changed files

The latest successful checks across 22 headless suites total **1,793**. The first 13 suites preceded the refinement-only grouping normalization; all refinement suites were rerun afterward. The separate pure observation mode adds 13 checks. These counts are not native or physical acceptance. Final self-contained Windows/fixture Release compilation has zero warnings and zero errors.

Legacy regression mocks were made explicit about testing the conservative wording path. Assertions were retained and fixtures reject unexpected structure-plan requests; new structure fixtures separately cover the new path. Old fixture failures and their successful reruns remain in private evidence. The final native source-field test compiles but was not executed while the desktop was locked.

Production changes are limited to:

- `services/Buddy.Server/Refinement.cs` and new `RefinementContract.cs`: bounded structural composition and honest result metadata.
- `apps/windows/Buddy.Windows/RefineWindow.cs`: structural review status without unreliable numeric claims.
- `apps/windows/Buddy.Windows/ScreenPerception.cs`, new `ScreenObservation.cs` and `ScreenObservationProperties.cs`: diagnostics and bounded caching with privacy/lifetime guards.
- `apps/windows/Buddy.Windows/DesktopAssistant.cs`: partial-observation notice lifecycle.

Tests add meaningful-refinement contract, independent boundary and actual-model fixtures; observation and read-only Comet Guide fixtures; dispatch modes; and targeted updates to Core, Repair, OptionsBoundary, ReliabilityBoundary, UserFailureBoundary and native UserFailure checks. Development records document accepted worker handoffs and subsequent integration-owner amendments.

Local evidence includes `final-headless-summary.json`, `final-independent2-native-build.txt`, `final-observation-pure.txt`, accepted handoff receipts and source snapshots. A bounded source scan checked 384 tracked files plus 18 new source files before this report, with no flagged payloads or recognizable secrets. Installed executable, library and build-info hashes, saved preferences and the 0.4.5 source archive matched their baseline in `final-preservation.json`; no saved state was restored.

The preserved preview remains `release/Buddy-0.4.5-local/Buddy.exe --home`. It does **not** contain this investigation. No new preview was packaged or installed while the native/Comet gate remains open. Physical microphone/headphone behavior, competing shortcuts, screenshot modes, real-browser Apply/Undo and native Stop/reopen still require their scoped acceptance checks; historical fixture passes do not establish them for this source.
