# Windows assistant preview — Buddy 0.3.0

This Windows-first increment follows the repository's TRD and UI/UX v1.2. It implements the core voice, Guide, Agent and web paths; it is not completion of every R1–R3 milestone. Android feature work remains deferred.

## Use it

1. Extract the versioned Windows ZIP. Quit the old Buddy from the tray, then run `Install-Buddy.cmd`. Your existing history, models and pairing remain in `%LOCALAPPDATA%\Buddy`.
2. In **PC setup & models**, choose an installed Ollama model. Live planning was checked with `gemma3:4b`; capability and speed depend on the chosen model. No cloud AI key is needed.
3. **Ctrl+Space** opens typed chat. **Ctrl+Shift+Space** opens the independent voice bubble. Speech stops before TTS starts. **Cursor & shortcuts** enables optional tap/hold, spoken answers and voice screen context.
4. **Home → Assistant settings** enables internet research and/or Agent mode. Both default off. The settings also expose a process blocklist and an encrypted activity log.
5. Focus the tool you need, summon Buddy, enter a task, and choose **Guide** or **Agent**. Examples: “Show me where Export is”; “Open Notepad and type a short packing list”. Explicit `Open`, `Click`, `Type`, `Do this`, and `Buddy agent` commands route to Agent; `Show me`, `Guide`, and `Walk me` route to Guide.
6. Agent displays its plan. Choose **Run this plan**, then approve each modifying step. It shows its target before acting. Stop with **Esc**, **Ctrl+Alt+Esc**, the **Stop** button, or by moving the pointer over 40 px during execution. A previously submitted UIA operation cannot always be retracted; verify the last step after a provider timeout.
7. Guide provides **Next/Back** and saves progress. **Resume walkthrough** restores the latest guide and resolves the target again. **Try pointing tutorial** opens a practice editor and Export button that do not modify a real document.

With Internet research enabled, chat can search DuckDuckGo or fetch a public HTTPS URL, read extracted text and cite actual sources. For a predictable first check, ask: “Read https://example.com and explain its purpose.” Search providers may throttle or return a challenge; Buddy reports that and can still fetch a supplied public URL. There are no browser cookies or sign-ins.

## Implemented boundaries

| Area | Behavior |
|---|---|
| Voice | Separate 360 px overlay, partial transcript, microphone level, single utterance or opt-in 250 ms tap/hold detection, sentence streaming TTS, stop/focus cleanup |
| Perception | Bounded UIA traversal, stable snapshot refs, role/name matching, password omission and frame redaction, secure/elevated and sensitive app checks |
| Frames | Active window only, at most 1280 px, memory only, local vision model only; skipped when UIA enumeration is incomplete or focus changed |
| Drawing | Per-monitor click-through, nonactivating ink; ring, arrow, highlight, underline, badge and label; expires on timeout, target position change, click, or leaving the target app |
| Guides | Structured local steps, real control targets, manual Next/Back, encrypted resume; ambiguous controls receive no guessed arrow |
| Agent | Open Notepad/Calculator/Explorer or a public HTTPS URL; UIA invoke/select/expand/toggle, type into accessible edit fields, read, wait and a small key-chord allowlist |
| Action safety | Agent off by default, visible plan, per-change approval, maximum 25 steps, dwell before acting, fresh target validation, provider timeout, mouse/Esc stop, encrypted metadata audit; ValuePattern edits offer guarded 30-second undo |
| Research | Off by default, structured choose-tool → fetch/search → evidence → answer loop, maximum four decisions, 500 KB/page, ten-second fetch budget, redirect and DNS/IP checks, no private network or credentials |
| Packaging | Self-contained x64 runtime, required dependency manifest, managed dependency checks plus real native apphost startup probe |

## Limits and remaining TRD work

- Full voice round-trip, microphone device failure/recovery, hold-hook conflicts, click-through and mixed-DPI acceptance must be checked on an interactive Windows desktop. No target accuracy or latency SLA is claimed.
- Windows System.Speech is used. Whisper, wake word, NAudio/VAD, continuous duplex conversation, spring animations and full onboarding remain future work.
- Guide targeting uses UIA; OCR/VLM coordinate grounding and automatic step detection/advancement are not implemented. Canvas-only controls may require manual explanation. Scroll that leaves a target's bounds unchanged is not yet a dedicated invalidation signal; marks expire after 15 seconds and every action revalidates its target.
- Capture uses the existing screen-copy fallback. Windows.Graphics.Capture, HDR handling, complete sensitive-URL recognition and OCR redaction need further work. Automatic voice frames are skipped unless the accessibility traversal completes; disable voice context for applications that expose sensitive content poorly.
- Agent is a conservative preview, not unrestricted computer access. Every modifying step is high-risk and asks permission. No arbitrary shell, scripts, raw coordinate clicks, autonomous terminal use, credentials, elevated apps, or background routines. Unsupported UIA controls stop with a Guide/manual fallback. Undo is available only for a ValuePattern field that still contains Buddy's exact value.
- A model must fit a bounded local context. Large tasks should be split into smaller steps. Web search availability depends on the provider. Fetched page text is untrusted; it cannot authorize actions.
- Remote phone control is not enabled; paired phones are refused by the Agent plan endpoint. No new Android interaction features are included.

## Validation

Run `scripts/build-windows.ps1` for service, desktop logic, assistant and package checks. It also executes `Buddy.exe --check-package` so missing runtime manifests fail the build.

Optional live inference and internet checks (temporary test state, no user documents):

```powershell
dotnet run --project tests/Buddy.Assistant.Tests -c Release -r win-x64 --self-contained true -- --live
```

Interactive native fixture (foreground desktop required; briefly opens a test window and edits only its own field):

```powershell
dotnet run --project tests/Buddy.Windows.IntegrationTests -c Release
```

The native fixture must fail if it cannot acquire foreground focus; it must not silently claim a skipped action passed. During this delivery the helper could not initialize and Windows denied fixture focus, so native action/microphone acceptance remains open. The 54 deterministic/live assistant checks passed, including actual Gemma grounded plans and a fetched, cited answer.

Before treating the preview as accepted: verify the separate voice shortcut; speak and stop a response; tap and hold; point at Export in the tutorial; run and undo a practice field edit; stop a multi-step run; move/scroll the target; try 100/150/200% scaling and a negative monitor origin; confirm web-off produces no research, then enable it and check a cited answer. Keep the release a testing prerelease until these checks pass.
