# Readiness probe 0.1.5 — scoped conversation routes

The user reported probe 0.1.4 with one visible editor candidate on an existing conversation. Role/user/assistant counts remain zero; file input count is 2+; streaming and conversation-route booleans are false. The screenshot shows visible messages and a `/g/g-p-.../c/UUID` address. Record only this generic shape and the structural counts; do not copy private chat text, project names, exact URL or IDs into source/evidence.

The result establishes candidate detection on this page, with no R02 refusal in this attempt. It does not establish exact composer identity, draft access, complete history or attachment support. The zero role counts demonstrate that the existing marker scan does not recognize the visible conversation. The screenshot cannot reveal the required DOM attributes.

## Focused change

PROBE-015 is owned by `/root` from `2d4d605543ad92631dc3ac9894d77eaea50dcf6a`. Version 0.1.5 adds `/g/g-.../c/UUID` alongside `/c/UUID` to the existing shape check. Project and GPT home routes, malformed UUIDs, unsupported prefixes and extra suffixes stay false. Strict origin/document binding remains independent of this boolean. The same seven structural fields are returned; no URL/slug/ID is exposed. The UI now explains that missing supported role markers can coexist with visible messages.

Composer and message-role selectors, permissions, popup ownership, exact tab/document binding, bounded traversal, cancellation and unavailable provider capabilities are otherwise unchanged. No guessed message selectors were added. Root Windows version remains 0.4.8; no app/extension/native host is installed by this work.

## Evidence

- 230 Node cases pass: 123 probe cases plus 107 existing browser cases. Twenty added cases cover scoped/plain routes, path rejection, query/fragment handling, origin refusal, non-disclosure and zero-role interpretation.
- 32 actual owned offline Chrome cases pass: previous 28 editor/layout/privacy cases plus four real location-parser checks. A synthetic visible message without provider markers still yields zero roles; the route boolean does not create a history capability.
- Running the same owned fixture against preserved 0.1.4 reproduces the expected project-route false negative. Chrome contexts are fresh/offline with fixture navigation intercepted; no live profile/tab or provider content is accessed.
- The previous 0.1.4 commit's [PR CI](https://github.com/Bhargav2301/Buddy/actions/runs/37877375423) and [push CI](https://github.com/Bhargav2301/Buddy/actions/runs/37877371589) both completed successfully: version, browser, Windows and Android. These runs are evidence for that earlier commit, not the new candidate.

Local receipts are in `C:/Projects/Buddy/diagnostics/readiness-015`. Review is by the integration owner; no independent review is claimed. Package provenance binds the actual tested script hash. Prior package folders are preserved.

## Remaining work

The next integration gap is establishing message-container structural evidence, followed by exact provider identity and history coverage. Repeated runs of the same seven-field probe cannot reveal which message attributes are missing. This route repair does not resolve the role counts or enable account/history/draft/file capabilities. Live 0.1.5 behavior remains untested.

A separate fresh 0.1.5 package is available through the [probe instructions](../integrations/readiness-probe/README.md). It is not necessary to repeat a live check merely to confirm the known zero-role limitation. The [0.1.4 report](Browser-readiness-0.1.4.md) retains the composer implementation evidence.

## Follow-up

The subsequently approved live structural inspection established a replacement message-marker path. See [0.1.6 implementation and evidence](Browser-readiness-0.1.6.md); this report retains the earlier route-only checkpoint.
