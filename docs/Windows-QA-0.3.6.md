# Windows QA corrections in 0.3.6

This local update addresses the owner's reported Amoeba research, Notepad Agent, misleading Guide completion, practice-window wording and taskbar identity problems. It retains the existing local-speech, capture/privacy, action approval, C/F3 voice and account-grant boundaries.

## Research, then grounded guidance

Pasted public HTTPS links are extracted deterministically from mixed prose, tables and escaped Markdown, including `https\://useamoeba.com/`. Every supplied address is validated before network activity. Supplied links bypass search; only discovered documentation links on the same supplied host are followed, with at most four fetched pages. Fragment-only variations do not consume separate fetches. Public DNS checks, redirect revalidation, no cookies/proxy/credentials, time limits, 500 KB response limits and text/HTML restrictions remain in effect.

Only pages actually fetched are cited as read. Video titles without URLs, pasted tables and search snippets are not represented as watched/read sources. Page instructions are untrusted data. Screen trees, screenshots and private references are not sent to search providers; local planning combines the request, fetched evidence and scoped app observation. Local pointing and creative writing do not require web search. A failed search allows explicitly labeled screen-only guidance.

Documentation informs a setup overview, not live coordinates. When controls are unobserved or the selected process differs from the requested Amoeba/Notepad app, Guide shows source-backed orientation and asks the user to focus the intended tool and confirm its version. It does not fabricate targets. Version numbers from documentation are not proof of the installed release. Full Guide review can include an overview plus a separate app/version handoff; ordinary voice/chat remain limited to three sentences.

Official Amoeba references used in actual bounded-fetch validation:

- https://useamoeba.com/docs/getting-started
- https://useamoeba.com/docs/agents/providers
- https://useamoeba.com/docs
- https://useamoeba.com/docs/help/troubleshooting
- https://useamoeba.com/docs/git/repositories
- https://useamoeba.com/changelog

The documented setup involves GitHub sign-in, organization selection, an intended local Git clone bound to a connected workspace, a configured local Claude Code/Codex CLI, coordination setup, and a small Mission Control session. That is reference information, not permission to authenticate, alter hooks or run a session. No Amoeba authentication/configuration was changed during this update. Its packaged executable/product metadata reports `1.130.0-beta`, which may be its underlying editor version; no mapping to the public 0.1.x release is claimed. Provider credentials are handled by the local CLI, but Amoeba coordination can send prompt-derived context to its Brain/Anthropic; it is not wholly local. Worktrees are not security sandboxes.

## Notepad writing and truthful Guide status

The exact request `Write a poem on a sailing boat in a lonely sea in the notepad` now generates a local draft and exposes the exact text before Run. It proposes a high-risk targeted insertion, with separate consequential approval. A fresh process identity, supported writable field, empty value and unchanged bounds are required; existing text is never replaced by this draft path. Readback must match. No Enter/send or file save is added. Missing, read-only, nonempty or stale targets stop the action. Completion of this path requires an actual successful insertion receipt and explicitly says no file was saved.

Known app aliases such as `Notepad.exe` canonicalize to the bounded allowlist. Unsupported model-generated launches produce a planning error rather than telling the user to provide an HTTPS URL. Arbitrary shells and arguments remain prohibited.

Guide refuses window titles as document text, unrelated Minimize/Maximize/Close steps, fabricated refs and mismatched names/roles. Requested words use bounded UIA text ranges inside the actual editor; unavailable or ambiguous ranges produce clarification. One-step teaching shares the title/chrome safeguards. Next means reviewed, Skip remains skipped, and neither proves success. Only independently observed step outcomes set the saved step-completion flag; even that does not claim the entire task is complete. Resuming an old guide replans from a fresh observation instead of reusing stale targets.

The practice screen is explicitly a simulation: sample text is not a command, pointing does not click, and its simulation button creates no file. A real save-demo was not added.

## Taskbar identity and evidence limits

Before this update, actual installed Buddy windows already exposed mint small/large icons, and all four installed shortcuts referenced the correct executable; no Buddy shortcut was found in the standard taskbar pin folder. Window AppUserModelID was absent. The precise cause of the screenshot's blank taskbar icon is therefore not proven to be a missing icon resource.

0.3.6 sets and reads back a stable process AppUserModelID, assigns it to native windows and the four installer-owned shortcuts, and retains distinct 16px/48px native icon handles. Native owned-window and shortcut tests verify these values and artwork. No global shell cache is cleared, no pins are altered and Explorer is not restarted. Actual taskbar pixels still need user observation.

Microsoft implementation references: https://learn.microsoft.com/en-us/windows/win32/shell/appids and https://learn.microsoft.com/en-us/windows/win32/properties/props-system-appusermodel-relaunchiconresource .

## Validation scope

The exact-input, source/redirect protection, empty-editor policy, completion and existing service/desktop suites pass. Actual local Gemma samples generate a poem and a source-backed Amoeba overview; actual public fetches read the official getting-started/provider pages. Native owned-window checks verify shell identity, icon sizes, practice wording, Next/Skip persistence and fresh resume behavior. Existing Settings, job approval/decline/Stop and single-companion checks pass.

The full native word-range/action-execution fixture is blocked before capture because Windows reports a secure desktop. This guard was not bypassed. The fixture exists for a later unlocked interactive run; no physical input, third-party GUI execution, acoustic or actual taskbar visual pass is claimed. Complete logs, installed provenance, backup/rollback verification and outstanding manual checks are in the installed-update report.
