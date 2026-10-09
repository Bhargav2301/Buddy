# October 5 feedback boundaries

This independent suite links production server files into its own output directory and records every compiled source hash. Its model is a leaf `HttpMessageHandler` with no socket transport. Preference tests use unique owned temporary directories; attempting an implicit installed preference path throws.

Run from the checkout with the existing SDK:

```powershell
dotnet run --project tests/validation/Buddy.FeedbackBoundary.Tests/Buddy.FeedbackBoundary.Tests.csproj -c Release -r win-x64 --self-contained true -p:BuddySourceRoot='<reviewed source root>\'
```

Use `-- --binding-only`, `-- --settings-only`, or `-- --lifecycle-only` for isolated checkpoints. `BuddyDesktopRoot` can independently select reviewed preference source. The lifecycle helper is compiled only when present in the source root; explicitly requesting absent lifecycle tests returns NOT RUN / exit 2. Final integrated runs should include every group.

The original baseline produced seven wrong-target failures: Camera to Calculator, Spotify to Comet, Notepad to Calculator, Explorer to Notepad, one explicit URL to another, native Spotify to an unsolicited website, and Camera continuation to Calculator. It also reproduced stale voice and extension-setting overwrite plus concurrent fixed-temporary-file collisions. These are independent synthetic reproductions of host gaps; they do not establish which event caused a particular installed screenshot. Existing cancellation and explicit wrong-app Guide checks passed. Preserve the initial failing logs.

Repaired-source tests additionally cover prohibited URLs, mismatched prior launch receipts, exact matching targets, unknown-app clarification/native-only choice, late canceled plans and continuations, terminal refinement deadlines and no-final streams, replacement ownership, callback cancellation, deferred enumerator disposal, and retained inference ownership while an ignored-cancel transport finishes. No test dispatches an action.

`FeedbackLifecycleChecks.Run()` is a separately compiled, root-only owned WPF fixture. It tests real Settings controls/navigation and failed-save retention, plus selected-region Enter routing intercepted before capture/model/microphone/speech. It is not invoked by this headless project and does not prove browser-field or installed-user acceptance. Root owns dispatch, serialized execution, current-source compilation and any actual-model/native acceptance.

Read-only audit also identified stale `LocalPromptWatcher` completion paths after `IsCurrent` and `Watch`; the lifecycle owner repaired them. Scheme/TLD speech cleanup and QuickChat/island ownership were inspected separately; visual layout and physical audio remain root/manual checks.
