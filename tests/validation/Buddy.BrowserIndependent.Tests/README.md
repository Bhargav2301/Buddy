# Independent browser61 checks

The acceptance corpus was frozen before inspecting adapter outputs. It specifies behavior, not the implementation's helper predicates. Tests will call the production JavaScript and C# contracts using explicit source overrides whose hashes are recorded with each run.

Permitted execution is headless, deterministic, and synthetic: Node's built-in test runner, injected DOM/provider/native-bridge adapters, and isolated .NET fixtures. No dependency download is required for the JavaScript tests. No real signed-in tab, browser profile, private history, original user file, upload, Send, model, or installed setting is touched.

The evidence must distinguish protocol validation, synthetic DOM behavior, compiled integration, and actual browser support. Unknown ChatGPT account/document/completion/attachment signals must fail closed. A matching URL, visible filename, ordinary file input, or simulated upload success is not enough to establish live support.

The corpus includes positive exact-bound cases and adversarial races. A partial effect must be reported honestly; cancellation is not proof that an earlier upload or draft mutation did not happen. Cleanup must not overwrite a newer user draft or delete an unrelated attachment. The original asset and OCR text remain distinct.

The final independent checkpoint has 85 passing Node cases (11 provider refusal, 24 injected DOM, 28 core, 22 controller/bridge) and 48 C# cases / 81 assertions. These are distinct fixture cases, not 133 live acceptance scenarios. Exact external source hashes and before/after stability checks are in the logs under `validation/browser61/tester` and the private handoff. The initial Node sandbox path failure is retained separately and is not a test failure.

Run Node with an absolute `BUDDY_BROWSER_SOURCE_ROOT` pointing at the production `integrations/browser` directory:

```powershell
node --test provider-boundary.test.cjs dom-boundary.test.cjs core-boundary.test.cjs bridge-boundary.test.cjs
```

Run the C# project with explicit production source root and the offline restore configuration. The project source-links the server into its own build directory; it does not write another worktree's build outputs:

```powershell
dotnet run --project Buddy.BrowserIndependent.Tests.csproj -c Release -r win-x64 --self-contained true -p:BuddySourceRoot=C:\path\to\source\ -p:RestoreConfigFile=C:\path\to\NuGet.offline.config
```

The native HTML fixture uses the same production DOM driver with actual textarea/contenteditable, File, DataTransfer and Event APIs, but its identity/history/attachment evidence remains synthetic. `stage-native-dom.py` only stages hash-bound local fixture files; it does not launch a browser. Root ran and recorded the first actual owned headless Chrome fixture (17 cases); QA verified its saved receipt/DOM output and input hashes. Root owns any rerun against a changed driver. That evidence is not ChatGPT account or upload acceptance.

Production ChatGPT remains readiness-only. Verified account/workspace/chat identity, real conversation completeness, and trustworthy attachment acknowledgments are still missing. No real browser account, private history, file upload or Send was exercised by these independent tests. Actual JavaScript-to-C# wire integration is a separate follow-up fixture; the bridge tests here inject its native endpoint.
