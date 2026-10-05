# Buddy development coordination

The development team uses one orchestrator and task-scoped specialists. The orchestrator owns the backlog, integration and user-facing status. Specialists own bounded implementation or review work. These are development roles; they do not enable an in-app autonomous agent swarm.

Start with [current state](STATE.md), [backlog and ownership](BACKLOG.md), [decisions](DECISIONS.md), and [evidence ledger](EVIDENCE.md). Read [repository instructions](../../AGENTS.md) before editing.

## Working cycle

1. Reconcile the checkout, installed baseline, authorization and unresolved questions with STATE.md. Preserve unrelated changes.
2. Assign a stable task ID and attempt with one owner, full base revision, isolated worktree, allowed files, dependency generations, acceptance criteria and a deliverable. Record actual agent/task identifiers when agents exist.
3. Resolve missing context through source/evidence first. Ask the orchestrator when it changes scope or another owner's contract. Escalate user preference or approval questions only when necessary, with a concrete reviewable result.
4. Specialists implement and run isolated focused checks in their ownership. Their handoff states changes, tests actually run, limitations, and dependencies. They must not describe proposed tests as passed.
5. The orchestrator reviews diffs, resolves conflicts and integration contracts, then serializes aggregate/native testing. Native fixtures must not compete for foreground focus or the installed profile.
6. Build a fresh isolated preview, verify provenance and package behavior, and record the evidence. Installation and publication are separate gates; a code change is not permission for either.
7. Update the backlog, state, decisions and implementation/partial/missing table. A task closes only when its acceptance criteria are met or its precise remaining blocker is recorded.

## Durable handoff format

Each specialist reports: task ID; changed files; concrete resulting behavior; test command/result; mocked versus real boundaries; pending risks/context gaps; and the next dependency. The orchestrator records summaries here rather than copying private machine logs or conversations.

If an agent disappears, the replacement reads its task row and inspects the actual diff before resuming. If work pauses, record active command/session IDs outside the repository if machine-private, describe unfinished work here, and avoid restarting completed checks without a changed input or unresolved concern.

## Integration gates

Source review → focused checks → applicable aggregate/native checks → separate preview/package → optional authorized installation → optional authorized publication/CI. One owner performs every shared-state gate. Failed or denied gates stop only dependent actions; independent authorized work can continue.

No cloud provider becomes active merely because a transport implementation exists. Live activation needs a chosen provider/model, implemented routing, explicit transmitted-content/cost consent, secure credential entry and real acceptance. Cloud audio additionally needs explicit audio-sharing consent.

## Mechanical intake checks

Run `python scripts/team-ledger.py validate` and `python scripts/team-ledger.py status`. Before accepting a worker handoff run `verify-handoff --handoff <local-json> --worker-root <assigned-checkout>`. The JSON includes task, attempt, owner, baseRevision, dependency generations, files with SHA256, behavior, limitations and tests with command/environment/snapshot/exitCode/log/status. The checker never executes those commands or applies files. Stale attempts and late output after acceptance are refused.

`python scripts/team-ledger.py snapshot` records the actual revision, dirty state and complete source hashes. Evidence is tied to that snapshot; implementation changes invalidate affected checks. Passed, failed, blocked and not-run results stay distinct. Pause/resume records include active work, actual diffs/processes, open gaps and next actions. Native fixtures and integration remain serialized.

The intake checker also verifies the actual worker Git HEAD and assigned checkout path. Passed tests must identify the current build-input snapshot; accepted dependency artifacts have content digests as well as attempt numbers. A test using external source overrides requires separately recorded input hashes and an integrated rerun; a worker-root snapshot alone is insufficient.
