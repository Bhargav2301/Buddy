# Buddy development team

Read `docs/development/STATE.md`, `BACKLOG.md`, `DECISIONS.md` and `EVIDENCE.md` before working. `docs/development/README.md` defines the operating workflow. These are project records, not authorization to access accounts or publish anything.

- The active orchestrator is the sole integration owner. It assigns explicit file ownership, resolves dependencies and context gaps, reviews specialist results, runs integrated checks, and updates these records.
- Specialists work in isolated worktrees and only in assigned files. Before changing another owner's file, send the proposed change to that owner/orchestrator. Do not revert another agent's work or mutate any Git index. The orchestrator alone owns the integration checkout and Git operations; file promises alone do not prevent index races.
- Specialists do not commit, install, push, change PRs, or launch aggregate desktop fixtures. Report changed files, behavior, tests, limitations and unresolved questions to the orchestrator.
- Keep local speech/audio local. Preserve settings, the user's chosen voice, capture/headphone policies, source-field review/Undo, and bounded action approvals. Mocked provider tests are not live account acceptance.
- Do not claim physical mic/headphone or third-party app acceptance from synthetic fixtures. Separate implementation, fixture validation, live integration and manual acceptance in reports.
- The installed baseline is preserved. Build new previews in a fresh directory. Installation and publication are serialized by the orchestrator and require authorization applicable to that action. Current restrictions are in STATE.md; do not retry a specifically denied action through another route.
- On pause: stop new mutations, finish or safely cancel active commands, record pending processes and exact uncommitted state, and acknowledge. On resume: read the records, inspect actual files/processes, reconcile differences, then continue. Silence or elapsed time is not approval.
- Never put user profiles, credentials, model weights, microphone recordings, private logs or recovery archives in Git. Record sanitized evidence summaries and local evidence filenames instead.

User instructions prevail over this workflow. If instructions conflict, identify the precise conflict to the orchestrator before dependent work; continue independent authorized work.
