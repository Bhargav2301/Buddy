# User-failure repair boundaries

```powershell
dotnet run --project tests/validation/Buddy.UserFailureBoundary.Tests/Buddy.UserFailureBoundary.Tests.csproj -c Release -r win-x64 --self-contained true
```

For isolated review, set `BuddySourceRoot` to the frozen server worker, `BuddyWindowRoot` to the frozen window worker and `BuddyControlRoot` to the frozen computer-use worker, each with a trailing separator. Production source is compiled into this project's own output; no other worker's build output is written. Every linked C# input is embedded with SHA-256 and checked before tests. Server inventory changes are also refused. The final integration still needs a fresh build and rerun.

The service cases use a leaf in-memory HTTP handler with no socket. Selection cases inject metadata and policy decisions; native defaults throw. Routine tests inject the backend and call the actual parser, bounded controller and global wrapper. They do not instantiate the foreground-event monitor, call native observation, validate real executables or launch applications.

Acceptance checks cover the exact reported poem, bounded retry, cosmetic-only echo, invented constraints, preserved negation/code/numbers, assessment and similarity vetoes, final assembled required additions, cancellation before and during repair, late completion disposal, and no persisted completion. Window tests cover destroyed handles, reused process IDs/lifetimes, denied process/elevation checks, privacy denial, expiry and explicit fresh recovery. Computer-use tests require one checked dispatch, owned receipts, fresh requested-app postconditions, no redispatch, exact allowlisted queries, and pending-operation ownership across cancellation and new wrapper instances.

Passing these cases establishes the specified mock/pure contracts. It does not establish actual browser refinement, installed-app accessibility, source-field support, physical audio, or general computer use. The separate native fixture uses owned WPF controls and an injected model/runner; its Comet mode is explicitly read-only and logs metadata counts only. Root owns native and local-model scheduling.
