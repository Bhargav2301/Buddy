# Buddy core and optional tools

The approved direction is the clicky-windows interaction model with extra capabilities kept optional. Buddy retains native WPF, offline speech/model processing and guarded target verification.

The current reference [app.py](https://github.com/AbhishekVulla/clicky-windows/blob/main/app.py) keeps the process alive after windows close; [tray.py](https://github.com/AbhishekVulla/clicky-windows/blob/main/tray.py) provides genuine Quit; [overlay.py](https://github.com/AbhishekVulla/clicky-windows/blob/main/overlay.py) follows the pointer with listening/thinking/pointing state. Reference source is MIT, while its PyQt runtime has separate terms. No reference source, artwork or PyQt dependency was imported.

| Area | Current implementation | Limit |
|---|---|---|
| Resident core | Independent companion, direct Voice, configured shortcuts, Home hide versus Exit; desktop/maximized-window visibility fixed | User hide/snooze and actual borderless fullscreen can suppress it |
| Local teaching | Bounded current-window observation, concise speech, separate readable lessons and freshly verified pointers, Next/Back, optional region input | Unsupported/private/incomplete contexts can refuse capture; actual target-app acceptance required |
| Prompt refinement | Explicit original-field metadata binding before summon, local refinement, anchored diff, Accept/reject/Undo; passive suggestion opt-in | Requires supported UIA Edit/Value/event support; never silently edits Buddy instead |
| Approved execution | Bounded capability jobs, one mutation lane, two read-only slots, receipts, continuation and fresh approvals; Stop cancels work | Local inference serialized; no arbitrary agent swarm, terminal execution or automatic uncertain-action replay |
| Local knowledge/history | Explicit scoped text import, encrypted job/knowledge state and app-scoped references | No universal app integration or passive folder import |
| Voice/appearance | Owner-supplied single character with independent runtime face, capture toggle, local voice choices, headphones-only policy | No clean faceless plate was supplied; physical speech/headphone acceptance remains manual |
| Connector prototypes | Consent/scope/token/protocol logic tested with mocks and own loopback fixtures | Real account grants are hard-disabled; no connected Gmail/Notion task capability |

See [0.3.9 workflow and validation boundaries](Windows-0.3.9.md), [voice notices](voice/README.md), and [branding provenance](../apps/windows/Buddy.Windows/Assets/Branding/README.md). Source-only publishing excludes user profiles, audio, model weights, runtimes and local recovery/build archives. No downloaded installer or cloud provider is required by normal local operation.
