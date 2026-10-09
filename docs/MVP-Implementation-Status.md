# Windows MVP implementation status

This is an unreleased implementation increment toward the accepted Windows MVP plan, not a completed 0.5.0 release. Android wire compatibility is retained. Existing installed files and user data have not been replaced.

Latest October9 continuation: [requested-detail replies and browser contract audit](Continuation62.md). A separate review package exists; live browser data capabilities and native MVP gates remain incomplete. Historical validation below retains its original scope and dates.

## Implemented in this increment

- Local Quick, Guided and Council refinement with real specialist passes, synthesis, progress events, cancellation, estimated scores, literal preservation and local embedding similarity checks. Missing validation models and oversized contexts retain the exact original. The legacy response fields remain available.
- Conversation pin/archive metadata and partial updates with encrypted persistence.
- Agent continuation using fresh screen context and execution results, bounded action and replan budgets, application-side approval decisions, and bounded accessibility workers. Unknown edits remain individually approved; unverified text-entry fallback was removed.
- Guide expectations, debounced optional advance, manual Skip, encrypted completion/resume and bounded replanning. Guide research uses only the explicit user task for web decisions.
- Shared Refine card with Quick/Guided/Council progress, technique overrides, estimated scores, original/rewrite views, Copy and guarded 30-second Undo. Ctrl+Alt+R explicitly captures supported AI-chat fields; Home and quick chat use the same card. The focused-field badge checks metadata without reading the draft. Native host-field acceptance remains pending.
- Ctrl+Alt+D captures a writable field and verifiable caret/selection, then opens local dictation. Insertion verifies both the original contents and selection; it never submits. Exact Undo expires after 30 seconds. Unexposed/empty caret geometry abstains and offers Buddy's draft workflow. Speech initialization is bounded and Stop disposes recognition off the presentation thread.
- Figma-derived shared theme resources, light/dark/system appearance, high-contrast color overrides, a 240 px Home rail, conversation search/pin/archive, saved walkthrough resume, Memories/Prompts/Devices views, and one Settings surface with nine categories. Existing entry points navigate the running Home directly.
- Original local Figma SVG faces replace the pointer glyph. Shared activity priority prevents conflicting surface callbacks from erasing active states. The interactive companion menu, spring following, grounded fly-to-target, listening pulse, reduced-motion placement, docking/snooze and fullscreen suppression are integrated; native motion acceptance remains pending.
- First-run naming, microphone cancellation, privacy explanation, model setup/readiness and shortcut/sample-point entry points are connected. Existing installations preserve their onboarding and shortcut choices.
- Guide and Agent use the shared theme; Agent has risk review cards and an execution-only controlling banner. AI setup exposes the local intent-check model download.
- Windows Graphics Capture replaces screen copying in manual and voice capture. Capture retains the operating system indicator, masks detected private fields, checks scope before and after capture, and disposes frame pools, sessions, devices and image buffers. Native capture acceptance is still pending.
- Bundled Tesseract OCR detects labels and private text locally. The English data is pinned and checksummed; native libraries and licenses are included. The package probe tests native OCR loading, which requires the Microsoft Visual C++ x64 runtime used by the upstream library.
- When accessibility cannot ground a Guide step, one exact, high-confidence OCR label may be corroborated by the configured local vision model. Control targets additionally require an independently detected closed visual boundary. Fresh capture rechecks the label, position and boundary. These targets remain guidance-only. Unlabeled icons and flat controls without reliable boundary evidence abstain; no pixel execution is available.
- Live Gemma testing exposed a document-text/button false positive and an incorrect returned reference. The schema now constrains the candidate reference, separates visual role from OCR accuracy, and requires independent boundary evidence. Two real-model synthetic regression cases pass after the fix; this is not the 150-case evaluation.
- Guidance ink clears on click, scroll or keyboard input, then re-grounds. No input content is retained or suppressed.
- Transactional per-user upgrades verify a staged package before swapping application directories. Previous binaries are retained for explicit rollback; interrupted swaps have bounded recovery. User data remains outside the transaction.
- Talk and Guide show clickable research sources. Encrypted conversation metadata records screen use, app names and source links without storing raw screen context or session screenshots. Android's existing stream handler ignores the optional metadata event.
- Agent interruption has an independent input-monitor thread, with cancellation fences before actions and key-down dispatch. Low-risk control capabilities are revalidated immediately before execution. A blocked presentation thread cannot prevent the monitor from requesting cancellation.
- Starting or restarting a desktop surface cancels conflicting desktop sessions, including Agent execution before voice begins. This handoff does not cancel unrelated Android requests.
- Screen & Privacy includes explicit typed-confirmation deletion. A verified helper waits for the owning Buddy process to exit and acquires its data mutex before removing the fixed profile subtree. Links are rejected before any deletion; shared models and app binaries are outside the operation. Only isolated fixture data was erased during testing.

## Validation recorded on 2026-09-29–30

| Gate | Result |
| --- | --- |
| Windows Release compilation | Passed, zero warnings/errors |
| Service, TLS pairing and desktop-only API authorization | Passed |
| Desktop logic, exact field restoration, dictation selection, interruption, preference migration and isolated installer recovery | 86 checks passed |
| Assistant planning, grounding, continuation, research metadata and vision evidence policy | 71 checks passed |
| Local refinement and MVP policy | 36 checks passed |
| Android build compatibility | App and instrumentation APK compiled; unit task has no sources; phone instrumentation pending |
| Native already-running/minimized Settings navigation and isolated layout/vector fixtures | 21 checks passed |
| Native UIA/capture fixture | Blocked at initial foreground assertion; subsequent capture/action checks did not run |
| Native OCR fixture recognition/redaction/cancellation and control boundaries | 7 checks passed; synthetic fixtures only |
| Isolated self-contained publish and package structure/model integrity | Passed; not a release package |
| Real local refinement, Gemma 3 4B + all-minilm 22M | All three modes completed; Quick retained original at 0.772 similarity, Guided accepted at 0.970, Council accepted at 0.978 |
| Real local vision regressions | Bordered button confirmed; plain document mention rejected with independent boundary evidence; two synthetic cases only |
| Interruption timing | Injected input fixture: 13.3 ms; physical input to dispatch-stop measurement remains pending |
| Creative-app workflows | Pending |
| 150-case grounding evaluation and 100 ms dispatch-stop measurement | Pending |
| Clean installation, upgrade/rollback and release package | Pending |

## Remaining release work

Figma access was resolved on 2026-09-30 after accepting the pending invitation. Full design context and screenshots were extracted for all six Windows surfaces and the Foundations board. Visual implementation and acceptance are in progress.

All six surfaces need populated-state comparison and remaining behavior validation, including companion menu/motion and supported host-field Refine/dictation. Empty or inaccessible caret geometry and flat canvas controls currently abstain. OCR/vision grounding has narrow real-model coverage; installer recovery has isolated transaction coverage. Required native microphone, DPI/accessibility, seven application workflows, 150-case grounding and actual installation/upgrade/rollback still need a usable interactive Windows desktop. Windows again refused foreground focus on the 2026-09-30 native fixture retry; none of its later capture/action checks ran. An interactive fixture now exposes a Run native checks button. Resolve and FL Studio require runnable installations and disposable sample projects.

Use `scripts/build-windows-preview.ps1` for a traceable testing package with per-file and ZIP checksums, rollback launchers, native dependency probes and recorded test output. It does not overwrite the released 0.3.1 ZIP or set completedMvp. The prerequisite helper opens Microsoft's signed installer for user review when the Visual C++ x64 runtime is missing.

Do not label 0.5.0 complete or distribute this increment as the finished MVP until the accepted gates pass. Fixture and policy tests cannot substitute for those acceptance results.

## Capture implementation references

- [Microsoft WPF capture integration sample](https://github.com/microsoft/Windows.UI.Composition-Win32-Samples/blob/master/dotnet/WPF/ScreenCapture/README.md)
- [Windows Graphics Capture lifecycle](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture)
- [HWND capture item interop](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow)
