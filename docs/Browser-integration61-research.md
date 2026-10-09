# Browser61: concrete ChatGPT browser integration

Research owner `/root/context_research60`; 2026-10-07. Research only: public primary documentation and Buddy source. No real browser/profile/account/chat access, installation, upload or submission. Existing ingestion source is unchanged. Read `Buddy-browser61/AGENTS.md` and current development records; the installation and live-operation holds still apply. Recommendations below are implementation contracts, not claims that ChatGPT DOM acceptance has been tested.

## Recommended implementation

Build a Manifest V3 extension, a small native messaging executable, a separate current-user Buddy browser broker, and one versioned ChatGPT site adapter. The extension's toolbar action binds one explicitly selected tab. Its service worker validates and routes narrow messages; an isolated-world top-frame content script reads the active conversation and performs reviewed draft/file operations. The native host transfers only admitted messages to Buddy, without a shell, arbitrary file reads or arbitrary network requests.

This is a real path to implementation: the adapter reads bounded role-labeled DOM turns, updates the actual composer, and assigns actual `File` objects to the actual conversation attachment input. It does not substitute an export queue, a bare path, OCR text or base64 in the prompt field. Exact site acceptance remains a distinct live gate.

Initial permissions should be `activeTab`, `scripting`, `nativeMessaging`; no persistent `host_permissions`, `tabs`, `history`, `cookies`, `debugger`, `webRequest`, clipboard or downloads permission. Use an explicit toolbar action, no automatic page scripts, top frame only and exact parsed origin `https://chatgpt.com`. Do not inherit the generic app-title allowlist. The temporary active-tab permission can survive same-origin navigation, so per-chat invalidation is still required. [Chrome activeTab](https://developer.chrome.com/docs/extensions/develop/concepts/activeTab).

Inject packaged code in `ISOLATED` world and require document-ID support. A minimum Chromium API level supporting document IDs (Chrome 106+) is a baseline, not a browser-brand certification; feature-probe required APIs. Avoid `MAIN` world, page globals and undocumented internal React/ProseMirror stores. [Chrome scripting](https://developer.chrome.com/docs/extensions/reference/api/scripting).

Use packaged scripts and a restrictive extension CSP. No external-message listener, web-accessible resource, `window.postMessage` bridge, remote code, eval or HTML rendering is needed. Untrusted source text is displayed as text. Content scripts remain less trusted than the worker and share a renderer with the page; data sent to them must be treated as disclosed to that page. This is why attachment consent must precede sending original bytes to the content script. [Chrome extension security](https://developer.chrome.com/docs/extensions/develop/security-privacy/stay-secure).

## Setup bundle for later review

Prepare these artifacts before requesting setup approval:

1. Hashed unpacked extension package with manifest, service worker, isolated adapter and extension-owned review page. Local development loading requires the browser's extension developer-mode workflow; do not silently change that setting.
2. Hashed native host executable and manifest with its absolute executable path and one exact reviewed extension origin. The extension ID must come from the built/loaded package, not a guessed constant. No wildcard origins.
3. Explicit per-user registration plan for the chosen browser, naming the single registry value and its previous state. Include a matching removal procedure that restores only that value if it still matches the installed artifact. Never alter unrelated extension policy or register every browser automatically.
4. A local pairing flow connecting the native host to a separate bounded browser broker. The host connects only to an explicit Buddy instance; no generic TCP listener or reuse of the execution-approval pipe. A transient pairing code/nonce stays in extension/host/Buddy contexts, never page DOM. Do not store chat/file payloads or pairing secrets in logs.
5. Stage 1 live scope: readiness and identity characterization only in one explicitly chosen ChatGPT tab. Read only the specifically approved origin/document/route and account/workspace identity evidence plus structural capability metadata; no transcript, draft text, selected file, attachment bytes or message submission. If setup is needed, its exact package and registration changes require their own concrete approval first.
6. Only after the required verified identity is demonstrably available, request a separate Stage 2 scope for one disposable test conversation, selected synthetic prior turns/draft and one synthetic TXT/PNG attachment, no Send. Stage 1 or installation approval does not authorize those reads or uploads.

Chrome uses a native-host JSON manifest, an exact extension-origin allowlist and a Windows HKCU/HKLM registration pointing to that manifest. The host receives length-prefixed UTF-8 JSON over stdin/stdout; host-to-extension messages are limited to 1 MB. `connectNative` maintains a port, whereas `sendNativeMessage` launches a host per message. Choose the connected port and close it on unbind. [Chrome native messaging](https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging?hl=en).

For Chrome, the frozen host name is `com.buddy.browser_context` and its single per-user key is `HKCU\Software\Google\Chrome\NativeMessagingHosts\com.buddy.browser_context`. For Edge, use its documented per-user `Software\Microsoft\Edge\NativeMessagingHosts` location, with the same host name and actual extension ID. Edge has registry fallback behavior; the installer must avoid accidentally selecting a stale host from another location. None of this was installed or inspected on the user's PC. [Edge native messaging](https://learn.microsoft.com/en-us/microsoft-edge/extensions/developer-guide/native-messaging).

Comet officially supports most Chrome extensions. That does not document every extension API or a Windows native-host registry path. Keep its transport capability unverified until an approved exact-browser handshake succeeds; do not guess a `Perplexity` registry key, overwrite Chrome registration as a workaround, or claim Comet's built-in assistant is the same as a web chat in a tab. [Comet extension support](https://www.perplexity.ai/help-center/comet/en/articles/11734716-extensions).

## Binding and evidence contract

Define a per-invocation binding containing a random connection epoch, extension origin, browser transport identity, tab ID, document ID, top-frame origin, adapter/version, canonical route, conversation identifier, DOM generation, composer identity, and separately qualified account/workspace identity evidence. User-facing browser/profile/account labels may help review but are not authenticated identity. Never use browser profile paths or account cookies as identifiers. **Production admission requires verified account/workspace identity. The currently researched DOM-only path has not established that evidence, so production history/draft/attachment admission stays unavailable.**

The worker derives sender tab/frame/document/origin from the browser-provided `MessageSender`; it must not accept those values merely because the content script included them in JSON. Require own extension ID, frame 0, nonopaque exact origin and matching admitted document. Browser-provided document lifecycle describes the connection's opening state, so recheck current state before action. [Chrome runtime sender metadata](https://developer.chrome.com/docs/extensions/reference/api/runtime#type-MessageSender).

The isolated adapter also rechecks `location`, its pinned DOM nodes, route and local generation before each operation and before producing a receipt. A bounded route poll plus scoped mutation observer can invalidate an active lease without adding `webNavigation` permission; perform synchronous final checks because polling alone cannot guarantee freshness. Invalidate on navigation/pagehide, origin or conversation change, account/workspace observation change, login/switcher overlay, composer replacement, background/visibility loss, broker disconnect, deadline or user Stop. Do not monkey-patch page history in MAIN world just to observe navigation.

ChatGPT supports switching accounts within one browser session while their chats/files/workspaces remain separate. Therefore a document ID, route or display name cannot authenticate the active account. Observed labels and fresh user confirmation do **not** meet this project's verified account/workspace requirement. Do not mark them verified, lower the requirement, or admit production operations from them. Stage 1 must characterize whether sufficiently strong exact identity evidence is available; the UI-only approach may remain technically unable to provide it. [OpenAI account switching](https://help.openai.com/en/articles/20001068-use-multiple-accounts-with-account-switching).

A regular conversation route can be one input to an observed chat identity; it does not resolve the account/workspace verification gap. Candidate patterns such as `/c/<opaque-id>` are development assumptions, not an official stable API. Reject unknown routes by default. New-chat and temporary-chat surfaces cannot reuse prior history; any future ephemeral mode needs the same required identity proof and its own accepted profile. Projects/custom GPT/shared-chat variants also need tested profiles. Temporary chats have distinct history/retention behavior; no retention exemption follows merely from a UI label. [OpenAI temporary chats](https://help.openai.com/en/articles/8914046-temporary-chat-in-chatgpt).

## ChatGPT adapter: implementable operations and honest limits

### Probe and history

Probe only the selected top-level page. Match the frozen capabilities `CaptureCompleteHistory`, `ReplaceDraft`, `TextUndo`, `StageOriginalFiles` and `ObserveReadyAttachments`; never one generic `supported=true`. Readiness is structural counts/booleans only. The host-owned provider profile fixes identity/history/composer/receipt evidence, original MIME types and capability revision; neither a page message nor a user label can create that profile. Production starts without an admitted ChatGPT profile. A feature's controls must all match the known structural profile uniquely.

No stable ChatGPT DOM selector or supported consumer-chat transcript API was found in the official documentation consulted. Candidate selectors such as `#prompt-textarea`, `[data-message-author-role]`, `[data-message-id]` or a conversation-turn test ID must be treated as hypotheses for one adapter version. Synthetic fixtures can test this profile's behavior, but cannot establish that current ChatGPT exposes those attributes. Do not broaden to the first textbox or arbitrary file input when a candidate fails.

For history, scope traversal to a unique conversation container and top-level turns; exclude sidebar/search, dialogs, composer, hidden alternate branches and tool detail panels. Parse only structurally explicit user/assistant roles. Keep order, observed turn identifiers, text hash and observed revision. Reject duplicate/conflicting roles, ambiguous nested matches, streaming/partial trailing pairs, changed text during capture, unknown branch state and budget overflow. Do not execute links, click history, scroll to load more or use private endpoints.

The frozen protocol requires a complete-history evidence signal and explicit first/last/earlier/later coverage. Already rendered pairs are not proof that the complete conversation is present. Do not admit `CaptureCompleteHistory` from a rendered subset, a momentarily stable last answer or absence of a Stop button. If exact complete-history evidence is unavailable, history capture stays unavailable; do not silently weaken it to partial coverage. Once a complete capture passes its 4 MiB/256-pair bounds, the root workspace can select an explicitly bounded subset for review. DOM text remains observed rendered text, not underlying original Markdown. Preserve code whitespace and role boundaries; never upgrade partial or truncated responses to complete pairs.

Sidebar search can span chats, projects and files in an account/workspace. It is not needed for current-conversation extraction and would exceed this scope. [OpenAI finding chats](https://help.openai.com/en/articles/10056348-finding-your-chats-projects-and-files-in-chatgpt).

### Draft replacement and Undo

Read the exact supported plain-text composer representation and freeze its digest with the binding. Review the complete final text and sources in Buddy or extension-owned UI. A one-use apply command contains expected original hash, final text/hash, context revision and expiry. Recheck scope and original text before writing. For a textarea use the native value setter and an input event; for contenteditable use a dedicated profile that preserves paragraph/newline semantics and yields to the site's own handling. Do not invoke submit, Enter, hidden keyboard shortcuts, clipboard paste, page application internals or a fallback arbitrary selector.

DOM-only contenteditable replacement might look correct while the application's internal editor state rejects or later replaces it. Require exact immediate and settled readback through the same adapter, unchanged scope, no validation/error state, and a user-visible final draft. If it reverts/transforms or cannot be represented without losing existing rich content, refuse or report uncertain. A readback proves the observed draft only, not future model input or submission. Record an Undo lease only after success, bound to exact current text, same scope and a short deadline; Undo restores only that draft and must not remove pre-existing attachments.

### Original attachment

Receive only explicitly reviewed TXT/MD/PNG/JPEG originals already retained by Buddy. Preserve basename, MIME, byte count and digest. Reconstruct a `File` from bounded decoded bytes, verify its content digest, create a `DataTransfer`, add that File, set the uniquely scoped attachment input's `files`, and dispatch the adapter's required input/change event. HTML explicitly supports assigning a FileList to a file input. Setting its string `value` to a local path is not a valid substitute. [HTML file input API](https://html.spec.whatwg.org/multipage/input.html#dom-input-files), [File constructor](https://www.w3.org/TR/FileAPI/#file-constructor).

This does not guarantee application acceptance: script-dispatched events have `isTrusted=false`, and a site can ignore them. Do not spoof trusted events, escalate to debugger/CDP, or trigger an OS file picker as a hidden fallback. [DOM event trust](https://dom.spec.whatwg.org/#dom-event-istrusted).

Before sending any bytes to the page, display original file metadata, preview, exact observed destination and an explicit warning that attaching can upload before Send. Require a separate attachment decision. Reject if scope changes before dispatch. After dispatch, observe only that operation's delta from the initial attachment set: a matching new card/control, an explicit profile-defined ready state or error, stable binding, and bounded completion time. Existing same-name cards, duplicate filenames, a button's presence, a FileList assignment, or lack of a spinner cannot by themselves establish successful attachment.

The strongest DOM-only receipt is normally `original passed to the selected input; matching attachment shown ready by the page`. It does not prove server byte identity or durable remote upload without a corresponding service acknowledgement. If no explicit completion state is available, return `pending/uncertain`, retain the exact attempted operation ID, and stop without retry. No automatic Send. A disconnect after dispatch is also uncertain, not a safely retriable failure. Removal from a draft cannot promise deletion of a remotely saved file.

ChatGPT upload availability and limits vary by account/workspace/model and current allowances. Keep Buddy's existing smaller local limits; do not infer availability from the upload button or plan name. TXT is documented, but current documentation does not establish every Markdown/MIME combination; capability checks and the actual error state must decide. Files may have separate Library retention, so clearing Buddy or deleting a draft is not an upload retraction. [OpenAI uploads](https://help.openai.com/en/articles/8555545-uploading-files-and-audio-to-chatgpt).

## Native and extension transport contract

Use the frozen v1 incoming wire kinds: `register`; `readiness.report`; `identity.bind`; `history.begin`, `history.chunk`, `history.end`; `draft.poll`, `draft.chunk.read`, `draft.state`; `undo.poll`, `undo.state`; `invalidate`; `disconnect`. These replace the earlier proposed operation names. Registration is unpaired and carries no account identifiers, content or files; transport-peer identity is host-owned. `HistoryDocument` carries complete pairs plus `originalDraft` and `composerId` captured under the same explicit user action and admitted identity, so Buddy never guesses an empty original. Reviewed draft/asset bytes are offered through the draft polling/chunk path; page state messages are receipts, not authority to create approval. No executable command, filesystem path, URL fetch, arbitrary DOM selector, JavaScript or provider key is accepted. The worker admits each direction only from its own UI or current bound content-script sender as appropriate.

Frozen v1 application caps: 65,536-byte serialized frames; 32,768-byte raw chunks; at most eight 2,000,000-byte original assets and 16,000,000 total original bytes; draft limits of 20,000 UTF-16 units and 65,536 UTF-8 bytes; history limits of 4 MiB and 256 pairs; four connections and maximum sequence 4,096. Pairing and review expire after two minutes, sessions after thirty minutes. The root context workspace selects a separately bounded subset. Keep ingestion format caps too, including 64 KiB TXT/MD. These are admission limits, not permission to read other chats or fill the entire budget. Enforce strict sequence/count/digest verification and expiry. Reject oversized length before allocation, malformed UTF-8/JSON, unknown or duplicate keys, replay and unexpected capabilities. The host writes framing only to stdout; sanitized diagnostics omit text, filenames, routes and payloads. A forged launch-origin argument alone proves no browser identity; explicit pairing/current-user IPC is not a defense against an already compromised same-user process.

Chrome extension messaging uses JSON serialization; do not expect ArrayBuffer/File objects to cross the worker/native boundary intact. Encode bounded byte chunks and validate after decoding. Content-script messages are untrusted inputs, not authority to fetch or execute. [Chrome message passing](https://developer.chrome.com/docs/extensions/develop/concepts/messaging).

Keep private payloads memory-only. Release/clear native and typed-array copies, revoke owned blob URLs and release File references when finished. JavaScript strings and browser-owned File objects cannot be securely zeroed by Buddy; do not promise complete memory erasure or control of the site's storage. Service workers can terminate and lose globals. Restart means a new connection epoch and fresh user binding, never resuming an unacknowledged mutation or replaying an upload. [Chrome service-worker lifecycle](https://developer.chrome.com/docs/extensions/develop/concepts/service-workers/lifecycle).

## Extension to other sites

Keep transport independent of `ChatGptAdapter`. Each future adapter owns an exact origin/route profile, conversation/role rules, plain-text serialization, file-control association and completion-state rules. Require the same state machine and adversarial fixture contract, then destination-specific live acceptance. Comet is a browser transport variant; Grok/Claude/Gemini/Perplexity are separate site adapters. Never enable another site because its composer or attach icon resembles ChatGPT.

## Hard failures and next gates

Refuse before effects for unverified account/workspace identity, wrong/opaque origin, subframe, unknown route, missing document support, unpaired broker, stale epoch/scope/review, ambiguous composer or roles, login/account overlay, nonvisible/unselected tab, changed original draft, unsupported MIME, exceeded budget or missing asset capability. After dispatch, distinguish failed, uncertain and observed-ready outcomes; never automatically replay. Stop ends local work but cannot recall bytes already passed to the page.

Implement and test the full broker/DOM operations on synthetic fixtures now. Then review the concrete extension/host/registration package for installation. The minimum live request is **Stage 1 readiness/identity characterization only**: one chosen browser/tab, tightly enumerated identity and structural observations, no transcripts/draft text/files/uploads. If it cannot establish the required verified account/workspace identity, production admission remains unavailable; do not use user confirmation as a substitute. Only after identity admission is established and separately scoped approval is obtained may Stage 2 exercise a disposable test conversation and synthetic draft/attachment without submission. A source build, fake upload card or generic protocol queue cannot substitute for these gates.


# Browser61 synthetic acceptance contract

Research recommendations only. No browser/native/DOM tests were run by this researcher. The product owners should exercise these through actual parser/adapter/transport code using owned HTML and in-memory byte fixtures. Preserve implementation, synthetic validation and live ChatGPT acceptance as separate statuses.

## Mechanism fixture

An owned HTML page should implement a real textarea or contenteditable composer, real file input, and deterministic local change handler. The handler reads `input.files[0].arrayBuffer()`, records exact MIME/name/size/hash in memory, then produces an explicit ready or failure state. It performs no fetch/XHR/WebSocket and never submits a form. This tests genuine FileList delivery rather than an adapter returning a fabricated receipt.

Exercise normal draft write/readback/Undo; exact multiline Unicode/code preservation; original TXT with BOM and PNG/JPEG bytes; ready/error/pending attachment outcomes; removal of only the operation-created attachment; and unchanged pre-existing attachments. Verify the actual DOM handler received the expected byte hash. A successful fixture still does not establish ChatGPT's current DOM acceptance.

## Scope and identity adversaries

- Same title/composer ID, different route/conversation; SPA pushState without document replacement.
- Same route with changed user/workspace observation; missing verified account/workspace evidence; current or stale user-confirmed labels presented as a substitute for verification. All must fail production identity admission.
- Same tab ID in a new epoch/document; back-forward restoration; document lifecycle no longer active.
- Wrong origin, lookalike domain, about:blank, data/blob origin and same-origin child iframe.
- Correct-looking sender fields forged in JSON while browser MessageSender differs.
- Composer removed/reinserted, two matching composers, iframe composer, login overlay, hidden/background tab.
- Popup or service worker reload between prepare and apply; host disconnect after asset dispatch; stale completion received afterward.

Each invalidation must clear reusable authority and prevent later callbacks from producing a current success. A post-dispatch interruption may be uncertain; it is not permission to replay.

## History adversaries

- Sidebar previews and a second conversation share the same role attributes; only the selected conversation container is eligible.
- Nested fake role attributes in message content; duplicate turn IDs; reordered turns; mixed tool/user roles; missing assistant partner.
- Assistant text mutates during capture; streaming final answer; stale hidden branch; regeneration replaces the same turn ID.
- Loaded subset lacks the frozen complete-history signal or has earlier/later gaps: capture admission fails. More than 256 pairs or 4 MiB blocks instead of truncation. Only an admitted complete capture may later yield an explicitly reviewed root-workspace subset.
- Code indentation, line breaks, Unicode combining sequences, emoji and hostile source instructions remain data.
- Source clear or chat change between history capture, local refinement and Apply invalidates the frozen payload.
- Never synthesize a completed pair from Apply, a draft readback, a task status, or a brief absence of activity.

## Original-asset adversaries

- Correct name with wrong bytes/hash, wrong MIME, wrong count, different BOM or newline bytes.
- File input accepts assignment but ignores synthetic change; handler reverts selection; all input/change handlers are absent.
- Existing same-name attachment; duplicate pending attachment; unrelated page error; same-size different bytes.
- Upload-like spinner disappears without an explicit ready state; ready card lacks operation association; declared success followed by removal/replacement.
- Unsupported extension/type, empty or oversized data, malformed base64, late chunk, duplicate chunk, chunk total overflow, wrong digest and cancelled assembly.
- Scope changes just before dispatch versus during the asynchronous acknowledgement. The latter stays uncertain if bytes were already handed over.
- Undo restores the exact draft only and never claims to delete a remote file. Stop frees Buddy-owned leases but cannot retract page-disclosed bytes.

## Transport adversaries

- Prefix length exceeds application cap before any large allocation; short/partial reads; zero/negative-as-unsigned length; invalid UTF-8; malformed/duplicate/unknown JSON keys.
- Unapproved extension origin, unpaired session, forged browser label, capability escalation, stale nonce, reordered sequence, expired one-use decision, repeated commit.
- Operation body tries to include a path, script, shell command, arbitrary selector or URL fetch.
- Worker is restarted or native port dies with a pending mutation: no background replay or silent reconnect-and-commit.
- Only extension-owned UI can approve disclosure/apply; untrusted page content cannot trigger it through an observation message.
- No transcript/file text, asset bytes, private URLs or pairing token appears in diagnostic output, disk cache, extension storage or evidence logs.

## Stage 1 readiness only, not part of this research

After exact package setup approval, request a fresh bounded readiness/identity characterization scope for one selected ChatGPT browser/tab. Enumerate only origin/document/route, structural capability metadata and specifically approved account/workspace identity evidence. No transcript, draft text, files, attachment bytes, upload or submission. Determine whether the required verified identity can actually be established. Observed labels/user confirmation are insufficient; a DOM-only adapter may remain technically unable to meet the requirement. Keep production admission unavailable if so.

## Separate Stage 2 test conversation, also not run

Only after verified identity admission succeeds and the user separately approves this scope: inspect the designated disposable conversation's synthetic prior turns, exercise synthetic draft acceptance/readback/Undo, and perform one explicitly approved synthetic original attachment without Send. Record exact observed states and byte hash before delivery; do not claim remote digest verification without a service receipt. Never read other history, broaden permissions or upload user files to repair a failed capability check.
