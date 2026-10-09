# Buddy readiness probe 0.1.6

This standalone Chrome extension repairs the readiness-only popup path. It is separate from the full browser/native-host adapter in `integrations/browser`. Its permissions remain `activeTab` and `scripting`. There is no service worker, host permission, native messaging, account integration, upload or Send operation.

## Use the updated probe

1. Extract the 0.1.6 ZIP into a new folder, or use the already extracted 0.1.6 folder supplied with this build.
2. Open `chrome://extensions`. Disable the previous Buddy readiness probes 0.1.2 / 0.1.3 / 0.1.4 / 0.1.5 so their toolbar buttons are not confused with the new one. Keep the old folders for recovery.
3. Choose **Load unpacked** and select the new folder containing this `manifest.json`. Its extension card and popup must both show **0.1.6**. Reloading an old folder does not update it.
4. Open the intended ChatGPT tab. Open the new probe from Chrome's toolbar, click **Check this tab's readiness** once and keep the popup open. The check is read-only; it does not send a message.
5. Report the seven structural fields or the displayed diagnostic code. A successful observation is not proof of verified account identity, complete history or working attachments.

Disconnect cancels the attempt immediately. It prevents subsequent API dispatch and discards late results. A structural read already handed to Chrome can still finish; that work cannot be recalled. No result is persisted. Closing or blurring the popup, navigation, a tab/window change or the ten-second deadline also cancels the attempt. A new check requires an explicit click; there is no automatic retry.

To remove this candidate, remove its extension card in Chrome. The preserved old folder is available if rollback is needed; 0.1.2 retains its known R02/Disconnect limitations, and 0.1.3 retains its narrow composer selector. No Buddy Windows reinstall or native-host registration is needed for this probe.

## Focus and document binding

0.1.2 runs `windows.getLastFocused()` in a background worker and rejects a false `focused` flag before tab lookup. R02 proves that predicate failed; it does not establish the exact live cause. 0.1.3 performs all orchestration in the genuine toolbar popup. It checks that `extension.getViews({type: 'popup'})` contains that exact window, that its packaged URL/document are unchanged, and that its own document is visible and focused.

The popup's current normal browser window must match the last-focused normal window. The one active, fully loaded ChatGPT tab is pinned by ID, window and exact URL. The parent window's focused flag can be false only while the authentic popup itself has focus. Parent focus alone never grants permission. Metadata binding and popup focus are rechecked around every awaited API call.

A first top-frame isolated script returns a nonce and Chrome supplies its document ID; it reads no DOM content. After fresh scope checks, one synchronous structural scan targets that exact document ID. It returns only five capped counts and two booleans. A result with the wrong document, frame, nonce or field schema is refused. Browser events cancel even during a pending final API response. This is a bounded sampled check, not an atomic guarantee against every OS scheduling race.

File and streaming selectors remain provisional. Version 0.1.4 recognizes a textarea or a contenteditable editing host when it has the legacy `prompt-textarea` ID, belongs to a form, or has `role="textbox"`. It uses the browser's `isContentEditable` state, including empty and plaintext-only attributes, instead of requiring the literal value `true`. Editable descendants are not counted again. Disabled/readonly fields, hidden/inert/ARIA-hidden ancestors and zero-sized rectangles are excluded.

These are **provisional editor candidates**, not verified ChatGPT composer identities. An unrelated editable form can still be a candidate. No candidate grants draft access. Zero means no supported candidate detected, not that the message box is absent; two or more means ambiguity. Shadow trees and subframes are not traversed. File inputs are controls, not uploaded files. The URL boolean recognizes `/c/UUID` and `/g/g-.../c/UUID`, including the project-prefixed shape observed in the user screenshot. It returns no URL, project slug or conversation ID. A recognized shape is not verified conversation identity; an unrecognized shape does not establish absence.

On October 9 the user reported a successful 0.1.3 observation and confirmed the composer was visibly present on the new-chat page. That establishes that this attempt passed the focus/structural path while missing its composer. The exact live markup had not yet been inspected at that checkpoint. The user subsequently returned a 0.1.4 observation with one editor candidate on an existing project conversation. That is live evidence of candidate detection on that page; the exact field identity and draft access remain unverified. Its role counts remained zero despite visible messages. Version 0.1.5 corrects the screenshot-supported scoped route mismatch and explains missing role markers. It does not change role selectors or enable history access. Repeating these counts cannot identify the correct message markup.

With the user's approval, the selected conversation was subsequently inspected through Chrome's Elements panel. The current page uses search-unit wrappers instead of the legacy author-role attribute. Version 0.1.6 supports the observed `fallback-turn-N:N:user` / `fallback-turn-N:N:assistant` key shape in `data-chatgpt-search-unit-key`, paired with the same `data-content-search-unit-key` on that wrapper or its first element child. Both conversation-root attributes (`data-chatgpt-conversation-selection-target="true"` and `data-thread-find-target="conversation"`) under `main` are required for this new path. Other key families are unsupported. Legacy role markers remain supported.

Nested aliases and duplicate keys within the same root count once; conflicting roles or overlapping identities are excluded. Only currently loaded DOM units count. Virtualized turns may be absent, so neither these counts nor a successful URL check establishes complete history. The probe does not read message text, role headings, private message IDs, drafts or account labels. The live inspection used a browser tool that could return rendered page text under the user's explicit approval; no private page content was copied into source or evidence. Inspection of the markup is distinct from executing the new extension on that page: live 0.1.6 acceptance is still pending.

The scan reads only structural attributes and layout visibility, with limits of 8,000 nodes and 100 ms. Reaching a limit refuses the observation instead of presenting partial totals. Chat text, input values, file lists/bytes, account labels, storage and cookies are not read. No listeners or controller state are left in the page.

## Validation and build

```text
node --test integrations/readiness-probe/tests/*.test.cjs
python scripts/package-readiness-probe.py
```

The Node suite covers the actual popup orchestrator using synthetic Chrome responses, cancellation/deadline/rejection at all 14 awaited boundaries, ownership and navigation, exact document targeting, late completions, capped structural fields and the no-content-read contract. These checks are not a real provider test. Existing 0.1.2 through 0.1.5 artifacts are preserved.

`tests/owned-dom.cjs` additionally runs actual DOM/layout checks in a fresh headless Chrome context. It requires an available `playwright` package and optionally `BUDDY_TEST_CHROME` pointing to Chrome. Run `node integrations/readiness-probe/tests/owned-dom.cjs`; dependencies are not installed by the script. It intercepts the synthetic ChatGPT-origin fixture before navigation with networking offline, never attaches to a user browser/profile, and verifies no content getters, input/submit events or DOM writes. Sixty owned cases pass: 28 editor/layout cases, 28 message-marker cases and four location-parser cases. Message cases cover the live-observed structure with invented content, aliases, duplicate virtualized units, conflicting roles/identities, unsupported scopes/keys and capped totals. Throwing getters also prohibit private message identifiers and labels. The preserved 0.1.5 source reproduces the observed-structure zero-role failure in the same fixture. This test is separate from the Node-only CI suite. `BUDDY_TEST_PROBE_SOURCE` can select a preserved probe for a regression comparison; its hash appears in successful receipts.

The HTML rules follow [MDN contenteditable](https://developer.mozilla.org/en-US/docs/Web/HTML/Reference/Global_attributes/contenteditable) and [isContentEditable](https://developer.mozilla.org/en-US/docs/Web/API/HTMLElement/isContentEditable). These references define browser semantics; they do not establish ChatGPT's current DOM.

Chrome API references: [current versus focused windows](https://developer.chrome.com/docs/extensions/reference/api/windows#the-current-window), [extension popup views](https://developer.chrome.com/docs/extensions/reference/api/extension#method-getViews), [popup lifetime](https://developer.chrome.com/docs/extensions/develop/ui/add-popup), and [document-targeted script injection](https://developer.chrome.com/docs/extensions/reference/api/scripting#type-InjectionTarget). The direct-popup design avoids a remote focus challenge, background session, or `runtime.getContexts` fallback.
