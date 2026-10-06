# Calculator launch investigation, October 6

Update: the user confirmed Calculator opened minimized. The subsequent supported restoration repair and its noninteractive validation are documented in [Calculator activation58](Windows-calculator-activation58.md). The investigation and reporting-only results below are historical evidence; they are not the full current source change.

The user-triggered hotfix57 update stopped in its first Calculator stage at 07:52:17 UTC. Calculator launched once, but the production adapter returned `Verified=false` after 30 checks. The helper rejected that result with `InvalidDataException`; the coordinator then reported `Scoped acceptance failed; no forward continuation.` No installation or rollback ran.

The original installed Buddy process is still running version 0.4.5. Read-only verification matched all 4,416 installed application files, including official rollback metadata, to the pinned prepared baseline. Calculator has a new process created at 07:52:11.4168747 UTC and an older process from the previous day; neither was closed by this investigation. The completed update helpers are absent, and the helper/coordinator receipts confirm settlement. All 263 recorded input samples retained the same baseline. The helper's `operationCanceled=true` was failure cleanup, not evidence that user Stop or changed input caused the rejection.

The failing branch is narrower than a generic app-launch error. The trace contains five Agent and five blocklist callbacks before verification, followed by exactly 30 Agent callbacks at verification entry and no more blocklist callbacks. A Calculator frame binding attempt would add both callbacks; a matching direct app would add a blocklist callback. Therefore none of these attempts entered the frame, package, executable or direct-window postcondition checks. The remaining possibilities are absent/non-visible foreground or an app-name mismatch; a final observation exception cannot be excluded. The receipt did not retain foreground identity or individual failure messages, so it cannot identify the exact early predicate or establish a Windows activation-permission cause.

## Focused source repair

The isolated `hotfix/launch-verification58` branch is based on `f8c56b90f6d80479717d0988182ba37492d998f1`. It retains only the final unsuccessful verification's exact allowlisted host explanation and maps it to a bounded task-report code. Foreground unavailable, foreground app mismatch, Calculator frame rejection, executable rejection, package/process mismatch, changed installation and changed window now remain distinct. Unknown/private text and malformed final results fall back to the generic message instead of retaining an earlier reason. Later success, exceptions, cancellation and settlement keep their existing behavior.

Only `BoundedComputerUse.cs`, `RoutineLaunchReport.cs` and their two existing test programs changed for this repair. No activation, foreground, input, identity, signature, package, timeout, retry or recovery policy changed. This repairs lost failure details; it does not resolve or claim to reproduce the historical native activation failure.

## Validation and remaining work

- 116 mock/owned-file computer-use checks passed, including one dispatch across 30 failed checks, final-reason replacement, malformed results, privacy, cancellation and settlement.
- 72 pure launch-report checks passed, including all seven final outcome codes and exact-message privacy boundaries.
- Both focused builds and the Windows Release build completed with zero warnings and errors.
- Independent read-only source reviews found no remaining blocker in this reporting change. No desktop, app activation, microphone, audio or model test was performed during this investigation.

The used launcher, sealed hotfix57 packages and all failure evidence remain intact. Its attempt and deadline are consumed: do not rerun or reset them, even before the old clock deadline. There is no replacement installer or new executable update launcher. The source repair is uncommitted and has not been packaged, installed or published. Publication remains blocked through the previously denied route.

The user later confirmed Calculator was minimized. New foreground verification still requires separate coordination. Existing installed settings are not restored or overwritten. The failed-run receipt confirms their hash was stable during that run; the later current preference hash differs, so later user/app changes are preserved rather than treated as corruption.

Private supporting evidence is under `validation/hotfix58`: `failed-run-inspection.json`, `process-state.json`, both focused build/run logs, `windows-build.txt`, and `FINAL-OUTCOME.json`. The original run remains `validation/hotfix57/LocalStart/runs/8292aba49a0240dd9ec15ac365b77a87`.
