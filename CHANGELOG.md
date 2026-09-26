# Changelog

Versions follow `major.minor.patch`; Git tags use `v` plus the version.
The root [VERSION](VERSION) file controls both applications. See
[Versioning](docs/Versioning.md) for the release process.

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
