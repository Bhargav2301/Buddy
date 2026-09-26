# Technical Specification: Nexa — Windows + Android AI Buddy Ecosystem

| Field | Value |
|---|---|
| Document | Technical Specification (TRD) v1.0 |
| Source PRD | PRD: Nexa — Voice + Type AI Buddy Ecosystem for Windows and Android (v1.0, 26 Sep 2026) |
| Author | Venkata Sai Bhargav Dhara |
| Status | Draft for engineering review |
| Date | 26 September 2026 |
| References | [HeyClicky](https://www.heyclicky.com/), [HeyClicky changelog](https://www.heyclicky.com/changelog), [Clarift](https://clarift.dpdns.org/) |

---

## 0. How to Read This Document

- Requirement IDs (`IN-01`, `CL-04`, …) come from the PRD. §20 maps every PRD requirement to the components that implement it.
- **MUST / SHOULD / MAY** follow RFC 2119 meaning.
- Where the PRD left a decision open (e.g., OQ3 Windows stack), this spec records the decision as an **ADR** (§3) so it can be reviewed separately.
- Numbers marked **[budget]** are engineering budgets to be verified in Phase 0 benchmarks.

---

## 1. System Overview

Nexa has four client surfaces and one cloud backend:

| Surface | Purpose |
|---|---|
| **Windows desktop app** | Hotkey assistant, screen capture, on-screen overlay, UI Automation grounding and actions, desktop Clarift field watcher |
| **Android app** | Floating bubble, default-assistant entry, MediaProjection capture, overlay, AccessibilityService grounding/actions, Nexa Keyboard (IME) |
| **Browser extension (MV3)** | AI-chat field detection and inline Clarift refinement in Chrome / Edge / Brave; page context for the desktop app |
| **Nexa Cloud** | Auth, sessions ("Buddies"), memory, model routing, realtime voice, grounding, agent orchestration, connectors, scheduler, sync, billing, Clarift adapter |

External dependencies: LLM/voice model providers, Clarift Integration API (proposed), Google / Microsoft / Notion OAuth APIs, FCM (Android push), WNS (Windows push), Stripe + Razorpay (billing).

### 1.1 Context diagram

```
                 ┌──────────────────────┐       ┌──────────────────────┐
                 │  Windows App (.NET)  │       │  Android App (Kotlin)│
                 │  + Native helper C++ │       │  + Nexa Keyboard IME │
                 └─────────┬────────────┘       └──────────┬───────────┘
                           │  Native Messaging             │
                 ┌─────────▼────────────┐                  │
                 │ Browser Ext (MV3/TS) │                  │
                 └─────────┬────────────┘                  │
                           │ HTTPS / WSS (TLS 1.3, JWT)    │
        ┌──────────────────▼───────────────────────────────▼─────────────────┐
        │                         API Gateway (Envoy)                        │
        ├───────────────┬───────────────┬───────────────┬────────────────────┤
        │ Core API      │ Realtime GW   │ Agent Svc     │ Sync Svc           │
        │ (TS/Fastify)  │ (Go, WS)      │ (Py+Temporal) │ (TS, WS + push)    │
        ├───────────────┼───────────────┼───────────────┼────────────────────┤
        │ Grounding Svc │ Model Router  │ Connector Hub │ Clarift Adapter    │
        │ (Py, GPU)     │ (Py)          │ (TS)          │ (TS)               │
        ├───────────────┴───────────────┴───────────────┴────────────────────┤
        │ Postgres 16 (+pgvector) │ Redis 7 │ Object store (R2/S3) │ Temporal │
        └────────────────────────────────────────────────────────────────────┘
               │                 │                  │                │
        LLM / Voice APIs   Google/MS/Notion   Clarift API     Stripe/Razorpay
```

---

## 2. Technology Stack

| Layer | Choice | Notes |
|---|---|---|
| Windows app | C# / .NET 8, WinUI 3 (Windows App SDK 1.6+) | UI, settings, Home |
| Windows native helper | C++20 DLL (P/Invoke) | Low-level keyboard hook, Graphics Capture, Direct2D/DirectComposition overlay, UIA COM client |
| Windows local ML | ONNX Runtime + DirectML; whisper.cpp (Vulkan/CPU); Silero VAD | Offline ASR, VAD, optional small LLM (WN-04) |
| Android app | Kotlin 2.x, Jetpack Compose, Coroutines/Flow, Hilt, Room, WorkManager | min SDK 29, target SDK 35 |
| Android local ML | Android `SpeechRecognizer` (on-device) + whisper.cpp JNI fallback; TFLite prompt-quality model | |
| Browser extension | TypeScript, Manifest V3, Vite build, Preact for card UI (Shadow DOM) | Chrome, Edge, Brave |
| Core API | TypeScript, Node 22, Fastify, Zod, Drizzle ORM | REST + OpenAPI 3.1 |
| Realtime gateway | Go 1.23, `nhooyr/websocket`, Opus codec | Low-latency audio relay |
| AI services | Python 3.12, FastAPI, Pydantic v2 | Model router, grounding, agent workers |
| Workflow engine | Temporal (self-hosted or Temporal Cloud) | Agents, routines, retries |
| Database | PostgreSQL 16 + pgvector (Supabase-compatible) | Primary store, embeddings for memory |
| Cache / queues | Redis 7 (Streams, rate limiting) | |
| Object storage | Cloudflare R2 (S3 API) | Agent output files, reference files (never screenshots) |
| Infra | Kubernetes (GKE/EKS) or Render for early phases; Terraform; Argo CD | |
| Observability | OpenTelemetry → Grafana (Tempo, Loki, Mimir); Sentry for crashes | |
| Feature flags | Unleash (self-hosted) | Remote config for chat-site selectors |
| Billing | Stripe (global), Razorpay (India, UPI) | |

---

## 3. Architecture Decision Records

### ADR-001: Windows client stack — WinUI 3 + C++ helper (resolves PRD OQ3)
- **Context:** Needs deep Win32/UIA access, a low-latency overlay, small idle footprint (≤ 150 MB RAM).
- **Options:** (a) WinUI 3/.NET 8 + C++ helper; (b) Tauri + Rust; (c) Electron.
- **Decision:** (a). UI Automation and Graphics Capture are first-class in C#/C++; WinUI 3 gives native Fluent UI; C++ helper handles hot paths (hooks, capture, overlay).
- **Consequences:** Two languages on Windows; installer ~90 MB with self-contained .NET trimmed. Electron rejected for memory; Tauri deferred due to weaker UIA bindings.

### ADR-002: Screenshots are never persisted
- Frames live only in process memory and in transit. Backend handlers process frames in request scope and zero buffers; object storage never receives frames. Enforced by code review rule + a CI test that fails if any frame type reaches the storage client (§13.3).

### ADR-003: Grounding = accessibility tree first, vision second
- UIA / AccessibilityNodeInfo give exact bounds when available; vision-language model (VLM) resolves ambiguity and handles canvas apps (Blender, games, FL Studio). Hybrid improves pointer accuracy toward the PRD ≥ 85% target.

### ADR-004: Temporal for agents and routines
- Agents are long-running, pause for approvals, retry tool calls, and must survive restarts. Temporal workflows provide durable state, timers (routines), and signals (approve/deny/stop).

### ADR-005: Clarift via adapter with fallback
- All Clarift calls go through `clarift-adapter`, which implements the proposed Clarift API contract (§9.6). If Clarift's partner API is unavailable, the adapter routes to an interim Nexa-hosted refinement engine exposing identical request/response shapes, so clients never change.

### ADR-006: Postgres as single source of truth; CRDT-free sync
- Sync uses server-authoritative, per-entity version vectors with last-writer-wins at field level (§11). Collaborative editing isn't required, so CRDTs aren't justified.

---

## 4. Windows Client Specification

### 4.1 Process model
| Process | Role | Lifetime |
|---|---|---|
| `Nexa.exe` (WinUI 3) | Home, settings, cards, auth, sync client | Starts at login (tray), ~60–90 MB idle |
| `nexa_native.dll` (in-proc) | Hotkey hook, capture, overlay renderer, UIA client | Loaded by `Nexa.exe` |
| `NexaAgentHost.exe` | Isolated computer-use executor (AG-03) running on a secondary desktop | On demand |
| `NexaNativeHost.exe` | Chrome/Edge Native Messaging host bridging extension ⇄ app | Spawned by browser |
| `NexaUpdater.exe` | MSIX / Squirrel-style updater | Scheduled |

Distribution: MSIX (Microsoft Store) + signed EXE installer (EV code-signing cert). Auto-update via MSIX App Installer or delta updates.

### 4.2 Global hotkeys (IN-02, IN-04, CL-06, AG-09)
- Implemented with `RegisterHotKey`; falls back to `SetWindowsHookEx(WH_KEYBOARD_LL)` for push-to-talk hold detection (key-down/key-up).
- Defaults (user-configurable, conflict detection on save):

| Action | Default |
|---|---|
| Summon / capture | `Ctrl+Space` |
| Push-to-talk (hold) | Hold `Ctrl+Space` > 250 ms |
| Toggle Voice ⇄ Type ⇄ Hybrid | `Ctrl+Shift+T` |
| Refine current field (Clarift) | `Ctrl+Alt+R` |
| Region select | `Ctrl+Shift+Space` |
| Stop all agents (kill switch) | `Ctrl+Alt+Esc` |

### 4.3 Screen capture (SC-01, SC-02, SC-07)
- API: `Windows.Graphics.Capture` (`GraphicsCaptureItem` for window or monitor) via Direct3D11 frame pool, single frame per trigger.
- Target selection: foreground window (`GetForegroundWindow`) → its monitor (`MonitorFromWindow`); region select draws a dimmed overlay and crops.
- Output: BGRA → downscale longest edge to 1568 px **[budget]** → WebP q=80 (~150–300 KB). DPI metadata retained for coordinate mapping.
- Capture indicator: yellow border flash + tray icon pulse for 600 ms (required by §13.1).
- Blocklist check (SC-09) runs **before** capture using process name, window title regex, and browser URL (via extension): if matched, capture is refused and the user sees "Nexa can't view this window."

### 4.4 UI tree extraction (SC-04)
- UIA COM client (`IUIAutomation`), `TreeScope_Subtree` from the foreground window with a CacheRequest for `Name, ControlType, BoundingRectangle, AutomationId, IsEnabled, IsOffscreen, Value (masked for password fields)`.
- Limits: max 1,500 nodes, depth 25, 150 ms timeout **[budget]**; truncated breadth-first.
- Serialization: compact JSON (`ui_tree_v1`, §9.3), password/`IsPassword` values dropped client-side.

### 4.5 Overlay renderer (SC-03)
- Per-monitor layered, topmost, click-through window (`WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST | WS_EX_NOACTIVATE`) rendered with DirectComposition + Direct2D at monitor refresh rate.
- Primitives: `arrow`, `circle`, `box`, `highlight`, `step_badge(n)`, `label(text)`, `path(points)` (hand-drawn style with slight jitter for friendliness).
- Anchors: each annotation references either an absolute point (normalized 0–1 in window space) or a UIA element `RuntimeId`. A `WinEventHook` (`EVENT_OBJECT_LOCATIONCHANGE`, `EVENT_SYSTEM_FOREGROUND`) re-anchors within 300 ms or dismisses (SC-03 AC).
- Excluded from own captures via `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)` so Nexa never sees its own drawings and Loom/Teams sharing isn't polluted.

### 4.6 Audio pipeline (IN-04, IN-07, IN-09, IN-10)
- Capture: WASAPI shared mode, 16 kHz mono, 20 ms frames; Silero VAD (ONNX) for end-of-speech (700 ms silence **[budget]**).
- Online: Opus 24 kbps over the realtime WebSocket (§8).
- Offline fallback: whisper.cpp `small` (multilingual) quantized q5, streaming chunks of 3 s.
- Playback: WASAPI; barge-in — user speech during TTS ducks and stops playback within 150 ms.
- Quiet Mode detector (IN-06): polls `IAudioSessionManager2` every 2 s for other processes holding capture (Teams, Zoom, Meet in browser), checks system mute and Calendar busy status (from connector cache). When true → default to Type mode and suppress TTS.

### 4.7 Desktop Clarift field watcher (CL-01)
- Detects focused editable controls in known desktop AI apps (ChatGPT, Claude, Copilot desktop) using UIA focus-changed events + `ValuePattern`/`TextPattern`.
- Per-app detection rules shipped as remote config (`clarift_targets.json`, §9.5.3) keyed by process name + AutomationId/ClassName.
- Badge rendered in the overlay next to the caret bounding rect (`TextPattern.GetSelection().GetBoundingRectangles()`).
- Text replacement: `ValuePattern.SetValue` where supported; else clipboard-safe paste (save clipboard → set → `SendInput Ctrl+A, Ctrl+V` → restore clipboard after 500 ms).

### 4.8 Computer-use executor (AG-03)
- Runs in `NexaAgentHost.exe` on a **separate desktop** (`CreateDesktop`) when the target app supports being launched there; otherwise uses UIA patterns (`InvokePattern`, `ValuePattern`, `SelectionItemPattern`) which don't move the real pointer.
- Synthetic mouse (`SendInput`) is the last resort and requires the user's per-task approval flag `allow_pointer`.
- Action whitelist, rate limit (≤ 5 actions/s), and kill switch hotkey handled in the native helper (not in managed code) so it works when the UI thread is blocked.

### 4.9 Explorer, terminal, Office (WN-01..03)
- Explorer context menu: `IExplorerCommand` sparse-package registration (Windows 11 modern menu) + classic `HKCR\*\shell` fallback on Windows 10. Launches `nexa://ask?file=<path>&action=explain|summarize`.
- Terminal helper: reads Windows Terminal / PowerShell buffer via UIA `TextPattern`; suggestions shown in card; commands only inserted, never executed, unless the user presses Enter.
- Office: UIA for guidance; Microsoft Graph (Word/Excel via OneDrive) for agent edits.

### 4.10 Local storage (Windows)
- `%LOCALAPPDATA%\Nexa\` — SQLite (`nexa.db`, SQLCipher-encrypted, key in DPAPI/Credential Manager) caching Buddies, saved prompts, settings, sync cursors.
- Tokens: Windows Credential Manager (`CredWrite`, per-user).
- Logs: rolling, 7 days, PII-scrubbed.

---

## 5. Android Client Specification

### 5.1 Modules (Gradle)
| Module | Contents |
|---|---|
| `:app` | Compose UI (Home, Buddies, settings), navigation, DI |
| `:core:model`, `:core:data` | Domain models, Room DB, repositories, sync client |
| `:feature:assistant` | Bubble, cards, capture orchestration |
| `:service:overlay` | `SYSTEM_ALERT_WINDOW` overlay + drawing engine |
| `:service:a11y` | `NexaAccessibilityService` (UI tree, actions, Clarift field watch) |
| `:service:voice` | `NexaVoiceInteractionService` (default assistant), audio pipeline |
| `:ime` | `NexaKeyboardService` (InputMethodService) |
| `:ml` | whisper.cpp JNI, TFLite prompt-quality model |
| `:connectors` | OAuth flows (AppAuth), share-sheet receiver |

### 5.2 Entry points (AN-01, AN-02, AN-04, AN-05)
| Entry | Implementation |
|---|---|
| Floating bubble | Foreground service `BubbleService` (type `specialUse`) + `TYPE_APPLICATION_OVERLAY` view; auto-hide when `WindowInsets` report immersive full-screen |
| Default assistant | `VoiceInteractionService` + `VoiceInteractionSessionService`; long-press home/power; receives `AssistStructure` + screenshot from the system **with user's assistant consent** — preferred capture path (no MediaProjection prompt) |
| Quick Settings tile | `TileService` |
| Widget | Glance app widget |
| Share sheet | `ACTION_SEND` / `ACTION_SEND_MULTIPLE` activity ("Ask Nexa about this") |
| Deep links | `nexa://` + App Links `https://nexa.app/b/<id>` |

### 5.3 Screen capture (SC-01, SC-10)
Priority order:
1. **Assist API** (when Nexa is default assistant): `onHandleScreenshot(Bitmap)` + `onHandleAssist(AssistStructure)`.
2. **MediaProjection** (bubble path): user consent per session (Android 14+ requires consent every session; single-app capture supported on Android 14 QPR2+). One frame via `ImageReader`, then projection stopped immediately.
3. **Accessibility `takeScreenshot()`** (API 30+) only when user enabled the a11y service and chose "Faster capture."

Secure windows: frames from `FLAG_SECURE` windows are black by OS design; Nexa MUST NOT attempt workarounds. Blocklist (SC-09) checked via the foreground package name from `AccessibilityEvent` / `UsageStatsManager` before capture.

Encoding: longest edge 1280 px **[budget]**, WebP q=75, low-data mode (AN-06) → 960 px q=60.

### 5.4 Accessibility service (SC-04, AG-04, CL-01)
- Config: `canRetrieveWindowContent=true`, `canPerformGestures=true`, event types `typeWindowStateChanged|typeViewFocused|typeViewTextChanged|typeWindowContentChanged`, `notificationTimeout=100`.
- `packageNames` left unrestricted but events from blocklisted packages are dropped at the first line of `onAccessibilityEvent`.
- UI tree: `rootInActiveWindow` traversal (max 1,000 nodes, 100 ms **[budget]**), password nodes (`isPassword`) masked.
- Actions: `ACTION_CLICK`, `ACTION_SET_TEXT`, `ACTION_SCROLL_FORWARD`, `dispatchGesture` for coordinates; visible "Nexa is working" chip overlay during agent runs (AG-04).
- **Play policy compliance:** prominent disclosure screen before enabling; declared `isAccessibilityTool=false`; Play Console Accessibility declaration + demo video. Reduced-permission mode works without the service (bubble + MediaProjection + keyboard) — see risk §19.

### 5.5 Overlay (SC-03)
- Full-screen `TYPE_APPLICATION_OVERLAY` with `FLAG_NOT_TOUCHABLE | FLAG_NOT_FOCUSABLE` for drawings; separate touchable window for cards.
- Compose Canvas drawing engine sharing the annotation schema (§9.4) with Windows.
- Re-anchor on `TYPE_WINDOW_CONTENT_CHANGED` by node `viewIdResourceName` + bounds matching.

### 5.6 Nexa Keyboard (AN-03, CL-*, IN-*)
- `InputMethodService` built on a forked open-source keyboard core (e.g., HeliBoard, GPL-3.0 — **legal review required**; alternative: in-house Compose keyboard).
- Toolbar: `🎤/⌨ mode toggle`, `✨ Refine` (Clarift), `/ saved prompts`, `Nexa` (open assistant with current field context).
- Target detection: `EditorInfo.packageName` matched against `clarift_targets.json`; refine badge only shown in AI-chat packages unless the user enables "all apps" (PRD OQ6).
- Replacement: `InputConnection.beginBatchEdit()` → `deleteSurroundingText` / `setComposingText` → `commitText` → `endBatchEdit()`; undo buffer kept 30 s (CL-08).
- No keystroke logging; the prompt text is read only on explicit refine or on typing pause in AI-chat targets with auto-refine enabled.

### 5.7 Audio (IN-*)
- `AudioRecord` 16 kHz mono, `VOICE_COMMUNICATION` source for echo cancellation; Silero VAD TFLite; Opus via `libopus` JNI.
- Offline: on-device `SpeechRecognizer` (`EXTRA_PREFER_OFFLINE`) where available; whisper.cpp `base` fallback.
- Language: auto + user-selected; code-mixed Telugu-English/Hindi-English uses the server ASR model tuned for Indic (§7.2).

### 5.8 Battery & background (AN-08)
- No background capture; foreground service only while bubble visible.
- Agent work runs server-side; the device only executes UI actions when an agent needs on-device control.
- `BatteryManager` < 15% and not charging → local agent actions paused, notification shown.
- WorkManager for sync (constraints: network) every 15 min + push-triggered.

### 5.9 Local storage (Android)
- Room + SQLCipher; keys in Android Keystore (StrongBox when available).
- Encrypted DataStore for settings.

---

## 6. Browser Extension Specification (WN-05, CL-01..09)

### 6.1 Components
| Part | Role |
|---|---|
| `service_worker.ts` | Auth token relay, API calls to Clarift adapter, remote-config fetch, Native Messaging to desktop app |
| `content_script.ts` | Field detection, typing observer, badge + card UI in Shadow DOM, text replacement |
| `popup.html` | Account, per-site toggles, mode defaults, units remaining |
| `offscreen.html` | Local prompt-quality model (ONNX Runtime Web, WASM) |

Permissions: `storage`, `scripting`, `nativeMessaging`, `activeTab`; host permissions requested **per site** (optional permissions) for AI-chat domains only.

### 6.2 Field detection
1. Site-specific selectors from remote config, e.g.:
```json
{
  "id": "chatgpt_web",
  "match": ["https://chatgpt.com/*", "https://chat.openai.com/*"],
  "input_selector": "#prompt-textarea, div[contenteditable='true'][id='prompt-textarea']",
  "send_selector": "button[data-testid='send-button']",
  "kind": "contenteditable",
  "version": 14
}
```
2. Generic fallback: largest visible `textarea` or `[contenteditable=true]` near bottom of viewport on pages whose `<title>`/URL matches the AI-chat classifier list.
3. `MutationObserver` re-binds on SPA navigation.

### 6.3 Typing observer & badge (CL-03)
- Debounce 1,200 ms after last `input` event; skip if length < 12 chars or > 20,000 chars.
- Local score via on-device model (§9.5.1); if score < threshold (default 60) show underline (CSS `text-decoration` on a mirrored overlay element, never mutating the site's DOM text) and a badge.
- Optional server Evaluator call (CL-17) only when the user hovers/clicks the badge or has "live score" enabled.

### 6.4 Text replacement (CL-04, CL-07, CL-08)
- `textarea`: native value setter via `Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype,'value').set` then dispatch `input` event (React-safe).
- `contenteditable` (ProseMirror/Lexical): select all within the element, `document.execCommand('insertText', false, text)` (still the most compatible path), fallback to synthetic `beforeinput` + `ClipboardEvent('paste')`.
- **Never** click the send button. A unit test asserts `send_selector` is never touched.
- Undo: original text stored in memory; toast "Undo" for 30 s; `Ctrl+Z` intercepted only while toast is visible.

### 6.5 Desktop bridge
- Native Messaging host `com.nexa.bridge` lets the desktop app (a) read the active tab URL for blocklist checks, (b) request page text for context, (c) share auth session. Messages are JSON, max 1 MB, schema-validated both sides.

---

## 7. Cloud Backend Services

### 7.1 Service catalog
| Service | Lang | Responsibilities | Scale unit |
|---|---|---|---|
| `api-gateway` | Envoy | TLS termination, JWT validation, rate limits, routing | HPA on RPS |
| `core-api` | TS/Fastify | Users, devices, Buddies, messages, memory, settings, saved prompts, usage, billing webhooks | Stateless pods |
| `realtime-gw` | Go | WebSocket sessions for Talk; audio relay to voice model; tool-call bridge | Sticky by session; ~2k concurrent/pod **[budget]** |
| `model-router` | Py | Policy-based model selection, prompt assembly, caching, fallbacks, cost metering | Stateless |
| `grounding-svc` | Py (GPU) | UI tree + frame → annotations (targets and coordinates) | GPU pool (L4) or provider API |
| `agent-svc` | Py + Temporal workers | Agent planning, tool execution, approvals, device action dispatch, routines | Temporal task queues |
| `connector-hub` | TS | OAuth token vault, Gmail/Calendar/Drive/Notion/M365 wrappers | Stateless |
| `clarift-adapter` | TS | Refine/evaluate/convert/saved/templates/usage; fallback engine routing | Stateless |
| `refine-engine` (fallback) | Py | Nexa-hosted Quick/Guided/Council pipelines | Stateless |
| `sync-svc` | TS | Change feed, device cursors, handoff, cross-device send, push (FCM/WNS) | WS + Redis Streams |
| `billing-svc` | TS | Plans, entitlements, metering, Stripe/Razorpay | Stateless |
| `notify-svc` | TS | Push/email notifications, routine announcements | Queue consumer |

### 7.2 Model routing (model-router)
Route table (config, hot-reloadable):

| Task | Primary | Fallback | Max latency |
|---|---|---|---|
| `talk.realtime` | Realtime speech-to-speech model (e.g., OpenAI Realtime class) | ASR → LLM → TTS cascade | first audio ≤ 1.2 s p50 |
| `talk.indic` | Cascade: Indic ASR (e.g., Sarvam/AI4Bharat-class) → multilingual LLM → Indic TTS | Realtime model | ≤ 2.0 s p50 |
| `vision.answer` | Frontier VLM | Secondary VLM | ≤ 4 s |
| `grounding.locate` | Grounding VLM (UI-specialized) + tree matcher | Frontier VLM with set-of-marks | ≤ 1.5 s |
| `agent.plan` | Reasoning model | Frontier LLM | n/a (async) |
| `refine.*` | Clarift API | `refine-engine` | per mode, §9.6 |
| `summary.memory` | Small cheap LLM | — | async |

Policies: plan-based access (Free → cheaper tier), per-user token budgets, prompt caching of system prompts, circuit breakers (5 consecutive failures → fallback for 60 s).

### 7.3 Grounding pipeline (grounding-svc) — SC-03/04
```
Input: frame (WebP), ui_tree_v1 (optional), user_query, app_hint
 1. Pre-filter tree: visible, enabled, on-screen, has name/role
 2. Candidate retrieval: embed query + node labels → top-k (k=20)
 3. Set-of-Marks: draw numeric marks on candidates in the frame
 4. VLM call: "Which mark(s) satisfy <query>? Return steps."
 5. If no tree / canvas app: VLM direct coordinate prediction (normalized)
 6. Confidence = f(VLM logprob, tree-match score, agreement)
 7. If confidence < 0.55 → return "uncertain" + text-only guidance
Output: annotations[] (§9.4), steps[], confidence
```
Benchmark harness: 200 tasks × 20 apps (Excel, Chrome, VS Code, Blender, DaVinci Resolve, FL Studio, Figma, Settings, WhatsApp, Gmail app, …) with labeled target boxes; metric = hit if predicted point inside target box; release gate ≥ 85%.

### 7.4 Step-completion detection (SC-05)
- Client sends lightweight signals (UIA/a11y events + perceptual hash of the target region every 500 ms while a walkthrough is active — hash only, no frame upload).
- Hash delta > threshold or expected element state change (e.g., `ToggleState`, new window) → client requests next step; server may request a fresh frame with the user's standing consent for that walkthrough.

---

## 8. Realtime Voice Protocol (Talk)

### 8.1 Transport
`wss://rt.nexa.app/v1/talk?buddy_id=<uuid>` — JWT in `Sec-WebSocket-Protocol` header. Binary frames = audio; text frames = JSON events.

### 8.2 Client → server events
| Event | Payload |
|---|---|
| `session.start` | `{input_mode: "voice"|"type"|"hybrid", lang, voice, tts_enabled, device_id, app_context:{process, title, url?}}` |
| `context.frame` | `{frame_id, mime:"image/webp", width, height, dpi, bytes_b64}` (≤ 600 KB) |
| `context.ui_tree` | `{frame_id, tree: ui_tree_v1}` |
| `input.audio` (binary) | Opus 20 ms packets, seq header 4 bytes |
| `input.audio.end` | `{}` (push-to-talk released / VAD end) |
| `input.text` | `{text, source:"typed"|"dictation_edited"}` |
| `response.cancel` | barge-in |
| `tool.result` | `{call_id, result}` for client-side tools (e.g., `read_selection`) |

### 8.3 Server → client events
| Event | Payload |
|---|---|
| `transcript.partial` / `transcript.final` | `{text}` (Hybrid mode: final is **not** auto-sent; client shows editable text — IN-01) |
| `response.text.delta` | `{text}` |
| `response.audio` (binary) | Opus |
| `overlay.draw` | `{annotations: Annotation[]}` |
| `overlay.clear` | `{}` |
| `tool.call` | `{call_id, name, args}` (client tools) |
| `agent.spawned` | `{agent_run_id}` |
| `usage.update` | `{talk_remaining, agent_remaining}` |
| `error` | `{code, message, retryable}` |

### 8.4 Mode semantics (IN-01, IN-07)
| Mode | Behavior |
|---|---|
| Voice | Audio streamed; `transcript.final` auto-committed as the user turn |
| Type | No audio; `input.text` only; TTS obeys `tts_enabled` |
| Hybrid | Audio streamed; `transcript.final` returned to client for editing; user submits via `input.text{source:"dictation_edited"}` |

Toggle mid-session sends `session.update{input_mode}`; the server stops consuming audio within one frame (IN-02 AC: ≤ 150 ms end-to-end measured on client).

---

## 9. Data Contracts

### 9.1 Core entities (PostgreSQL)

```sql
CREATE TABLE users (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  email CITEXT UNIQUE NOT NULL,
  display_name TEXT,
  locale TEXT DEFAULT 'en-IN',
  plan TEXT NOT NULL DEFAULT 'free',          -- free|pro|max|student
  created_at TIMESTAMPTZ DEFAULT now(),
  deleted_at TIMESTAMPTZ
);

CREATE TABLE devices (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID REFERENCES users(id) ON DELETE CASCADE,
  platform TEXT NOT NULL,                     -- windows|android|extension
  name TEXT, app_version TEXT, os_version TEXT,
  push_token TEXT, last_seen_at TIMESTAMPTZ,
  revoked_at TIMESTAMPTZ
);

CREATE TABLE buddies (                         -- conversations / tasks
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID REFERENCES users(id) ON DELETE CASCADE,
  title TEXT, kind TEXT NOT NULL,             -- talk|agent|walkthrough
  pinned BOOLEAN DEFAULT false, archived BOOLEAN DEFAULT false,
  origin_device_id UUID REFERENCES devices(id),
  version BIGINT NOT NULL DEFAULT 1,
  created_at TIMESTAMPTZ DEFAULT now(), updated_at TIMESTAMPTZ DEFAULT now()
);

CREATE TABLE messages (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  buddy_id UUID REFERENCES buddies(id) ON DELETE CASCADE,
  role TEXT NOT NULL,                          -- user|assistant|tool|system
  input_mode TEXT,                             -- voice|type|hybrid
  content JSONB NOT NULL,                      -- text, annotations, tool refs (NO images)
  screen_summary TEXT,                         -- short text summary of screen, never pixels
  created_at TIMESTAMPTZ DEFAULT now()
);

CREATE TABLE memories (                        -- HM-03, HM-04
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID REFERENCES users(id) ON DELETE CASCADE,
  kind TEXT NOT NULL,                          -- preference|fact|session_summary
  text TEXT NOT NULL,
  embedding VECTOR(1024),
  source_buddy_id UUID,
  created_at TIMESTAMPTZ DEFAULT now(), deleted_at TIMESTAMPTZ
);
CREATE INDEX ON memories USING hnsw (embedding vector_cosine_ops);

CREATE TABLE agent_runs (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID REFERENCES users(id),
  buddy_id UUID REFERENCES buddies(id),
  temporal_workflow_id TEXT UNIQUE,
  status TEXT NOT NULL,                        -- queued|running|awaiting_approval|succeeded|failed|cancelled
  goal TEXT NOT NULL,
  target_device_id UUID,
  steps JSONB DEFAULT '[]',
  created_at TIMESTAMPTZ DEFAULT now(), finished_at TIMESTAMPTZ
);

CREATE TABLE approvals (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  agent_run_id UUID REFERENCES agent_runs(id) ON DELETE CASCADE,
  action_class TEXT NOT NULL,                  -- send|post|purchase|delete|install|pointer
  summary TEXT NOT NULL, payload_preview JSONB,
  decision TEXT,                               -- approved|denied|expired
  decided_by_device UUID, decided_at TIMESTAMPTZ,
  expires_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE artifacts (                       -- AG-08 files
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID REFERENCES users(id), agent_run_id UUID,
  name TEXT, mime TEXT, size_bytes BIGINT, storage_key TEXT,
  thumbnail_key TEXT, created_at TIMESTAMPTZ DEFAULT now()
);

CREATE TABLE routines (                        -- AG-07
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID REFERENCES users(id),
  name TEXT, instruction TEXT NOT NULL,
  cron TEXT NOT NULL, timezone TEXT NOT NULL DEFAULT 'Asia/Kolkata',
  announce BOOLEAN DEFAULT true, active BOOLEAN DEFAULT true,
  temporal_schedule_id TEXT, created_at TIMESTAMPTZ DEFAULT now()
);

CREATE TABLE connector_accounts (
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID REFERENCES users(id),
  provider TEXT NOT NULL,                      -- google|microsoft|notion|clarift
  scopes TEXT[] NOT NULL,
  token_ref TEXT NOT NULL,                     -- pointer into KMS-encrypted vault
  status TEXT DEFAULT 'active', created_at TIMESTAMPTZ DEFAULT now()
);

CREATE TABLE saved_prompts (                   -- CL-16
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID REFERENCES users(id),
  clarift_id TEXT,                             -- if mirrored in Clarift
  title TEXT, body TEXT NOT NULL, technique TEXT, tags TEXT[],
  version BIGINT DEFAULT 1, updated_at TIMESTAMPTZ DEFAULT now()
);

CREATE TABLE refine_events (                   -- CL analytics (CL-19), no prompt text by default
  id BIGSERIAL PRIMARY KEY,
  user_id UUID, device_id UUID, target_app TEXT,
  mode TEXT, technique TEXT, units_used INT,
  score_before INT, score_after INT,
  outcome TEXT,                                -- accepted|edited|dismissed|undone|error
  latency_ms INT, created_at TIMESTAMPTZ DEFAULT now()
);

CREATE TABLE usage_ledger (                    -- metering
  id BIGSERIAL PRIMARY KEY,
  user_id UUID, meter TEXT,                    -- talk_msg|agent_msg|clarift_unit|dictation_sec
  quantity INT, period_start DATE,
  ref_id TEXT, created_at TIMESTAMPTZ DEFAULT now()
);

CREATE TABLE settings (
  user_id UUID REFERENCES users(id), device_id UUID NULL,  -- NULL = global
  key TEXT, value JSONB, version BIGINT DEFAULT 1,
  PRIMARY KEY (user_id, device_id, key)
);

CREATE TABLE blocklist_rules (                 -- SC-09 user extensions
  id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
  user_id UUID, platform TEXT,
  match_type TEXT,                             -- process|package|url|title_regex
  pattern TEXT, created_at TIMESTAMPTZ DEFAULT now()
);
```
Row-Level Security: every table filtered by `user_id = auth.uid()` (Supabase-compatible policies) for any direct client reads; services use a service role.

### 9.2 Settings keys (selected)
| Key | Type | Default |
|---|---|---|
| `input.default_mode` | `voice|type|hybrid` | `voice` (Windows), `type` (Android keyboard) |
| `input.mode_per_app` | `map<app_id, mode>` | `{}` |
| `input.quiet_mode_auto` | bool | `true` |
| `voice.tts_enabled`, `voice.speed`, `voice.id` | | `true`, `1.0`, `"warm"` |
| `clarift.enabled` | bool | `true` |
| `clarift.automation` | `suggest|auto_quick|ask_heavy` | `suggest` |
| `clarift.default_mode` | `quick|guided|council|auto` | `auto` |
| `clarift.technique_per_app` | map | `{}` |
| `clarift.excluded_apps` | string[] | `[]` |
| `clarift.private_mode` | bool | `false` |
| `clarift.score_threshold` | int | `60` |
| `privacy.faster_capture_a11y` | bool | `false` |
| `agents.suggestions_enabled` | bool | `false` |

### 9.3 `ui_tree_v1`
```json
{
  "v": 1,
  "platform": "windows",
  "window": {"title": "Untitled - Excel", "process": "EXCEL.EXE", "bounds": [0,0,1920,1080], "dpi": 144},
  "nodes": [
    {"id": "n12", "rid": "42.1234.4.5", "role": "Button", "name": "Insert", "bounds": [312,64,368,92],
     "enabled": true, "parent": "n3"}
  ],
  "truncated": false
}
```
Android uses `rid = viewIdResourceName` and `role = className` short form.

### 9.4 Annotation schema (shared by both overlays)
```json
{
  "id": "a1",
  "type": "arrow|circle|box|highlight|step_badge|label|path",
  "anchor": {"node_rid": "42.1234.4.5"} ,
  "point": {"x": 0.182, "y": 0.071},
  "size": {"w": 0.03, "h": 0.026},
  "step": 1,
  "text": "Click Insert",
  "style": {"color": "accent", "stroke": 3, "handdrawn": true},
  "ttl_ms": 15000
}
```
`point`/`size` normalized to window bounds; `anchor` takes precedence when resolvable.

### 9.5 Clarift client-side data

#### 9.5.1 Local prompt-quality model (CL-03, CL-20 fallback)
- Features: length, presence of role/context/goal/format/constraints markers, question-only detection, vague-verb list ("make", "do", "something"), language ID, domain classifier (coding/writing/research/image/data).
- Model: gradient-boosted trees → ONNX (≤ 300 KB) + rules; output `score 0–100`, `missing[]` (e.g., `["output_format","audience","success_criteria"]`), `domain`.
- Used for: underline trigger, auto-mode selection, and offline/zero-units "suggest-only" tips (template-based rewrite hints, not a full refine).

#### 9.5.2 Auto mode & technique selection (CL-11, CL-12)
```
mode = user_override
    ?? (len > 800 || flagged_important)          -> "council"
    ?? (domain in {coding, spec, data} || len > 250) -> "guided"
    ?? "quick"
technique = per_app_pin
    ?? {math|logic: "chain_of_thought", planning: "tree_of_thoughts",
        agent_ide: "react", classification: "few_shot",
        review: "role_persona", workflow: "prompt_chaining",
        explanation: "meta_reflection"}[domain]
    ?? "zero_shot"
```
Heavy modes (guided/council) under `ask_heavy` automation always require an explicit click/voice confirmation showing unit cost.

#### 9.5.3 `clarift_targets.json` (remote config)
```json
{
  "version": 37,
  "web": [ { "id": "chatgpt_web", "match": ["https://chatgpt.com/*"], "input_selector": "#prompt-textarea", "kind": "contenteditable" } ],
  "windows": [ { "id": "chatgpt_desktop", "process": "ChatGPT.exe", "uia": {"control_type": "Edit", "automation_id": "PromptTextBox"} } ],
  "android": [ { "id": "chatgpt_android", "package": "com.openai.chatgpt" },
               { "id": "claude_android", "package": "com.anthropic.claude" },
               { "id": "gemini_android", "package": "com.google.android.apps.bard" },
               { "id": "perplexity_android", "package": "ai.perplexity.app.android" } ]
}
```
Signed (Ed25519) and cached; clients reject unsigned config.

### 9.6 Clarift Integration API (proposed contract, implemented by `clarift-adapter`)
Mirrors Clarift's modes (Quick Refine 1 unit, Guided Fix 2 units, Full Council 3 units), eight techniques, templates, reference files ≤ 10 MB, Projects, Saved, Evaluator, Converter, Analytics, and unit quotas as shown in the [Clarift workspace](https://clarift.dpdns.org/).

```
POST /v1/refine
{
  "prompt": "write a resume summary for data science",
  "mode": "quick" | "guided" | "council",
  "technique": "zero_shot" | "few_shot" | "chain_of_thought" | "tree_of_thoughts" |
               "role_persona" | "prompt_chaining" | "react" | "meta_reflection" | null,
  "template_id": null, "project_id": null,
  "reference_file_ids": [],
  "target": "chatgpt" | "claude" | "gemini" | "perplexity" | "image" | "agent" | "generic",
  "client": {"surface": "extension|windows|android_ime", "app_id": "chatgpt_web"},
  "idempotency_key": "uuid"
}
→ 200
{
  "refined_prompt": "...",
  "changes": [{"type": "added_context|added_success_criteria|set_output_format|added_constraints|clarified_scope|role_added", "explanation": "..."}],
  "score_before": 42, "score_after": 86,
  "units_used": 1, "units_remaining": {"daily": 29, "monthly": 536},
  "engine": "clarift" | "nexa_fallback",
  "latency_ms": 2140
}
Errors: 402 QUOTA_EXHAUSTED, 413 PROMPT_TOO_LARGE, 429 RATE_LIMITED, 503 UPSTREAM_UNAVAILABLE
```
Other endpoints: `POST /v1/evaluate`, `POST /v1/convert`, `GET|POST|PATCH|DELETE /v1/saved`, `GET /v1/templates`, `GET /v1/projects`, `POST /v1/files` (multipart ≤ 10 MB, returns `file_id`, 24 h TTL), `GET /v1/usage`.

Latency SLOs (p50 / p95) **[budget]**: quick 3 s / 6 s; guided 7 s / 12 s; council 12 s / 20 s. Council and Guided stream partial passes via SSE (`Accept: text/event-stream`, events `pass.start`, `pass.done`, `final`) so the card shows progress.

Auth: OAuth 2.0 Authorization Code + PKCE; Nexa stores Clarift refresh token in the connector vault; scopes `refine`, `evaluate`, `saved:rw`, `templates:r`, `projects:r`, `usage:r`.

Fallback engine (`refine-engine`) pipelines:
| Mode | Pipeline |
|---|---|
| quick | 1 LLM pass: intent-preserving rewrite with explicit task, context, constraints, output format, success criteria |
| guided | 3 passes: Structure expert → Critic → Editor |
| council | 5 specialists (Domain, Structure, Clarity, Safety/Constraints, Output-format) in parallel → Synthesizer |
All passes include an **intent-preservation check** (embedding similarity original vs. refined ≥ 0.80 and an LLM judge); failures return the original with tips.

---

## 10. Agent System (agent-svc)

### 10.1 Workflow (Temporal)
```
AgentRunWorkflow(goal, user_id, buddy_id, target_device?)
  ├─ PlanActivity            → plan{steps[], required_tools[], risk_classes[]}
  ├─ loop step in plan:
  │    ├─ if step.risk in {send, post, purchase, delete, install, pointer}:
  │    │     RequestApproval → wait Signal(approve|deny) with timeout 30 min
  │    ├─ ExecuteToolActivity (server tool) | DispatchDeviceAction (client tool)
  │    ├─ ObserveActivity (results / fresh UI tree)
  │    └─ Replan if deviation
  ├─ ProduceArtifacts (docs/sheets/CSV) → R2
  └─ Notify (card update, announcement, push)
Signals: approve(approval_id), deny(approval_id), stop(), user_followup(text)
Queries: status(), steps()
```
Kill switch (AG-09): client hotkey → `POST /v1/agents/{id}/stop` and a local immediate halt of device actions; workflow receives `stop()` and cancels activities within 2 s.

### 10.2 Tool registry
| Tool | Side | Risk class | Notes |
|---|---|---|---|
| `gmail.search`, `gmail.read` | server | none | |
| `gmail.draft` | server | none | |
| `gmail.send` | server | send | approval |
| `calendar.list`, `calendar.create_event` | server | none / send (if attendees) | |
| `drive.search`, `drive.create_file` | server | none | |
| `notion.search`, `notion.create_page`, `notion.update_page` | server | none | |
| `m365.mail.*`, `m365.calendar.*`, `onedrive.*`, `teams.post` | server | send for post | P1 |
| `files.write_doc`, `files.write_sheet`, `files.write_csv` | server | none | AG-08 conventions: answer first, short columns, one link column |
| `web.search`, `web.fetch` | server | none | |
| `device.ui_tree`, `device.screenshot` | client | none (consent per task) | |
| `device.click`, `device.type`, `device.scroll`, `device.open_app` | client | pointer (Windows synthetic input) / none (UIA/a11y patterns) | |
| `device.run_command` | client | install/delete class if destructive | Windows only, never auto-executes |
| `clarift.refine` | server | none | agents may refine their own sub-prompts |

Tool schemas are JSON Schema; the planner receives only tools permitted by plan + connected accounts + device capabilities.

### 10.3 Device action channel
- Server → device via the sync WebSocket (`device.action` event) or FCM/WNS wake-up + fetch.
- Each action: `{action_id, run_id, type, target(node_rid|point), args, deadline_ms}`; device replies `{action_id, ok, observation(ui_tree diff), error?}`.
- Device refuses actions targeting blocklisted apps or when battery guard is active (AN-08).

### 10.4 Routines (AG-07)
- NL schedule → cron via LLM + validator (`cron-parser`), confirmation shown to user.
- Temporal Schedule per routine; `timezone` honored; Do Not Disturb silences announcements only.
- Suggestions (AG-10) job: nightly per user, uses last 2 days of message summaries; auto-pause after 3 unopened mornings.

---

## 11. Sync & Cross-Device Ecosystem (EC-*)

### 11.1 Change feed
- Every mutable entity has `version BIGINT`; writes increment version and append to `change_log(user_id, entity, id, version, op, at)`.
- Clients hold a cursor per user; `GET /v1/sync?cursor=<n>` returns ordered changes (max 500) + next cursor; live updates via `wss://sync.nexa.app/v1` (`change` events).
- Conflict: field-level last-writer-wins using server receive time; text bodies of saved prompts keep the losing version in `saved_prompt_revisions`.

### 11.2 Handoff (EC-02)
- `POST /v1/handoff {buddy_id, to_device_id?}` → push to target (or all devices) with deep link `nexa://buddy/<id>?resume=1`.
- Home shows "Continue from <device>" banner for Buddies updated on another device within the last 30 min.

### 11.3 Cross-device send (EC-03) and clipboard (EC-07)
- Files ≤ 100 MB via presigned R2 upload, E2E-encrypted with a per-user device key pair (X25519; key exchange at device linking); TTL 24 h.
- Clipboard bridge (P2): opt-in, text only, E2E-encrypted, never stored server-side beyond delivery.

### 11.4 Phone as remote / second screen (EC-04, EC-05)
- Phone opens a `remote` session bound to a PC device; audio from phone streams to `realtime-gw` with `target_device_id=PC`; overlay events route to PC; walkthrough steps mirror to phone as a list.

### 11.5 Notifications (EC-06)
- `notify-svc` picks the preferred device (`settings.notifications.preferred_device`) else most recently active; fallback all.

---

## 12. Public REST API (core-api) — summary

Base `https://api.nexa.app/v1`, JWT bearer, JSON, OpenAPI 3.1 published.

| Method & Path | Purpose | PRD |
|---|---|---|
| `POST /auth/oauth/{google|microsoft}` , `POST /auth/refresh` | Sign-in | EC-01 |
| `POST /devices`, `DELETE /devices/{id}` | Register/revoke device | EC-01 |
| `GET/POST /buddies`, `PATCH /buddies/{id}` (pin, archive, title) | Home | HM-01 |
| `GET /buddies/{id}/messages` | History | HM-01 |
| `POST /ask` | Non-realtime typed ask (Type mode fallback, extension) | IN-01 |
| `POST /ground` | Frame + tree → annotations | SC-03 |
| `GET/DELETE /memories`, `POST /memories` | Memory view/delete | HM-03 |
| `POST /agents`, `GET /agents/{id}`, `POST /agents/{id}/stop`, `POST /agents/{id}/followup` | Agents | AG-* |
| `POST /approvals/{id}` `{decision}` | Approvals | AG-05 |
| `GET/POST/PATCH/DELETE /routines` | Routines | AG-07 |
| `GET /artifacts/{id}/download` | Presigned URL | AG-08 |
| `GET/PUT /settings` | Settings | HM-05 |
| `GET /usage` | Talk/agent/Clarift units | HM-06, CL-20 |
| `POST /connectors/{provider}/connect`, `DELETE /connectors/{id}` | Connectors | AG-02 |
| `/clarift/*` | Proxied to clarift-adapter (§9.6) | CL-* |
| `GET /sync`, `POST /handoff`, `POST /send` | Ecosystem | EC-* |
| `POST /account/export`, `DELETE /account` | Data rights | §13 |

Error model: `{"error": {"code": "QUOTA_EXHAUSTED", "message": "...", "retryable": false, "details": {}}}`. Rate limits: 60 req/min/user default; `/ground` 20/min; `/clarift/refine` 30/min.

---

## 13. Security & Privacy Engineering

### 13.1 Capture consent & indicators
- Every capture requires a user trigger event ID (hotkey/bubble/assist) propagated in `context.frame.trigger_id`; server rejects frames without a valid, recent (< 10 s) trigger token signed by the client session key.
- Visible indicator on each capture (Windows border flash, Android system MediaProjection chip + Nexa pulse).

### 13.2 Client-side redaction
- Before upload: regex + ML redaction on the UI tree text and OCR of frame regions for card numbers (Luhn), Aadhaar (Verhoeff), PAN, IFSC/UPI IDs, email/OTP in SMS notifications, password fields. Redacted regions blurred in the frame (Gaussian σ=12).
- Default blocklist includes major Indian banking/UPI apps (e.g., `net.one97.paytm`, `com.phonepe.app`, `com.google.android.apps.nbu.paisa.user`, bank apps), password managers, `chrome://` incognito, and Windows Credential UI.

### 13.3 No-persistence guarantees (ADR-002)
- Frame types (`FrameBytes`) are distinct types in Go/Python/TS; the storage client's type signature rejects them; CI contract test `test_frames_never_persisted`.
- Model provider contracts: zero data retention (ZDR) endpoints where available; otherwise excluded from training by contract.
- Logs: frames never logged; prompt text logged only as hash + length unless user opts into diagnostics.

### 13.4 Secrets & tokens
- Server: connector tokens encrypted with envelope encryption (Cloud KMS, per-user DEK); `token_ref` only in Postgres.
- Client: Credential Manager / Android Keystore; refresh tokens rotated; device revocation invalidates all tokens within 60 s (Redis denylist).

### 13.5 Transport & platform
- TLS 1.3 only, HSTS, certificate pinning (SPKI) in mobile and desktop clients with backup pins.
- Windows binaries EV-signed; Android App Bundle with Play App Signing; extension published via Chrome Web Store / Edge Add-ons.

### 13.6 Agent safety
- Approval required for risk classes; approvals expire (30 min); payloads shown verbatim (email body, recipients).
- Prompt-injection defense: tool outputs and page content wrapped as untrusted data; planner system prompt forbids following instructions from content; high-risk actions require re-confirmation if triggered after reading external content.
- Per-run budgets: max 50 steps, 20 min wall-clock (configurable up to 2 h for Max plan).

### 13.7 Compliance
- India DPDP Act 2023: consent notices, purpose limitation, data principal rights (export/delete), grievance officer contact.
- GDPR-ready: DPA with processors, data export (`POST /account/export` → ZIP within 72 h), deletion within 30 days including backups rotation.
- Google Play: Accessibility API declaration, prominent disclosure, Data safety form; Microsoft Store policies.
- Children: age gate 13+ (16+ where required).

### 13.8 Threat model (STRIDE highlights)
| Threat | Mitigation |
|---|---|
| Malicious site injects instructions into refined prompt | Refine engine treats page content as data; only user-typed text is refined |
| Extension used to exfiltrate chats | Per-site optional permissions, no background reading, open-source content script for audit |
| Stolen device token | Short-lived JWT (15 min), device-bound refresh, remote revoke |
| Agent spoofed approval | Approvals signed by device key; server verifies device ownership |
| Overlay clickjacking | Overlay is click-through; interactive cards never overlap system permission dialogs (detected via window class) |

---

## 14. Performance Budgets

| Metric | Budget | Measured at |
|---|---|---|
| Summon → UI ready | ≤ 300 ms p95 | Client trace |
| Capture + encode | ≤ 120 ms (Win), ≤ 250 ms (Android) | Client |
| UI tree extraction | ≤ 150 ms (Win), ≤ 100 ms (Android) | Client |
| Mode toggle | ≤ 150 ms | Client |
| First audio token (Talk) | ≤ 1.2 s p50, ≤ 2.5 s p95 | Client → first audio frame |
| Grounding response | ≤ 1.5 s p50 | Server |
| Inline underline after pause | ≤ 500 ms (local model) | Extension/IME |
| Quick Refine end-to-end | ≤ 3 s p50 | Client |
| Windows idle RAM / CPU | ≤ 150 MB / < 1% | Perf lab |
| Android idle battery | < 1%/h with bubble | Battery Historian |
| Extension content-script cost | < 2 ms per keystroke, < 10 MB heap | Chrome perf |
| Sync propagation | ≤ 2 s p95 | Server |

---

## 15. Observability

- **Tracing:** OpenTelemetry across clients (sampled 5%), gateway, services, Temporal; trace ID shown in the in-app "Report a problem" dialog.
- **Metrics:** RED per service; business events (§15.1) to a product analytics store (PostHog self-hosted or ClickHouse).
- **Crash reporting:** Sentry (Windows native minidumps, Android NDK, extension).
- **Dashboards:** Talk latency, grounding accuracy (thumbs + benchmark), agent success, refine acceptance, unit burn, cost per active user.
- **Alerts:** p95 first-audio > 3 s for 10 min; refine error rate > 5%; agent failure rate > 25%; Clarift upstream 5xx > 2% (auto-switch to fallback).

### 15.1 Analytics events (PRD §15 metrics)
| Event | Properties |
|---|---|
| `guided_task_completed` | app_id, steps, duration_ms, input_mode |
| `input_mode_changed` | from, to, trigger (hotkey/auto_quiet/manual) |
| `refine_shown` / `refine_outcome` | surface, app_id, mode, technique, score_before/after, outcome, units |
| `overlay_feedback` | thumbs, confidence, app_id |
| `agent_run_finished` | status, steps, approvals, duration |
| `handoff_used` | from_platform, to_platform |
| `plan_upgraded` | from, to, source |
No prompt text or screen content in analytics.

---

## 16. Billing & Entitlements

| Meter | Free | Pro | Max |
|---|---|---|---|
| Talk messages (voice or typed) | 30/month (typed capped 30/day) | Unlimited (fair use 3,000/month) | Unlimited |
| Dictation | 60 min/month | Unlimited | Unlimited |
| Agent messages | 25/month | 150/month | 1,000/month |
| Clarift inline | Quick via linked Clarift quota | + Guided | + Council |
| Routines | 1 | 10 | 50 |
| Local model mode | — | — | Yes |

- Entitlements cached in Redis (`ent:{user_id}`), refreshed on billing webhooks.
- Metering writes to `usage_ledger` idempotently (`ref_id` unique per message/refine).
- Stripe (cards, global) and Razorpay (UPI, cards, India) with GST invoices; student discount via verification workflow (manual review v1).
- Hitting agent limit: Talk continues; agents return `402` with upgrade CTA (matches reference behavior where talk keeps working and agent messages pause ([HeyClicky](https://www.heyclicky.com/))).

---

## 17. Testing Strategy

| Level | Scope | Tools |
|---|---|---|
| Unit | Parsers, mode selection, redaction, schema validation | xUnit (C#), GoogleTest (C++), JUnit5 + Turbine (Kotlin), Vitest (TS), pytest |
| Contract | Client ⇄ API (OpenAPI), realtime events, Clarift adapter vs. mock Clarift | Pact, Schemathesis |
| Integration | Services with Postgres/Redis/Temporal in containers | Testcontainers |
| UI automation | Windows app & overlay | WinAppDriver / FlaUI |
| Android UI | Bubble, IME, a11y flows | Espresso, UI Automator, Compose tests |
| Extension E2E | Detection + replacement on top chat sites (recorded fixtures + live nightly) | Playwright with extension loading |
| Grounding eval | 200-task benchmark, release gate ≥ 85% | Custom harness |
| Refinement eval | 500 prompt set; intent preservation ≥ 95%, score uplift ≥ +30 median, human rating | LLM-as-judge + human panel |
| Agent eval | 100 scripted tasks with sandbox accounts; success ≥ 80% | Temporal test env |
| Performance | Budgets in §14 | WPR/WPA, Perfetto, k6 (backend) |
| Security | SAST, DAST, dependency scan, pen-test before launch | Semgrep, ZAP, Dependabot/Snyk |
| Privacy | `test_frames_never_persisted`, log scrubbing tests | CI |

Key acceptance tests from the PRD are encoded as automated tests, e.g.:
- `CL-07`: Playwright asserts no click/submit event on `send_selector` after Accept across all configured sites.
- `CL-08`: Undo restores exact original text (byte-equal) within 30 s.
- `IN-02`: mode toggle latency ≤ 150 ms (FlaUI timer, 50 runs, p95).
- `SC-09`: capture refused for each blocklist entry (Windows process list, Android package list).

---

## 18. DevOps, Environments & Release

### 18.1 Environments
`dev` (per-PR preview for backend), `staging` (prod-like, sandbox connector accounts, mock Clarift + real Clarift staging), `prod` (multi-AZ; primary region `asia-south1`/`ap-south-1` Mumbai for Indian latency; secondary `us-east`).

### 18.2 CI/CD
- Monorepo (`/apps/windows`, `/apps/android`, `/apps/extension`, `/services/*`, `/packages/schemas`) with Nx/Turborepo for TS, Gradle, MSBuild.
- Shared schemas in `/packages/schemas` (JSON Schema → generated TS/Kotlin/C#/Python types).
- GitHub Actions: lint → unit → contract → build → sign → deploy (Argo CD for services).
- Client release trains: Windows & Android every 2 weeks; extension weekly; remote config (selectors/blocklists) anytime, signed.
- Staged rollouts: Play 5% → 20% → 100%; MSIX flighting rings; feature flags per requirement ID.

### 18.3 Data & DR
- Postgres PITR (7 days), daily snapshots (30 days), cross-region replica; RPO 5 min, RTO 1 h.
- Temporal persistence replicated; R2 versioning for artifacts.

---

## 19. Risks (technical) & Mitigations

| Risk | Mitigation in design |
|---|---|
| Play rejects AccessibilityService | Assist API as primary capture, reduced-permission mode, feature-flag a11y actions, Galaxy Store fallback |
| Chat-site DOM churn | Signed remote selectors + generic detector + nightly live E2E alerting |
| contenteditable replacement breaks editors | Per-site strategy field (`execCommand` / paste / native setter), canary rollout per site |
| Realtime model latency in India | Mumbai region, Opus, regional provider endpoints, cascade fallback |
| GPU cost for grounding | Tree-first matching reduces VLM calls; cache per (app, version, query) |
| Clarift API delay | `refine-engine` fallback with identical contract (ADR-005) |
| Windows overlay conflicts with games/anti-cheat | Auto-disable overlay for full-screen exclusive apps and known anti-cheat processes |
| Keyboard fork licensing (GPL) | Legal review; in-house keyboard as alternative path |

---

## 20. Requirements Traceability Matrix

| PRD ID | Component(s) | Spec § |
|---|---|---|
| IN-01, IN-07 | Realtime GW, Win audio, Android audio, extension `/ask` | 4.6, 5.7, 8.4 |
| IN-02, IN-04 | Win hotkeys, Android mode chip/IME toggle | 4.2, 5.6 |
| IN-03 | Settings `input.mode_per_app` | 9.2 |
| IN-05 | On-device wake word (P2) | 4.6 |
| IN-06 | Quiet Mode detector | 4.6 |
| IN-08, AN-07 | Model router `talk.indic` | 7.2 |
| IN-09 | whisper.cpp / on-device recognizer | 4.6, 5.7 |
| IN-10 | TTS settings | 9.2 |
| SC-01, SC-02, SC-07 | Win capture, Android capture | 4.3, 5.3 |
| SC-03, SC-04 | Overlay, UIA/a11y trees, grounding-svc | 4.4, 4.5, 5.4, 5.5, 7.3 |
| SC-05, SC-06 | Step-completion detection | 7.4 |
| SC-08 | Android camera mode (CameraX → `/ask`) | 5.2 |
| SC-09, SC-10 | Blocklist, secure windows, redaction | 4.3, 5.3, 13.2 |
| CL-01, CL-02 | Remote targets, extension, desktop watcher, IME | 4.7, 5.6, 6.2, 9.5.3 |
| CL-03, CL-17 | Local quality model, Evaluator | 6.3, 9.5.1, 9.6 |
| CL-04..CL-08 | Card UI, replacement, undo, never-send | 4.7, 5.6, 6.4 |
| CL-09 | Hybrid → refine hook | 8.4, 9.6 |
| CL-10, CL-11, CL-12 | Mode/technique selection | 9.5.2, 9.6 |
| CL-13..CL-16 | Templates, reference files, Projects, Saved | 9.6, 11.1 |
| CL-18, CL-19 | Converter, analytics | 9.6, 15.1 |
| CL-20 | Usage metering & fallback | 9.5.1, 16 |
| CL-21, CL-22 | `changes[]` explanations, drills | 9.6 |
| AG-01..AG-10 | agent-svc, connector-hub, device channel, routines | 10 |
| HM-01..HM-06 | core-api, memories, settings deep links | 9.1, 12 |
| EC-01..EC-07 | sync-svc, handoff, send, remote | 11 |
| LM-01..LM-05 | Walkthrough storage (`buddies.kind=walkthrough`), flashcard generator job, progress aggregates | 9.1, 10.2 |
| WN-01..WN-05 | Explorer menu, terminal helper, Office, local model, extension | 4.9, 6 |
| AN-01..AN-08 | Android entry points, IME, low-data, battery | 5 |

---

## 21. Phase-wise Engineering Plan (maps to PRD §16)

| Phase | Weeks | Engineering deliverables | Exit criteria |
|---|---|---|---|
| 0 | 1–6 | Win capture + overlay spike; UIA extractor; grounding benchmark v0; realtime GW prototype; Clarift API contract signed or fallback engine v0 | Grounding ≥ 70% on benchmark; first audio ≤ 1.5 s p50 |
| 1 | 7–14 | Windows alpha: hotkeys, modes, Talk, overlay, extension with Clarift quick refine, Home, Google + Notion connectors, agents P0 with approvals | 50 alpha users; crash-free ≥ 99%; refine acceptance ≥ 30% |
| 2 | 15–22 | Android beta: bubble, Assist API, MediaProjection, overlay, Nexa Keyboard + Clarift, sync v1 | Play internal testing approved; battery budget met |
| 3 | 23–24 | Billing (Stripe + Razorpay), localization EN/HI/TE, pen-test, store listings | All P0 acceptance tests green; security findings ≤ low |
| 4 | Months 7–9 | Handoff, send, routines, M365, computer-use (UIA/a11y), Evaluator, techniques, templates | Agent success ≥ 80%; cross-device ≥ 10% WAU |
| 5 | Months 10–12 | Walkthrough library, flashcards, progress, Converter, Analytics, local model mode | PRD lagging targets on track |

---

## 22. Open Technical Questions

| # | Question | Owner | Blocking |
|---|---|---|---|
| TQ1 | Clarift partner API availability, auth model, and unit billing reconciliation | Clarift owner | Yes (CL-10+ with real Clarift) |
| TQ2 | Realtime model provider(s) for Indic languages at acceptable cost | ML lead | No |
| TQ3 | Self-host grounding VLM on GPUs vs. provider API — cost crossover point | ML + Infra | No |
| TQ4 | Keyboard: fork (GPL) vs. in-house | Legal + Android lead | Yes for AN-03 |
| TQ5 | Secondary-desktop computer use compatibility with modern apps (UWP, Chromium GPU) | Windows lead | No (UIA path exists) |
| TQ6 | Store ToS for inline text replacement in third-party AI apps via IME/a11y | Legal | No |
| TQ7 | Final product name (placeholder "Nexa") affects domains, package IDs (`app.nexa.*`) | Founder | Before Phase 3 |
