# Buddy readiness probe 0.1.3

This standalone Chrome extension repairs the readiness-only popup path. It is separate from the full browser/native-host adapter in `integrations/browser`. Its permissions remain `activeTab` and `scripting`. There is no service worker, host permission, native messaging, account integration, upload or Send operation.

## Use the updated probe

1. Extract the 0.1.3 ZIP into a new folder, or use the already extracted 0.1.3 folder supplied with this build.
2. Open `chrome://extensions`. Disable the old Buddy readiness probe 0.1.2 so its toolbar button is not confused with the new one. Keep its folder for recovery.
3. Choose **Load unpacked** and select the new folder containing this `manifest.json`. Its extension card and popup must both show **0.1.3**. Reloading the old 0.1.2 folder does not update it.
4. Open the intended ChatGPT tab. Open the new probe from Chrome's toolbar, click **Check this tab's readiness** once and keep the popup open. The check is read-only; it does not send a message.
5. Report the seven structural fields or the displayed diagnostic code. A successful observation is not proof of verified account identity, complete history or working attachments.

Disconnect cancels the attempt immediately. It prevents subsequent API dispatch and discards late results. A structural read already handed to Chrome can still finish; that work cannot be recalled. No result is persisted. Closing or blurring the popup, navigation, a tab/window change or the ten-second deadline also cancels the attempt. A new check requires an explicit click; there is no automatic retry.

To remove this candidate, remove its extension card in Chrome. The preserved old folder is available if rollback is needed; it retains the known R02/Disconnect limitations. No Buddy Windows reinstall or native-host registration is needed for this probe.

## Focus and document binding

0.1.2 runs `windows.getLastFocused()` in a background worker and rejects a false `focused` flag before tab lookup. R02 proves that predicate failed; it does not establish the exact live cause. 0.1.3 performs all orchestration in the genuine toolbar popup. It checks that `extension.getViews({type: 'popup'})` contains that exact window, that its packaged URL/document are unchanged, and that its own document is visible and focused.

The popup's current normal browser window must match the last-focused normal window. The one active, fully loaded ChatGPT tab is pinned by ID, window and exact URL. The parent window's focused flag can be false only while the authentic popup itself has focus. Parent focus alone never grants permission. Metadata binding and popup focus are rechecked around every awaited API call.

A first top-frame isolated script returns a nonce and Chrome supplies its document ID; it reads no DOM content. After fresh scope checks, one synchronous structural scan targets that exact document ID. It returns only five capped counts and two booleans. A result with the wrong document, frame, nonce or field schema is refused. Browser events cancel even during a pending final API response. This is a bounded sampled check, not an atomic guarantee against every OS scheduling race.

The scan uses the existing provisional role/file/streaming selectors plus the legacy textarea composer form. It reads only structural attributes and layout visibility, with limits of 8,000 nodes and 100 ms. Reaching a limit refuses the observation instead of presenting partial totals. Chat text, input values, file lists/bytes, account labels, storage and cookies are not read. No listeners or controller state are left in the page.

## Validation and build

```text
node --test integrations/readiness-probe/tests/*.test.cjs
python scripts/package-readiness-probe.py
```

The Node suite covers the actual popup orchestrator using synthetic Chrome responses, cancellation/deadline/rejection at all 14 awaited boundaries, ownership and navigation, exact document targeting, late completions, capped structural fields and the no-content-read contract. These checks are not live Chrome popup acceptance or a real provider test. Existing 0.1.2 artifacts are preserved.

Chrome API references: [current versus focused windows](https://developer.chrome.com/docs/extensions/reference/api/windows#the-current-window), [extension popup views](https://developer.chrome.com/docs/extensions/reference/api/extension#method-getViews), [popup lifetime](https://developer.chrome.com/docs/extensions/develop/ui/add-popup), and [document-targeted script injection](https://developer.chrome.com/docs/extensions/reference/api/scripting#type-InjectionTarget). The direct-popup design avoids a remote focus challenge, background session, or `runtime.getContexts` fallback.
