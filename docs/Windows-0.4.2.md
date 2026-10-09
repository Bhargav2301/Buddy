# Windows 0.4.2: core teaching and validation report

This testing build extends the local recognition/refinement work in [0.4.0](Windows-0.4.0.md) and preserves the [0.4.1 speech-feedback correction](Windows-0.4.1.md). The [33-row Clicky comparison](Clicky-comparison-0.4.md) distinguishes implemented behavior, partial equivalence and missing integrations, with links to both codebases. It uses a pinned reference revision and does not claim copied code or identical artwork.

## Using the changes

Ask a screen-teaching question, then use Talk for a follow-up. Buddy retains at most ten Q&A pairs within the same selected window, application and title, for ten minutes. Focus/app/title changes, Stop or New lesson clear that temporary context. Every response still uses fresh evidence; prior suggestions are not proof that you completed a step.

For charts, canvases or other visual content, check **Include an image of the selected window for this question** before confirming your words. This is a deliberate one-question choice. The existing selected-region flow remains available. Capture stays scoped to that window/region, applies existing privacy masking, and refuses incomplete or stale accessibility/capture state. No screenshots are saved or reused as later observations.

For a requested comparison or several markings, one answer can show up to four distinct circle/arrow/underline/label annotations. Each target needs current UIA/OCR/visual evidence. A changed or unverified target suppresses the complete packet. Annotations remain click-through, short-lived and invalidated by input/focus changes; they execute nothing.

Speech starts after the complete answer passes the three-sentence policy. One later sentence is rendered while the current sentence plays, through one concrete endpoint for the whole utterance. Stop, device/default-route changes and headphone policy continue to cancel output with no fallback. Every necessary qualification remains in the queue. This is sentence prefetch, not speaking unvalidated model tokens; physical latency and perceived voice quality remain separate acceptance questions.

**Settings → Memory → Remember my screen-teaching questions and answers** is off by default. If enabled, up to ten Q&A pairs per app and one hundred total are encrypted in a separate local file. Answers may contain text you deliberately asked Buddy to read. Raw screen fields, screenshots, microphone audio and window titles are not stored. View and Clear controls are provided; turning the option off stops both saving and using these notes. Main profile schema3 and existing preferences remain compatible.

## Evidence and limits

The final Release build completed with zero warnings and zero errors. All 754 assertions passed: 504 across nine service/policy suites and 250 across thirteen native Windows fixture modes.

| Service/policy suite | Passed | Native Windows fixture | Passed |
|---|---:|---|---:|
| Service | 32 | Recognition lifecycle | 5 |
| MVP | 36 | Speech feedback | 23 |
| Execution | 9 | Core teaching | 22 |
| Assistant | 71 | Teaching | 22 |
| Desktop logic | 108 | Source-field refinement | 25 |
| Preview policy | 45 | Guide | 26 |
| Teaching policy/service | 46 | QA regression | 23 |
| Optional jobs/knowledge/protocols | 104 | Character expressions | 5 |
| QA regression | 53 | Settings/navigation | 46 |
| | | Jobs | 22 |
| | | Presence | 8 |
| | | Preview interaction | 16 |
| | | OCR | 7 |

A separate real local vision run passed 24 assertions: it repeats the 22 core teaching assertions and adds two real-model checks. An owned fixture displayed two bars and labels solely as rendered pixels, absent from its accessibility tree. The production selected-window capture reached the local vision model, which correctly described both labels and the larger bar without inventing an action or annotation. A previous attempt refused when foreground focus changed; the successful run did not weaken that check.

The [core teaching fixtures](../tests/Buddy.Windows.IntegrationTests/CoreTeachingChecks.cs), [teaching service checks](../tests/Buddy.Teaching.Tests/Program.cs) and [offline protocol checks](../tests/Buddy.Optional.Tests/ProviderChecks.cs) cover follow-up bounds and expiry, focus/title invalidation, explicit image selection, multiple grounded markings, refusal of stale targets, sentence playback ordering/cancellation, encrypted memory bounds/clear, and malformed or unsupported provider events. Existing suites retain shortcut, capture-affinity, route-policy, Stop/reopen, speech cleanup and approved-launch coverage. The unchanged pre-pause source checks are reused; the final separate package receives fresh launch, payload, branding, voice and recognition probes before installation.

Owned WPF/UIA fixtures exercise actual capture, overlays, source-field editing and speech callback routing; mocked inference is labelled separately from real local model probes. These checks cannot establish physical microphone/accent accuracy, perceived voice quality, headphone unplugging behavior or real application compatibility. The user confirmed actual ChatGPT/Codex refinement; actual Comet/Grok still needs review/Accept/Undo without submission. Custom fields without a safe writable UIA capability refuse rather than redirect into Buddy or use keystroke/clipboard fallback.

Independent Linux Blender testing exercised real Blender operations, but did not run Buddy's Windows WPF/UIA integration. That evidence must not be presented as end-to-end Buddy/Blender acceptance. Sparse custom-canvas accessibility may permit a scoped image explanation while providing no independently verifiable clickable target. Buddy must abstain from invented labels or coordinates.

The separate preview/package, source manifest, exact commit, installed hashes, saved-data comparisons and complete rollback inventory are checked during the authorized local update. User profiles, model weights, private logs and recovery archives are excluded from source. The original Piper C/F3 assets and audio checksum are preserved; source and package tests do not claim an exact match to a fictional or actor's voice.

## Optional integration status

[ProviderProtocols](../services/Buddy.Server/ProviderProtocols.cs) and [offline fixtures](../tests/Buddy.Optional.Tests/ProviderChecks.cs) implement text request/complete-response shapes for OpenAI, Anthropic, Gemini and OpenRouter. They reject incomplete, blocked, tool-bearing, malformed, cancelled and oversized responses. The text Realtime reducer bounds text, rejects audio/function events, ignores stale/duplicate responses and yields an answer only after completed status. These components have **no credential access, network transport, socket, audio input or production routing**. Live provider and realtime voice integrations remain missing.

Protocol references: [OpenAI Chat](https://developers.openai.com/api/reference/resources/chat), [OpenAI Realtime conversations](https://developers.openai.com/api/docs/guides/realtime-conversations), [Anthropic stop reasons](https://platform.claude.com/docs/en/build-with-claude/handling-stop-reasons), [Gemini content generation](https://ai.google.dev/api/generate-content), [OpenRouter Chat](https://openrouter.ai/docs/api/api-reference/chat/create-a-chat-completion). Offline fixture conformance is not live-provider acceptance.

Activation requires implemented transport/routing, a selected provider/model, explicit approval of transmitted content and cost, securely supplied credentials, and real acceptance tests. Cloud speech additionally needs explicit microphone-audio sharing approval. No account grant, paid service, cloud audio, unrestricted shell, public release or merge is part of this build.
