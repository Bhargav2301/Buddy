# Root-only semantic refinement run

Run with root's exclusive model slot and the already selected installed model:

```powershell
dotnet run --project tests/validation/Buddy.RefinementMeaningLive.Tests/Buddy.RefinementMeaningLive.Tests.csproj -c Release -r win-x64 --self-contained true -- --live --model=gemma3:4b --rounds=3
```

`--case=reported-poem-exact` narrows an investigative run; the complete catalog remains the acceptance set. `BuddySourceRoot` supports a frozen worker source override with embedded source hashes; root reruns the integrated tree. No model installation/unload, installed profile, browser, field write, action or audio occurs.

Useful structure must pass the independently authored fixture-specific meaning oracle. Higher model scores do not count. Safe original retention is reported separately and **fails a scenario whose utility target remains unmet**, including the user's exact poem. A contradictory input can safely refuse without becoming a useful-rewrite pass. Exit zero is still only the specified golden evidence; root reviews actual wording and records any architecture/model limitation explicitly.

The 14 scenarios include all eight exact prior comparison prompts. Records tag that cohort and the summary reports its distinct-case denominator separately from rounds and the six additional adversarial cases. Raw `/api/chat` responses are recorded only for these fixed canned prompts, bounded to 40KiB per response. Headers, embedding vectors and arbitrary user data are omitted. Responses are buffered without byte changes for evidence; this runner makes no first-token latency claim.

The separate native `CometGuideChecks.Run(bool localModel=false, ScreenObservationOptions? observationOptions=null)` harness uses production capture and, only with a second explicit opt-in, a minimal fixed-label browser-chrome context for local Guide. It requires `BUDDY_COMET_BLOCKED_APPS`; model mode also requires `BUDDY_COMET_GUIDE_ALLOW_LOCAL_MODEL=1` and `BUDDY_COMET_GUIDE_MODEL`. It never transmits page, address, title or editable values and never executes the guide. Only its bounded final generated guide prose is logged with fixed-public-chrome provenance; no raw HTTP or full observed context is recorded. Optional capture strategy/depth parameters preserve production bounds and are root-controlled diagnostics. Secure-desktop refusal remains a blocker, never a reason to bypass access checks.
