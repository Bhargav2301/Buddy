# Windows MVP implementation status

This is an unreleased implementation increment toward the accepted Windows MVP plan, not a completed 0.5.0 release. Android wire compatibility is retained. Existing installed files and user data have not been replaced.

## Implemented in this increment

- Local Quick, Guided and Council refinement with real specialist passes, synthesis, progress events, cancellation, estimated scores, literal preservation and local embedding similarity checks. Missing validation models and oversized contexts retain the exact original. The legacy response fields remain available.
- Conversation pin/archive metadata and partial updates with encrypted persistence.
- Agent continuation using fresh screen context and execution results, bounded action and replan budgets, application-side approval decisions, and bounded accessibility workers. Unknown edits remain individually approved; unverified text-entry fallback was removed.
- Guide expectations, debounced optional advance, manual Skip, encrypted completion/resume and bounded replanning. Guide research uses only the explicit user task for web decisions.
- Verified-field editing adapter and exact 30-second Undo guards. Its focused-field shortcut/card integration is still pending.
- Existing Home Refine applies only to an unchanged draft and never sends the host form. AI setup exposes the local intent-check model download.
- Windows Graphics Capture replaces screen copying in manual and voice capture. Capture retains the operating system indicator, masks detected private fields, checks scope before and after capture, and disposes frame pools, sessions, devices and image buffers. Native capture acceptance is still pending.

## Validation recorded on 2026-09-29

| Gate | Result |
| --- | --- |
| Windows Release compilation | Passed, zero warnings/errors |
| Service, TLS pairing and desktop-only API authorization | Passed |
| Desktop logic and exact field restoration | 41 checks passed |
| Assistant planning, grounding, continuation and research policy | 59 checks passed |
| Local refinement and MVP policy | 36 checks passed |
| Native already-running/minimized Settings navigation | 8 checks passed |
| Native UIA/capture fixture | Blocked at initial foreground assertion; subsequent capture/action checks did not run |
| Real model refinement, OCR, creative-app workflows | Pending |
| 150-case grounding evaluation and 100 ms dispatch-stop measurement | Pending |
| Clean installation, upgrade/rollback and release package | Pending |

## Remaining release work

Exact Figma extraction remains blocked by the connector's authenticated account/file access. Six-surface visual implementation and design acceptance require that access (or an explicit user choice to use the document/browser preview instead).

Shared appearance resources, unified Settings and Home organization, rounded companion/session coordination, onboarding completion, the shared Refine card/shortcut and caret dictation, bundled OCR and vision grounding, privacy deletion controls, and installer rollback/probes still need implementation or integration. Required native voice, DPI/accessibility and app-level guidance acceptance must run on a usable interactive Windows desktop. Resolve and FL Studio require runnable installations and disposable sample projects.

Do not label 0.5.0 complete or distribute this increment as the finished MVP until the accepted gates pass. Fixture and policy tests cannot substitute for those acceptance results.

## Capture implementation references

- [Microsoft WPF capture integration sample](https://github.com/microsoft/Windows.UI.Composition-Win32-Samples/blob/master/dotnet/WPF/ScreenCapture/README.md)
- [Windows Graphics Capture lifecycle](https://learn.microsoft.com/en-us/windows/apps/develop/media-authoring-processing/screen-capture)
- [HWND capture item interop](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow)
