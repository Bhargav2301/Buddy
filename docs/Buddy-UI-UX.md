# Buddy — UI/UX Design Document

| Field | Value |
|---|---|
| Document | UI/UX Design Document v1.2 |
| Product | Buddy ([Bhargav2301/Buddy](https://github.com/Bhargav2301/Buddy) v0.2.0) |
| Companion doc | `Buddy-TRD.md` |
| Reference | [heyclicky — an AI buddy on your Mac](https://www.heyclicky.com/) · [changelog](https://www.heyclicky.com/changelog) |
| Date | 28 September 2026 (Asia/Calcutta) |
| Status | Draft — curated decisions for a more interactive Buddy |
| Changelog | **v1.2** — Platform decision: Windows WPF primary design target for R1/R2/R3; Android as Phase 2 adaptation notes |

---

> **Platform decision (authoritative — user direction):** Build the **Windows app first**, then move to Android. All UI/UX for R1/R2/R3 (0.3 Alive, 0.4 Points, 0.5 Hands) is designed and delivered for **Windows WPF**. Android is **Phase 2 after Windows validation** — adaptation notes for voice/talk and guide *display* only where Assist APIs allow; **computer-use stays PC-side**. Do **not** treat Android as a v1 / concurrent delivery surface for this cycle.

---

## 1. Purpose

Buddy today is a technically sound **local-first** assistant (WPF primary + Compose Android exists + embedded ASP.NET + Ollama), but its interaction model is still “a chat window that sits near the cursor.” heyclicky’s appeal is the opposite: the assistant *is* a character on your screen that sees what you see, talks with you, and **draws on the screen to point the way**.

This document curates UI/UX decisions to move Buddy from chat tool → on-screen companion **on Windows first**, while keeping Buddy’s differentiators: **local inference**, **explicit consent**, **Windows-first** (Android Phase 2 adaptation, not concurrent v1 delivery).

It **MUST** address the same three requirements as the TRD:

| # | Requirement | UX translation |
|---|---|---|
| **R1** | Fix voice shortcut (today voice only opens the chat bar) | Dedicated **Voice Overlay**; hold-to-talk; chat bar stays for typing |
| **R2** | Real AI assistant + computer control | Visible **Agent HUD**, plan cards, confirmations, kill-switch banner |
| **R3** | Internet + draw on screen to guide | **Research** status + citations; **Draw/annotate layer**; Guide stepper |

### 1.1 What heyclicky gets right (the bar)

Sources: homepage FAQ + changelog (v1.0 launch through v1.0.52).

| heyclicky behavior | UX principle Buddy should adopt |
|---|---|
| One hotkey → sees screen, talk out loud | Single summon gesture; voice-first; zero typing required |
| Walks you through whatever you’re working on | Step-by-step guidance anchored to real UI |
| Draws on screen to point the way | Visual grounding overlay (arrows, rings, highlights) |
| “Say heyclicky agent” → does the task | Explicit escalation Explain → Act |
| Only sees screen when hotkey pressed; screenshots never stored | Momentary, visible capture |
| Kaomoji / playful onboarding (Lenny: “favorite onboarding”) | Personality + hands-on first two minutes |
| Works with anything on screen, no plugins | App-agnostic; integrations optional |
| Quiet on calls / DND; announcements tunable | Respect focus; don’t narrate research loudly |
| Computer-use permission scoped; prefer not wrecking screen share | Visible control + careful HUD |

### 1.2 Where Buddy stands (v0.2 gaps — verified)

Sources: `docs/Feature-Coverage.md`, `docs/Architecture.md`, `docs/Buddy-Setup-Guide.md`, `QuickChatWindow.cs`, `MainWindow.cs`, `CursorCompanionWindow.cs`.

- Companion is a 46×52 mint arrow glyph with 4 moods (Idle/Listening/Thinking/Speaking); no Looking/Pointing/Unsure/Agent states.
- **R1:** `Ctrl+Shift+Space` → `OpenQuick(true)` → voice inside **QuickChatWindow** (480×354 chrome). Setup Guide literally: “Activate voice **in the compact bar**.”
- No hold-to-talk; one utterance; no wake word; TTS speaks full answer after completion.
- Screen capture requires Home preview; **not** tied to summon (“Opening the bar does not capture your screen”).
- No grounding overlay (SC-03/04 deferred). `OverlayNative` places windows; it does not draw ink.
- No Guide / step detection (SC-05/06 deferred).
- No Agent mode (AG-01..10 deferred). Buddy states it cannot perform computer actions.
- No internet research UI.
- Onboarding is a **setup guide document**, not an in-product heyclicky-style tutorial.

---

## 2. Design principles

1. **The buddy is the interface.** The character near the cursor is primary; windows are secondary.
2. **Show, don’t tell.** When the answer refers to something on screen, point before (or instead of) describing.
3. **Momentary, visible perception.** Buddy only looks when summoned; the user always sees *that* it is looking.
4. **Voice first, keyboard equal.** Every action reachable by voice, hotkey, and click.
5. **Never steal focus.** Overlays are click-through and non-activating unless the user engages.
6. **Explain before act.** Guide is default for “how do I…”; Agent requires explicit trigger + visible plan.
7. **Local and honest.** Surface model state and limits (“Thinking on your PC…”); web research is an explicit opt-in.
8. **Chat is a tool, not the product.** Fixing R1 means voice must escape the chat chrome.

---

## 3. Personality and visual identity

### 3.1 Character

- **Name:** Buddy. Voice: warm, brief, slightly playful; never sarcastic about user errors.
- **Form:** Small rounded companion (evolve from current mint arrow toward blob + kaomoji face), 32–40 px logical, DPI-scaled, vector-crisp.

### 3.2 Expression states

| State | Face / cue | Motion | Trigger |
|---|---|---|---|
| Idle | `^ ‿ ^` | Slow breath 1.00↔1.03 | Default |
| Listening | `• ᴗ •` + pulsing ring | Ring follows mic level | PTT / voice overlay |
| Looking | `◉ _ ◉` | 400 ms scan sweep | Screen captured |
| Thinking | `- _ -` + dots | Orbit | Waiting on first token / tools |
| Speaking | `^ ▽ ^` | Mouth ~ TTS amplitude | TTS |
| Pointing | `^ ω ^` | Flies to target | Overlay drawn |
| Unsure | `¯\_(ツ)_/¯` | Shrug | Low confidence |
| Error / offline | `(¬_¬)` | Grey | Ollama down |
| Agent working | `ง •̀_•́ ง` | Busy bounce | Agent run |
| Researching | `⌕ ˘ ▕` | Soft pulse | Web tool active (R3) |
| Sleeping | `- ω -` | Faded 40% | Fullscreen / snooze |

Transitions ≤ 200 ms; honor reduced-motion OS settings (static face swap).

### 3.3 Visual tokens

- Accent: teal `#20B8A6` (evolve from current `#8EE4C5` mint) for companion, ink, focus rings; neutrals follow OS theme.
- Overlay ink: accent @ 90% + 2 px white halo.
- Type: Segoe UI Variable (**Windows WPF primary**). Bubble ≥ 14 px. *(Phase 2 Android: system sans — adaptation note only.)*
- Radius: 12 px bubbles, 20 px compact bar; Mica/Acrylic on Windows 11 when available.

---

## 4. Information architecture

```
Buddy
├── Cursor companion (always-available presence)
├── Voice Overlay          ← R1 primary voice surface (NEW)
├── Speech bubble          ← streaming answers / chips
├── Compact chat bar       ← typed QuickChat (KEEP, demote voice)
├── Draw / annotate layer  ← R3 (NEW)
├── Agent HUD              ← R2 (NEW)
├── Home (MainWindow)
│   ├── Conversations
│   ├── Walkthroughs (Guide history)
│   ├── Memories / Prompts
│   ├── Devices (pairing)
│   └── Settings
│       ├── General / Companion
│       ├── Shortcuts (PTT vs tap, voice, stop)
│       ├── Voice
│       ├── Screen & privacy
│       ├── Internet research (R3 toggle)
│       ├── Guide & Agent (R2)
│       └── Devices
└── Privacy center
```

**Primary IA target:** Windows WPF (companion, Voice Overlay, draw layer, Agent HUD, Home, Privacy center).

**Phase 2 Android (not v1 delivery):** When Windows R1–R3 is validated, adapt bubble + Assist bottom sheet to mirror Talk/Guide *display* visuals where Assist APIs allow; Agent plans display-only with “Continue on PC.” No on-device computer-use UI as an execution surface.

---

## 5. Surfaces

> **Design target:** All surfaces below are specified for **Windows WPF** unless a subsection is explicitly labeled Phase 2 Android adaptation.

### 5.1 Cursor companion

- Spring-follow (stiffness ~180, damping ~22); offset ~18 px; flip at edges.
- Auto-hide: exclusive fullscreen / presentation; optional snooze.
- Dock: drag to corner to pin.
- Click → compact bar; right-click → quick menu (Talk / Guide / Settings).

### 5.2 Voice Overlay (R1 — NEW; replaces voice-in-chat as default)

**Problem:** Today voice opens the opaque Quick Chat window (`QuickChatWindow`), covering work and feeling like “chat with a mic button.”

**Decision:** Voice Overlay is a **lightweight, non-modal** layer:

- Companion → Listening with mic ring.
- Thin transcript bubble (max ~360 px) anchored to companion — not a 480×354 panel.
- Looking pill top-center while capture active: “● Looking” (no emoji dependency).
- Accent border flash on captured window.
- On release: Looking → Thinking → streaming text in bubble → TTS.
- Chips after answer: Follow-up · Guide me · Do it · Cite (if web) · Open in Home.
- Auto-collapse ~6 s after TTS; hover keeps open.

**Chat bar** remains for typing, attachments (Screen, Selection, Clipboard), and accessibility fallback.

### 5.3 Compact chat bar (existing QuickChatWindow)

- Keep for typed input; remove “voice is primarily here” messaging.
- Mic button MAY launch Voice Overlay (preferred) or legacy in-bar listen behind a setting “Classic voice in chat bar.”
- Attach chips with explicit on/off state.

### 5.4 Draw / annotate layer (R3 — NEW)

| Primitive | Use | Spec |
|---|---|---|
| Ring | Click here | 3 px circle, one pulse then steady |
| Arrow | Path from companion → target | Curved stroke, 350 ms draw-on |
| Highlight | Region | 25% accent fill |
| Underline | Menu/text | 3 px scribble |
| Badge | Multi-step order | 18 px 1,2,3 |
| Label | Caption | ≤ 5 words |

Rules: max 3 primitives; click-through; clear on target click / scroll / 15 s; companion flies to target (the “alive” moment). Hand-drawn jitter (heyclicky v1.0.40). Never fake a target when Unsure.

### 5.5 Agent HUD (R2 — NEW)

- Top banner while controlling: “Buddy is controlling — Esc to stop” + screen accent edge.
- Plan card: numbered actions with risk tags; red for high-risk.
- Follow-up mic/text on the card after completion (heyclicky agent card pattern).
- No silent actions; narrate before each step.

### 5.6 Home

- Left rail: Conversations (+ search/pin/archive over time), Walkthroughs, Memories, Prompts, Devices, Settings.
- Conversation view: badges like “used screen: Excel — Book1” / “researched: 2 sources” — **no stored images**.

### 5.7 Privacy center

- Last N captures: time, app, scope, redaction count, text summary.
- Blocklist editor (banking, password managers default-on).
- Toggles: wake word (P2), spoken answers, auto-advance Guide, **internet research**, **agent mode** (off by default).

### 5.8 Phase 2 Android adaptation notes (not 0.3–0.5 delivery)

- **Do not** design or ship Android Voice Overlay / draw / Agent HUD as part of 0.3–0.5.
- After Windows validation: map Talk transcript + Looking/Researching states into Assist / bubble chrome; Guide steps as a read-only stepper; Agent plan cards with **Continue on PC** (execution remains on Windows).
- Computer-use controls, kill-switch banner, and ink overlay stay PC-side until the Windows agent is solid.

---

## 6. Interaction flows

> Flows below are **Windows WPF** primary. Android equivalents are Phase 2 adaptations, not concurrent delivery.

### 6.1 R1 — Voice shortcut fix

**Today (broken relative to intent):**

1. User presses Ctrl+Shift+Space.
2. Compact chat window opens and activates.
3. Listening happens inside that window.
4. UX = chat product with voice bolted on.

**Target flow:**

1. User **holds** Ctrl+Space (or taps dedicated voice shortcut).
2. ≤ 100 ms: companion Listening + mic ring; Looking pill if capture on; **no chat chrome**.
3. User speaks; live transcript in bubble.
4. Release / pause → Looking sweep → Thinking → answer streams; TTS from first sentence.
5. If answer references UI → Pointing + draw layer (R3).
6. Follow-up: hold again (context retained for short window) or tap Follow-up chip.
7. Esc dismisses overlay without opening Home.

**Settings copy change:** Replace “Activate voice in the compact bar” with “Hold to talk opens Voice Overlay; tap opens chat.”

**Edge cases:**

| Case | Behavior |
|---|---|
| No speech pack | Overlay says install language / type in chat; don’t fail silently |
| Focus loss mid-listen | Stop mic (keep current `Deactivated` safety), show “Mic stopped” |
| Ollama down | Error face + one-line fix CTA `[Open PC setup]` |
| User wants classic bar voice | Setting “Classic voice in chat bar” |

### 6.2 Talk with screen (“what’s this?”)

1. Hold PTT → capture + UIA (with Looking indicators).
2. Ask; answer may include `targets` → draw.
3. Bubble cites nothing fake; Unsure asks user to scroll/focus.

### 6.3 R3 — Guide + draw

1. User: “Show me how to export this.”
2. Mode → Guide; stepper “Step 2 of 5” in bubble.
3. Each step: one sentence + ring/arrow + optional shortcut chip.
4. Auto-advance when expect matches (debounced), else Next/Back/Skip.
5. Missing target → Unsure + clarifying ask — **never** a wrong arrow.
6. Resume walkthrough from Home → Walkthroughs.

### 6.4 R3 — Web research + on-screen guidance

1. User enables Settings → Internet research.
2. Query needing external facts → companion Researching state + “Looking up…” in bubble (calm; don’t over-narrate — heyclicky calmed web narration).
3. Results summarized with **title + URL** chips; Open opens browser.
4. If task is “use this tool on my screen,” combine: research docs **and** draw on the live UI (Guide).
5. If web disabled: Buddy offers enable toggle or best-effort local knowledge with honesty.

### 6.5 R2 — Agent computer control

1. Trigger: “Buddy, do it” / “Buddy agent…” / Do-it chip on a Guide plan.
2. Plan card appears; user confirms.
3. Banner + AgentWorking face; each action highlighted + narrated.
4. High-risk pauses every time.
5. Completion summary + Undo where possible (ValuePattern edits, 30 s).
6. Stop: Esc / Ctrl+Alt+Esc / big mouse move.

**Intent routing:** Prefer Guide for learning verbs (“how do I”, “show me”); Agent for execution verbs (“do it”, “click”, “send for me”) after confirm.

### 6.6 Dictate & Refine (keep)

- Dictate hotkey → caret-anchored waveform; insert on release; Refine chip 5 s.
- Refine: diff in bubble; Apply / Undo; never auto-send (existing rule).

### 6.7 Onboarding (“Hello, my name is”)

Target: first pointed value < 90 s after install (model download may continue in parallel).

1. Companion hatches + name-tag; optional rename.
2. Mic permission in context.
3. Friendly model pull progress.
4. **Hold and ask what this is** on sample card → deterministic ring (success independent of VLM).
5. Privacy card: “I only look while you hold. I never save screenshots.”
6. Optional: enable internet research explanation; Agent remains off with tease chip “Later: I can do it for you — with your OK.”
7. Settle beside cursor.

Inspired by heyclicky hands-on tutorial (changelog v1.0.40) and praised onboarding.

---

## 7. Modes cheat sheet

| Mode | Purpose | Entry | Exit |
|---|---|---|---|
| Talk | Q&A ± screen ± web | PTT / wake / tap bar | Release, Esc, timeout |
| Guide | Multi-step pointing | “show me how…”, Guide chip | Done / stop / Esc |
| Dictate | Speech → focused field | Dictate hotkey | Release |
| Refine | Rewrite draft | Refine action | Apply / Undo |
| Agent | Buddy performs actions | Explicit agent phrase / Do it | Done / Stop / kill switch |

---

## 8. Feedback, latency, errors

| Moment | Target | If slow |
|---|---|---|
| Hotkey → Listening ring | ≤ 100 ms | Ring before mic ready |
| Release → Looking | ≤ 150 ms | Sweep |
| Release → first token | ≤ 1.5 s local 4B | After 2 s “Thinking on your PC…”; after 8 s offer smaller model |
| First sentence → TTS | ≤ 300 ms | Text always visible |
| Grounding → ink | ≤ 2.5 s | “Searching” wiggle |
| Web first result | ≤ 3–5 s | “Still researching…” |

Errors: in-character, one line, actionable — never modal over the user’s work.  
Example: `(¬_¬) My brain (Ollama) isn’t running. [Start it]`

---

## 9. Accessibility

- Companion states via shape **and** UIA LiveRegion (**Windows** — primary for 0.3–0.5).
- High contrast: system highlight colors for ink.
- Reduce motion: no fly-to; instant overlay.
- Keyboard-only: every hold gesture has toggle alternative.
- Locales: externalize strings; Telugu/Hindi early candidates; TTS per language.
- *Phase 2 Android:* TalkBack / Assist announcements when adapting voice/guide display — not a v1 acceptance gate.

---

## 10. Edge cases (curated)

| Edge | Decision |
|---|---|
| Exclusive fullscreen game | Sleep companion; no overlay; no capture |
| Screen sharing / call | Quiet Mode: suppress unsolicited TTS (manual first; detector later) |
| Password / banking window | Block capture; explain why |
| Mixed DPI monitors | Reuse placement tests; ink mapping must match |
| Agent asks to paste secrets | Refuse / confirm; never scrape password fields |
| Web returns blocked region | Honest error (heyclicky re-routes; Buddy should say blocked + suggest VPN/user action without over-promising) |
| Small 0.6B model agent | Cap tools; prefer Guide; message limits plainly |
| Voice shortcut conflict | Keep working binding; show tray fallback (existing pattern) |

---

## 11. Prioritized UI decisions (with rationale)

| Priority | Decision | Rationale | Platform / Req |
|---|---|---|---|
| **P0** | Split Voice Overlay from QuickChat (**WPF**) | Fixes R1; matches heyclicky summon feel | Windows 0.3 |
| **P0** | Hold-to-talk + capture-on-hold (**WPF**) | Makes “it sees what you see” obvious | Windows 0.3 |
| **P0** | Expressive states + speech bubble (**WPF**) | Companion becomes the product | Windows 0.3 |
| **P0** | In-product onboarding with first ring (**WPF**) | Time-to-delight; heyclicky lesson | Windows 0.3 |
| **P0** | Draw layer + UIA grounding (**WPF**) | Fixes R3 core; biggest gap vs heyclicky | Windows 0.4 |
| **P0** | Guide mode stepper (**WPF**) | Learning use case (DaVinci/FL Studio class) | Windows 0.4 |
| **P1** | Internet research toggle + citations | Fixes R3 web half without breaking offline | Windows 0.4 |
| **P1** | Agent HUD + plan/confirm/stop (**WPF**, PC execute) | Fixes R2 safely | Windows 0.5 |
| **P1** | Privacy center log | Trust for capture+web+agent | Windows 0.4 |
| **P2** | Classic voice-in-bar setting | Compat for users who liked bar | Windows 0.3 |
| **P2** | Wake word, localization polish | Windows expansion after core R1–R3 | Windows 0.5.x |
| **Phase 2** | Android voice/talk + guide *display* adaptation; handoff / “Continue on PC” | After Windows validation — **not** concurrent with 0.3–0.5 | Android post-0.5 |

---

## 12. Settings IA (target)

- **General:** name, face, theme, size, follow/dock.
- **Shortcuts:** summon, PTT vs toggle, voice one-shot, dictate, stop; conflict helper.
- **Voice:** device, TTS voice/speed, speak answers (always / voice-only / never), barge-in, classic-bar voice.
- **Screen & privacy:** capture scope, blocklist, capture log.
- **Internet:** allow research, provider status, clear research cache.
- **AI:** chat model, vision/grounding model.
- **Guide & Agent:** auto-advance, agent enable, confirmation strictness.
- **Devices:** pairing, revoke.

---

## 13. Success metrics

- Time to first **pointed** answer after install (< 3 min including model pull start).
- % summons using Voice Overlay (> 50% of voice turns not via chat bar).
- Guide completion rate (> 60%).
- Grounding precision (≥ 85% on eval set).
- Agent stop/undo rate (< 10% of runs as trust signal — not zero).
- Web: % research answers with ≥1 citation when web used.

---

## 14. Emulate vs differentiate (UI summary)

| Emulate heyclicky | Differentiate as Buddy |
|---|---|
| Voice-first overlay summon | Local “on your PC” honesty |
| Draw-to-teach | **Windows-first** WPF surfaces; Android Phase 2 display adaptation |
| Guide then Agent escalation | Agent off by default; stronger confirm UX; execute on PC |
| Playful onboarding | No cloud account / notch requirement |
| Privacy: capture only on summon | Screenshots never leave disk; web opt-in |
| Quiet when busy | Explicit research + agent toggles in Privacy center |

---

## 15. Sources cited

- Repo: `README.md`, `docs/Architecture.md`, `docs/Feature-Coverage.md`, `docs/Buddy-Setup-Guide.md`, `docs/Validation.md`, `CHANGELOG.md`
- Code: `MainWindow.cs` (hotkey → `OpenQuick`), `QuickChatWindow.cs`, `CursorCompanionWindow.cs`, `OverlayNative.cs`, `DesktopPreferences.cs`
- [heyclicky.com](https://www.heyclicky.com/), [heyclicky.com/changelog](https://www.heyclicky.com/changelog)
- Attached prior drafts: Buddy UI/UX Design Document v1.0, Buddy TRD v1.0, Nexa Technical Specification v1.0 (26 Sep 2026)

---

*End of UI/UX Design Document v1.2*
