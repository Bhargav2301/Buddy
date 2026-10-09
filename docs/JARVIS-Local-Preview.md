# Buddy local preview and requirements trace

This review build combines the approved Windows fixes with the local foundation of the JARVIS program. It is based on the clean `feature/interactive-assistant` checkout at `dba5acad4cbd39a6795c05bdcc226dfbd75a2694`. Satonara's installed 0.3.1 build is `93e141ca2d6c79a2ab74f830549d49652e0b6200`; it has not been replaced. This preview is not a completed 0.6–0.9 release.

## Source and authority

Requirements: Buddy_JARVIS_PRD.docx v1.0, Buddy_JARVIS_TRD.docx v1.1 addendum and Buddy_JARVIS_UIUX.docx v1.0, dated 3 October 2026, Library version 0. Full text and tables were read through Library and cross-checked with the parent review. Local DOCX materialization failed because the Library identity helper requires `os.setxattr`, unavailable in Windows Python; the source documents were not altered. Full extracted text is retained in the task's requirements folder.

The user's approved local fixes and later confirmed phased approach govern this build. No account setup, cloud audio, provider key handling, paid service, sidecar, new brand asset, push, merge or installed-app replacement is included. Existing artwork remains a placeholder pending user-supplied official assets.

## Implemented Windows changes

| Area | Result | Main files |
|---|---|---|
| Shortcuts | Independent chat and voice chords; chat never invokes the microphone. Native conflicts retain prior registrations. No silent startup fallback. Ctrl+Space warns about ChatGPT; Windows+Space is excluded. Optional hold behavior uses only a successfully registered voice chord. Stop-chord conflicts are reported. | DesktopPreferences, MainWindow, AssistantSettings, MainWindow.Preferences |
| Recognition | Selected Windows speech language, selected local microphone, local PCM capture; missing/ambiguous devices fail instead of silently switching. Installed-app grammar includes Comet. Confidence below 0.80, close alternatives, rejections and all recognized action requests require editable review. Home dictation leaves uncertain text for Copy/review. | LocalSpeechInput, MicrophoneStream, SpeechReview, VoiceOverlayWindow |
| Replies | Calm, concise system policy; ordinary chat and voice have at most three sentences. Overlong answers are regenerated once in full, then replaced by a safe short fallback if necessary. No truncation of later safety qualifications. Plans, per-action risks and approval details are separate and remain complete. Sources remain structured clickable evidence. | ConversationalReply, BuddyService, OllamaEngine |
| Local speech | Full composed text is cleaned before synthesis. Windows voices and an owner-approved optional Piper 1.8.0/VCTK worker in the isolated neural preview; voice choice, pace, preview and Stop. Normal synthesis stays in memory. Only neutral audition samples are saved for review; no cloud speech service. | LocalVoiceOutput, NeuralSpeechSynthesizer, SpeechText, piper_worker.py, MainWindow.Preferences, VoiceOverlayWindow |
| Headphones | Explicit active headphone/headset endpoint required when enabled. WASAPI opens that concrete endpoint; no default-device fallback. Endpoint state/removal/property changes and render-default changes synchronously cancel the active route, synthesis and playback. Reconnect never resumes speech. Driver form-factor ambiguity fails closed. | LocalVoiceOutput, AudioRouteGuard |
| Screenshot protection | Persistent toggle applies to current and new Buddy windows; default remains protected. It controls Windows display affinity, not a camera or every capture mechanism. Screen context remains request-scoped and memory-only. | CaptureProtection, OverlayNative, MainWindow, DesktopPreferences |
| Comet | Exact “Open Comet [Browser]” requests produce one bounded action. Resolve only known installed paths; reject links; validate Authenticode offline and Perplexity publisher; no arguments, PATH lookup, script or shell. Verify foreground executable identity. The plan and launch both require approval. | InstalledAppResolver, AssistantPlanning, AssistantModels, DesktopAssistant |
| Preview isolation | Separate data, mutex, activation channel and loopback-only port 47839. A marker automatically enables isolation even when Buddy.exe is opened directly. Installation/rollback and installed-profile deletion are disabled. The original app remains on its original profile and port. | PreviewEnvironment, Program, DesktopActivation, BuddyHost |

The original Ctrl+Space recommendation in the PRD/UIUX is intentionally not the default on this PC because the user's ChatGPT collision takes precedence. Windows can detect RegisterHotKey conflicts, but another application's low-level hook can still intercept a chord; the settings explain this practical limit.

## JARVIS requirements trace

| Requirement | Current slice | Later work and gate |
|---|---|---|
| JR-01 PC home | Existing resident tray/Home preserved; Close hides and Quit stops the process/service. Preview is visibly separate. | Optional Keep Buddy awake requires explicit setting; no power policy changed. |
| JR-02 Android relay | Existing paired-PC architecture preserved. No Android secrets or independent brain added. | Same brain metadata/chip and PC-awake routing in 0.8; Android not built or device-tested in this Windows batch. |
| JR-03 summoned listening | No wake word or always-on recorder. Mic begins only on explicit voice invocation. | Optional wake word requires opt-in and visible indication. |
| JR-04 typed plan/approval | Existing bounded typed plans retained; every click, invoke, type and launch now high risk. Full plan and action review remain. | Additional tool implementations must enter the same gate; model-supplied risk is never authoritative. |
| JR-05 read-only concurrency | ToolRegistry rejects parallel mutation tools and unknown tools. | Harness scheduling and per-task grants remain 0.7 work; no parallel agent executor is enabled. |
| JR-06 Hermes | Not installed, launched or connected. | Optional non-elevated sidecar with proposal-only RPC, no SendInput; revoke/kill tests required. |
| JR-07 router | Server IBrain, BrainRequest, OllamaBrain and BrainRouter; explicit choice/phrase > skill pin > default > local. Production registers only local. /v1/brains is truthful. | Real provider adapters and explicit account authorization in 0.6; skill pin storage in 0.7. |
| JR-08 fallback/privacy | Local fallback before provider output, with actual identity metadata. Bank titles and credential processes strip cloud candidates. Cloud adapter contract currently gets only the explicit current text; previous turns, memories, UIA, titles and images are stripped. Fake-provider tests verify boundaries. | A future screen-sharing grant must be explicit and tested before widening the cloud payload; no adapters presently send cloud requests. |
| JR-09 visible brain | Local 12px companion pill and pre-answer NDJSON brainId. Brains settings accurately describe the local-only preview. | Dynamic client chip, spoken-provider consistency and tray in-flight cloud dot required before enabling cloud. |
| JR-10 USER.md | Future work explicitly identified in Memory settings. | Encrypted user-visible Me editor, explicit interview and deletion contract in 0.7; no plaintext USER.md created. |
| JR-11 encrypted memory | Existing encrypted facts preserved; Honcho absent/off. | Separate text-only consent, destination, kill and revoke behavior in 0.9; screenshots never eligible. |
| JR-12 inspect/delete facts | Existing inspectable fact list and deletion retained. | Natural-language memory query endpoint and proof that deleted facts disappear from the next prompt in 0.7. |
| JR-13 skills | Built-in actions described honestly; ToolRegistry supplies deny-unknown, allowlist and grant policy. | Editable SKILL.md folders, schema/path validation, import/save review, brain/voice pins and no autopublish in 0.7. |
| JR-14 Refine skill | Existing local checked refinement and no-send behavior retained. | Route it through SkillHost without making another product surface in 0.7. |
| JR-15 connectors | No new connector or credential. | Credential Manager handles, per-account grants, revocation and fail-closed in-flight requests in 0.6/0.9; official API capabilities must be verified. |
| JR-16 voice channels | Configurable Windows voice/hold chord with confidence review. | Android long-press and local talk-only offline fallback require Android acceptance. |
| JR-17 channels | Channels settings identify this PC/paired Android, Telegram later, Slack later, iMessage unavailable. | Telegram explicit bot token, pairing, first-message confirmation and per-destination delivery in 0.8; never automatic fan-out. |
| JR-18 voice providers | Windows local voices and explicitly approved Piper/VCTK evaluation with pace/preview, bounded stdio synthesis and strict output routing. Licenses/hashes retained; see docs/voice. | User listening preference and physical headphone acceptance remain pending. ElevenLabs requires separate key, cost/audio-sharing consent. British/calm means style, not actor cloning. |

## API and architecture sequencing

Implemented: authenticated GET /v1/brains, optional brainId/skillId fields on /v1/chat, NDJSON brain metadata, 4 MiB request cap, local IBrain adapter and router/tool-policy tests. A non-null skillId currently returns an explicit unavailable error. Unknown/unconnected providers report local fallback rather than claiming an external account was used.

Not implemented or exposed as pretend-success stubs: /v1/accounts, GET/PUT /v1/me, GET/POST /v1/skills, /v1/memory/query, /v1/channels/telegram. Each needs versioned validation, ownership and grants, deletion/revoke semantics, cancellation and audit contracts before exposure. Future audit entries must include actual brainId and tool/target/result without credentials, images or page bodies. A provider's consumer account/custom GPT/bot cannot be promised to expose an API without checking its official capabilities.

DesktopHandoffBrain must be explicit, named and focused, with a visible banner and confirmed Agent Send. It cannot be selected automatically. No cookie scraping or background vendor UI control is part of this build.

## Verification and remaining acceptance

See the task's validation folder and final report for exact run results. Fake-provider tests prove policy behavior, not vendor connectivity. Native fixtures use disposable Buddy windows and generated content; their layout PNGs are test artifacts, not persisted user screenshots.

Physical checks remain: recognition accuracy with the user's voice/accent and both microphones; chosen voice comfort; WH-1000XM5 disconnect, Bluetooth profile changes and default-output changes during speech, confirming silence on speakers and no automatic resume; actual competing ChatGPT shortcut behavior; ordinary tray reopen and repeated Start/Stop on the preview. Native callbacks are event-driven, but acoustic interruption latency cannot be established without hardware observation. The future cloud/sidecar cancellation target under 100 ms and account-free first pointed answer under 3 minutes are release acceptance goals, not claimed measurements here.

Do not replace the installed app until the user reviews this preview and approves replacement. Preserve the original checkout, baseline files and any unrelated settings.
