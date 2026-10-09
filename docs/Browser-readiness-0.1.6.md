# Readiness probe 0.1.6 — observed message containers

The user reported zero role counts despite visible messages in probe 0.1.4. After the user explicitly approved inspection of the selected conversation, Chrome's Elements panel exposed a different message structure: search-unit keys ending in `:user` or `:assistant` inside a marked conversation root. The previous detector only counted `data-message-author-role`. This explains the detection miss on the inspected user and assistant containers.

## Implementation

PROBE-016 is owned by `/root`, based on `a8d91e29354aca6ae450d9691f1016d9602cfcf4`, in the canonical integration checkout. Version 0.1.6 recognizes the observed `fallback-turn-N:N:user|assistant` key family on `data-chatgpt-search-unit-key`. It requires matching `data-content-search-unit-key` on the same wrapper or first element child, plus both `data-chatgpt-conversation-selection-target="true"` and `data-thread-find-target="conversation"` on an ancestor beneath `main`.

Legacy author-role markers remain supported. Nested aliases and repeated keys in the same root are deduplicated. Conflicting roles, conflicting nested identities and group collisions abstain. Unsupported new key families or missing/mismatched structural evidence do not create counts. Headings, bubble shapes and message text are not role evidence. Internal grouping is bounded by the same 8,000-element / 100 ms scan; only the original seven capped fields leave the page.

The page virtualizes turns. The popup therefore says counts cover loaded DOM only and virtualized turns may be absent. Account, complete-history, draft and attachment capabilities remain unavailable. The 0.1.5 scoped-route fix is included. Permissions, popup/document binding, cancellation, no-submit behavior and the separate full browser adapter remain unchanged.

## Evidence and review

- Live structural inspection: one user wrapper and one assistant wrapper were inspected in the selected conversation using Chrome's Elements panel. Both used the supported paired key structure; virtualization was also visible in the DOM. This is evidence for selectors, not a successful execution of 0.1.6 or proof of all provider layouts.
- Existing 0.1.5 source reproduces the bug in the owned fixture: the observed user/assistant pair yields zero role units instead of two.
- 231 Node tests pass: 124 readiness cases and 107 existing browser regressions, including cancellation, ownership, document binding, non-disclosure and loaded-DOM interpretation.
- 60 owned offline Chrome cases pass: 28 editor/layout, 28 message-marker and four route cases. They exercise the actual detector and DOM, including duplicate/conflict handling, unsupported structures, caps, zero content/identifier/label reads, zero input/submit events and zero DOM writes. Fresh headless context, intercepted synthetic HTML, no real provider profile or network request. Fixtures use invented keys and content.
- Integration-owner source review only; no independent review is claimed. Exact source hashes and retained failure/success receipts are local under `C:/Projects/Buddy/diagnostics/readiness-016`.

The browser tooling could return rendered chat text under the user's explicit inspection approval. No private text, page screenshots, account labels, project names, exact conversation URLs or IDs were copied into source/evidence. A keyboard shortcut initially opened ChatGPT settings; navigation returned to the conversation without changing settings, then Developer Tools was opened through Chrome's menu and closed after inspection. No message, draft edit or upload was performed.

## Delivery and remaining acceptance

Version, ledger and source-payload checks pass. A fresh 0.1.6 package is built from the tested source; older packages remain intact. Packaged `probe.js` SHA-256 is `c67d01710cb12b5fa305d3c1f49da92274153591a81927d538a3f5f8ff1aecaa`; ZIP SHA-256 is `3ef4a3006190d556b0b52f4af23678f05542be77723e89069752b2d0bae2ab53`. The preserved 0.1.5 ZIP hash is unchanged. Follow the [packaged loading instructions](../integrations/readiness-probe/README.md). No Windows reinstall or native-host registration is needed. Windows source remains 0.4.8; this does not complete the Windows MVP or change installed Buddy.

Live execution of the new 0.1.6 extension remains untested. The next useful check is one deliberate invocation of that version on the selected conversation. Expected improvements are recognized loaded message units and the scoped conversation route; exact counts depend on currently loaded turns. A zero count can still indicate an unsupported layout. Provider identity, full-history coverage and production browser-adapter admission remain separate work. New CI status must be verified on the committed revision; earlier runs are not current-head evidence.
