# Independent local task boundaries

Headless checks link the actual journal source. They cover whole-token authority, replaced and late callbacks, terminal immutability, readiness versus completion, duplicate events, detached snapshots, bounded records/stages, display validation, clock reversal and concurrent replacement/completion. They do not execute or cancel any real operation and do not prove the UI emits truthful events.

```powershell
dotnet run --project tests/validation/Buddy.LocalTaskBoundary.Tests/Buddy.LocalTaskBoundary.Tests.csproj -c Release -r win-x64 --self-contained true -p:BuddySourceRoot=C:\path\to\reviewed-source\
```

The executable prints and verifies an embedded SHA-256 receipt for each compile input, including an external `BuddySourceRoot` override. Any source change requires a rebuild. Root must rerun on the integrated final tree. No native windows, model calls, network, installed profile, task actions or persistence are used. Caller redaction remains a contract; the journal cannot infer which supplied labels are private.
