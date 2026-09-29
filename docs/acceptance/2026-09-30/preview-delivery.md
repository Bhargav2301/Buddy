# Windows preview delivery

Source commit: `93e141ca2d6c79a2ab74f830549d49652e0b6200` on `feature/interactive-assistant`, draft PR #1.

Package: `Buddy-Windows-MVP-Preview-93e141ca-20260929-213035.zip` (96,770,660 bytes). The filename and build metadata use UTC; the local date was 30 September 2026.

SHA-256: `2A15F2312D9FDC5FD5E03D5AD05253B81F7680D72908F3CE39ABF109CF279CBD`

The package keeps version 0.3.1 as its base, identifies its channel as MVP preview, and records `completedMvp: false`. It does not represent completed 0.5.0.

## Verified for this package

| Check | Result |
| --- | --- |
| Service / TLS / paired-device authorization | 32 passed |
| Desktop policy, edits, installer recovery, preference migration | 86 passed |
| Assistant / research / vision evidence policy | 71 passed |
| Refinement / MVP behavior policy | 36 passed |
| Native Settings activation and isolated visual fixtures | 21 passed |
| Native OCR / redaction / boundary fixtures | 7 passed |
| Self-contained Windows publish | Passed |
| Managed/native package structure and pinned OCR model | Passed |
| Native apphost/OCR dependency probe | Passed |
| ZIP SHA-256 and fresh extraction | Passed |
| Every extracted file versus manifest | 666 verified, no unexpected files |
| Dependency probe from fresh extracted directory | Passed |

The repeat injected-input fixture measured 21.2 ms to the cancellation fence. This remains a synthetic measurement, not the required physical input-to-dispatch-stop measurement.

The package includes per-suite logs, dependency logs, earlier real-model synthetic reports, per-file checksums, installation/rollback launchers, and preview instructions. Its prerequisite helper was inspected but not run; the native probe succeeded using the runtime already installed on this PC.

Windows PowerShell initially lost the exit-code value of a fast `Start-Process -PassThru` dependency probe even though the app logged success. The shared build helper now owns the process handle from creation. It was verified under Windows PowerShell 5 and in the complete build and extraction checks.

## Additional compatibility checks

Android app sources and its instrumentation APK compiled successfully with the repository's pinned dependencies and the installed Adoptium JDK 21. The default JAVA_HOME pointed to a JRE without jlink; only the build process was given the full JDK path. The unit-test task reported NO-SOURCE, so no Android unit assertions are claimed. Phone instrumentation was not run. The service suite above covers the paired-device protocol.

The first GitHub Windows CI run exposed a synthetic fixture geometry issue: point-sized text changed with the runner's DPI, leaving the drawn border outside the bounded search. The fixture now uses explicit pixel-sized text and a fitting border. All seven OCR checks passed locally with the corrected fixture. The production boundary detector is unchanged; this does not expand its grounding claims.

## Open acceptance

Native capture/action checks previously stopped at the first foreground assertion. An interactive fixture was opened for a deliberate user click; no subsequent checks are counted until its result is recorded. Real microphone, supported host-field workflows, populated visual comparisons, screen-reader/mixed-DPI, all seven application workflows, 150-case grounding, physical stop latency, and actual clean installation/0.3.1 upgrade/rollback remain open.

The existing installed executable, released ZIP, and user profile were not replaced. The preview is available locally under the repository's `dist` directory. Follow `docs/Windows-MVP-Preview.md` for installation and rollback when validating it.
