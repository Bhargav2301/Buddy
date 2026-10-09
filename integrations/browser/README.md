# Buddy browser adapter preview

This is an **uninstalled MV3 implementation with synthetic validation**, not a claim of working live ChatGPT attachment/history support. It has no Send, submit, Enter, clipboard, navigation, remote-code, telemetry or page-network implementation.

The production registry contains only `https://chatgpt.com`. Its explicitly provisional `chatgpt-dom-v1-unverified` profile can characterize structural readiness. Account/workspace identity, complete-history coverage and correlated upload completion have not been characterized on a real account. Consequently all content capture, draft changes, Undo and asset operations refuse in production, including direct calls to the driver. Profile labels, filenames, the presence of a file input and absence of a spinner are not proof. No undocumented session endpoint, cookies, storage, page scripts or tokens are read.

The same DOM driver is implemented for admitted profiles: exact composer read/replacement/readback; one-use text Undo only while composer, account, workspace, chat, draft and attachment set remain unchanged; full alternating user/assistant pairs with explicit completion and coverage proof; original byte/hash validation and `File`/`DataTransfer` selection; and separately correlated visible completion receipts. File selection dispatches `input` and `change` and may initiate a provider upload before Send. Unknown/pending/error/removed uploads cannot become success. The wait is bounded to 100 observations and ten seconds. No rollback or attachment removal is automatic.

## Current browser interaction

The popup offers a read-only readiness check, visible pairing with Buddy, explicit capture, polling a reviewed draft, explicit text Undo, and disconnect. Readiness returns only counts/booleans/fixed statuses; it does not return prompt/history/file text, page title, account labels or tokens. Pairing starts only the fixed native host `com.buddy.browser_context`, which must be separately registered and authorized by the Windows integration owner. This folder does not install or register it.

Only `activeTab`, `scripting`, and `nativeMessaging` permissions are requested. The explicit extension click supplies temporary tab access. There are no host permissions, automatic content scripts, `externally_connectable`, web-accessible modules, storage permissions, or universal provider registrations. Source-reviewed providers may be added to `providers/registry.js`; pages cannot register a provider or turn on capabilities through attributes, query strings, localStorage or a file-input heuristic. Tests' admitted synthetic profile is never loaded by the extension. Comet and other Chromium-compatible browsers require their own native messaging/setup validation; this is not a compatibility claim.

The background pins the browser-provided top-frame document ID, tab and browser window plus a per-document generation. Every content request checks the same focused browser window and its active tab before and after dispatch. Navigation, removal, another tab/window, native disconnect and mid-operation focus loss invalidate authority. Idle focus may move to Buddy for review; a later operation must return to the same original foreground window/tab. The content side rechecks account/workspace/chat and composer identity and exact captured turn data during staging. A failure after a consumed review is reported conservatively as possible partial effects.

## Wire v1 and cancellation

The companion `BrowserContext*.cs` broker owns pairing, admission, immutable reviews and host cancellation. Registration contains no content. Subsequent envelopes carry version, connection ID, nonce, strictly increasing sequence, kind, complete destination binding and typed payload. The native frame cap is 65,536 UTF-8 bytes. Chunks contain at most 32,768 decoded bytes and must match exact order, length and SHA-256. Empty draft/Undo originals use one explicit empty index-zero chunk. Drafts are capped at 20,000 UTF-16 code units and 65,536 UTF-8 bytes; history at 4 MiB and 256 pairs; eight original assets at 2,000,000 bytes each and 16,000,000 bytes total.

`draft.poll` consumes a host-approved review once. No assets/text can be substituted after that grant. Before DOM changes the extension obtains another `draft.state: pending` acknowledgement. This is a freshness check, not a synchronous cancellation guarantee: the Windows owner must terminate the corresponding pipe/native peer on Stop, context invalidation, review edit or rejection. Native-port disconnect invalidates content immediately and pending work settles normally. A completed input/file assignment cannot be promised undone by cancellation. The extension has no force-stop/retry loop. State and retained data live only in memory, never browser storage; worker restart loses the session and requires a new pairing.

`undo.poll` consumes a separate host request. The exact original is fetched/hashed again, the retained original and staged receipt must match, and only text is restored. Attachments must remain exactly as confirmed and are not deleted. There is no message transmission operation in the wire contract or extension.

## Validation

Run with Node 22 or newer, no npm install/dependencies:

```text
node --test integrations/browser/tests/core.test.cjs integrations/browser/tests/bridge.test.cjs
```

The tests use a synthetic tree DOM, real local `File` byte blobs, a `DataTransfer` fixture and a fake native port. They test the actual shared driver/controller/bridge source. They do not access a real account, browser profile, history or provider. Independently owned Chromium DOM tests can exercise the same driver with synthetic provider evidence on an owned local page. Neither type of test establishes live ChatGPT identity, upload success or model access to files.

The remaining smallest live step is a separately authorized, per-click readiness characterization on a user-selected ChatGPT tab. That mode reads only structural status, not history, prompt text, file names/content, tokens or account labels. Enabling a live identity/history/upload profile requires new reviewed provider-specific evidence and fresh independent validation; this implementation never silently enables it.

## Primary API references

- [Chrome activeTab](https://developer.chrome.com/docs/extensions/develop/concepts/activeTab): explicit invocation and temporary origin access.
- [Chrome scripting](https://developer.chrome.com/docs/extensions/reference/api/scripting): isolated-world injection and browser-generated document IDs.
- [Chrome native messaging](https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging): framed JSON and the native-host output limit; this protocol deliberately uses a smaller 64 KiB cap.
- [Chrome windows](https://developer.chrome.com/docs/extensions/reference/api/windows): current window differs from focused window; use focused identity explicitly.
- [OpenAI account switching](https://help.openai.com/en/articles/20001068-use-multiple-accounts-with-account-switching): active accounts/workspaces stay separate. This is not a DOM identity API.
- [OpenAI file uploads](https://help.openai.com/en/articles/8555545-uploading-files-and-audio-to-chatgpt): provider file capabilities/limits do not define an extension upload-completion API.
