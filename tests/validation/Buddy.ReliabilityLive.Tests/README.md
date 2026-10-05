# Real local-model reliability checks

This opt-in runner exercises production `BuddyService` with an already installed local Ollama model in an isolated, disposable encrypted profile. It uses only canned prompts and a synthetic single-control snapshot. It does not use the installed HTTP server or credentials, capture an application, execute actions, edit an external field, run tools from model output, or produce audio. Network transport is fixed to loopback with redirects and proxies disabled. No download, model unload or forced latency occurs.

The integration owner must schedule an exclusive model slot. Build alone, or running without both opt-in arguments, makes no model calls:

```powershell
dotnet build tests/validation/Buddy.ReliabilityLive.Tests/Buddy.ReliabilityLive.Tests.csproj -c Release -r win-x64 --self-contained true
dotnet run --project tests/validation/Buddy.ReliabilityLive.Tests/Buddy.ReliabilityLive.Tests.csproj -c Release -r win-x64 --self-contained true -p:BuddySourceRoot=C:/absolute/reviewed/checkout/ -- --live --model=gemma3:4b --rounds=1
```

The source-root override needs a trailing slash. The explicit model should match the user's selected installed model; this runner never reads or changes their selection. Up to three sequential rounds are allowed. First-round residency is unknown; subsequent rounds are identified without claiming forced cold/warm conditions.

Each case records its exact prompt/context, elapsed service time, result and pass/fail. Acceptance requires:

- Reported Comet Agent request: canonical Comet action or explicit actionless clarification, without an exception, unsupported launch or action execution. Clarification is labelled as incomplete task resolution.
- Exact Open Comet control: canonical high-risk plan, labelled as deterministic rather than model generation.
- Empty-context Guide: useful orientation with no fabricated pointer.
- Nested GuidePlan: real model generates steps bound to the one supplied synthetic button. This is not observed-window acceptance.
- One-sentence rainbow reply: one complete relevant sentence, no Guide/Agent offer, and one completion; a generic fallback does not pass relevance.
- Exact installed Friday-email prompt and asking-for variant: no invented reason/deadline/audience obligation. Keeping the original is a safety success, explicitly labelled as no successful rewrite demonstrated.

Source/fixture hashes are embedded at build and verified at execution. Timing is service completion latency, not sound latency or physical perception. The harness prints observed window, Make plan UI, action execution and audio as NOT RUN.

The separate native dispatcher `--reliability44` runs owned-window UIA plus mocked inference; `--reliability44-live` runs the desktop owner's real-model fixture. Root must serialize both. That fixture must confirm the actual observed requested editor, actual Make plan route and verified ink before claiming UI acceptance. Neither fixture establishes real Comet browser, source-field replacement/Undo, physical mic/headphone or third-party application acceptance.
