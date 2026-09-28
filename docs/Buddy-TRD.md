# Buddy — Technical Requirements Document (TRD)

| Field | Value |
|---|---|
| Document | Technical Requirements Document v1.2 |
| Product | Buddy (home base: [Bhargav2301/Buddy](https://github.com/Bhargav2301/Buddy)) |
| Baseline | v0.2.0 (`VERSION`, `CHANGELOG.md`, 26 Sep 2026) |
| Reference product | [heyclicky](https://www.heyclicky.com/) · [changelog](https://www.heyclicky.com/changelog) |
| Companion doc | `Buddy-UI-UX.md` |
| Author context | Sai dhara / Bhargav2301 |
| Status | Draft for engineering — source material for parent deliverable |
| Date | 28 September 2026 (Asia/Calcutta) |
| Changelog | **v1.2** — Platform decision: Windows-first for R1/R2/R3; Android deferred to Phase 2 |

---

> **Platform decision (authoritative — user direction):** Build the **Windows app first**, then move to Android. All R1/R2/R3 work for releases **0.3 Alive**, **0.4 Points**, and **0.5 Hands** targets **Windows WPF + embedded Buddy.Server on the PC**. Android is **Phase 2 after Windows validation** — parity for voice/talk and guide *display* only where Assist APIs allow; **computer-use execution stays PC-side** until the Windows agent is solid. Do **not** plan concurrent Windows+Android delivery for this cycle.

---

## 0. How to read this document

- **MUST / SHOULD / MAY** follow RFC 2119.
- Requirement IDs (`FR-VCE-*`, `FR-AGT-*`, `FR-NET-*`, …) are Buddy-specific for this TRD. Where they advance Nexa/PRD IDs from the attached Nexa Technical Specification and repo `docs/Feature-Coverage.md`, the mapping is noted in §14.
- **Verified** means confirmed in repo docs or source (`README.md`, `docs/Architecture.md`, `docs/Feature-Coverage.md`, `docs/Validation.md`, `docs/Buddy-Setup-Guide.md`, or listed `.cs` files). Do not treat aspirational attached drafts as shipped behavior.
- This TRD scopes the next releases (0.3 → 0.5) that make Buddy a heyclicky-class **interactive on-screen companion** on **Windows first**, while preserving Buddy’s differentiator: **local-first inference on the user’s Windows PC**. Android work is out of the 0.3–0.5 delivery path (Phase 2).

### 0.1 Three mandatory product requirements (must appear in both TRD and UI/UX)

| # | Requirement (user-stated) | Root cause in v0.2.0 (verified) | Target outcome |
|---|---|---|---|
| **R1** | Fix the voice chat shortcut — currently it only uses the chat window for voice chat as well | `Ctrl+Shift+Space` and “Start voice” both call `OpenQuick(true)` → `QuickChatWindow.Open(startVoice: true)`. Voice is a mode *inside* the compact chat bar, not a dedicated voice overlay/PTT surface. Confirmed in `MainWindow.cs` (`Hook` id==3 → `OpenQuick(true)`), `QuickChatWindow.cs`, `DesktopPreferences.cs`, Setup Guide. | Proper voice mode: hold-to-talk overlay beside the companion; chat bar remains for typed input |
| **R2** | Local models are connected but Buddy still lacks real AI assistant capabilities and cannot control the computer based on user commands | Ollama chat works (`OllamaEngine`, `/v1/chat`). Feature Coverage marks **AG-01..10 Deferred**: no computer-use executor, connectors, approvals, routines. Buddy “states that it cannot perform these actions.” | Tool-using agent + gated computer control (UIA-first) with plan/confirm/kill-switch |
| **R3** | Buddy should access the internet and draw on the screen to explain/guide the user for any tool or task | No web tool in server. Feature Coverage: **SC-03/04 Partial/deferred** — “no model-grounded arrows, boxes, click-through drawings.” `OverlayNative.cs` only places companion/quick-chat windows; it is not a draw layer. Capture is preview-gated in Home, not tied to summon. | Momentary screen+UIA context on summon; optional web research tools; click-through draw/annotate overlay for Guide mode |

---

## 1. Purpose and scope

### 1.1 Purpose

Define the technical foundation for Buddy to move from “local chat app with a cursor glyph” to an **on-screen companion** that:

1. Talks with the user via a **true voice mode** (R1).
2. **Plans and acts** on the PC when explicitly asked (R2).
3. **Sees the screen**, **researches the web** when needed, and **draws guidance** on top of the user’s apps (R3).

### 1.2 In scope (Windows-first — R1/R2/R3 cycle)

- **Primary delivery surface:** Windows WPF client (`apps/windows/Buddy.Windows`) enhancements for all of 0.3 / 0.4 / 0.5.
- Embedded `services/Buddy.Server` API extensions (chat context, ground, guide, agent plan, web tools) running on the Windows PC.
- Local Ollama models (chat + optional vision/grounding) on the Windows host.
- Optional outbound HTTPS for web research tools (user-controlled).
- Acceptance, Validation.md scripts, and device testing for this cycle are **Windows-only**.

### 1.3 Out of scope (this TRD cycle)

- **Android client delivery** for 0.3–0.5 (voice/talk, guide display, or agent). Android is **Phase 2 after Windows validation** — see §12. Existing Android chat/pairing may keep working against the PC server but is not a delivery target for R1–R3 features in this cycle.
- Concurrent Windows+Android feature parity work for the next cycle.
- Cloud multi-tenant hosting, billing, OAuth account system (Nexa Phase cloud).
- macOS client (heyclicky’s platform; Buddy differentiates with **Windows-first** local companion, Android later).
- Browser extension (WN-05 deferred in Feature Coverage).
- Always-on continuous screen watching.
- Unattended background agents without user confirmation for high-risk actions.

### 1.4 Product vision vs heyclicky

| Dimension | heyclicky ([heyclicky.com](https://www.heyclicky.com/), changelog) | Buddy target |
|---|---|---|
| Positioning | “AI buddy that lives on your Mac”; interface problem, not model problem | Same interface ambition **first on Windows**, local-first; Android Phase 2 |
| Summon | Press hotkey → sees screen → talk out loud | Hold-to-talk (R1) + momentary capture **on Windows** |
| Guidance | “Draws right on your screen to point the way”; walkthroughs up to 15 steps | Overlay primitives + Guide mode (R3) on WPF |
| Agency | “Say heyclicky agent and it does the task”; Clickys, computer use, connectors | Explicit Agent mode; UIA-first **PC** control (R2); connectors phased |
| Privacy | Sees screen only on hotkey; screenshots never stored; text summaries kept | Preserve Buddy’s stronger local-first bar: inference on PC; screenshots request-scoped |
| Models | Frontier cloud (GPT Realtime / GPT-6 per changelog) | Local Ollama by default; optional web for research only |
| Home UI | Notch / Clickys home | Keep Buddy Home + companion; no Mac notch |
| Platforms | Mac-first product | **Windows-first** for R1–R3; Android after Windows agent is solid |

**Emulate:** voice-first summon, momentary visible capture, draw-to-guide, explain-before-act, personality/onboarding energy, kill-switch culture.

**Differentiate:** local inference, no required cloud account, **Windows-first** (Android later), explicit consent for agent and web, transparent “running on your PC” latency honesty.

---

## 2. Current state summary (verified as of v0.2.0)

### 2.1 Architecture as-is

```
Buddy.Windows (WPF, .NET 8)
  ├── MainWindow          — Home: chat, setup, pairing, screen text/capture preview
  ├── CursorCompanionWindow — 46×52 mint glyph, 33 ms follow timer, click-through
  ├── QuickChatWindow     — compact chat/voice BAR (voice lives HERE today)
  ├── OverlayNative       — Win32 place/configure for companion & bar (NOT draw ink)
  └── RegisterHotKey      — Ctrl+Space (chat/voice), Ctrl+Shift+Space (voice→bar), Ctrl+Alt+Esc

Buddy.Server (embedded Kestrel)
  └── /v1/chat, /v1/refine, conversations, prompts, memories, pair, status
        └── OllamaEngine → qwen3:4b-instruct-2507-q4_K_M (+ optional gemma3:4b vision)

Android (Kotlin/Compose) — exists today (pinned HTTPS to PC; bubble, tile, assistant, basic keyboard); **not in 0.3–0.5 delivery scope** (Phase 2)
```

Sources: `README.md`, `docs/Architecture.md`, file tree under `apps/windows/Buddy.Windows/`, `services/Buddy.Server/`.

### 2.2 What works

- Real local Ollama inference; NDJSON streaming chat; encrypted PC store; QR pairing with cert pin.
- Cursor companion + compact bar; configurable main shortcut; one-utterance System.Speech voice.
- Reviewed UIA text + window capture preview in Home; images request-scoped (not persisted in `ChatMessage`).
- Android chat against PC; Assist text / share image paths.

### 2.3 Gaps mapped to R1–R3

| Gap | Evidence | Blocks |
|---|---|---|
| Voice = chat window mode | `OpenQuick(true)`; Setup Guide: “Activate voice **in the compact bar**” | R1 |
| No hold-to-talk / no keyboard hook | Architecture §9: “never … installs a keyboard hook”; Feature Coverage IN-02/04 Partial | R1 |
| No agent / computer control | Feature Coverage AG-01..10 Deferred; Setup Guide §7 | R2 |
| Chat is Q&A only — no tools | `BuddyService.Chat` builds prompt + history; no tool loop | R2 |
| No draw overlay | Feature Coverage SC-03/04; no annotation renderer beyond companion glyph | R3 |
| No internet / web search tool | Server has no `web.search`/`web.fetch`; Ollama is local | R3 |
| Capture not on summon | Architecture §9: “No screen capture happens on activation”; Setup Guide | R3 |

### 2.4 Platforms

| Surface | Stack (verified) | Role in this TRD cycle |
|---|---|---|
| Windows | C# / .NET 8 **WPF** (not WinUI 3 — Architecture ADR differs from Nexa ADR-001) | **Sole delivery target** for R1–R3 (0.3 / 0.4 / 0.5) |
| Android | Kotlin 2 / Jetpack Compose | **Phase 2** after Windows validation — voice/talk + guide *display* parity only where Assist APIs allow; no computer-use execution on device |
| Service | ASP.NET Core embedded in desktop process | Single-user local on the Windows PC |
| Inference | Ollama on Windows PC | No cloud API key required |

Cross-platform C# beyond Windows is **not** claimed by the repo; Android is a separate Kotlin app. Do not invent a .NET MAUI/macOS path in this TRD. **Do not schedule concurrent Windows+Android feature work** for 0.3–0.5.

---

## 3. Functional requirements

### 3.1 R1 — Voice mode & shortcut fix (`FR-VCE`)

| ID | Requirement |
|---|---|
| **FR-VCE-01** | MUST provide a **Voice Overlay** surface distinct from `QuickChatWindow`. Voice MUST NOT require opening the chat bar as the primary path. |
| **FR-VCE-02** | MUST support **hold-to-talk**: while the configured PTT chord is held, listen + (per FR-PER) capture; on release, end utterance and send. Tap (< 250 ms) MAY open the compact chat bar (preserve typed path). |
| **FR-VCE-03** | Default PTT SHOULD be Hold **Ctrl+Space** (configurable). Dedicated one-shot voice (`Ctrl+Shift+Space`) SHOULD open Voice Overlay (not chat bar). Setting “Start voice” on main shortcut MUST bind to Voice Overlay. |
| **FR-VCE-04** | Voice Overlay MUST show: companion Listening state, mic level ring, live partial transcript bubble, Looking/Thinking/Speaking states — without a large opaque chat chrome covering the user’s work. |
| **FR-VCE-05** | Implementation MAY use opt-in `WH_KEYBOARD_LL` only when user enables hold-PTT; MUST fall back to `RegisterHotKey` toggle mode when hook unavailable or disabled. MUST NOT log keystrokes. |
| **FR-VCE-06** | Global Stop (`Ctrl+Alt+Esc` / Esc) MUST cancel mic, TTS, inference, overlay, and agent within 100 ms budget (NFR). |
| **FR-VCE-07** | Barge-in: pressing PTT during TTS MUST stop speech immediately and start listening. |
| **FR-VCE-08** | Streaming TTS SHOULD speak sentence chunks as deltas arrive (upgrade from post-complete `SpeakAsync` of full answer in `QuickChatWindow`). |
| **FR-VCE-09** | Compact chat bar MUST remain for typed input; Voice button inside bar MAY start Voice Overlay or in-bar listening for compatibility, but shortcut defaults MUST prefer Overlay. |

**Acceptance criteria (R1)** — see §13.1.

### 3.2 Perception / screen context (`FR-PER`) — supports R3

| ID | Requirement |
|---|---|
| **FR-PER-01** | On PTT-down (or explicit Screen chip), MUST capture active window (preferred: Windows.Graphics.Capture with OS border) or configured scope; downscale ≤ 1280 long edge; JPEG ~q80; memory-only. |
| **FR-PER-02** | MUST take a UIA snapshot of foreground window (name, control type, automation id, bounds, enabled); depth/node/time budgets: depth ≤ 8, ≤ 400 nodes, ≤ 150 ms p95. |
| **FR-PER-03** | MUST redact password fields (`IsPassword`), apply process/title blocklist; blur redacted regions in image before model/web tools see them. |
| **FR-PER-04** | Image bytes MUST remain request-scoped and MUST NOT be persisted in conversation store (preserve Architecture behavior). Capture log MAY store time/app/scope/redaction count/text summary only. |
| **FR-PER-05** | Visible indicators MUST appear while looking: accent border/pill (“Looking”); cannot be disabled (privacy). |

### 3.3 R3 — Screen drawing & Guide (`FR-GRD`, `FR-GDE`)

| ID | Requirement |
|---|---|
| **FR-GRD-01** | MUST add per-monitor `OverlayWindow`: topmost, layered, `WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`, excluded from capture (`WDA_EXCLUDEFROMCAPTURE`) so Buddy never inks itself. |
| **FR-GRD-02** | Primitives MUST include: Ring, Arrow (animated), Highlight, Underline, Badge, Label. Max 3 concurrent by default. |
| **FR-GRD-03** | Grounding pipeline MUST prefer UIA element ref → fuzzy text+role → optional VLM bbox + OCR verify. Confidence < 0.6 → no fake arrow; Unsure state + clarifying question. |
| **FR-GRD-04** | Coordinate mapping MUST handle mixed DPI using existing `OverlayPlacement` monitor math extended for ink. |
| **FR-GRD-05** | Invalidation: clear/re-ground on foreground change, target move/scroll, or TTL (~15 s). |
| **FR-GDE-01** | Guide mode: `/v1/guide/start` returns ordered steps `{instruction, target, expect}`; client advances on expect match or manual Next/Back/Skip. |
| **FR-GDE-02** | Guide MUST narrate (voice/bubble) and draw the current step’s target. |
| **FR-GDE-03** | Walkthrough progress SHOULD persist (text + step index) in encrypted store for resume. |

### 3.4 R3 — Internet access (`FR-NET`)

| ID | Requirement |
|---|---|
| **FR-NET-01** | MUST provide optional **web research** tools callable by the agent/chat planner: at minimum `web.search` and `web.fetch` (HTTPS). |
| **FR-NET-02** | Web tools MUST be **off by default** or gated behind Settings (“Allow Buddy to use the internet for research”). Local chat MUST continue to work fully offline when web is disabled. |
| **FR-NET-03** | Fetched page content MUST be treated as **untrusted data** in prompts (same wrapping pattern as screen context in `BuddyService`). |
| **FR-NET-04** | MUST NOT upload screenshots to third-party web APIs. Web tools operate on URLs/queries; vision stays local (Ollama) unless a future explicit user opt-in says otherwise. |
| **FR-NET-05** | Rate-limit and size-cap fetches (e.g. ≤ 500 KB text extracted, timeout ≤ 10 s). Show “Researching…” status in Voice Overlay / bubble. |
| **FR-NET-06** | Cite sources in the answer (title + URL) when web was used. |

### 3.5 R2 — AI assistant, tools, computer control (`FR-AGT`)

| ID | Requirement |
|---|---|
| **FR-AGT-01** | Agent mode MUST be **disabled by default**; enable in Settings with plain-language explanation of control scope. |
| **FR-AGT-02** | Planner (`/v1/agent/plan`) MUST return typed actions, e.g. `Click(ref)`, `Type(ref,text)`, `Keys(chord)`, `Invoke(ref)`, `Open(app|url)`, `Wait(expect)`, `Read(ref)`, `WebSearch(query)`, `WebFetch(url)`. |
| **FR-AGT-03** | Each action MUST carry risk `low|high`. High includes send/submit/delete/pay/install keywords and blocklisted apps — those MUST require explicit per-action confirm. |
| **FR-AGT-04** | Executor MUST prefer UIA patterns (`InvokePattern`, `ValuePattern`, …). `SendInput` only after `ElementFromPoint` re-verify; never move user’s real pointer silently without visible agent cursor/overlay narration (align with heyclicky lesson: floating agent cursor caused capture/share issues — prefer UIA; if synthetic pointer used, isolate carefully). |
| **FR-AGT-05** | Before each action: overlay highlights target, bubble narrates, ≥ 600 ms dwell; kill switch Esc / Ctrl+Alt+Esc / physical mouse move > 40 px pauses. |
| **FR-AGT-06** | Caps: max ~25 actions/run; per-action timeout ~10 s; audit log (action, target name, result) — no screenshots in audit. |
| **FR-AGT-07** | Chat model MUST gain a **tool-use loop** (plan → tool results → answer), not only single-shot completion. Local model prompting MUST use structured JSON / Ollama `format` where reliable; degrade gracefully on small models. |
| **FR-AGT-08** | Computer-use execution MUST remain **Windows/PC-side** for this cycle and until the Windows agent is solid. Android MUST NOT execute computer-use; Phase 2 MAY display plans and request PC confirmation only. |

### 3.6 Companion & modes (`FR-CMP`, `FR-MOD`)

| ID | Requirement |
|---|---|
| **FR-CMP-01** | Companion state machine MUST extend beyond Idle/Listening/Thinking/Speaking to include Looking, Pointing, Unsure, Error, AgentWorking, Sleeping (see UI/UX doc). |
| **FR-CMP-02** | Spring-follow SHOULD replace rigid 33 ms snap (CompositionTarget.Rendering). |
| **FR-MOD-01** | Modes: Talk (default), Guide, Dictate, Refine (existing `/v1/refine`), Agent. Mode entry via intent phrases and/or chips. |

### 3.7 Onboarding & privacy (`FR-ONB`, `FR-PRV`)

| ID | Requirement |
|---|---|
| **FR-ONB-01** | In-product tutorial MUST demonstrate hold-PTT + first successful ring on a sample UIA window (deterministic grounding) within ~90 s of install after model pull starts. |
| **FR-PRV-01** | Privacy center: capture log, blocklist editor, web-access toggle, agent toggle, delete-all-local-data. |
| **FR-PRV-02** | Mandatory visible indicators for mic, looking, agent banner. |

---

## 4. Non-functional requirements

| ID | Requirement |
|---|---|
| NFR-01 | Hotkey → listening indicator ≤ 100 ms p95 |
| NFR-02 | PTT release → first token ≤ 1.5 s p50 on RTX-class / 4B model (honest slower path on CPU) |
| NFR-03 | UIA snapshot ≤ 150 ms p95; capture ≤ 80–120 ms |
| NFR-04 | Grounding end-to-end ≤ 2.5 s p95 (UIA path ≤ 1 s) |
| NFR-05 | Idle CPU ≤ 1%; idle app RAM ≤ 150 MB excluding Ollama |
| NFR-06 | Overlay never takes focus (`GetForegroundWindow` unchanged in automated test) |
| NFR-07 | Zero persisted screenshots (CI asserts store/temp) |
| NFR-08 | Per-monitor DPI v2 correct (extend `Buddy.Desktop.Tests`) |
| NFR-09 | UIA LiveRegion / accessible names for companion state changes |
| NFR-10 | Offline Talk/Guide-without-web MUST work with web tools disabled |
| NFR-11 | Agent kill-switch latency ≤ 100 ms local halt |

---

## 5. Target architecture

```
┌──────────────────────── Buddy.Windows (WPF) ────────────────────────┐
│ CompanionController (state machine) ── CompanionWindow (face/anim)  │
│ InputOrchestrator: HotkeyService · PttHook(opt-in) · WakeWord(P2)   │
│ VoiceOverlayWindow (R1) · QuickChatWindow (typed) · MainWindow      │
│ PerceptionService: Capture (WGC) · UiaTree · Redactor               │
│ VoiceService: Mic · VAD · STT · TTS(streaming)                      │
│ OverlayWindow (draw ink, R3) · GuideEngine                          │
│ AgentExecutor (UIA + gated SendInput, R2)                           │
└──────────────┬──────────────────────────────────────────────────────┘
               │ in-proc BuddyService (desktop) / HTTPS (Android)
┌──────────────▼──────────── Buddy.Server ────────────────────────────┐
│ /v1/chat (+context, +tools, mode) · /v1/ground · /v1/guide/*        │
│ /v1/agent/plan · tool runtime: web.search, web.fetch (R3)           │
│ PromptBuilder · GroundingResolver · Session/Walkthrough store       │
└──────────────┬──────────────────────────────────────────────────────┘
               ▼ Ollama: chat · vision/VLM · (opt) whisper.cpp
               ▼ HTTPS out (optional): search/fetch providers
```

### 5.1 API changes

| Method | Path | Notes |
|---|---|---|
| POST | `/v1/chat` | Add `context: { screen?, uia?, app, title }`, `mode: talk\|guide\|agent`, tool-call NDJSON events |
| POST | `/v1/ground` | `{ query, context }` → `{ targets[], confidence }` |
| POST | `/v1/guide/start`, `/v1/guide/{id}/step` | Plan + progress |
| POST | `/v1/agent/plan` | Typed actions; **execution client-side only** |
| GET | `/v1/privacy/log` | Capture log metadata |

NDJSON additions: `targets`, `step`, `sentence` (TTS boundary), `tool_call`, `tool_result`. Old clients ignore unknown types.

Body limit: raise carefully for screen+UIA (e.g. 3→4 MB); still exclude raw images from persistence.

### 5.2 AI agent + tool-use model

1. **Talk:** answer with optional screen context; emit `targets` when pointing helps.
2. **Guide:** multi-step plan; client drives expect-checks; redraw each step.
3. **Agent:** plan-then-act; tools include UI actions + web; high-risk confirm; audit.

Local 4B models are weak at long tool loops — TRD requires:

- Short action schemas; few tools exposed per turn.
- Hardware tiering: CPU-only → UIA grounding, limited agent; ≥6 GB VRAM → VLM grounding enabled.
- Clear user messaging when the model cannot complete a tool plan.

---

## 6. Voice pipeline & shortcut behavior (R1 detail)

### 6.1 As-is (bug)

```
Ctrl+Space        → OpenQuick(ShortcutStartsVoice?) → QuickChatWindow
Ctrl+Shift+Space  → OpenQuick(true)                 → QuickChatWindow + StartListening()
Tray "Voice"      → OpenQuick(true)                 → same
```

Voice recognition (`System.Speech`, single utterance) runs inside the chat bar; user experience is “chat window that listens,” not heyclicky-style voice overlay.

### 6.2 To-be

| Gesture | Behavior |
|---|---|
| Hold Ctrl+Space ≥ 250 ms | Voice Overlay: Listening + capture; release → send |
| Tap Ctrl+Space < 250 ms | Open QuickChatWindow (typed) |
| Ctrl+Shift+Space | Voice Overlay one-shot (toggle listen / VAD end) |
| Ctrl+Alt+Esc | Global cancel |
| Esc | Cancel current overlay/voice |

STT providers (interface): System.Speech (current) → Windows.Media.SpeechRecognition → whisper.cpp (P1).  
TTS: OneCore / SAPI streaming chunks → Piper (P1).

---

## 7. Internet access design (R3 detail)

| Concern | Decision |
|---|---|
| Default | Web tools disabled until user enables “Research on the internet” |
| Providers | Pluggable: e.g. Bing/Brave/DuckDuckGo search API or HTML search scrape with ToS-safe approach; `web.fetch` via HttpClient + readability extract |
| Privacy | No screenshot upload; queries/logs scrubbed; user can clear research history |
| Offline | Core Talk/Guide/UIA agent paths work without net (except Open URL) |
| Abuse | Blocklist of URL schemes (`file:`, `localhost` admin, etc.); no automatic credential entry from web pages into agent without confirm |

---

## 8. Screen overlay / drawing system (R3 detail)

Reuse lessons from heyclicky changelog (hand-drawn style, clear after speech/step, spatial context, max shapes, exclude from capture) and from attached prior Buddy UI/UX draft:

- Hand-drawn stroke feel (slight jitter) for friendliness.
- Companion **fly-to-target** on Pointing.
- Never draw when confidence low.
- Click-through so user can click the real control under the ring.

`OverlayNative` today is **placement infrastructure** — keep it; add a separate ink `OverlayWindow` / composition layer. Do not overload companion window into a full-screen ink surface.

---

## 9. Data, privacy, security

| Topic | Requirement |
|---|---|
| Screenshots | Never persisted (Architecture ADR; Feature Coverage; Setup Guide) |
| Screen text in prompts | Wrapped as untrusted (`<untrusted_screen_context>` already in `BuddyService`) |
| Web content | Same untrusted wrapping |
| Agent injection | Allowlist actions, caps, high-risk confirm, kill switch |
| Keyboard hook | Opt-in, single chord, no buffering, removable |
| Elevation | Buddy MUST NOT require admin; refuse secure desktop / UAC surfaces |
| Android (Phase 2) | No agent execution; out of 0.3–0.5 delivery; later display-only plans + PC confirm |
| Compliance posture | Align with India DPDP-minded consent and delete-all; local-first reduces cloud exposure |

---

## 10. Dependencies

| Dependency | Role | Notes |
|---|---|---|
| .NET 8 / WPF | Windows client | Existing |
| Ollama + Qwen3 4B Instruct | Chat | Existing default tag |
| Optional VLM (e.g. qwen2.5vl) | Grounding fallback | New for R3 |
| System.Speech / future whisper.cpp | STT | Upgrade path |
| Windows.Graphics.Capture | Capture | Prefer over BitBlt |
| UI Automation | Grounding + agent | Existing partial (`Native.ReadWindow`) |
| NAudio / WASAPI (proposed) | Mic level / PTT audio | New |
| HttpClient + search provider | Web tools | New, optional |
| Android Assist / SpeechRecognizer | Phase 2 mobile display parity | Existing partial; not 0.3–0.5 scope |

---

## 11. Risks

| Risk | Impact | Mitigation |
|---|---|---|
| Local 3–4B tool-use / grounding weak | Wrong arrows / failed agent | UIA-first; confidence gate; Unsure; shorter plans |
| CPU-only latency vs cloud heyclicky | Feels sluggish | Immediate UI feedback; tiering; optional smaller model |
| LL keyboard hook AV flags | Install friction | Opt-in; signed binary later; RegisterHotKey fallback |
| Chromium thin UIA | Poor grounding | Accessibility flags hint; VLM fallback |
| Agent misuse / prompt injection | Harmful actions | Confirmations, allowlist, caps, kill switch |
| Web fetch ToS / legal | Compliance | Provider choice; user toggle; cite sources |
| heyclicky floating cursor caused black captures | Share/Loom breakage | Prefer UIA; `WDA_EXCLUDEFROMCAPTURE`; avoid covering targets with opaque HUD |
| Splitting eng across Windows + Android too early | Delayed R1–R3 on the real computer-use surface | **Windows-first mandate**; Android Phase 2 only after Windows agent validated |
| Android users expecting R1–R3 parity in 0.3–0.5 | Expectation mismatch | Docs/release notes: Android Phase 2; PC remains the agent host |

---

## 12. Phased implementation plan

**Platform rule:** Releases **0.3 / 0.4 / 0.5 are Windows-only delivery**. Do not staff concurrent Android feature work in this cycle. Android begins only as **Phase 2** after Windows R1–R3 validation (especially after the Windows agent in 0.5 is solid).

| Release | Theme | Platform | Scope | Est. |
|---|---|---|---|---|
| **0.3 “Alive”** | **R1** | **Windows WPF** | Voice Overlay, hold-PTT, companion states, capture-on-hold, streaming TTS, onboarding | 3–4 wks |
| **0.4 “Points”** | **R3 draw + guide** | **Windows WPF** | Overlay ink, UIA grounding, Guide mode, privacy center; web search **read-only** behind toggle | 4–5 wks |
| **0.5 “Hands”** | **R2** | **Windows WPF** | Agent plan/execute (PC-side), audit, undo; richer tool loop; whisper/Piper P1 | 4 wks |
| **0.5.x** | Windows polish | **Windows WPF** | Wake word P2, localization (strings), agent reliability hardening | 2–3 wks |
| **Phase 2** | Android adaptation | **Android (after Windows validation)** | Voice/talk + guide *display* parity where Assist APIs allow; plan cards display-only with “Continue on PC”; **no on-device computer-use**; handoff to Windows agent | TBD after 0.5 solid |

Each 0.3–0.5 phase MUST include automated tests where possible plus **Windows** device acceptance (Validation.md already notes GUI not executed on Windows in CI). Phase 2 Android acceptance is separate and gated on Windows agent quality.

---

## 13. Acceptance criteria for the three mandatory requirements

### 13.1 R1 — Voice shortcut / voice mode (**Windows**)

1. On **Windows**, pressing the dedicated voice shortcut does **not** open `QuickChatWindow` as the primary surface; Voice Overlay appears within 100 ms with Listening state.
2. Hold-PTT: key-down starts listen+capture indicators; key-up sends; chat bar stays closed unless user taps.
3. Typed path still works via tap shortcut / Open quick chat.
4. Esc / Ctrl+Alt+Esc stops mic and any in-flight reply.
5. Manual **Windows** acceptance script recorded in `docs/Validation.md` extension.
6. Android voice-overlay parity is **not** an acceptance gate for 0.3 (Phase 2).

### 13.2 R2 — Real assistant + computer control (**Windows PC**)

1. With Agent enabled on **Windows**, user can say/type a concrete UI task (e.g. open Notepad and type a sentence); Buddy returns a visible plan and executes with confirmations **on the PC**.
2. High-risk action pauses for explicit confirm.
3. Kill switch halts within 100 ms; audit entry written without screenshots.
4. With Agent disabled, Buddy refuses control and offers Guide instead.
5. Regression: pure Q&A chat without agent still works offline.
6. No requirement that Android execute or co-deliver agent actions in 0.5.

### 13.3 R3 — Internet + draw-to-guide (**Windows**)

1. With web enabled, “look up X and summarize” uses `web.search`/`web.fetch` and cites URLs; with web disabled, same query stays local / asks to enable.
2. In Guide or Talk-with-screen on **Windows**, Buddy draws at least Ring/Arrow on a correct UIA target in the onboarding sample window (≥ 85% on internal eval set over time).
3. Overlay is click-through and does not steal focus; excluded from capture.
4. No screenshot files remain under `%LOCALAPPDATA%\Buddy` after sessions (automated assert).
5. Android guide *display* is Phase 2 — not a 0.4 acceptance gate.

---

## 14. Traceability

| This TRD | Repo / Nexa / heyclicky |
|---|---|
| R1 / FR-VCE-* | Advances Feature Coverage IN-02/04; fixes Setup Guide voice-in-bar behavior; emulates heyclicky hotkey voice |
| R2 / FR-AGT-* | Closes AG-01..04 subset; heyclicky “agent” / computer use (UIA-first) |
| R3 / FR-GRD, FR-GDE, FR-NET | Closes SC-03/04, SC-05/06 direction; heyclicky draw-on-screen + walkthroughs; adds web tools Buddy lacks |
| Privacy | Architecture decisions 6–9; heyclicky FAQ “only when hotkey”; screenshots never stored |
| Attached drafts | Prior Buddy TRD/UIUX attachments and Nexa Technical Specification used as design input — **implementation truth is the GitHub repo v0.2.0** |

---

## 15. Open questions

| # | Question | Why it matters |
|---|---|---|
| OQ1 | Ship hold-PTT via LL hook in 0.3 or toggle-only first? | AV / trust vs UX parity with heyclicky |
| OQ2 | Web search provider (API key vs keyless)? | Cost, privacy, India availability |
| OQ3 | Stay on WPF or migrate toward WinUI 3 + C++ helper (Nexa ADR-001)? | Effort vs overlay performance (Windows-only question for this cycle) |
| OQ4 | Which VLM tag for grounding on consumer GPUs? | Quality vs VRAM |
| OQ5 | Agent synthetic cursor: ship or UIA-only after heyclicky’s capture regressions? | Reliability vs visibility |
| OQ6 | Product naming consistency (Buddy vs historical “Nexa” docs)? | Docs/packages only |
| OQ7 | **Resolved for this cycle:** Windows first, then Android — when is Phase 2 start gated? | Exit criteria for “Windows agent solid” (suggested: 0.5 acceptance §13.2 green + soak) before Android voice/guide display work |
| OQ8 | Phase 2 Android: Assist-only guide display vs limited overlay APIs? | Scope after Windows validation; not blocking 0.3–0.5 |

---

## 16. Sources cited

- GitHub [Bhargav2301/Buddy](https://github.com/Bhargav2301/Buddy) v0.2.0: `README.md`, `CHANGELOG.md`, `VERSION`, `docs/Architecture.md`, `docs/Feature-Coverage.md`, `docs/Validation.md`, `docs/Buddy-Setup-Guide.md`
- Source: `apps/windows/Buddy.Windows/{MainWindow,QuickChatWindow,CursorCompanionWindow,OverlayNative,DesktopPreferences}.cs`, `services/Buddy.Server/{BuddyService,OllamaEngine}.cs`
- [https://www.heyclicky.com/](https://www.heyclicky.com/) and [https://www.heyclicky.com/changelog](https://www.heyclicky.com/changelog)
- Attached: prior Buddy TRD draft, Buddy UI/UX Design Document draft, Nexa Technical Specification (author Venkata Sai Bhargav Dhara, 26 Sep 2026)

---

*End of TRD v1.2*
