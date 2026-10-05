# Preserved feedback47: bounded acceptance plan

STATUS-51 attempt 1. **Plan only; no foreground, app, model, microphone or network operation runs now.** Root must announce a fresh bounded window and check the active-desktop guard before any launch/input. The completed 11:40:53 owned-fixture window is not continuing permission to take foreground control. This plan tests the preserved candidate; it does not validate newer followup50 source.

## Candidate and preservation

Candidate: `release/Buddy-0.4.5-feedback47-review/Run-Preview.cmd`, using its isolated preview namespace and existing local assets. Base `ced111d78a4e4230e53d8d359ca167b231e29e56`; sealed source `197836cd6b4cef72621f1aad1785f2dc6036d2b106f14ae15ed0eb8ba9190441`; build-input snapshot `de84c846a506a142883cb7855f3f20812509ada9f9a41f3f843239d524623348`. Verify bytes before and after the window; a mismatch stops testing rather than silently rebuilding/replacing this preview.

| Candidate file | SHA256 |
|---|---|
| Buddy.exe | `1d8d89ee7f1f474da29b2ec6b59bbdbaa8565515542858e17d3f09992aeae578` |
| Buddy.dll | `274641ac5fc97a544f2eb5d689db66d8a09b55af00b2d570648b54c689946ad2` |
| Buddy.Server.dll | `0703ecb8716b8730ed5f42319acc83fb5f36e12ea2a127fd4181036de1cccced` |
| Build-Info.json | `618f1d360d1f259c934fa11a45f129d320727130b0dd7733552679b6d02e0bb1` |
| Run-Preview.cmd | `a989da3efdd4670955039dffd36b19391c07cdbc9d155d87a456cb73934eeda4` |

Hashes were read without running the candidate. Its `SHA256SUMS.txt`, `Source-SHA256.json` and private `validation/feedback47/FINAL-REVIEW.json` identify the full package/source evidence. The reported exact-source archive is `release/Buddy-feedback47-exact-source-197836cd6b4c.zip`, SHA256 `7b71d5fbf225deec9835363fcea859ee9b71c29f7a7c404bfd81bcb2e436d1b8`. Build metadata's old pending-foreground wording predates the completed owned window; preserve the sealed file and add acceptance receipts separately.

Installed 0.4.4 and current preferences/voice remain authoritative. PID 33820 was the installed owner at the last receipt, not an identity to reuse blindly: freshly verify path/process lifetime and current activity. Do not silently kill installed Buddy or change its shortcuts.

## Announced-window prerequisites

1. Parent announces purpose, timing and a hard end time; confirm no active user task or unsaved work will be disturbed. Freshly require the normal active desktop. On secure-desktop refusal, Stop, target ambiguity or new user activity, stop dependent work without bypass.
2. Choose one explicit ownership mode. **Coexistence:** retain installed Buddy and use preview menu routes; record shortcut conflicts/companion suppression and make no hotkey acceptance claim. **Preview alone:** only after a parent-announced graceful pause and confirmation of no active operation, close installed Buddy through its normal exit, then start the preview. At cleanup close only the owned preview and restore installed Buddy. Never force-kill the installed process to make a fixture pass.
3. Verify candidate hashes, isolated namespace and exact owned processes. Preserve clipboard using the test-owned adapter if needed; avoid clipboard use if unrelated concurrent changes cannot be preserved. Keep evidence to canned test content, expected identities and bounded outcomes.
4. Use the prepared local `validation/followup50/browser-acceptance-fixture.html` in a **new Comet tab/window**. It contains a labelled textarea with canned poem text, no script/form/network, restrictive CSP including `form-action 'none'`, and disabled sending control. Do not edit existing tabs or unsent drafts on account pages. Opening this fixture is not done by preparing this plan.

## Cases and pass criteria

| Case | Bounded procedure during the announced window | Required evidence and limits |
|---|---|---|
| Browser-field refinement lifecycle | Focus only the owned local Comet textarea; invoke the preview's existing source-field route. Review the actual original/proposal and wait only within the existing bounded deadline. Do not bypass capture/focus/field guards if unsupported. | Meaningful faithful change or explicit original-kept/no-change outcome; visible terminal state/Stop. A no-change result validates truthful handling but not successful rewriting. Record the exact canned output and phase timings. |
| Apply and exact Undo | If a meaningful proposal exists, explicitly Accept into that same owned field without sending, verify the field value, then Undo within its supported window and verify the exact original. | Fresh field/window identity before each mutation; no focus or clipboard fallback that bypasses the adapter. If read-only/unsupported/stale, stop and record the limitation. This proves only real Comet accessibility on owned local HTML, **not an arbitrary ChatGPT/Grok editor**. |
| Exact routine app opens | One explicit request each for Comet, Calculator and Spotify, using supported existing routes and bounded one-dispatch verification. Reuse a matching existing app only where the production policy permits. | Record requested alias, verified destination, launch/foreground result, elapsed time and required clicks. Wrong target, duplicate dispatch, unavailable registration, timeout or missing fresh postcondition is a failure. Comet-opening is already a user-reported success; verify the candidate separately. **Never activate Camera.** Spotify is launch-only: no sign-in, playback, library/account navigation or account actions. |
| Stop and recovery | Use Stop during a pending harmless owned request if a controllable pending phase exists, then reopen the relevant preview view. Do not start another effect merely to simulate cancellation. | No stale proposal/result, replay or second dispatch; a visible recoverable state. This bounded cancellation check does not claim the user's unspecified item-15 safe-failure test was performed. |
| Resident/retained UI observation | Observe preview close-to-tray/reopen and retained result/footer/notch controls in the selected ownership mode. Use menu routes if coexistence prevents hotkeys or suppresses the preview companion. | Distinguish the already reported Home-close residency success from preferences/notch issues. Do not infer long-running, physical-shortcut or multi-monitor acceptance from this short window. |

No microphone recording, audio playback, music, camera, account setup, external agent hooks, remote uploads, downloads or security changes belong to this window. No new web query is required for the local browser fixture. Region/image internet research and general model teaching need their own outcome-specific evidence; source cards or this app-open adapter do not establish them.

## Stop, cleanup and installation gate

Stop at the announced deadline or first unsafe/unverifiable state. Record the failure without retry loops, identity substitution, native-shell fallback or weakened guards. Cancel owned pending work; close only the preview and the new test-owned page/windows created for this plan, preserving pre-existing Comet tabs and applications. Restore the clipboard safely if the test changed it; restore installed Buddy only if this window gracefully paused it. Do not replay work on restoration. Return controls and record any residual owned process or unresolved test draft.

Root records each case as passed/failed/not run with the candidate hashes, actual app/field identity, outcome and limits. Recheck preserved candidate bytes and installed application/settings against a fresh pre-window baseline; never restore an older preference file over newer user changes. Keep all original receipts and sealed47 files intact. No result for this plan is claimed yet.

**Installing a working update is already authorized within the continuing user request**, after relevant actual acceptance and a fresh recoverable backup. Root must back up the latest installed binaries, preferences and required state, verify a recovery path, preserve the current speaker/settings, and install only the exact accepted candidate. A newer followup50 build needs its own final checks and separate fresh package; feedback47 acceptance cannot certify different bytes or unaccepted teaching. Missing mandatory quality gates remain blockers, not requests for duplicate installation permission.

**Publication remains separately denied.** Installation authority does not permit a push, release, account grant, credential setup or retry of denied publication. This specialist only authors the plan; root owns any later execution and recovery.
