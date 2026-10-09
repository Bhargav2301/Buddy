# Continuation62 — requested detail and browser contract review

The October 9 continuation handoff was reconciled with the canonical `C:/Projects/Buddy/repository` checkout, `feature/interactive-assistant`, draft PR1 and clean starting revision `415fe28ae3caf58c1b0ba4fd049af9045a43e63e`. Origin matched; both head CI runs 37882669728/37882666389 had passed. The environment worked. Historical snapshots, installed app, user settings/models and probe 0.1.6 were preserved.

## Implemented: explicitly requested longer local replies

Ordinary local chat previously rejected every answer over three sentences or 1,600 UTF-16 code units, even when the current request asked for detail. `ReplyConstraints` now recognizes explicit English detail phrases and sentence requests from one through twelve. A specific sentence bound takes precedence; a direct brevity request overrides a vague detail request. Quoted examples, code fences, inline code and block quotes do not change the budget. Common negated detail requests remain concise. This is a finite parser, not comprehensive language understanding; unsupported wording retains the default.

Detailed replies are bounded to 12 sentences / 6,000 UTF-16 code units, with a six-sentence target to discourage padding. Out-of-range explicit sentence requests return a clear error before inference. Ordinary replies retain three sentences /1,600 units, and one/two-sentence requests retain their limits. Only the current request sets the budget; previous detailed turns, memories and attached context cannot promote it.

The model receives one consistent length instruction. Over-budget answers are recomposed whole once, with measured draft length and all qualifications retained; repeated failure returns an honest fallback. No sentence or trailing caution is cut away. Detail is reviewed as a complete answer even when staged voice is enabled. Source-label and unsolicited Guide/Agent-promotion checks remain. Guide/Agent plans, staged speech, region research and optional cloud provider limits are unchanged. Very long answers remain visible on screen; the existing local speech limit of 1,600 characters still refuses oversized speech rather than truncating it.

Ollama's `done_reason=length` is now refused for every model path, including non-thinking models and hybrid output after its reasoning boundary. Buffered ordinary chat emits no completed answer and saves no conversation pair in that case. Stop also discards late detailed output. The request/Android stream schema is unchanged.

## Browser phase: exact remaining blocker

Source inspection confirms `integrations/browser/providers/chatgpt.js` has `identityVerified`, `draftVerified`, `historyVerified`, `attachmentsVerified` and `liveValidated` false. The provider registry is source-owned. `BrowserContextBroker` receives no admitted production profiles and refuses stronger operations with `identity_profile_unavailable`, even after pairing or forged identity evidence. The full adapter's old provisional selectors are not the standalone probe 0.1.6; the latter remains a separate, user-validated structural checkpoint.

The inspected page provides loaded message wrappers and a route, not an authenticated active account/workspace signal, a complete-history boundary across virtualized turns, or a correlated attachment-ready receipt. No stronger contract was established in this continuation. Existing DOM/host tests verify refusals, selected-context integrity, original bytes, one-use reviews, stale/cancelled operation handling and no Send; their positive identities/receipts are synthetic. No browser registration, expanded private-data inspection, upload or new unchanged probe run was attempted. Reviewed local/text context remains available. This blocker cannot be solved by relabelling counts as identity or collecting more unrelated private data.

## Validation and measured limitations

- Planning/reply service suite: 169 assertions pass, covering explicit detail, default/tighter bounds, quoted/negated requests, oversized requests, source/promotion rejection, complete correction, exact persistence, context/previous-turn isolation, token-budget refusal and Stop.
- Existing suites pass: Preview 45, Staged 89, SpeechFeedback 72, Teaching 46, RegionResearch 71, plus the core service suite. The speech checks are synthetic/owned, not a physical microphone or headphone acceptance.
- Browser checks pass: 231 Node cases; 69 broker assertions; 12 framing and 14 selected-context checks; 48 independent C# cases / 81 assertions. These counts identify separate modes, not live provider acceptance.
- Windows Release compilation passes with zero warnings/errors. A separate self-contained `Buddy-0.4.8-continuation62-review` build passes package/type/native-binary checks and its headless dependency/native OCR-load probe. OCR loading is not recognition-accuracy acceptance.
- Version and 83-task acyclic ledger checks pass. `PlanningRepair`, `Staged` and `SpeechFeedback` now run in the Windows CI package script. No destructive package-building script was run locally; the fresh review directory was published explicitly.
- Actual installed local `gemma3:4b`, loopback-only, four synthetic prompts, isolated encrypted temporary store: returned 3/6/12/1 sentences for default rainbow / six-sentence rainbow / detailed RAM-vs-SSD / one-sentence RAM-vs-SSD. All four fit their contracts. The tested service DLL matches the published service SHA-256 `66ce5e88e08e37c53867966a96241989567ace4a2cfbd4156ca124abf45edadd`.

The first detailed RAM test failed its limit twice and returned the fallback; raw synthetic response evidence showed excessive length. A lower target and explicit draft-length feedback improved length compliance without weakening the ceiling. The later accepted RAM explanation still supplied unrequested, unverified hardware latency figures and an unnecessary follow-up. Therefore four compliant lengths are **not** four factually validated answers or broad useful-assistant acceptance. No model-quality score or accuracy guarantee is claimed. Earlier failed outputs remain in local evidence.

An initial no-restore build lacked this test project's assets; restoring dependencies resolved it. An aggregate test launch incorrectly forwarded `--ignore-failed-sources` into the browser fixture's mode arguments; separate restore/run commands corrected that invocation. Failed and corrected logs are retained rather than recast as product failures. Root performed source/output review; no independent reviewer is claimed.

Local evidence, exact source/assembly hashes and package receipts live under `C:/Projects/Buddy/diagnostics/continuation-20261009`. No private user conversation/profile data or local receipts are committed. New CI must be read against its exact new revision.

## Delivery state and remaining work

| Item | Status |
| --- | --- |
| Canonical source | 0.4.8 continuation; checkpoint on existing feature branch/PR |
| Separate Windows review build | Built and dependency checked; not installed |
| Installed Buddy | Last recorded 0.4.5; unchanged by this work, not freshly inventoried |
| Browser | Probe detection accepted on one page; stronger provider contract unavailable |
| Answer detail | Implemented and bounded; broader factual accuracy still needs evaluation |
| Native MVP | Physical field/voice/monitor/Guide/Agent and grounding gates remain open |

The next independent quality work is grounding factual explanations and improving OCR/Guide usefulness with honest output evaluation. Named local-agent integration still needs a supported client/version/API. Calculator59 remains a separate unintegrated intake; installation and live Calculator tests remain on hold. No merge, release, native-host setup, account grant or live data transmission was performed. Version 0.5.0 remains incomplete.
