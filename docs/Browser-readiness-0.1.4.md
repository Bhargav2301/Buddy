# Readiness probe 0.1.4 — visible composer detection

## User-reported live observation

On October 9, the user returned a successful observation from 0.1.3: composer candidates 0; role/user/assistant elements 0; file input elements 2+; streaming false; conversation-shaped URL false. They confirmed the message box was visible on the new-chat page. The R02 focus refusal did not recur in this attempt. This is a user-reported live check, not an assistant-inspected tab or proof of all popup/cancellation behavior.

Zero roles and the route result are consistent with the confirmed new-chat page. The zero editor count is a false negative for its visibly present composer. File input elements do not establish attachments, uploaded bytes, or account identity. No private content is included in the recorded observation.

## Change and limitations

PROBE-014 is integrated by `/root` in the canonical checkout from `2dec6fb1ec7af48934209dcb510331369fce948f`. It changes only the standalone readiness probe, supporting tests and records. Windows source remains version 0.4.8.

The former selector required the legacy `prompt-textarea` ID and literal `contenteditable="true"` (or a textarea with that ID). It misses valid empty/plaintext-only/case-insensitive editable attributes and changed-ID editors. Version 0.1.4 counts textarea or rich editing hosts identified by that ID, form ancestry or the accessible textbox role. Browser-computed editability avoids reading field contents; editing descendants are not counted twice. Disabled/readonly/hidden/inert candidates are excluded. Multiple candidates remain ambiguous.

This fixes demonstrated selector limitations. **The exact markup causing the user's live false negative is unknown.** No live DOM was inspected, and the broader form/role rule remains provisional. It can count unrelated editable forms, ignores shadow roots/subframes, and cannot establish provider identity or permissions. Visible reporting now explains zero/one/multiple candidates, file-control counts and the limited `/c/UUID` route pattern. The exact seven-field response, caps, bounded scan, popup ownership, document binding and cancellation controls remain unchanged.

## Validation

- 210 Node cases pass: 103 probe cases plus 107 existing browser cases, including all 14 API cancellation/deadline/rejection boundaries and three new interpretation cases.
- The same owned fixture reproduces an empty-contenteditable false negative against preserved 0.1.3 (expected assertion failure). This establishes a concrete selector bug, not the unknown live markup.
- 28 actual Chrome DOM cases pass in owned offline HTML with a fresh headless context. These cover legacy/empty/plaintext-only/changed-ID/form/role editors, nested editing hosts, disabled/readonly/hidden/inert cases, ambiguity and a synthetic reproduction of the reported seven-field shape. Content getters throw if accessed; no input/submit events or DOM writes occurred.
- Chrome requests were intercepted into owned HTML; no live profile, extension, tab, provider content, file upload or message was accessed. These fixture results do not prove live 0.1.4 composer recognition.

Local receipts are under `C:/Projects/Buddy/diagnostics/readiness-014`, outside Git. The source review is by the integration owner; no independent specialist review is claimed. Dependencies, root native source and installed Buddy are unchanged.

## Delivery

The whitelist packager creates fresh `Buddy-browser-readiness-0.1.4` unpacked files, ZIP and SHA-256 checksums. Previous package folders are preserved. Disable older probe cards and load the new folder using the [probe instructions](../integrations/readiness-probe/README.md); verify the popup says 0.1.4. One deliberate check on the same visible new-chat editor is the next live acceptance step. No message needs to be sent. A count of one would establish detection on that page only; full browser integration still needs verified identity/history/draft/attachment contracts.

HTML behavior references: [contenteditable](https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Global_attributes/contenteditable), [isContentEditable](https://developer.mozilla.org/en-US/docs/Web/API/HTMLElement/isContentEditable). The prior [R02 repair](Browser-readiness-0.1.3.md) remains a historical record.
