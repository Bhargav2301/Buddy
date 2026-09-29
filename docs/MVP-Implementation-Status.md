# Windows MVP implementation status

This is an unreleased implementation increment toward the accepted Windows MVP plan, not a completed 0.5.0 release. Android wire compatibility is retained. Existing installed files and user data have not been replaced.

## Implemented in this increment

- Local Quick, Guided and Council refinement with real specialist passes, synthesis, progress events, cancellation, estimated scores, literal preservation and local embedding similarity checks. Missing validation models and oversized contexts retain the exact original. The legacy response fields remain available.
- Conversation pin/archive metadata and partial updates with encrypted persistence.
- Agent continuation using fresh screen context and execution results, bounded action and replan budgets, application-side approval decisions, and bounded accessibility workers. Unknown edits remain individually approved; unverified text-entry fallback was removed.
- Guide expectations, debounced optional advance, manual Skip, encrypted completion/resume and bounded replanning. Guide research uses only the explicit user task for web decisions.
- Shared Refine card with Quick/Guided/Council progress, technique overrides, estimated scores, original/rewrite views, Copy and guarded 30-second Undo. Ctrl+Alt+R explicitly captures supported AI-chat fields; Home uses the same card. Unsupported accessibility fields abstain. Native host-field acceptance remains pending.
- Figma-derived shared theme resources, light/dark/system appearance, high-contrast color overrides, a 240 px Home rail, conversation search/pin/archive, saved walkthrough resume, Memories/Prompts/Devices views, and one Settings surface with nine categories. Existing entry points navigate the running Home directly.
- Original local Figma SVG faces replace the pointer glyph. Shared activity priority prevents conflicting surface callbacks from erasing active states. Spring following, reduced-motion placement, tray docking/snooze and fullscreen suppression are integrated; native motion acceptance remains pending.
- First-run naming, microphone cancellation, privacy explanation, model setup/readiness and shortcut/sample-point entry points are connected. Existing installations preserve their onboarding and shortcut choices.
- Guide and Agent use the shared theme; Agent has risk review cards and an execution-only controlling banner. AI setup exposes the local intent-check model download.
- Windows Graphics Capture replaces screen copying in manual and voice capture. Capture retains the operating system indicator, masks detected private fields, checks scope before and after capture, and disposes frame pools, sessions, devices and image buffers. Native capture acceptance is still pending.
- Bundled Tesseract OCR detects labels and private text locally. The English data is pinned and checksummed; native libraries and licenses are included. The package probe tests native OCR loading, which requires the Microsoft Visual C++ x64 runtime used by the upstream library.
- When accessibility cannot ground a Guide step, one exact, high-confidence OCR label may be corroborated by the configured local vision model. Fresh capture must still show the same label at the same location. These targets remain guidance-only. Icons without reliable text evidence abstain; no pixel execution is available.
- Guidance ink clears on click, scroll or keyboard input, then re-grounds. No input content is retained or suppressed.
- Transactional per-user upgrades verify a staged package before swapping application directories. Previous binaries are retained for explicit rollback; interrupted swaps have bounded recovery. User data remains outside the transaction.

## Validation recorded on 2026-09-29–30

| Gate | Result |
| --- | --- |
| Windows Release compilation | Passed, zero warnings/errors |
| Service, TLS pairing and desktop-only API authorization | Passed |
| Desktop logic, exact field restoration, companion activity/motion and isolated installer recovery | 71 checks passed |
| Assistant planning, grounding, continuation, research and vision evidence policy | 64 checks passed |
| Local refinement and MVP policy | 36 checks passed |
| Native already-running/minimized Settings navigation and isolated layout/vector fixtures | 21 checks passed |
| Native UIA/capture fixture | Blocked at initial foreground assertion; subsequent capture/action checks did not run |
| Native OCR fixture recognition/redaction/cancellation | 5 checks passed; synthetic fixture only |
| Isolated self-contained publish and package structure/model integrity | Passed; not a release package |
| Real model refinement/vision and creative-app workflows | Pending |
| 150-case grounding evaluation and 100 ms dispatch-stop measurement | Pending |
| Clean installation, upgrade/rollback and release package | Pending |

## Remaining release work

Figma access was resolved on 2026-09-30 after accepting the pending invitation. Full design context and screenshots were extracted for all six Windows surfaces and the Foundations board. Visual implementation and acceptance are in progress.

Focused-field badge and caret dictation, companion fly-to-target/interactive menu, full context/source metadata badges, and delete-all-local-data controls still need integration. All six surfaces need populated-state comparison and remaining behavior validation. OCR/vision grounding and installer recovery have fixture coverage but still need real-model and app-level validation. Required native voice, DPI/accessibility and app-level guidance acceptance must run on a usable interactive Windows desktop. Resolve and FL Studio require runnable installations and disposable sample projects.

Do not label 0.5.0 complete or distribute this increment as the finished MVP until the accepted gates pass. Fixture and policy tests cannot substitute for those acceptance results.

## Capture implementation references

- [Microsoft WPF capture integration sample](https://github.com/microsoft/Windows.UI.Composition-Win32-Samples/blob/master/dotnet/WPF/ScreenCapture/README.md)
- [Windows Graphics Capture lifecycle](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture)
- [HWND capture item interop](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow)
