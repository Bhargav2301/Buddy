# Windows 0.4.4: reliability and Night Mint

This local build starts from **1b330c4d8aecbba5bcac447fcf64281b6b1643d6 / 0.4.3**. The recoverable installed baseline is **dd87c798a5973277856261a3410b804073cd951b / 0.4.2**. Its application, profile and previous preview were preserved during implementation and tests. Paused streaming experiments are excluded.

The source now passes **1,563 service/policy/native assertions** plus **26 real local-model checks**. This is bounded evidence for the tested cases, not universal application compatibility. Packaging, source hashes and any later authorized installation are recorded in the private release handoff so this report does not claim a future operation succeeded.

## What failed in the installed application

The integration owner's `validation/baseline-audit/DIAGNOSIS.md` records actual requests to the running installed service using its selected local `gemma3:4b` model. These are distinct from the 0.4.3 aggregate fixture results. The diagnostic context explicitly had no accessible app window; no plan was executed and no external source field was edited.

| Baseline probe | Actual result | What it establishes |
|---|---|---|
| Service health and selected model readiness | Passed | The local service/model connection worked in these probes; it does not prove all tasks work |
| `Guide me through Comet browser` submitted to Agent planning | Failed with `INVALID_PLAN`: unsupported app launch | Reproduces the reported error. An isolated trace using the exact installed assembly put `comet` in `open.target` while leaving `open.value` empty; execution validation correctly refused it |
| Same text submitted to Guide with no observed controls | Passed, general lesson without a fabricated pointer | The Guide service route can respond. Actual Comet UI routing, screen capture, lesson usefulness and grounding were not tested |
| Exact `Open Comet Browser` planning request | Passed, canonical `open.value=comet` | Bounded plan construction only. The separately checked installation identity does not establish launch/execution acceptance |
| Chat request explicitly asking for one sentence | Failed instruction fidelity: two sentences plus an unsolicited capability offer | The ordinary three-sentence ceiling was insufficient to honor a tighter user constraint |
| Refinement sample | API accepted; intent fidelity failed | It added a reason-for-leave requirement and deadline/commitment constraints absent from the source. Embedding similarity 0.9154 and model self-assessment did not catch the additions |
| Actual Comet/Grok source-field use, app execution and physical audio/device changes | Not run in this checkpoint | These acceptance gaps remain open |

Baseline logs are `installed-live-results.txt`, `installed-assembly-plan-trace.txt` and `comet-installation-check.json`, retained privately outside source. The parent independently viewed the user screenshot; this executor's Library materialization failed, so it did not bypass metadata validation or claim to view a partial file. No credentials, recording, installed setting or unrelated conversation was exported or changed by the documented probes.

## Implemented behavior

| Area | Result and evidence | Remaining boundary |
|---|---|---|
| Guide and Agent routing | Make plan re-evaluates edited guidance-only requests and visibly selects Guide. Editing a reviewed action goal invalidates Run. Mixed teach-and-perform still uses preparation and separate action approvals. | Owned native fixture; no actual Comet input dispatched. |
| Plans and clarification | Action-specific schema, exact supported-alias normalization for the measured Comet target/value error, one bounded correction, then nonexecutable clarification. Empty actions never enable Run. | Strict execution validation, verified installed-app aliases and launch approval remain required. No arbitrary shell/path/argument support. |
| Guide grounding and Stop | Known-app missing views explain the absent verified controls without claiming app health or installed version. Malformed current targets get one bounded repair. Contradictory references cannot regain a pointer by lesson-name matching. One cancellation owner spans attempts and fallback. | General/future lessons may explain without ink. Safe refusal is not proof the requested task completed. |
| Conversational replies | Explicit one/two-sentence requests are honored under the ordinary three-sentence ceiling. Clean the complete draft, reject unsolicited offers/source-label inventions, and regenerate once without truncation. Necessary qualifications remain in the full review. | Guardrails are not a general factual-truth classifier. Detailed plans and safety review remain complete. |
| Refinement | Independent conservative wording/literal/order checks reject newly invented requirements. Pure local technique selection, context provenance and explicit-unit budget core retain originals when loss is unavoidable. | Desktop technique/context/budget controls are not yet exposed. Six canned Friday-off rewrites passed real local-model checks; arbitrary custom editors remain unverified. |
| Companion and navigation | Optional Hidden/Compact/Expanded top-edge bar, local task status, Type/Voice/Guide/Refine/Stop and original artwork. Reversible core-first presentation retains existing features/settings. | Hidden and core-first off remain defaults; status does not claim continuous service monitoring. App notes still share Task Center. |
| Night Mint | Approved 1.1 palette across native controls, opaque panels/popups, semantic and disabled text, 3-DIP focus ring plus gap, selection/error roles and marked refinement diffs. Wrapped Home lifetime and voice-capture labels. | Explicit Light/Dark/System preferences remain supported. Actual high-contrast OS toggle and wallpaper/display variations were not exercised. |

## Validation

| Scope | Result | Private evidence |
|---|---|---|
| Fifteen service/policy/mock suites | **1,145 passed**; includes 122 planning-repair, 185 refinement-core and 40 independent reliability boundaries | `release-Buddy.*.txt` |
| Seventeen owned native suites | **418 passed**; includes 70 Night Mint, 39 reliability UI, 45 parity and 14 tray/routing | `accepted-native-*.txt` |
| Real local model, service path | **21/21** over three rounds: concise relevant chat, canonical Comet planning, missing/matched-view lessons, faithful refinement | `combined-live-three-rounds.jsonl` |
| Real local model plus observed native window | **5/5**: actual Make plan/Guide and verified owned control; source field unchanged | `accepted-native-live-guide.txt` |
| Release native compilation | **Zero warnings/errors** | `release-render-build.txt` |
| Theme evidence | Actual WPF popup/keyboard/hover/press states, rendered pixels, native role contrast, Guide/Voice/Home/diff renders | `theme/.../native-theme-results.json` and PNGs |

The native role checks meet 4.5:1 for tested text pairs and 3:1 for tested essential edges/focus. Disabled text measured 6.50:1; focus ring/gap 14.67:1. The design browser's 652 measurements and 26 interactions are separate evidence, not native test counts. The final direct owned-window images preserve layout; failed renderer experiments remain in the private logs.

Earlier live failures are retained: the rainbow reply fell back, a missing Comet view incorrectly inferred nonresponse, and a Guide target used the role label instead of its observed name. Prompt consistency, deterministic missing-view handling and bounded target correction resolved those cases in the latest repeated service and native runs. Independent review additionally found the contradictory-reference and inter-attempt Stop gaps; dedicated service and native regressions now pass. Stale legacy native fixtures were updated to plan the final goal, supply preparation responses and use the current Guide surface. Tray routing uses real F23/F24 fixture registrations and native messages so the installed application's Space shortcuts remain available; physical key competition remains manual.

## Recovery, settings and acceptance

Local installation of the tested reliability/Night Mint build is authorized, subject to the actual runtime approval gate. The recovery workflow retains a fresh application/profile/shortcut backup, rehearses schema-3 save/reload on a copied profile, checks all pre-install saved files, and selects Night Mint while preserving every other existing preference, including the current voice. The isolated preview retains its own data/activation namespace and an installation-blocking marker. Exact package, archive, installation and rollback verification results are in the private final handoff; no successful installation is inferred from this source document alone.

Publication remains blocked by the previous automatic approval review; no push, PR update, merge or release was retried. No paid service, account activation, cloud speech/audio transmission, credential/security setting or unrelated application operation is included.

Remaining manual acceptance: physical microphone wording/accent and uncertain-transcript review; subjective local voice quality; headphones disconnect and default-device changes during speech; real competing shortcut applications; screen capture behavior in the user's capture tool; actual Comet launch and Grok/other custom-editor review/Accept/Undo. Start/Stop, reopen, low-confidence callbacks, route cancellation and capture policy have fixture evidence, not physical acceptance. Piper offers local selectable voices and pacing but does not guarantee cloud-voice naturalness or J.A.R.V.I.S.-level delivery.

The 0.4.3 staged-speech option remains off by default and experimental: its three earlier real-model probes fell back without a staged sentence. Local partial Whisper transcripts, reliable token-time first-audio streaming, a long-answer override and live provider routing remain incomplete. No all-app claim follows from these tests. See the [upstream/platform/license matrix](Upstream-union.md), [feature inventory](Existing-feature-migration.md) and [Night Mint notes](Night-Mint.md).
