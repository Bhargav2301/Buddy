# Hotfix57 ownership

- APP-57 /root/app47: isolated workers/app57; exact host/package/child verification and adversarial fixtures.
- CORE57 /root/core53: private acceptance helper preparation only, fixed0.4.7 bindings supplied by root after sealing; no native execution.
- QA57 /root/qa43: read-only independent trust and child-binding review.
- Root: consumer integration, version/docs, final checks, exact-DLL read-only probe and sealing. Installation remains authorized but foreground requires coordination; publication remains blocked.

# Hotfix56 ownership

- REFINE-56 attempt1 /root/refine53: comparison grammar and meaning tests; accepted.
- APP-56 attempt1 /root/app47: fixed Calculator resolution and bounded task reports/source review; eight-file handoff accepted.
- /root/qa43: independent read-only trust/no-replay review; no blocking finding.
- Root: error-result UI, integrated checks, exact-source publication, fresh recovery snapshot and coordinated native/update gates. Followup55 remains paused. Private intake receipts remain outside Git under validation/hotfix56.

# Backlog and ownership

Only the orchestrator changes assignment/status columns. Specialists report completion with evidence.

| ID | Work and acceptance | Owner/task | Allowed write scope | Status/dependencies |
|---|---|---|---|---|
| DEV-01 | Establish actual team, file ownership, project memory, handoff and review gates | Orchestrator `/root` | AGENTS.md; docs/development; version/changelog; integration scripts | Validated: actual team and 12 coordination checks |
| SP-43 | Opt-in genuine sentence generation/playback overlap; default concise replies and complete guidance/action review; one endpoint across gaps; cancellation/no replay; focused tests | `/root/speech43`, attempt 1 | StagedConversation.cs; Models.cs; BuddyService.cs; LocalVoiceOutput.cs; SentencePlayback.cs; VoiceOverlayWindow.cs; new tests/Buddy.Staged.Tests | Accepted; 89 focused + 6 independent checks pass; real-model staging acceptance remains open |
| UX-43 | Optional compact pointer/activity presentation preserving mascot; 5/15/30-second ink lifetime with stale-input/focus safeguards; local provider setup/status; UI tests | `/root/desktop43`, attempt 1 | CompanionActivity.cs; CompanionFace.cs; CursorCompanionWindow.cs; DesktopPreferences.cs; MainWindow.cs; MainWindow.Preferences.cs; GuidanceOverlay.cs; VoiceTeaching.cs; DesktopAssistant.cs; new native ParityVisualChecks.cs | Accepted; 45 native parity checks pass; no source-field safety fallback |
| NET-43 | Bounded HTTP/realtime text transport components using injected mocked transport; no production network activation or credentials; focused tests | `/root/provider43`, attempt 1 | New ProviderTransports.cs and related new server files; new tests/Buddy.Provider.Tests | Accepted; 198 mock-only checks pass; no live routing |
| QA-43 | Independent review of streaming/privacy/route/stale-target boundaries; own integrated/native parity harness and regression run plan | `/root/qa43`, attempt 1 | New test files or runner scripts under tests/validation; IntegrationTests Program.cs dispatch only | Accepted; cancellation fixes verified in final root; 1,092 total application assertions pass |
| DOC-43 | Keep implemented/partial/missing/live-unverified comparison and new batch report accurate; inspect remaining per-app knowledge/editor gaps | `/root/docs43`, attempt 1 | docs/Clicky-comparison-0.4.md; docs/Windows-0.4.3.md | Accepted; root updated final evidence, failed model probe and remaining gaps |
| INT-43 | Integrate reviewed handoffs, version/report, final regression and isolated preview | Orchestrator `/root` | Reviewed source amendments; VERSION/manifest; README/CHANGELOG; reports | Source validated; package evidence in local final handoff; no install/publication |
| PUB-42 | Publish exact tested 0.4.2 plus reports, verify draft PR and CI | Orchestrator only | GitHub feature branch/PR after approval | BLOCKED: trusted direct approval required; no retry |
| ACCEPT | Physical mic/headphone, shortcut competition, actual Comet/Grok/Blender | User/assisted manual checks | No automated third-party UI workaround | Open; fixture evidence is insufficient |

## Shared contracts

- Desktop preference `StreamVoiceSentences` defaults false; `CompactPointerMode` defaults false; `InkLifetimeSeconds` defaults 15 and accepts only 5, 15 or 30.
- Staged speech is ordinary text-only voice, no screen/web/action pathway. It emits independently reviewed complete sentences while later sentences are generated, with at most three total. Report that this is staged model calls, not token-level output from one inference. Unsafe/unsupported preflight uses existing whole-answer behavior; after speech starts, failure stops without replaying or pretending completion.
- One audio endpoint and cancellation owner cover the entire streamed utterance, including silent generation gaps. No speaker fallback or device reacquisition between sentences.
- UI transport setup is a disconnected draft. It accepts no secrets, sends no network requests and must clearly state live activation is unavailable.
- QA owns IntegrationTests Program.cs dispatch additions. Desktop owns its new visual fixture; speech/provider specialists own isolated new test projects. Notify QA before builds.

Machine-readable assignment authority: [tasks.json](tasks.json). All attempts start from `dd87c798a5973277856261a3410b804073cd951b` in separate detached worktrees. `scripts/team-ledger.py validate` checks IDs, generations and dependency cycles; `verify-handoff` rejects stale owner/base/attempt, changed file hashes, out-of-scope paths and unmet dependency generations. It only verifies for review and grants neither integration nor publication authorization. The orchestrator copies only reviewed files, reconciles current integration changes first, and runs final-tree checks.


## Current 0.4.4 assignments

PLAN-44 owns server planning/Guide/reply policy, UI-44 owns desktop routing/presentation, REFINE-44 owns refinement and pure core, QA-45 owns independent boundary/live harnesses and native dispatch, DOC-44 owns source/platform/license and migration reports, and INT-45 owns final integration/version/artifacts. Read the exact task scopes in tasks.json. No worker commits/index/installation/publication. All handoffs need current build-input snapshots and root reruns.

Shared contracts: actionless clarification is a review result that never enables execution; ActionPolicy strict execution validation stays strict. Agree any added plan metadata between PLAN-44 and UI-44 before edits. REFINE-44 owns Models.cs changes; other owners must request changes there. Root retains three-sentence ordinary ceiling, explicit tighter reply limits, complete action/safety review, and existing source-field diff/Undo. Live model and native execution are serialized by root.

MINT-44: user-selected Night Mint 1.1 native theme; owner /root/desktop43, existing isolated desktop44 worktree, incremental ownership recorded in tasks.json. Root owns native rendering checks and all integration.

Final INT-45 source checks:1,563 application assertions plus26 real-model checks pass. MINT-44 accepted with70 native checks. Source sealing, separate preview and authorized recoverable installation are root-owned; final package/install receipts remain outside Git. PLAN-44,UI-44,REFINE-44,QA-45,DOC-44,MINT-44 are accepted. Physical/third-party acceptance stays open.

## Active 0.4.5 scope

Complete the local Buddy-draft refinement workflow: automatic/manual technique prerequisites and rationale; explicit destination counts/limits; removable reviewed pasted/file resources; accurate warnings/omission/conflict display; request-revision binding. Keep source-field quick refinement unchanged. Tasks REFINE-45, RESOURCE-45, UI-45, QA-46, DOC-45 and INT-46 are isolated and root-coordinated. No installed replacement or account/hook integration.

Advanced options for original external fields remain open. A compatible future flow can prepare a one-shot options snapshot before the shortcut, then freshly capture and verify the source. Editing options after capture must discard the session and require a new capture; it must not relax the focus, field identity, review or Undo checks.

After the tested batch, prioritize Guide access to imported knowledge, explicitly requested longer answers with the ordinary three-sentence default, local session infrastructure with mocks and no installed hooks/grants, and actual-model early speech.

## Active quality gates

REFINE-47, WINDOW-46, QA-48 and REG-46 handoffs are accepted and integrated. Root amendments and evidence are documented in Windows-quality46.md. Meaningful structure passes the original eight actual-model cases consistently in two rounds; 12/14 expanded cases improve. One expected contradiction clarification and one conditional similarity rejection remain separate outcomes. The conditional case is an unmet utility gate, not a successful rewrite.

INT-47 independent work is ready: 1,793 headless checks, 13 pure diagnostic checks, clean Windows compile and root output review. Actual cached UIA, native regression/preview and Comet Guide acceptance are blocked until the user unlocks Satonara and confirms readiness with an ordinary Comet tab visible. Do not bypass secure desktop. Root alone runs actual model/native tests. No new preview, installation or publication before the outstanding applicable gates and separate authorization.

## Feedback47 final noninteractive gate

UI-47, VOICE-47, LIFE-47, APP-47, QA-49, DOC-47, DISPATCH-48, RESEARCH-48, GUIDE-48 and PACKAGE-49 are integrated. INT-48 owns final preview/source receipts and the next announced foreground gate. Current scope, actual-model evidence and explicit remaining Coucou groups are in [Windows-feedback47](../Windows-feedback47.md). The earlier conditional and readonly-resolution failures are resolved; physical audio, actual app activation, UI/region/capture/tray acceptance, general model Guide and exact screenshot SSL reproduction remain distinct open items.
