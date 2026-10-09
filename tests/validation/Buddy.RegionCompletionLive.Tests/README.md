# REGION-50 noninteractive completion harness

Only the root integration owner runs this executable with actual Ollama or the internet. With no explicit `--live-model` flag it prints NOT RUN and exits 2 before creating transports or temporary state. `--liveweb` separately opts into the one fixed reviewed canned query: `What causes rainbows in water droplets?`. It is never extracted from the image or expanded by a model. `--research-only --liveweb --live-model` runs that case separately.

```powershell
dotnet run --project tests/validation/Buddy.RegionCompletionLive.Tests -c Release -r win-x64 --self-contained true -- --live-model --model=gemma3:4b --liveweb --output=C:\path\to\new-evidence.jsonl
```

The output file must be a new absolute local path with an existing parent directory. Omit `--output` to use console JSONL only. The root can run the compiled self-contained executable directly with the same arguments. The already-installed selected model must support images; no model is downloaded or silently substituted. Default is gemma3:4b. A source override can be built with `-p:BuddySourceRoot=C:\absolute\source\root\`; include the trailing separator. The build source-links production service files into the isolated runner output and embeds every compiled input's hash. Startup refuses changed source or a changed server file inventory. Root can rerun against an integrated teaching amendment without changing this harness.

The image case calls actual `BuddyService.Teach` with a region image, a neutral owned context and no observed controls. The default image is a deterministic, pure managed PNG, not a screenshot: a short orange bar A labelled 2 and a blue bar B labelled 6, three times as tall. The question asks to compare the two bars but does not include the expected colors, values or labels. PNG bytes are sent only to the fixed loopback model endpoint; evidence stores their SHA256/count, not the bytes. No HWND, live UIA, screen capture, microphone, audio playback, installed application or installed user profile is used.

For an explicitly owned nonprivate recorded PNG, pass both `--owned-png=C:\absolute\fixture.png` and `--expectation-json=C:\absolute\fixture.json`. PNG size is bounded at 2 MB and 2048 by 2048; UNC, relative and linked paths are refused. The independent expectation file has this shape:

```json
{
  "question": "A canned question that does not disclose the expected answer",
  "requiredFactAlternatives": [["known fact wording", "accepted synonym"], ["second independent fact"]],
  "forbiddenClaims": ["known contradictory claim"],
  "valueFacts": [{ "names": ["named object", "accepted object alias"], "values": ["6", "six"] }]
}
```

The root operator must verify that a supplied PNG and question are owned and nonprivate. Expected facts are not sent to the model. Textual fact checks reject generic acknowledgements, common clarification fallbacks, extra actions, invented annotations, contradictory listed claims, echoes and answers over three sentences. The synthetic fixture additionally requires explicit blue/B/right=6 and orange/A/left=2 relations and rejects swapped/conflicting assignments. Relation parsing expects a named object followed by its value within the same bounded clause before another object's name; valid unusual wording may conservatively fail. These checks are not a semantic proof: inspect the recorded full answer, including comparison direction and any extra claims. Custom image expectations can declare valueFacts using the same checks.

The research case calls actual `ResearchReviewedRegion` with the exact canned query. It uses production `WebResearch` search, HTML extraction and fetch logic through an injected auditing HTTP handler. That handler reproduces production's TLS defaults, public-address DNS guard, redirect/cookie/proxy settings; it is not the literal default constructor instance. It rejects non-HTTPS/private destinations, request bodies, cookies and authorization before dispatch, and logs every public request destination/method plus DNS results. Search queries and fetch attempts are counted separately. This proves which requests this harness made; it does not prove universal transport behavior or reproduce the previously reported SSL error.

Only actually fetched public readable pages can satisfy the source gate. The summary must explain the independently known rainbow mechanism using light, droplets, refraction and reflection, rather than just saying it found sources. Raw URLs, fabricated source links and generic/missing-evidence clarifications fail useful completion even when the production service safely returns them. Full canned model request text, response bodies, source URL/title/character-count/hash and final result are recorded; source pages may be bounded excerpts and the answer still needs human factual review. No image, audio, region question, screen context or saved history is included in the research network request.

Cases run sequentially under 90-second outer deadlines; research retains its production 60-second total bound. StopAll is called after each case. If the region call throws or times out, research is skipped to avoid overlapping a potentially unsettled model call; root can run research separately after settlement. Model residency is not forced, so elapsed time is not a cold/warm benchmark. Exit 0 means all attempted usefulness gates passed, 1 means a case failed, and 2 means explicit opt-in/readiness/source/probe setup was blocked. Owned temporary encrypted state is cleaned only after checking its absolute temp prefix, direct entries and absence of links. Evidence is intentionally retained at the root-supplied output path.

This is service-level completion evidence, not the full gesture-to-result flow. The earlier native region routing fixture intercepted before capture; neither that fixture nor this runner alone proves rectangle drawing, Enter, actual capture, local analysis and final UI rendering end to end. No installation, account, permission grant, native foreground action or physical audio acceptance is included.

Worker headless validation:

```powershell
dotnet run --project tests/Buddy.RegionCompletion.Tests -c Release -r win-x64 --self-contained true
```

These tests inject both model and public HTTP responses, exercise the actual production service paths, independently decode the synthetic PNG pixels, verify no answer facts leak into model text, and probe the request audit's refusal behavior. Mock results are separate from root's actual local-model/network evidence.
