# Supplied specification → Buddy 0.1 coverage

Status labels describe implemented code, not device-level certification. See `Validation.md` for executed tests and limitations.

| Area / spec IDs | Status in 0.1 | Concrete behavior or remaining work |
|---|---|---|
| Native Windows / native Android | Implemented | .NET WPF and Kotlin Compose; neither is a web wrapper |
| Local AI connection | Implemented | Actual Ollama integration, model setup/status, optional vision model; no canned answer fallback |
| IN-01, IN-07 input modes | Partial | Type, explicit voice, hybrid review; OS speech replaces realtime gateway |
| IN-02, IN-04 shortcuts | Partial | Configurable compact chat/voice activation, Ctrl+Shift+Space for one utterance, Ctrl+Alt+Esc to stop; no press-and-hold hook or per-app mode rules |
| Windows cursor companion | Implemented in code | Native transparent click-through companion beside the pointer; compact streaming chat/voice bar; monitor work-area placement; preferences and tray controls; Windows device acceptance pending |
| IN-03 app-specific modes | Deferred | No per-application mode memory |
| IN-05 wake word | Deferred | No background wake-word listener |
| IN-06 quiet mode | Partial | Manual read-aloud control; no meeting/calendar detector |
| IN-08 / AN-07 languages | Partial | Model can respond in multiple languages; dictation depends on installed OS language; UI is English |
| IN-09 offline recognition | Partial | Installed native recognizers; no bundled Whisper fallback |
| IN-10 TTS | Partial | Installed device voices; no voice marketplace or per-app voice settings |
| SC-01/02/07 screen input | Partial | Windows UIA text and reviewed visible-window capture; Android Assist text and user-shared/picked images; no Android MediaProjection |
| SC-03/04 grounding and overlays | Partial / deferred | Screen text available; no model-grounded arrows, boxes, click-through drawings or coordinate execution |
| SC-05/06 step detection | Deferred | No screen-state monitoring or automatic step advancement |
| SC-08 camera | Partial | Android image picker/share input; no live CameraX guidance |
| SC-09/10 privacy | Partial | Password text filtering, basic sensitive-app/title checks, preview, secure-window respect; no comprehensive banking URL classifier or image/OCR redaction |
| CL-01/02 field watcher | Partial | Explicit Android keyboard selection refinement; no automatic desktop/browser field detection |
| CL-03 prompt scoring | Deferred | No score or trained quality model |
| CL-04..08 apply/undo/never-send | Partial | App draft review/apply; Android draft undo; keyboard explicit apply and guarded 30-second undo; no automatic send |
| CL-09 hybrid refinement | Implemented | Dictated draft can be refined before submitting |
| CL-10..12 modes/techniques | Partial | One local quick rewrite pipeline; no Clarift partner API, Council or Guided multi-pass implementation |
| CL-13..16 reusable prompts | Partial | Saved prompts on the shared PC store; no projects, file references or template gallery |
| CL-17..22 evaluator/analytics/drills | Deferred | No evaluator scores, converter, unit quotas or training drills |
| AG-01..10 agents | Deferred | No computer-use executor, connectors, autonomous actions, approvals, routines or task scheduler. Buddy states that it cannot perform these actions. Stop cancels its chat generation. |
| HM-01 conversation home | Implemented | Create, resume, delete, export; no pin/archive/search yet |
| HM-03/04 memory | Partial | Explicit user-saved facts, editable by delete/re-add; no automatic extraction or vector retrieval |
| HM-05 settings | Partial | Local model, pairing, entry points and voice controls; no global settings sync |
| HM-06 usage | Deferred | No paid provider usage or quota accounting |
| EC-01 sync | Partial | Both clients use the same authoritative PC store; six-second visible refresh; no OAuth account |
| EC-02 handoff | Partial | Manually resume shared history; no push handoff notification |
| EC-03..07 cross-device extras | Deferred | No file transfer, clipboard bridge, remote control, second screen or push routing |
| LM-01..05 learning | Deferred | No persisted walkthroughs, flashcards or progress system |
| WN-01..03 desktop integration | Partial | Explicit foreground context; no Explorer menu, command execution or Office/Graph editing |
| WN-04 local model | Implemented | Local PC Ollama inference; models downloaded separately |
| WN-05 extension | Deferred | No browser extension included |
| AN-01/02 entry points | Implemented in code | Optional bubble, default-assistant service, Quick Settings tile; device checks pending |
| AN-03 keyboard | Partial | In-house basic QWERTY/symbol keyboard with explicit selected-text refine; no swipe/autocorrect/multilingual layouts |
| AN-04/05 share/deep links/widget | Partial | Text/image share receiver and pairing links; no home-screen widget |
| AN-06 low data | Partial | Images resized to 1280 px; no separate low-data profile |
| AN-08 battery/background | Partial | Foreground bubble only when explicitly enabled; no polling in a background Android service; no battery benchmark |
| Distribution | Testing packages | Self-contained unsigned Windows package and debug-signed Android APK; no store publication or automatic updater |
| Performance/security release gates | Not certified | No native-device latency/battery/grounding benchmarks, penetration test, or store acceptance |

The source specification itself schedules the complete ecosystem across multiple engineering phases. This alpha establishes native installation, real local inference, shared conversations and explicit context input. Advanced automation and the commercial cloud platform remain separate engineering milestones.
