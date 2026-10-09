# Continuation63 — installed update and OCR correction

The user explicitly requested updating installed Buddy, continuing feature development, and pushing changes to GitHub. This supersedes the earlier installation hold. Merge/release and stronger browser-account admission remain separate; no such action is part of this change.

## Installed baseline

The existing 0.4.5 app was not running. The verified continuation62 package (source a80b143) was installed as 0.4.8 using Buddy's staged installer. Its old application directory was retained for rollback. The settings, encrypted conversation store and TLS/pairing file were byte-identical immediately after installation, before launch. The installed executable launched successfully and its Home-to-Settings navigation was observed through the supported Windows accessibility tool. No settings were changed and no conversation was sent. This establishes that upgrade and navigation path, not microphone/Agent/Guide acceptance.

## New feature: correct extracted text against its original image

Chat context now offers **Correct extracted text** for retained OCR sources. The editor displays the original selected image with keyboard-accessible zoom and an editable text area. Reset restores the opening text; Cancel discards edits. Saving stages the correction and requires a separate source review before it can be used. Original bytes remain unchanged; first-extraction method/hash, corrected-text hash and a human-correction label remain distinct.

Corrections bind to the exact chat, source digest and workspace revision. Stale or removed sources cannot be saved. Every changed correction cancels old frozen context and prepared drafts; unchanged text preserves its existing review. Unicode, newline and total-memory limits are enforced without truncation. Original-attachment requirements remain unchanged. No model is asked to guess the correction, and Save never transmits, attaches a file or submits a host form.

This improves OCR review usefulness; it does **not** fix the previously observed automatic digit-recognition failure. The original image remains the comparison reference. Factual model accuracy, general OCR/Guide usefulness, seven-app/grounding evaluation and physical/native workflows remain open.

## Validation

- 82 original-asset assertions, including corrections, first-extraction provenance, byte-identical originals, stale/cross-chat rejection, review invalidation, exact Unicode/newlines, capacity limits and original-delivery preservation.
- 64 unshown WPF checks, including correction preview/zoom, reset, save-without-dispatch, cancellation, released preview/editor contents and visibly rejected stale save. These instantiate real WPF controls without foreground windows; they are not visible acceptance.
- 77 workspace checks, 69 browser-context assertions and 169 planning/reply assertions pass.
- Release Windows publish and package/type/dependency/native OCR-load checks pass. New context and unshown WPF checks are included in Windows CI.
- Root reviewed source and results. Local sanitized receipts are in `diagnostics/continuation63`; source contains no private profiles, logs or image captures.

## Delivered installed checkpoint

Continuation63 implementation `d26824861b8d7fc93744728adf47595acf2de339` is installed in the per-user Programs/Buddy directory. All 688 manifest entries matched the installed files. Three saved-data files again remained byte-identical before launch, and the previous binaries are retained for rollback; the older 0.4.5 backup also remains. Home opened, and a second `--settings --quiet` invocation exited successfully with one installed Buddy process. A fresh accessibility observation confirmed the Settings/General surface. No settings or messages were changed.

The visible synthetic-image correction walkthrough could not be completed: the supported Windows tool first returned `coordinate input geometry is unavailable`; refreshing the window permitted a retry, but no Talk surface was observed. Keyboard focus also did not visibly change. No synthetic image was selected, attached or sent. This is a recorded native-input limitation, not visible correction acceptance or proof of an app-level Talk defect. The 64 unshown WPF checks remain separate evidence.

Both exact-implementation [PR CI](https://github.com/Bhargav2301/Buddy/actions/runs/37950567912) and [push CI](https://github.com/Bhargav2301/Buddy/actions/runs/37950561319) pass version, browser, Windows and Android; draft prerelease was skipped. The Windows job includes the new asset/workspace and unshown WPF suites. Documentation-only delivery follow-up has separate CI.

Verified archive: `Buddy-Windows-0.4.8-continuation63.zip`, SHA-256 `dafbd3934b2c9e586e4baedb419aa390f441d09567290033d1ac1c6ad56a8246`. Exact source backup: `Buddy-Source-d268248.zip`, SHA-256 `39b728a470cad9640c4721569deb8603aeac3ba3359fbca7b410121e7a8622ec`. The package is immutable and identifies the implementation commit; this later report records installed acceptance. No merge/tag/release was made. Version remains 0.4.8 development, not completed 0.5.0 MVP acceptance.
