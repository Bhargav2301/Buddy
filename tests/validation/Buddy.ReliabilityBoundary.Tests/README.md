# Independent reliability regression checks

This runner uses the production service/store with a queued in-memory Ollama transport and a disposable encrypted profile. It cannot execute desktop actions, use the installed profile, capture a window, or contact a model. It compiles source into its own output directory; `BuddySourceRoot` never writes another owner's bin/obj.

The unchanged failure goldens are the observed `open.target=comet`/empty `value`, a two-sentence promotional answer to an explicit one-sentence request, and refinement adding reason/deadline obligations to a Friday-leave email. Baseline `1b330c4` reproduced all three failures before repair. These failures remain failures in the baseline logs, rather than becoming expected-success assertions. Expanded checks cover clarification versus execution, launch boundaries, whole-answer regeneration, cancellation, fidelity, explicit Unicode budget units, required decisions and source provenance.

Run from the repository root with the existing local SDK/package cache:

```powershell
dotnet run --project tests/validation/Buddy.ReliabilityBoundary.Tests/Buddy.ReliabilityBoundary.Tests.csproj -c Release -r win-x64 --self-contained true -- --goldens-only
dotnet run --project tests/validation/Buddy.ReliabilityBoundary.Tests/Buddy.ReliabilityBoundary.Tests.csproj -c Release -r win-x64 --self-contained true -p:BuddySourceRoot=C:/absolute/reviewed/checkout/ -- --area=planning
```

The source-root override needs a trailing slash. Areas are `planning`, `chat`, `refine`, `core`; omit the area for all checks. The pure new core is conditionally compiled when its source exists; selecting it against baseline records a failure indicating that the implementation is absent.

Every case records its own outcome and failure, and the process returns nonzero if any selected case fails. Build-time hashes of production and fixture code are embedded in the binary; execution rejects changed/deleted inputs and added production source files. Source snapshots and assembly hashes are emitted as JSON lines. Preserve logs outside tracked source, and rerun on the final integrated tree. The old baseline log and intermediate owner-source checks do not establish final integration acceptance.

Review acceptance means a plan can be displayed. It never means an action ran. The tests separately require the strict execution validator to reject clarification and malformed launch plans. Model-attested preservation and cosine=1 are deliberately adversarial fixtures, not quality measurements.
