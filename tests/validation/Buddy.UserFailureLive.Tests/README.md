# Root-only local refinement acceptance

```powershell
dotnet run --project tests/validation/Buddy.UserFailureLive.Tests/Buddy.UserFailureLive.Tests.csproj -c Release -r win-x64 --self-contained true -- --live --model=gemma3:4b --rounds=3
```

Use the actual selected, already installed local model and an exclusive root-assigned model slot. This runner never installs or unloads a model. A `BuddySourceRoot` override compiles frozen reviewed server sources into the runner's own output; embedded hashes reject source drift and the final integration requires a fresh rerun.

The exact reported poem is `Write a poem on a boat sailing in a sea on a lonely night`; the prior email golden is `Write a polite email requesting Friday off.` An accepted candidate must be lexically changed, retain explicit concepts including `night`, and have a strictly higher model assessment. The independent oracle compares normalized letters/numbers/marks. A period, case change or whitespace change fails the candidate check. An exact-original rejected result with explicit NoChange, no scores/similarity and no claimed changes passes **outcome integrity**, but is separately counted as truthful no-refinement and never establishes usefulness. Model scores remain self-reported, so accepted candidates still require human quality review. Output and latency are logged only for these canned prompts. There is no source-field Apply, real browser UI, installed-service or physical-audio acceptance here.

The first live attempt used an earlier synthetic poem and a weak visible-change check; it accepted period-only output. The second required lexical change but accepted Write-to-Draft with unchanged scores. Both runs are preserved as superseded evidence and are explicitly **not quality acceptance**.

The separate native `UserFailureChecks.RunCometReadOnly()` mode is root-only. It requires the current privacy blocklist through `BUDDY_COMET_BLOCKED_APPS` (missing means NOT RUN), enumerates selectable live Comet windows and invokes production UIA capture with a pinned identity, without focus, input, screenshots or launches. The fixture itself does not read installed preferences; root records its separate read of only BlockedApps. It logs counts, control roles and typed error metadata, never page text, titles or private values. Read-only observation is narrower than end-to-end browser task acceptance.
