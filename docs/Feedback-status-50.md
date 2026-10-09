# October 5 feedback: original 17-item mapping

STATUS-51 attempt 1. **Numbers below now match the original user list**, recovered through the parent-supplied private `validation/followup50/original-feedback.json`. Concerns are summarized, not reproduced as a conversation transcript. This replaces STATUS-50's provisional grouping; the earlier handoff remains historical evidence.

Installed Buddy remains **0.4.4 / `950095dc1f8d1ce86b0f17bf8919a3465344e369`**. Feedback47 is a preserved, separate 0.4.5 candidate: source snapshot `197836cd6b4cef72621f1aad1785f2dc6036d2b106f14ae15ed0eb8ba9190441`, base `ced111d78a4e4230e53d8d359ca167b231e29e56`. Followup50 adds source changes without changing that candidate or establishing installation acceptance.

The previous foreground window finished **2026-10-05 at 11:40:53 UTC**: 130 owned checks passed (15 settings/region, 55 visual, 32 observation, 14 preview policy, 14 tray/routing), plus visible/responding isolated preview startup. Startup did not test model chat. Fixtures/preview closed, controls were free, only original installed PID 33820 remained, and installed hashes/preferences were unchanged. Receipts: `validation/feedback47/foreground-five-minute.json` and `foreground-preservation.json`.

## Latest acceptance update

The actual sealed executable package/dependency/OCR and branding checks passed at13:39UTC. The13:41–13:46announced attempt ended13:41:57: candidate field capture refused full containment, owned window closed, no field edits; proposal/Apply/Undo and packaged Stop/launch/reopen were not run. Its receipt lacks numeric geometry, so no production cause is asserted. Headless harness repair now passes77checks with exact field binding, geometry evidence and independent packaged scopes. These are harness checks, not acceptance of the blocked workflow. Foreground is paused for fresh Ready and a newly announced deadline; installed/candidate bytes remain preserved. See [concise original17 report](Original17-user-report.md) and private validation/acceptance47/CHECKPOINT.json.

## Original item status

Short receipt names are under private `validation/feedback47/`; `followup50/` names are under `validation/followup50/`. Owned fixtures, actual model service calls, read-only observation and successful user workflows remain separate evidence categories.

| Original item | Concern or success, summarized | Implementation and specific evidence | Remaining acceptance or gap |
|---|---|---|---|
| 1 | Colors/text were acceptable; prefers Black. Area selection works until Enter. | **Implemented:** [Black palette][theme], [Enter/default question][voice], [region request ownership][region]. `foreground-feedback-visual47.txt` checks readable opaque surfaces; `foreground-feedback-lifecycle47.txt` checks Enter once, blank default and stale/cancel routing. | Preserve reported selection success. Routing was intercepted before capture/inference. Earlier chart calls missed the comparison; final local chart service correctly states A=2, B=6 and B taller in three sentences (`followup50/region-comparison-third-revision.jsonl`). Full gesture remains unaccepted. |
| 2 | Recognition works well; settings/shortcuts are inconsistent; app opening needs four clicks. | **Reported recognition success retained.** [Merged preferences][prefs]/[autosave][save] pass persistence/retry fixtures. Saved chord conflicts preserve a working binding/warning. [Exact route][routes]/[bounded dispatch][open] bypass redundant planning for supported requests. Lifecycle/tray/policy receipts cover temporary chords/routing; `sealed-Buddy.AppBinding.Tests.txt` covers bound dispatch. | Actual reduced-click launches and competing keyboard hooks remain unaccepted. Recognition success does not establish every physical endpoint. Broader/consequential actions retain review. |
| 3 | Gmail/other connectors and APIs are inactive. | **Disconnected:** [catalog][connectors] has `AccountGrantsEnabled=false`; [provider factory][providers] has `LiveActivationAvailable=false`; [host][host] uses Ollama. [Setup UI][settings] cannot connect. `sealed-Buddy.Provider.Tests.txt` is mock transport evidence only. | Credentials alone are insufficient: adapters/routing, registered setup, secure entry, scoped/content/cost consent, revocation and live acceptance remain. Coucou services need separate integrations. No activation in this batch. |
| 4 | Comet opened correctly; Guide failed. | **Reported Comet-opening success retained. Partial Guide repair:** [observation][observation], [chrome ancestry][chrome], [authored lesson][lessons]. `final-comet-guide.jsonl`: complete read-only Comet capture, 123 visited/108 elements and useful authored Reload guidance. | Authored pass made no model inference/action. Final nine-case model replay and conceptual-identity comparison each give four correct host clarifications but no accepted result among the five model-dependent cases. Missing outcome checks, unsupported scope/state claims and model HTTP500 repetition aborts remain; general teaching is **not accepted** (`followup50/final51-root-review.json`). |
| 5 | Refinement stays busy across windows. | **Implemented:** [service deadlines][refine]/[request owner][request] cover queue/total timeout, terminal validation, Stop and late results. `sealed-Buddy.RefinementLifecycle.Tests.txt` and `sealed-Buddy.FeedbackBoundary.Tests.txt` exercise ignored cancellation/terminal races. | Actual external-field hang/retry/completion remains unaccepted; service checks do not validate every field/overlay. |
| 6 | Understand selected image/tool and question, then research online with image plus context. | **Partial:** [region flow][region] supports local selected-image explanation plus explicit [reviewed-text web research][research]. `sealed-Buddy.RegionResearch.Tests.txt`: 71 mocks. `followup50/region-baseline-root-review.json`: actual text search/fetch/local answer passed in about 4.8 seconds, one search/three fetch attempts/one usable source; no image/audio sent to web. | **Image-based internet search is missing.** Local image interpretation plus text research does not meet the full image-and-context request. No implicit screenshot upload. Automatic tool-context research/full gesture remain unaccepted; final canned chart comparison passes service usefulness; full gesture remains unaccepted. |
| 7 | Chat text and pointer face clip. | **Implemented:** [template padding][controls], [face sizing][presentation], [QuickChat scrolling][chat]. `foreground-feedback-visual47.txt` checks owned 100/125/150/200% layout scales/footer. `followup50/offline-visual-review.json`: root saw no apparent clipping in expanded-bar/short-footer PNGs. | Limited fixture dimensions; actual multi-monitor DPI remains deferred under item 13. |
| 8 | Refinement is not useful/correct. | **Partial:** [source-bound structure][contract] retains roles/order/literals; final changes use [inline review][inline]/[guarded Apply/Undo][field]. `request-label-actual-14-two-rounds.jsonl` and root review: 13 useful unique scenarios twice, plus two expected contradictory-count clarifications. | Improvement is modest structure; rough grammar remains. Actual external-browser Apply/Undo is unverified. Advanced resources/budgets/options in the original field remain missing; Buddy-draft controls are separate. |
| 9 | Robotic voice; prefer a natural source cue to spoken links. | **Implemented:** [speech cleanup][speech] suppresses URLs/citations and cues only actual attached sources; [output][output] renders one complete bounded reply. `sealed-Buddy.SpeechFeedback.Tests.txt`: 72 text/injected checks. `preview-voice-f3.txt`: silent synthesis/checksum pass. | Naturalness and actual playback need user acceptance. Preserve speaker85/F3; no cloud audio. Reliable early staging/requested-long-answer override remain separate gaps. |
| 10 | Headphone controls generally work well. | **User-reported general success.** [Output][output] retains endpoint/headphones-only guards. `foreground-preview-policy47.txt` checks silence without a selected endpoint; speech mocks cover cancellation. | Not detailed unplug/replug, default-device-switch or physical playback acceptance. Do not broaden this success or invent a failure. |
| 11 | A few apps work, including Comet/Calculator/Notepad; others fail. | **Reported successes retained.** [Intent binding][intent]/[signed resolver][resolver] address observed Camera-to-Calculator/Spotify-to-Comet substitutions. App-binding mocks pass; `package-final-installed-resolution.txt` resolves Comet/Camera/Spotify read-only. | Actual Spotify activation and broad app support remain unaccepted. Unsupported requests fail closed. Camera activation is excluded from the proposed acceptance window. |
| 12 | Buddy stays available after Home closes; preferences sometimes fail to save. | **Reported residency success retained.** `foreground-tray-routing.txt` checks close-to-tray/reopen/Stop. [Preference merge][prefs]/[flush][save] pass owned lifecycle persistence/conflict/failure-retry checks. | Residency does not close preference failures. Installed settings remain preserved; preview repair is not an installed 0.4.4 fix. |
| 13 | Multi-monitor testing deferred until later. | **Deferred by user.** [Layout bounds][presentation] have owned fixture evidence; no actual multiple-monitor acceptance. | Keep deferred; four fixture scale settings do not establish a pass. Excluded from the bounded app window. |
| 14 | Buddy absent from screenshots; panes disappear too quickly. | **Partial:** [capture protection][capture] excludes Buddy when enabled; persisted off mode permits capture. `foreground-preview-policy47.txt` checks both modes on an owned window. [QuickChat][chat]/[region results][region] retain content; visual checks cover retained draft/answer. | Missing Buddy pixels can be expected protection behavior. Exact short-lived reported panes need user-flow acceptance. Guidance ink still expires and clears stale targets; retention must not weaken those guards. |
| 15 | Safe-failure test not performed. | **Not run by the user.** Internal [controller][controller-tests]/[request lifecycle][request] refusal/cancellation checks do not replace this user test. | Keep **not run**, not passed. The record does not specify a concrete scenario to reenact; do not invent one. |
| 16 | Notch vanished automatically and did not return. | **Implemented:** [island][island] persists Hidden/Compact/Expanded, has no idle-hide timer, recovers after ownership suppression and makes fullscreen suppression optional. Visual receipt checks transitions/Stop; isolated startup was visible. | Actual long-running/fullscreen/multi-monitor use remains separate. New cards do not prove installed 0.4.4 resolved this report. |
| 17 | Add Coucou notch features with Buddy art. | **Partial:** original Buddy surface plus current-source task cards/journal. [Local scope][notch-scope] records 94 pure journal checks, 20 worker presentation checks and clean compilation; native50 not run. [33-row audit][coucou] retains platform/asset distinctions. | General model teaching unaccepted. Local cards are not external agents. Embedded chat/files, hooks/approval relay, cloud providers, service dashboards/platform extras remain gaps. No full-Coucou claim. |

## Current boundary and next gate

The parent supplied the original context: item 10 concerns headphones generally, item 13 defers multi-monitor testing, and item 15 is an unperformed safe-failure test. Earlier provisional rows must not be cited as original numbering.

Old foreground-pending statements in [Windows-feedback47](Windows-feedback47.md) and the [Coucou audit](Coucou-notch-feedback47.md) predate 11:40:53; receipts supersede only those named owned checks. Final source passes 959 checks across 15 pure/injected headless suites (`followup50/final51-headless-summary.json`). Final model and regional outcomes are in `followup50/final51-root-review.json`; actual native50 execution remains pending. Historical failures and the sealed candidate remain preserved.

The [bounded feedback47 acceptance plan](Feedback47-acceptance-plan.md) requires a newly announced window and fresh desktop guard; none runs now. **Installation is already authorized within the continuing request after actual acceptance and a fresh recoverable backup.** It is not blocked on asking the same permission again. Publication remains separately denied. Neither authority activates accounts, uploads images, records microphones or installs external hooks. The latest STATE entry explicitly supersedes older separate-installation-permission wording; no new installation approval is needed after the acceptance and backup gates.

[theme]: ../apps/windows/Buddy.Windows/BuddyTheme.cs
[voice]: ../apps/windows/Buddy.Windows/VoiceOverlayWindow.cs
[region]: ../apps/windows/Buddy.Windows/VoiceOverlayWindow.Region.cs
[research]: ../services/Buddy.Server/ReviewedRegionResearch.cs
[prefs]: ../apps/windows/Buddy.Windows/DesktopPreferences.cs
[save]: ../apps/windows/Buddy.Windows/MainWindow.PreferencePersistence.cs
[settings]: ../apps/windows/Buddy.Windows/MainWindow.Preferences.cs
[open]: ../apps/windows/Buddy.Windows/RoutineAppOpen.cs
[dispatch]: ../apps/windows/Buddy.Windows/DesktopAssistant.cs
[controller-tests]: ../tests/Buddy.ComputerUse.Tests/Program.cs
[intent]: ../services/Buddy.Server/AppLaunchIntent.cs
[resolver]: ../apps/windows/Buddy.Windows/InstalledAppResolver.cs
[observation]: ../apps/windows/Buddy.Windows/ScreenPerception.cs
[lessons]: ../services/Buddy.Server/BrowserGuideLessons.cs
[chrome]: ../apps/windows/Buddy.Windows/BrowserChromeObservation.cs
[refine]: ../services/Buddy.Server/Refinement.cs
[request]: ../apps/windows/Buddy.Windows/RefinementRequest.cs
[contract]: ../services/Buddy.Server/RefinementContract.cs
[inline]: ../apps/windows/Buddy.Windows/InlinePromptWindow.cs
[field]: ../apps/windows/Buddy.Windows/FocusedFieldEditor.cs
[core-report]: Windows-0.4.5.md
[speech]: ../apps/windows/Buddy.Windows/SpeechText.cs
[output]: ../apps/windows/Buddy.Windows/LocalVoiceOutput.cs
[controls]: ../apps/windows/Buddy.Windows/Themes/Controls.xaml
[presentation]: ../apps/windows/Buddy.Windows/CompanionPresentation.cs
[chat]: ../apps/windows/Buddy.Windows/QuickChatWindow.cs
[island]: ../apps/windows/Buddy.Windows/CompanionIsland.cs
[notch-scope]: Local-notch-scope-50.md
[coucou]: Coucou-notch-feedback47.md
[connectors]: ../services/Buddy.Server/ConnectorPlumbing.cs
[providers]: ../services/Buddy.Server/ProviderTransports.cs
[host]: ../services/Buddy.Server/BuddyHost.cs
[capture]: ../apps/windows/Buddy.Windows/CaptureProtection.cs
[routes]: ../apps/windows/Buddy.Windows/AssistantSettings.cs
