# Buddy 0.1 — implemented architecture

## Decisions

1. **Local PC AI is the selected operating mode.** The user explicitly chose local PC inference. Windows hosts Ollama and the shared service; Android is a native companion. There is no hosted backend, API key field, or paid provider dependency in this release.
2. **Native WPF replaces WinUI 3 for this first Windows build.** This keeps native Win32 hotkeys, UI Automation, speech and tray integration while allowing the C# code to be cross-compiled in the available environment. No Electron, PWA or WebView is involved. This differs from source ADR-001 and requires Windows UI validation before a production release.
3. **Embed one ASP.NET Core service.** The initial single-user workflow does not require Kubernetes, Temporal, Redis or separate cloud microservices. `BuddyHost` runs within the desktop process. Exiting the app stops phone access; closing the window to its tray keeps it available.
4. **Encrypted per-user snapshot store.** Chat, notes, device hashes and settings live in an encrypted JSON snapshot, replaced atomically after a successful mutation. This replaces the spec's SQLCipher/PostgreSQL approach for the local alpha. It loads history into memory; it is not intended for a large multi-user archive.
5. **Manual pairing with exact certificate pinning.** QR carries host, port, certificate fingerprint, and a one-use six-digit code. A successful pair returns a random device token. No unauthenticated history endpoints, blanket TLS bypass, automatic port forwarding or public internet deployment exists.
6. **Explicit context sharing.** Windows UI Automation text and visible-window screenshots require preview. Android Assist text and shared images enter a visible draft attachment. No background screen watcher, keyboard logger or unattended executor is included.
7. **OS speech for the alpha.** Windows uses installed System.Speech engines; Android uses only its on-device recognizer when available. This replaces the spec's realtime Opus gateway, Whisper fallback and duplex VAD pipeline. Hybrid mode reviews recognized text; Voice sends the recognized utterance.
8. **Native cursor companion and compact bar.** A separate transparent WPF tool window uses no-activate/click-through styles and a 33 ms UI timer to sample pointer position. It never calls SetCursorPos, injects clicks, or installs a keyboard hook. Win32 physical positions and monitor work areas keep geometry separate from WPF logical units; DPI changes trigger repositioning. A second WPF window activates only on the user's shortcut or tray action and calls the existing `BuddyService` directly. Desktop preferences are non-secret per-PC JSON in `desktop.json`; conversations and pairing remain in the encrypted store.
9. **Shortcut and microphone lifecycle.** RegisterHotKey uses no-repeat, with a separate candidate ID during changes so a rejected shortcut preserves the old binding. Windows-reserved shortcuts are never forced through a low-level hook. Quick voice records one utterance, stops before inference/TTS, and invalidates queued recognition callbacks when canceled. Escape, Stop, dismissal, loss of focus and Quit stop an active microphone. No screen capture happens on activation.

## Message transaction

The client supplies a conversation ID and random request ID. The service validates limits, rejects concurrent turns in the same conversation, builds bounded context, and waits for the single inference slot. It streams the model's final-answer content. Only a completed response commits a user/assistant pair atomically. Cancellation or failure leaves history unchanged. A repeated committed request ID replays the existing answer without another inference call.

The default is the dated `qwen3:4b-instruct-2507-q4_K_M` tag, whose ordinary response content streams directly. The unqualified `qwen3:4b` tag currently resolves to a thinking-only variant and is excluded from the model picker. For the optional smaller/larger hybrid Qwen models, both `think=false` and `/no_think` are requested; a fallback stream filter removes reasoning through a closing `</think>` marker, including a split marker. In those hybrid models, if no marker occurs, content is released on completion, which can delay first visible text. Model references: [Ollama Qwen3 tags](https://ollama.com/library/qwen3/tags) and [Qwen3 Instruct 2507](https://huggingface.co/Qwen/Qwen3-4B-Instruct-2507).

Screenshot bytes and screen text are supplied only to the current inference request. They are absent from the persisted `ChatMessage` model. The visible user message, answer, timestamp and input mode are stored. Written answers may describe the supplied screen content.

## API

Except `/health` and `/v1/pair`, every endpoint requires `Authorization: Bearer <device token>`. Responses have `Cache-Control: no-store`. There is a per-IP fixed-window rate limit of 120 requests per minute and a 3 MB request-body limit.

| Method | Path | Purpose |
|---|---|---|
| GET | `/health` | Product/version only |
| POST | `/v1/pair` | Redeem current pairing code |
| GET | `/v1/status` | Actual Ollama reachability and model availability |
| GET / POST | `/v1/conversations` | List summaries / create conversation |
| GET / DELETE | `/v1/conversations/{id}` | Read / delete history |
| POST | `/v1/chat` | NDJSON `status`, `delta`, `done`, or `error` events |
| POST | `/v1/refine` | Intent-preserving prompt rewrite; does not create a message |
| GET / POST | `/v1/prompts`, `/v1/memories` | User-managed reusable context |
| DELETE | `/v1/prompts/{id}`, `/v1/memories/{id}` | Remove saved item |

Model downloads, model selection, pairing-window creation and device revocation are local desktop operations. They are not exposed as unauthenticated or phone-admin API routes.

## Boundaries

- TLS 1.2/1.3 is supported for Android 10 compatibility, differing from the spec's TLS-1.3-only requirement.
- Certificate pinning is based on exact leaf-certificate SHA-256. It intentionally permits the self-signed certificate displayed by the PC. Certificate replacement requires re-pairing. The initial certificate lasts three years; automatic certificate rotation is not implemented.
- Explicit memories are truncated to 5,000 characters in model context; older chat messages are bounded by a character budget. These are approximate context budgets, not tokenizer-exact accounting.
- Sync is a six-second refresh while visible, not the proposed push change feed or conflict-resolution system.
- Speech services, installed language packs, PC hardware, network isolation and firewall configuration remain device-specific integration dependencies.
- There is no claim that every screenshot is automatically redacted or that managed memory is fully zeroized. Preview is mandatory on Windows capture; Android image/context previews are visible before Send.
