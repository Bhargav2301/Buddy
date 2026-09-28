# Buddy validation record

## Windows assistant preview — 28 September 2026

29 service checks, 22 desktop logic checks and 54 assistant checks passed on the Windows development host, including four live checks against the installed Gemma 3 4B model and real public HTTPS. The checks cover actual structured action and guide planning, fetched evidence passed back to inference, citations, ambiguous/stale targets, allowlisted actions, web-off isolation, blocked URLs/IP ranges/redirects, HTML extraction, size limits, encrypted audit metadata, and global cancellation. The self-contained Windows package also passed PE/dependency validation and the native apphost integrity probe.

The interactive native fixture compiled but could not obtain foreground focus, including after an explicit activation attempt. Its action and overlay assertions did **not** execute. The desktop computer-use helper also failed to initialize (`failed to write kernel assets`, OS error 3) after a reset. No claim is made that microphone/PTT, UIA actions in user applications, overlay accuracy, or mixed-DPI behavior passed native acceptance. Run the fixture and the checklist in [Windows-Assistant-Preview.md](Windows-Assistant-Preview.md) in an interactive Windows session.

Earlier validation records below are historical. See the branch's CI run for current package and regression checks.

Historical build date: 26 September 2026.

## Repository CI — version 0.2.0

The [first GitHub Actions run](https://github.com/Bhargav2301/Buddy/actions/runs/36247292696)
compiled and published the native Windows application on a Windows runner. All
28 service assertions, all 13 desktop logic assertions, and the Windows package
integrity check passed. The version/manifest/changelog check also passed.

Android setup in that run stopped before compilation because the setup action
requested the obsolete SDK `tools` package. The workflow now explicitly requests
platform-tools, Android platform 35, and Build Tools 34.0.0. Consult the
[latest Actions run](https://github.com/Bhargav2301/Buddy/actions/workflows/build.yml)
for the rebuilt Android result and release status.

CI does not drive the Windows UI or connect to a real microphone. The manual
Windows acceptance items below remain open. Earlier Linux compilation and Android
device records are historical evidence for the pre-repository source snapshot;
they are not a claim that every later APK has been retested on a device.

## Windows cursor companion 1

- The updated WPF application and embedded service compile using the existing official .NET 8 reference assemblies.
- Thirteen desktop logic assertions pass, including 1,815 simulated pointer positions across monitor origins and 100–300% scale factors, lower/right edge flips, non-primary monitor coordinates, shortcut conflict rollback, successful replacement, no-repeat and cleanup. See `desktop-logic-tests.txt`.
- The compact bar uses the existing real `BuddyService.Chat` streaming path, with a conversation ID and unique request ID. No demonstration responses are added. The original deterministic service and Android integration records below remain the evidence for those unchanged shared components.
- Speech engines are initialized only by an explicit voice action. Recognition callbacks are invalidated on cancellation; TTS starts after recognition has stopped. These lifecycle paths were reviewed and compiled; live speech and focus changes require Windows testing.
- The Windows package retains the repaired Desktop runtime and passes the package integrity check after update.
- **Not executed here:** the native cursor window, click-through behavior, hotkey delivery, mixed-DPI monitor transitions, real microphone/TTS, tray interactions and installer. The build host is Linux. Logic and package checks are not substitutes for these Windows acceptance checks.

Suggested device acceptance: install the updated Windows package; verify the mint companion follows without blocking clicks; press Ctrl+Space, send a real message, and find it in Home/Android history; press Ctrl+Shift+Space and speak once; verify Esc and clicking outside stop listening; change to a free shortcut and verify the previous one is released; try a conflicting shortcut and verify the working one survives; test each monitor and display scale; hide the companion in settings; quit from the tray and confirm windows and shortcuts are removed.

## Windows repair 1

The original delivered Windows ZIP failed the new package check: `WindowsBase.dll` was the 16,208-byte Core compatibility facade, which has no `System.Windows.DependencyObject` implementation. The rebuilt ZIP uses the 2,254,632-byte Windows Desktop assembly from the official 8.0.31 runtime. In the fallback packaging process, files are copied in Core → ASP.NET → Windows Desktop order, so Desktop implementations win name collisions. The normal Windows build script uses `dotnet publish` and now runs the package check before creating the ZIP.

The check reproduces the failure against the original ZIP and passes against the replacement. It reads PE metadata to check Windows x64 hosts/native binaries, essential WPF type definitions, reachable managed type-reference dependencies, and the absence of reference-only assemblies. Unused legacy type forwarders in framework compatibility facades are not treated as application dependencies. See `windows-package-checks.txt` for the results.

An incomplete-extraction regression also correctly rejects a missing QR library. The repaired application's managed entry point passed package-check mode under the Linux .NET host. Removing its WindowsBase component produced the expected caught error, exit code 1 and diagnostic log. This validates the error path without executing WPF, Windows native code or the installer.

The native `--install` path copies the application into the current user's Programs folder and creates optional shortcuts, without PowerShell execution-policy changes. `Install-Buddy.cmd` and `Run-Buddy.cmd` retain failure output and enable .NET host tracing. A core-only entry point catches WPF initialization failures and writes `%LOCALAPPDATA%\Buddy\Logs\startup.log`. Windows speech synthesis is initialized on first read-aloud use, so an unavailable voice does not block the window or mark a saved answer as failed.

The corrected managed application compiles successfully. **The Windows GUI and installer have still not been executed on Windows in this environment.** Package checks establish the known packaging fix, not full device acceptance. Android's previously delivered APK is unchanged.

## Verified

| Check | Result |
|---|---|
| Windows application and shared service compilation | Passed using .NET 8 Roslyn with official Windows Desktop and ASP.NET reference assemblies |
| Portable Windows app-host creation | Passed using the official .NET HostWriter; Windows x64 GUI subsystem and application manifest included |
| Self-contained runtime packaging approach | Equivalent Linux self-contained package executed all service tests without a system .NET installation |
| Service correctness/security suite | 28 assertions passed |
| Real local model integration | Ollama 0.34.4 with `qwen3:4b-instruct-2507-q4_K_M`; actual model inference through `BuddyService` returned `4` for a simple arithmetic request and committed the conversation |
| Reasoning-prefix handling | Regression reproduced with a real model; final-answer filter added; split-marker regression assertion passed |
| Android APK compilation and packaging | Gradle 8.9, Android Gradle Plugin 8.7.3, Kotlin 2.0.21, JDK 17; APK generated successfully |
| Android instrumentation test APK | Compiled successfully |
| Android native runtime integration | Both instrumentation tests passed on Android 10/API 29: exact-certificate rejection, pairing, visible Send with the keyboard open, a real local AI answer, shared history, and sensitive-field exclusions |
| Android lint | No errors and 25 warnings; warnings concern intentional exact-certificate trust, synchronous credential persistence, bubble accessibility lint, a newer dependency version, and English UI strings |
| Android package signature | Verified with Android SDK `apksigner`; APK Signature Scheme v2 valid, one testing signer |

`service-tests.txt` contains the exact passing assertions. The suite covers availability, missing models, stream completion, encrypted persistence, request idempotency, screenshot/context non-persistence, sensitive-text redaction, refinement without sending, one-use pairing, attempt limits, token validation/revocation, cancellation, generation locks, actual HTTPS authentication and shared history, and untrusted-certificate rejection.

The live inference test is separate from the deterministic stub-based suite. No mock inference engine or canned response route is included in the shipped applications.

Live testing found that the generic `qwen3:4b` tag resolves to a thinking-only model. A request for a one-word answer exceeded the UI test timeout with that model. The shipped default and setup script now use the explicit dated Instruct tag, which completed the repeated live inference check. This is a model-selection correction, not a claimed hardware performance benchmark.

Android runtime testing also found a TLS connection cleanup on the main thread after pairing. Cleanup and cancellation now run on a dedicated background executor.

A small-screen keyboard check exposed duplicated bottom spacing that hid the Send control. The Compose content now consumes the Scaffold padding before applying keyboard insets, and the UI test checks that Send is displayed while entering a message.

## Environment limits and release gates

- The build environment is Linux. **The Windows GUI has not been launched on Windows.** Hotkeys, tray behavior, Windows speech, UI Automation, screenshot preview, firewall setup, DPAPI integration and installer behavior require a Windows device acceptance pass.
- Android device UI validation is recorded separately below. An Android 15 software emulator could not complete a stable boot under this environment's resource limits. Hardware acceleration was unavailable.
- Voice recording, recognition quality, read-aloud, camera scanning, bubble permissions, keyboard text replacement, assistant activation and protected-app behavior need a real Android device pass.
- The optional Gemma vision model path is implemented, but image-answer quality was not benchmarked or certified during this build.
- No performance-budget, battery, grounding accuracy, penetration-test, Google Play, Microsoft Store, publisher-signing or multi-user production gates have been completed.
- Windows is an unsigned portable alpha. Android is a debug-signed testing build. These are installable test packages, not store-certified releases.

## Android UI execution

The final testing APK was installed on an Android 10/API 29 x86_64 emulator at 720 × 1280. Both instrumentation tests passed (`OK (2 tests)`, 63.528 seconds). The test paired using a fresh one-use link, rejected a deliberately incorrect certificate fingerprint, sent “Reply with the single word READY.” through the native chat UI to the actual PC service and Ollama model, received the answer, and found the conversation in History. It also checked platform password field types and configured sensitive-text exclusions.

`android-instrumentation.txt` contains the passing result. The emulator ran without hardware acceleration; a previous run encountered system UI stalls and the software renderer was restarted. This pass validates the tested flow, not all Android versions or device permissions. Native Windows execution and the device integrations listed above remain release gates.
