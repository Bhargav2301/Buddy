# Readiness probe 0.1.3 — R02 and early Disconnect repair

**October 9 follow-up:** the user reported a successful live 0.1.3 check, with a missed visible new-chat composer. See [0.1.4 follow-up](Browser-readiness-0.1.4.md). The pending-live statements below describe the original build checkpoint.

The reported **R02 / 0.1.2** stops before tab lookup or script injection. It establishes that the old background-worker browser-focus predicate failed; the screenshot and code do not identify the exact live cause.

The preserved 0.1.2 code also has a reproducible early-Disconnect race: Disconnect during its initial window lookup leaves no session to close, then the resumed lookup can inject, read structure and recreate a session. Both the R02 predicate and the race were reproduced using synthetic Chrome APIs against the original hashed files. No live browser was operated.

## Implementation

The new standalone `integrations/readiness-probe` uses a **direct popup owner**. It requires the exact packaged popup view, unchanged popup document, visibility and actual document focus, then pins the popup's normal browser window, one selected tab, exact URL, nonce and Chrome-supplied top-document ID. A false parent-window focused flag is admissible only while the genuine toolbar popup itself owns focus. No foreground activation or focus-stealing workaround is used.

An attempt exists before the first await. Disconnect, popup blur/close, navigation, tab/window changes and a ten-second deadline revoke it. Cancellation settles the pending caller without waiting for a stalled Chrome promise, and guards prevent follow-on API dispatch or state resurrection. An already-dispatched structural read may finish; its result is discarded. Popup Stop feedback cannot be overwritten by an earlier completion.

A metadata-only first injection obtains the browser's document ID. One subsequent isolated scan targets that exact document and returns five capped counts and two booleans. DOM traversal is limited to 8,000 elements and 100 ms; incomplete scans refuse rather than returning partial results. It reads structural attributes/visibility only. There is no service worker, native host, runtime-message handler, persisted page controller, browser storage, network call, prompt/history/file/account read or Send path in this standalone package. Permissions remain `activeTab` and `scripting`.

This is independent of the full browser/native-host adapter. That adapter's live identity/history/attachment work remains incomplete. The older Port/getContexts design notes were not implemented; the current direct-popup implementation and tests are the source of truth. Buddy Windows stays at 0.4.8; the probe has its own 0.1.3 version.

## Validation and delivery

**207 synthetic Node cases pass:** 100 new probe cases and 107 existing browser boundary cases. Coverage includes all 14 awaited API boundaries for Disconnect, expiry and browser rejection; exact owner/document checks; late completions; navigation/window events; no-content-read and capped-output rules; and actual popup.js Stop/result rendering. The first candidate passed 96/97 cases; a late window-change case led to explicit window-focus event cancellation and its corrected event fixture. Final tests include this case.

The original 0.1.2 source was read only. Its bridge SHA-256 is `86e5e186961bf879cdcea0ebba2137205bf209d2a1d52962440faffe90e73b18`; reproduction and final test logs are local under `diagnostics/readiness-013`, outside Git. The code and package are reviewed by the integration owner; no independent human or specialist review is claimed.

`scripts/package-readiness-probe.py` creates a fresh, whitelist-only unpacked folder, ZIP and SHA-256 checksums. Existing packages are refused, never overwritten. The package contains six runtime/document files plus checksums, without test fixtures or full-adapter capabilities. A new CI job runs the probe and existing browser regressions using Node 22.

**Live acceptance is pending.** Synthetic API responses cannot prove that Chrome on this PC supplies the expected popup metadata or that the real R02 report is resolved. No extension was loaded/reloaded, profile or registry changed, live tab inspected, or readiness check invoked during this repair. Use the [packaged instructions](../integrations/readiness-probe/README.md) to load the new folder, verify the visible 0.1.3 label, and perform one deliberate read-only check. Reloading the preserved 0.1.2 folder will retain the old code.

Chrome references and exact permission/selector changes are documented in the [probe README](../integrations/readiness-probe/README.md). Future live provider integration and original-attachment disclosure remain separate work.
