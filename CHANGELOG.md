# Changelog

Versions follow `major.minor.patch`; Git tags use `v` plus the version.
The root [VERSION](VERSION) file controls both applications. See
[Versioning](docs/Versioning.md) for the release process.

## [0.3.0] - 2026-09-28

- Separate 360 px voice overlay for Ctrl+Shift+Space, with transcript, microphone level, sentence-based speech and cancellation. Optional tap/hold main shortcut.
- Windows Guide and opt-in Agent preview: local structured plans, live UIA targets, click-through arrows/highlights, saved walkthrough progress, confirmed allowlisted actions, emergency stop, and guarded field undo.
- Opt-in public HTTPS research with bounded search/fetch, source citations, redirect/DNS checks, and isolated untrusted page content.
- Per-capture privacy checks, password redaction, encrypted activity metadata, and memory-only voice frames when accessibility enumeration is complete.
- Require Buddy.deps.json and probe the native apphost during packaging; add noninteractive installer flags and self-contained regression tests.
- If the original Qwen default is missing but Gemma 3 4B is installed, select Gemma on startup. Preserve installed or custom model selections.
- Validation: live Gemma planning, grounding and cited research checks passed. Native foreground acquisition was blocked in this automation session; microphone, real app execution, monitor/DPI and guide accuracy acceptance remain manual. See docs/Windows-Assistant-Preview.md. Android feature work remains deferred.

## [0.2.0] - 2026-09-26

First version tracked in this GitHub repository. This imports the latest Buddy
source, including the earlier Windows repair and cursor companion work.

### Included

- Native Windows WPF application and native Android Compose application connected
  to local Ollama inference on the PC.
- Windows cursor companion, compact chat bar, one-utterance voice activation,
  configurable shortcuts, tray controls, and optional spoken answers.
- Correct Windows Desktop runtime packaging, per-user installer, portable launch
  script, startup logs, and package integrity checks.
- QR pairing, pinned HTTPS, encrypted local state, shared conversations, saved
  prompts and memories, plus Android assistant, keyboard, bubble, and tile entry points.
- Existing service, desktop logic, package, and Android integration test sources,
  together with the original requirements and validation records.

### Changed

- Shared version number for Windows assembly metadata, Android app details,
  service health responses, diagnostic logs, and download filenames.
- GitHub Actions builds Windows and Android, runs the automated checks, and
  prepares a tagged draft prerelease after a successful build on `main`.
- Release downloads include a setup guide, changelog, source commit, and SHA-256
  checksums. A version check rejects mismatched manifests and incomplete notes.
- Android CI explicitly installs SDK 35 and Build Tools 34.0.0 instead of the
  setup action's obsolete `tools` package default.

### Validation limits

- Desktop logic and package checks do not prove that cursor movement, hotkeys,
  microphones, or installation work on a user's Windows machine. Those still
  require device acceptance as described in `docs/Validation.md`.
- CI produces an Android **testing APK** with a temporary debug signing key.
  It may require uninstalling a previously signed testing APK before installation.
- Local AI setup still requires Ollama and a model on the Windows PC.

## Before repository tracking — reused 0.1.0 archives

The initial native apps, Windows packaging repair, and cursor companion were
delivered under the same `0.1.0` filenames before repository version tracking.
They were not separate Git releases. The import commit preserves the latest
combined source snapshot; older release tags have not been reconstructed.
