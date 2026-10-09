# Independent followup53 boundary fixture

This project tests actual production logic using fake transports, clocks and other dependencies. It must never open WPF windows, start a pipe/listener, use a live provider/model, read an installed profile or execute an app action. A passing test is pure regression evidence, not workflow, native, model or physical acceptance.

The sibling acceptance matrix is `docs/Followup53-acceptance-matrix.md`. Stable worker APIs are linked into this project's own build outputs; every compile input is embedded as a SHA256 receipt and checked again at execution. Source-root overrides and exact commands/results will be recorded in the final handoff. No source file from another owner's checkout is edited or copied into the deliverable.

Five explicit lanes are available: `teaching`, `core`, `refine`, `provider`, `notch`. Absent linked checkpoints refuse the lane. No live/native switch exists. The notch lane uses injected model/file delegates and creates only its own unique temporary notes directory; it does not invoke the linked Windows reader. Provider tests instantiate a mock leaf `HttpMessageHandler`, never the production transport or pipe. The broker is in-memory only.

Run on an integrated source tree:

```powershell
dotnet run --project tests/validation/Buddy.Followup53Boundary.Tests/Buddy.Followup53Boundary.Tests.csproj -c Release -r win-x64 --self-contained true '-p:BuddySourceRoot=C:\path\to\integrated\Buddy\' -- --lane=teaching
```

With unchanged build inputs, run each remaining lane using `--no-build`. Worker checkpoints use `BuddyCoreRoot`, `BuddyRefineRoot` or `BuddyNotchRoot` for those desktop source links, with `BuddySourceRoot` selecting the complete server source set. SourceReceipt refuses execution if compiled inputs changed after the build. The actual commands, exact external hashes and retained first failures are recorded in the handoff and logs.

Independent results: core15, external options17, teaching15, provider/broker37 and notch37 passed. These are separate boundary results, not a combined acceptance score. Source53 teaching usefulness, account/pipe interoperability, actual external-field Apply/Undo and all native interactions remain outside this worker's claims. `aggregate-plan.json` is a read-only allowlist for a root-owned serial aggregate, not a completed aggregate receipt.
