# Buddy

Native Windows and Android applications backed by an AI model running on the user's Windows PC.

**Current version:** [VERSION](VERSION) · testing prerelease. [Changes](CHANGELOG.md) · [Downloads](https://github.com/Bhargav2301/Buddy/releases) · [Build status](https://github.com/Bhargav2301/Buddy/actions/workflows/build.yml).

Buddy is a local-first cursor companion for voice, screen-aware teaching and reviewed prompt refinement. Home holds settings/history; closing it keeps the configured companion running. Optional action agents, local knowledge and Android access retain explicit privacy and approval boundaries. See [the current source checkpoint and acceptance limits](docs/Source-sync-2026-10-09.md).

## Start using it

Read [the setup guide](docs/Buddy-Setup-Guide.md). The Windows download contains a self-contained native `Buddy.exe`; the Android download is a signed testing APK. Install Ollama and download a model on the PC, then pair Android by QR code. There is no cloud API key requirement.

**Windows installation:** the updated Windows ZIP corrects the `WindowsBase.dll` packaging collision. Extract it into a fresh folder, then run `Install-Buddy.cmd` to install, or `Run-Buddy.cmd` to launch with startup diagnostics. Logs are in `%LOCALAPPDATA%\Buddy\Logs`. Windows voice initialization is deferred until read-aloud is used. The native installer keeps PC conversations and pairing state in the user profile.

**Windows companion:** Use your configured voice/chat shortcuts or click the companion to talk. Right-click for Type, Guide and optional tools. **Refine source field** captures the original supported AI composer before Buddy takes focus, then offers a field-anchored diff, Accept and Undo without sending. **Refine Buddy draft** is a separate local action. **Hide Home - keep Buddy running** preserves the tray service; **Exit Buddy (stops companion)** exits. Current settings and voices survive local upgrades. See [current behavior, tests and limits](docs/Source-sync-2026-10-09.md).

**Settings:** Open Buddy again to show Home and select Settings, use the tray or Buddy Settings shortcut, or run `Open-Buddy-Settings.cmd` in a portable folder. `--background` requests companion startup. Offline Piper voice setup is optional and separate; model weights and user profiles are never committed.

See the [core teaching and validation report](docs/Windows-0.4.2.md), [local Whisper evidence](docs/Whisper-local.md), and [evidence-linked Clicky comparison](docs/Clicky-comparison-0.4.md) for implementation status and remaining gaps.

**October 9 source checkpoint:** version 0.4.8 includes the handoff's integrated browser61 refinement/context work and preceding Calculator fixes. The last recorded installed version in the handoff was 0.4.5; source synchronization does not install an update. Browser integration remains incomplete for real provider history and attachments. See the [source reconciliation and fresh checks](docs/Source-sync-2026-10-09.md), [browser preview limits](docs/Windows-browser61.md), and [development records](docs/development/README.md).

The standalone [readiness probe 0.1.6](docs/Browser-readiness-0.1.6.md) recognizes the message-container structure inspected in Chrome and deduplicates loaded units. All 231 Node and 60 owned offline Chrome cases pass. Live execution of the new extension remains pending; loaded DOM counts do not establish complete history or draft access.

## Project map

| Path | Purpose |
|---|---|
| `apps/windows/Buddy.Windows` | C#/.NET 8 native WPF application; chat, local setup, QR pairing, Windows speech, hotkeys, UI Automation context, capture preview |
| `apps/android` | Kotlin 2 / Jetpack Compose native app; pinned HTTPS, chat, history, voice, share receiver, bubble, tile, default assistant, basic keyboard |
| `apps/windows/Buddy.BrowserHost`, `integrations/browser` | Uninstalled native-messaging/browser context foundation; production provider support remains incomplete |
| `services/Buddy.Server` | Embedded ASP.NET Core service, local model adapter, encrypted state, pairing and shared conversation API |
| `tests/Buddy.Tests` | Dependency-free executable service test suite, including actual TLS/HTTP integration |
| `tests/Buddy.Windows.PackageChecks` | Windows PE architecture, WPF type and managed dependency validation; runs on any .NET 8 build host |
| `tests/Buddy.Desktop.Tests` | Placement across monitor origins/scales and shortcut conflict regression checks |
| `tests/Buddy.Assistant.Tests` | Grounding, action policy, web isolation, tool loop and cancellation; optional live Ollama checks |
| `tests/Buddy.Windows.IntegrationTests` | Interactive native fixture for UIA edits/invocation, overlay flags and cancellation |
| `apps/android/app/src/androidTest` | Native Android UI integration and sensitive-field tests |
| `scripts` | Windows build, per-user install, local AI setup, narrowly scoped firewall setup |
| `docs` | Setup, coverage, architecture, and validation |

## Version history

Both apps read the root `VERSION` file. [CHANGELOG.md](CHANGELOG.md) records release changes, and Git tags identify the exact source for each download. Successful builds on `main` prepare a tagged draft prerelease. See [the versioning guide](docs/Versioning.md) to prepare future versions and publish reviewed downloads.

## Build

### Windows

Install the .NET 8 SDK on Windows 10/11 x64. Run `scripts/build-windows.ps1` in PowerShell. It executes service tests, publishes the self-contained native application, and creates the distribution ZIP. The desktop UI is WPF; it does not use a browser or WebView.

### Android

Use JDK 17, Android SDK 35, and the included Gradle 8.9 wrapper. Open `apps/android` in Android Studio, or run:

```sh
cd apps/android
./gradlew :app:assembleDebug :app:lintDebug
```

On Windows use `gradlew.bat`. The testing APK is written to `app/build/outputs/apk/debug/app-debug.apk`. Production distribution requires your own persistent release signing key and the relevant store review; never use a debug key for a public release. A build made with a different testing key cannot update the supplied APK in place. Uninstall the old testing APK first if Android reports a signature mismatch; the PC history is retained.

### Service tests

```sh
dotnet run --project tests/Buddy.Tests -c Release
```

These tests create temporary data, exercise mock upstream inference and real pinned HTTPS, then remove the test data. They do not require an installed model. The separate live Qwen3 test executed during delivery is described in `docs/Validation.md`.

### Android integration test

Start Buddy on the PC, download its chat model, and generate a fresh QR pairing link. Use a device/emulator that can reach the PC address. Build `:app:assembleDebugAndroidTest`, install both APKs, then run:

```sh
adb shell am instrument -w -e pairLink 'buddy://pair?host=PC_IP&port=47831&pin=PC_FINGERPRINT&code=FRESH_CODE' app.buddy.local.test/androidx.test.runner.AndroidJUnitRunner
```

Use the actual unexpired link from Buddy; do not paste the placeholders. The UI test pairs, refuses a deliberately wrong certificate pin, sends a real AI request, and checks history. It requires a clean unpaired app state and a responsive inference runtime.

## Design and privacy

The PC is the single source of truth. Android never receives an AI-provider key. It stores its device token encrypted with Android Keystore. PC state is encrypted with ASP.NET Data Protection, whose key ring is protected by current-user Windows DPAPI. Both clients share conversations, prompts, and explicitly saved memories.

Phone transport uses a self-signed PC certificate whose SHA-256 fingerprint is transferred in the physical QR pairing step and checked on every TLS connection. The custom trust manager intentionally trusts only that exact certificate, rather than trusting arbitrary self-signed certificates. Pairing is a five-minute, single-use window with five failed attempts maximum. Device tokens are random 256-bit values; the PC stores hashes for phone tokens.

Images and raw screen context are request-scoped and excluded from persisted conversations. The written AI answer is persisted and may describe an image. Application requests and prompts are not logged by the embedded HTTP service. The store is intended for one person's paired devices, not multi-tenant internet hosting.

## Source references

- Supplied `Technical Specification: Nexa — Windows + Android AI Buddy Ecosystem`, v1.0, 26 September 2026.
- [Updated Windows TRD](docs/Buddy-TRD.md) and [UI/UX specification](docs/Buddy-UI-UX.md), v1.2; [0.3.0 implementation scope](docs/Windows-Assistant-Preview.md).
- [Ollama chat API](https://docs.ollama.com/api/chat)
- [Ollama on Windows](https://docs.ollama.com/windows)
- [Qwen3 4B model](https://ollama.com/library/qwen3:4b-instruct-2507-q4_K_M)
- [Gemma 3 4B model](https://ollama.com/library/gemma3:4b)
- [Android Gradle Plugin 8.7 compatibility](https://developer.android.com/build/releases/agp-8-7-0-release-notes)
- [Android VoiceInteractionSession](https://developer.android.com/reference/android/service/voice/VoiceInteractionSession)
- [Android SpeechRecognizer](https://developer.android.com/reference/android/speech/SpeechRecognizer)

Third-party dependencies remain under their respective licenses. The Windows runtime's license and notices are included with its distribution. Ollama and model weights are downloaded separately from their official sources.

Current local Windows update: [0.4.0 behavior](docs/Windows-0.4.0.md), [local Whisper](docs/Whisper-local.md), and [pinned Clicky comparison](docs/Clicky-comparison-0.4.md).
