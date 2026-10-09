# Probe0.1.4 and user observation — October 9

User-reported real0.1.3 check: editors0, roles0, users0, assistants0, file inputs2+, streamingfalse, conversationroutefalse. User confirmed the new-chat message box was visible. This run passed its structural path; it did not recognize the editor. No assistant live DOM inspection occurred.

Candidate0.1.4:210/210 synthetic Node cases pass, plus28/28 actual owned offline Chrome DOM cases. The latter uses fresh headless context, intercepted fixture HTML, actual layout/editability and throwing private-content getters; zero content reads, input/submit events and DOM writes. This is isolated native-browser evidence, not a live provider check. Root integration review only. Exact receipts/source hashes are local under `diagnostics/readiness-014`; [report and limits](../Browser-readiness-0.1.4.md).

# Probe0.1.3 evidence — October 9

Preserved0.1.2 synthetic reproduction: false window focus yieldsR02 before injection; Disconnect during the first pending window call still permits later injection/read and session recreation. Source hashes and reproduction receipt are local in `diagnostics/readiness-013/legacy-reproduction.json`.

Final combined Node run:207/207 cases pass (100 new probe +107 existing browser). Cancellation/expiry/rejection at all14 awaited boundaries, immutable popup/window/tab/document scope, navigation and late results, structural privacy limits and popup Stop UI are covered. Initial96/97 failure identified an unhandled late window-focus event; production now subscribes to it and final coverage passes. These are synthetic fixtures, not live Chrome or provider acceptance.

Package integrity is bound through the whitelist builder and SHA256SUMS. No browser profile, live DOM, installed app, native registry, account or message was touched. Full scope/limits: [readiness repair report](../Browser-readiness-0.1.3.md).

# October 9 source synchronization evidence

Exact handoff archive SHA-256: `a0f80c8587efb1d3f54ab413082a82f7b6ee1c2107779586cddaae332c70a5f1`. All 680 entries match the original live browser61 source. Imported commit `9df490c` matches all entries with Git-only newline normalization in 88 text files. All three unpublished ancestor commits and the complete source/staged payload passed the bounded runtime/recognizable-secret scan.

Fresh checks: Windows Release and browser-host builds, both zero warnings/errors; 26 selected .NET suite invocations, including 72 context integration assertions and 56 unshown WPF checks; 107 JavaScript cases. Source version check reports 0.4.8 / Windows 0.4.8.0 / Android code 4009. Work ledger validates 82 tasks and acyclic dependencies.

Initial framework-dependent test launches failed because the installed ASP.NET runtime is 10, while tests target 8. Cached self-contained .NET 8 reruns pass; no system runtime was installed. The context generator then exposed its outdated callback terminator; a one-line fixture update fixes extraction of unchanged production logic. An attempted explicit RID server path was absent; the successful fixture binds the actual freshly built net8.0 server DLL. All failed attempts and successful reruns remain in local `diagnostics/sync-20261009`, outside Git.

[The synchronization report](../Source-sync-2026-10-09.md) lists command scopes and limitations. No fresh actual-model, Android, live-provider, original-upload, physical voice, foreground or installed-upgrade acceptance is claimed.

# Browser61 and refinement61 evidence - 2026-10-07

Exact packaged Server bc36d2ac614513dbcebb2e2f8a07bbc2951aa5b7c912919e7375ef2cef75ec8b: all18 synthetic local-model calls settled. Frozen original12:10 faithful useful changes/2 truthful unchanged/0 unsafe accepted observed; prior8 useful outputs unchanged. Six builder-authored additional cases pass independently as supporting evidence, not unseen heldouts. Root affected refinement checks:7 builds/8 runs,1,501 assertions; allpass,0warnings/errors. See private validation/refinement61.

Final browser source:107JS cases,69broker+27wire-fixture assertions,48 independentC#cases/81assertions,12framing+14selectedcontext+15actualownedhost checks,56unshownWPF checks pass. Actual JS-to-C# wire passes normal/emptydraft cases with child settlement and exact originalbytes/stage/Undo/noSend. Actual isolatedChromeDOM17checks pass against final driver. All identity/history-completion/attachment receipts are synthetic; no live provider account or upload acceptance. Unrelated final refinement source change does not relabel these as exact-final-DLL browser tests; unchanged browser source/nativehost bytes are separately bound.

Preview quiet package/dependency/native-OCR-load and branding pass. WindowsPowerShell preparation-only checks for Chrome/Edge pass with syntheticID, BOM-freeJSON, exactorigin/hosthash; no registry read/write/policycopy. Preserve wrongunshownDLL launcher failure, setupcmdlet compatibility failure, and reproduced/refused semicolon ambiguity. OCR ALPHA123->ALPHA125 remains unresolved.

Fresh hashes match installed service0.4.5, all626sealedsource60 files, source60archive, two sealedpreview60assemblies and sixdisabledCalculator59diagnostic files. No wholeinstalled/settings manifest claim. Final source/archive/package/input seals in validation/browser61; [report](../Windows-browser61.md). No install, browsergrant, realhistory/upload, physicalaudio, liveCalculator or publication.

# Refinement60 evidence - 2026-10-07

Final focused batch: 12 clean builds and 13 successful runs, including pure/injected refinement, context, source reader and independent boundary/regression suites. The changed unshown WPF suite subsequently passed49 checks; its last added capacity-refusal regression preserves source errors without showing a window. The changed reader passed27 actual owned TXT/MD/PNG/JPEG checks. Static package/dependency/OCR-load and branding checks pass. No visible field, audio, microphone, physical headphone or Calculator acceptance occurred.

Independent frozen12 integrated output review: eight faithful useful improvements, four unchanged, zero unsafe accepted rewrites; numbers/units and Unicode-name utility remain unmet. This lane binds service de8634750b1d79c108ccdf06bba4cd041cbd030d12fc77f0b2975ac76bdfe8a9. Four final packaged context cases pass independent review: three context assemblies and one bounded grammar repair with complete context. Final Server9538649bdda8bffc49df5c101ba53b6adbc6962794b87905a39429de1e5eda72; App9fd07657ed9658ddcd7ad162f05744f5e4319a301c15fb074b4de7af8bd7fcaf.

OCR failure is preserved: synthetic ALPHA123 recognized as ALPHA125. Native byte-reader success is distinct from recognition accuracy. Exact final corpus/input/output and synthetic one-use payload hashes were independently reviewed; no real attachment/history delivery is claimed.

Private validation/refinement60 contains final-checks-v2/receipts.json, final-unshown-observation.json, ingestion-owned-assets-v3/receipt.json, ingestion-owned-fixtures-v2/ocr-result.json, tester/INTEGRATED-REVIEW and PACKAGED-CONTEXT-REVIEW, context-final-model-results.json, package-checks.json, final-source-intake/receipts.json and FINAL-OUTCOME.json. Earlier failed commands remain intact. The final outcome binds the source archive and package manifest. No private settings or recovery archives enter Git.

# Version 0.4.8 package evidence

Final59-command regression passes with build snapshot5e1a33d557e42857c853305e9ed12dc4e81634797478ae309e6394d74428f074. Static package validation and exact packaged --check-package pass; no desktop startup. Packaged Server comparison refinement accepted in18.505seconds, similarity0.9005715024022094, no profile/field writes; current credentials were not decoded. Private package/provenance/helper timing records are under validation/hotfix58. Native acceptance and installed replacement remain pending.

# Calculator activation58 evidence

User observation: Calculator opened minimized during the prior failed run. New isolated repair: AppBinding432 mock/pure checks, ComputerUse132 mock/owned-file checks, RoutineLaunchReport82 pure checks and UserFailureBoundary56 mocked cases pass. Four test builds and Windows Release compile: zero warnings/errors. Native interop compiled but was not invoked. app47 policy handoff is four fixed hashes; qa43 reviewed root native/target/policy and exact-message privacy, caught a within-sample root race, then confirmed the repaired production recheck and injected regression.

Logs and receipts: private validation/hotfix58/activation-*. Earlier failed-run investigation/FINAL-OUTCOME and all hotfix57 artifacts remain intact. No new live installed-state, foreground, microphone, audio, model or activation measurement is claimed. See [repair report](../Windows-calculator-activation58.md).

# Historical launch investigation58 evidence

Original local START run8292aba49a0240dd9ec15ac365b77a87: one production dispatch,30 false verification outcomes,263 unchanged input samples, settled=true, guardFailed=false, preferencesStable=true. Coordinator childSettled=true, stagecalculator, recovered=false. Only calculator.started exists; installation and recovery did not begin. Read-only process metadata confirms new Calculator process creation during dispatch and the unchanged original Buddy process. All4,416 installed application files match the pinned prepared baseline, including earlier official rollback metadata.

Root final focused checks: ComputerUse116 mock/owned-file checks; RoutineLaunchReport72 pure checks; both builds and Windows Release compilation0warnings/errors. APP47 independently confirmed callback accounting; QA43 found no source blocker. The early inspection's static-only manifest omitted known installed rollback metadata; the corrected inspection uses the complete pinned prepared application manifest and passed. Both inspection logs remain preserved. Source/reporting repair remains distinct from unresolved native activation acceptance. See [investigation](../Windows-launch-investigation58.md) and private validation/hotfix58 evidence.

# Historical hotfix56 evidence

The installed refinement engine returned punctuation-only then verbatim candidates; no provider error. The initial fixed preview accepted the exact reported comparison with the actual local model, similarity0.9005715024022094. Installed Calculator resolution failed at the file-only publisher guard; the candidate resolved its exact healthy Store registration read-only. Worker comparison465, report58 and launcher160 checks passed. Root final aggregate58/58, final package/runtime checks and both exact0.4.6 binary probes pass; comparison similarity0.9005715024022094 in16.8seconds. Fresh backup verified4,416application files,9profile files and4shortcuts without stopping the installed app. Independent review found no trust bypass. No foreground launch or installed replacement occurred. See [hotfix report](../Windows-hotfix56.md); private receipts remain outside Git.

# Evidence ledger

Logs are kept in the local execution workspace, outside source. This ledger is sanitized. A test claim names its scope and must not substitute for physical or live application acceptance.

## October 5 feedback47 evidence

[Current report](../Windows-feedback47.md) supersedes the quality46 conditional failure and old secure-desktop status below. Sealed29suite receipts and build logs are private under validation/feedback47. Actual local14case/tworound matrix:26useful outputs/13unique plus2expected contradiction clarifications, zero unmet utility or unsafe outcomes, no saved content. Root wording/Unicode review retained. Actual readonlyComet authored Guide passes complete observation/chrome ancestry/fresh grounding/useful manual explanation; no model inference or action. Actual readonlyComet/Camera/Spotify resolver passes; launches remain untested. Package/silent speech receipts are distinct from physical/foreground acceptance. Installed baseline and previous preview stay preserved.

## Current quality46 independent results

See [quality investigation](../Windows-quality46.md). Final accepted worker handoffs are integrated, with root amendments for adjacent-role grouping, review presentation and obsolete fixture protocols. Latest successful headless results total 1,793 across 22 suites; 13 pure observation diagnostic checks pass separately. Final Windows Release compile has zero warnings/errors. Native final-source execution remains blocked by the secure desktop, not a reported pass.

`validation/quality46/actual-14-goldens-final-two-rounds.jsonl`: actual local Gemma service, 14 cases twice; 12 useful unique cases (24 outputs), one expected contradictory-count clarification twice, one conditional utility failure twice. All original eight cases improve in both rounds. `final-root-quality-review.json` records agent inspection of wording/relations and modest utility limitations. The harness exits 1 for unmet conditional utility; no all-pass claim. Existing embedding threshold remains 0.80; rejected conditional similarity is 0.7505545693567622. No audio, browser writes, installed-profile reads or new models in this fixture.

Read-only baseline Comet observations establish a cold traversal-time limit and seven warm depth-12 omissions behind the incomplete 153-control result. Cached traversal and real Guide have not been tested successfully: the next probe stopped at secure-desktop checks before UIA. The user must unlock the desktop and confirm readiness; do not bypass the guard. `final-preservation.json` verifies unchanged installed0.4.4 hashes/preferences and preserved0.4.5 source archive. No new preview/install/push.

| Revision/batch | Evidence | Result and limits |
|---|---|---|
| 0.4.2 / dd87c798 | core42 aggregate and native logs; Windows-0.4.2.md | 754 assertions passed: 504 service/policy + 250 native owned-fixture checks. Final Release build had zero warnings/errors. |
| 0.4.2 / dd87c798 | core42-local-vision.txt | 24 passed, comprising 22 repeated core assertions + two real local-model checks. Pixel-only owned chart described through selected-window capture; no third-party acceptance. |
| 0.4.2 installed | core42-installed and core42-preflight evidence | Package/OCR, branding, exact F3 checksum, synthetic Whisper, eight matching application/asset hashes, schema-3 preservation and five byte-identical saved files passed. No physical mic/audio playback claim. |
| 0.4.2 recovery | core42-rollback-dryrun.txt | 4443 recovery hashes, six transaction hashes and four shortcuts verified. Rollback was not applied. |
| 0.4.2 source | core42-manifest-verification.json | 294 source/archive hashes matched; source safety scan passed. Source/report publication remains blocked; no 0.4.2 CI run. |
| 0.4.3 final source | team43-final-aggregate.txt; team43-final-retry-native-aggregate.txt; team43-final-native-build.txt | 1,092 passed: 797 service/policy/mock + 295 native owned-fixture checks. Zero Release warnings/errors. Build-input snapshot unchanged across tests. |
| 0.4.3 local model | team43-live-staged.txt; team43-live-staged-inspection.txt; team43-live-staged-format.txt | Staging acceptance FAILED on three canned gemma3:4b probes. Validator emitted no staged sentence; whole-answer fallback completed/stored. No audio or physical latency test. |
| 0.4.3 team gates | team43/pause-resume.json; team43/final-input-verification.json | Controlled pause rejected intake, reconciled all five actual worker HEADs/snapshots, then resumed. Late accepted output refused without integration edits. |
| 0.4.3 workflow | team43-final-coordination.txt | 12 separate checker tests passed. These are not application assertions. |
| 0.4.2 preserved during final 0.4.3 tests | team43-final-retry-baseline evidence | Fresh pre-run schema 3 existing fields preserved; five other saved files byte-identical; installed application still 0.4.2. Earlier snapshot mismatch after intervening installed use was retained, never restored over newer data. |

Integration record format: revision/diff identifier; command or test mode; result/count; mock/real/physical boundary; local evidence filename; known limitation. Failed attempts and the fix/retest that resolves them stay visible when they explain a material boundary.

## Development-team integration

- Five actual specialists delivered 29 reviewed source/test/report files through scoped, dependency-bound handoffs. Original accepted hashes remain in tasks.json; subsequent root documentation updates and the plan prompt format example are integration-owner amendments.
- QA reproduced two cancellation races against baseline, then passed six independent root-source checks after the fix. Mocked overlap/route tests do not replace physical headset disconnect acceptance.
- The final staged suite passed 89 checks; provider components passed 198 mock-only assertions; new native parity mode passed 45. These are included in the 1,092 application total, not extra counts.
- The root format-only plan prompt amendment was followed by a complete final aggregate/native rerun. Three real model probes remain failed staging acceptance, not successful streaming. No gate was relaxed and no microphone/audio was involved.
- Coordination checker SHA256 `ED0160764007AD455500D3CD953952A1E7C77221F40EEBF16CEBAFCAD9480C32`; test SHA256 `4F64E47343A250FFBCC0EA38BD498B3B7BF45B5C222728EC30FFE380E70CE742`. Twelve tests were independently repeated by QA and the integration owner.
- Preview packaging, launch, checksums and source-archive receipts are recorded outside source in the final handoff. No new installation, GitHub write or live provider/audio use occurred.

## 0.4.4 final integration

- Fifteen `release-*` service suites: 1,145 passing assertions/cases. Production inputs remained unchanged through the final native-only fixture corrections.
- Seventeen `accepted-native-*` suites: 418 passing checks, including Night Mint70, reliability39, parity45, source field25, native QA23 and tray routing14. Tray routing uses F23/F24 fixture registrations, never the installed Space chords. Native build0warnings/errors.
- `combined-live-three-rounds.jsonl`:21/21 real Gemma service checks. `accepted-native-live-guide.txt`:5/5 actual observed owned-window model checks. No audio, executed Comet action or external source-field edit.
- Independent QA found contradictory-reference/name resolution and a Stop ownership gap. Root fixed both and added focused regressions; planning suite122 and native reliability39 include those boundaries.
- Theme receipt and14 owned PNGs record exact roles, contrast, keyboard focus, selected/disabled/error states, Home, Guide, Voice and refinement diffs. Earlier failed geometry-render experiments are preserved; final window renders use native layout.
- Package/source/archive, fresh recovery, copied-profile rehearsal and installed verification are recorded in the private final handoff after source seal. No profile, keys, raw user logs or model weights enter Git.

## 0.4.5 audit checkpoint

Three independent read-only audits reconcile 950095dc against pinned local upstream files. Existing comparison introductions and rows need correction; 0.4.4 reliability fixes are tested and installed. New batch integration tests are not yet complete; historical counts are not new acceptance.


## 0.4.5 integrated preview evidence

The integrated batch passed **2,006 application checks: 1,518 service/policy/owned-file checks across 20 suites and 488 native checks across 20 modes**. The Windows Release build finished with zero warnings or errors. A focused final refinement-instruction amendment was followed by reruns of all six refinement-using service suites; native fixtures used the final production code. Tests exercise owned WPF/UIA windows, synthetic speech callbacks, cancellation, exact source Apply/Undo, footer reachability, window/process faults, one-use controller dispatch and bounded executable leases. These are not physical microphone/headphone or actual-browser editing passes.

The actual local Gemma3:4b golden set ran eight canned prompts for three rounds: **24 outcome-integrity checks passed**, with nine model-assessed changed candidates and fifteen truthful no-refinement results. Root output review found useful, modest grammar improvements for two cases (rough poem and email), repeated consistently; the third accepted case was only stylistic. Five cases, including the exact user poem, retained the original. This does **not** establish broad semantic refinement or successful refinement of that poem. Model scores are estimates, not measured quality. Earlier punctuation-only, synonym-only and numeric/ordering failures are retained in private evidence; the final instruction preserves exact numeric/ordering tokens without relaxing fidelity.

An explicitly authorized read-only Comet probe verified its live process identity and observed 153 UIA controls. The snapshot was incomplete, so image capture remains refused. No browser input, focus change, screenshot or page-text logging occurred; only control counts/roles and diagnostics were retained. Actual Guide lesson usefulness, original browser-field Accept/Undo and the new production app-launch adapter still need user acceptance.

Root visually inspected owned no-refinement, short-work-area footer and Night Mint diff renders. Legacy tests were updated for the new footer tree and real-window selection requirement; a cancellation test now uses an actual changed proposal. Production safety guards were not weakened to make these fixtures pass.

Local receipts: `validation/core45/service-summary.json`, `native-summary.json`, `final-live-golden-three-rounds.jsonl`, `golden-output-review.json`, `actual-comet-readonly.jsonl`, and the final build/package/source manifests. Package and smoke receipts are external to source so the sealed commit remains reproducible. Installed 0.4.4 is preserved; no new installation or publication is included.

## Local-start timing readiness

Oct5 private acceptance47/local-start receipts: 19 launcher tests pass using fake child processes; fixed arguments and unchanged runner validated through deliberately expired-deadline refusal before desktop work. 637 runner files and 5 candidate identity files verify; 4418 candidate static files/source archive and 4 installed 0.4.4 hashes match. No foreground test, native acceptance, installation or policy change. Remote dispatch latency is unbounded by available evidence; proposed local 180 second window retains 90 second admission/20 second cleanup guards and documents cooperative OS limits.


## Followup54 final integrated evidence

Private validation/followup54/final-validation.json reconciles all57commands with production inputs unchanged; three standalone fixture-only changes explain resumed segments. First Guide routing regression was repaired and126Guide checks pass. QA54 final160independent checks,64shared-surface and32refinement unshown checks are within the57commands, not additional totals. Actual local-agent IPC27checks are separate. Windows0warnings/errors,12ledger tests,571-file bounded source scan pass. Actual local refinement14case round returns13useful structures/1expectedclarification; four finite articles improve and no threshold was relaxed. General model teaching remains unaccepted;4of5independent positive concept phrasings succeed, Zoom/Button unsupported. No native/physical/cloud acceptance follows from these results. Package manifests and preservation receipts remain separate.

## Focused hotfix57 evidence

All59 final aggregate commands pass with unchanged build inputs;243 app-binding and36 framed-completion/lifecycle checks are included. Exact combined0.4.7 DLL read-only verification bound the already-existing Calculator root/direct child and exact package, including a fresh consumer check, in877ms. Final service comparison refinement passed in20.1seconds with similarity0.9005715024022094 and no source-field/profile writes. Package/runtime and independent source review pass. Actual new activation, Home/Stop/reopen and installed update remain pending coordination; physical audio is not claimed. Private evidence under validation/hotfix57 contains receipts, manifests and helper bindings. Publication remains blocked and a new commit requires its own authorization.
