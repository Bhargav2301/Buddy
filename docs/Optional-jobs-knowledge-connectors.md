# Optional jobs, app knowledge and provider connections

These are optional capabilities beside Buddy's read-only one-step teaching core. They do not turn spoken guidance into automatic mouse or keyboard control.

## Local jobs

Open Settings > Add-ons > Local jobs and app knowledge. Say “Start an agent to …” to prepare an action job, or “Search my app notes …” to search references for the selected app. Typed entry is also available. Agent mode must be enabled separately before an action plan can be prepared.

Only one job is active at a time. Jobs show planning, approval, running, verification and terminal status, with observed results. There are at most 25 action receipts and three replans; the latest 20 jobs are stored encrypted locally. Run this plan approves the reviewed plan, and consequential steps still pause for Allow or Decline. Stop/Escape cancel further work. A dispatched action cannot always be undone; cancelled or uncertain results explicitly require inspection. A cancelled plan cannot be restarted with its old Run button.

Completion requires successful recorded results and verification. Failed or ambiguous actions do not become a successful job merely because the model says “done.” Interrupted jobs are marked for review after restart and never replayed. Clearing job history requires stopping active work first. Core guidance still executes no actions; no unrestricted shell or terminal feature is added.

The saved-state format advances to version 3 so older builds refuse to silently discard job/knowledge data. Existing fields are preserved during migration. Reverting to 0.3.4 therefore requires the saved-data baseline as well as the old application; the prepared rollback helper preserves the current profile separately before restoring the baseline. Do not use a binary-only downgrade with the newer profile.

## Versioned local app references

Import a folder you explicitly choose, and label it with the exact process name and the app version covered by the notes. Import reads only visible `.md` and `.txt` files directly inside that folder: no subfolders, hidden/system files, linked paths or background watcher. Limits are 40 files, 64 KB each and 512 KB total, with strict UTF-8 decoding. The original files are never modified.

Copies are encrypted in Buddy's local state. Inspect sources shows the original folder, import time, app version, filenames and content hashes. Reimport replaces that source's saved snapshot and generates a new content revision when its files change. Remove deletes the imported copy, leaving the original files alone. Automatic version detection is not claimed; the app-version label is supplied by the user.

Search returns up to five matching excerpts with filename, line, version and revision. Teaching may use matching references only as untrusted historical context through the existing local model. Current observed controls take precedence; text inside a note cannot become an executable instruction. Removing or replacing a reference during inference prevents presentation of an answer based on the old snapshot. References are displayed separately from concise speech. No private reference or screen context becomes a web search query.

A note mentioning FL Studio or Claude Code does not establish that every control, terminal state or app version can be observed reliably. Current UI grounding, confirmation and capture restrictions still apply.

## Draw, then speak

Region selection still defaults to microphone-off question entry. The separate “Listen after I finish drawing an area” setting enables a staged flow: hold the area shortcut, drag around a region, release, then speak one local utterance. Listening begins only after a valid selection and ends on pause, Finish, Stop, Escape, focus change or the 30-second cap. Uncertain words still require review. It does not record simultaneous speech while the user is drawing. Physical microphone and competing-shortcut acceptance remain manual.

## Connector release boundary

**This build cannot grant account access.** Connect controls are disabled and the production account entry point rejects before a browser, callback listener or provider request starts. No credentials are bundled, imported from another app, or collected by this UI. Real external tasks remain unavailable.

Implemented engineering components are: Google PKCE and state/expiry/replay validation; a bounded random-port loopback receiver; provider token exchange/refresh/revoke; encrypted token storage; consent/connected/denied/reconnect/revocation-pending lifecycle; cancellation; narrow read adapters; and error/size/time limits. These were verified with mock provider HTTP plus an owned loopback fixture. This is not evidence that real account onboarding passed.

Failed revocation disables saved tokens before contacting the provider and retains an explicit unconfirmed status across restart. Provider-confirmed revocation removes local tokens. Diagnostic strings redact tokens/client secrets; protocol errors omit response bodies and inner transport details. Production HTTP disables automatic redirects and cookies. No send, draft, edit, arbitrary URL or generic external-tool execution surface is present.

### Gmail decision and registration required

The proposed capability is **message metadata only**: `https://www.googleapis.com/auth/gmail.metadata`. The read adapter returns at most five recent message headers (Subject, From, Date); it does not fetch bodies/attachments or use the unsupported `q` query parameter. The user must specifically approve this scope. An owner-managed Google Cloud project needs Gmail API enabled, consent configuration/test-user or publishing requirements satisfied, and a **Desktop app OAuth client**. No project, billing option, verification submission or credential was created during implementation.

The flow uses the system browser and a random `127.0.0.1` callback port; no deprecated copy/paste authorization or firewall/URL-ACL change is required. Google can impose verification requirements on restricted scopes. Its revocation endpoint can remove all scopes granted to the project, so a dedicated Buddy project avoids coupling unrelated integrations. Sources: [Google native OAuth](https://developers.google.com/identity/protocols/oauth2/native-app), [Gmail scopes](https://developers.google.com/workspace/gmail/api/auth/scopes), [message listing](https://developers.google.com/workspace/gmail/api/reference/rest/v1/users.messages/list).

### Notion decision and registration required

The proposed capability searches titles of at most five pages explicitly shared with the connection. Public Notion OAuth requires an owner-registered connection restricted to **Read content**, a registered HTTPS callback, and an approved confidential broker to keep the shared client secret out of distributed desktop files. The protocol components can run at that broker, but no broker has been hosted or connected; its hosting, trust and deployment are a separate user decision. No secret is requested until that design and access are approved.

The provider page picker controls which pages are shared. The adapter uses API version `2026-03-11` and exposes title search only; it does not fetch page bodies or edit content. Notion does not return the same granular scope list as Google, so registration capabilities must be reviewed separately; a successful read probe does not prove the underlying integration lacks other permissions. Sources: [Notion authorization](https://developers.notion.com/guides/get-started/authorization), [title search](https://developers.notion.com/reference/post-search), [token refresh](https://developers.notion.com/reference/refresh-a-token), [revocation](https://developers.notion.com/reference/revoke-token).

Until these provider-specific decisions, registrations and explicit authorization are complete, the installed UI continues to say not connected. Mock success never appears as a real account result.
