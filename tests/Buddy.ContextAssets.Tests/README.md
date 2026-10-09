# Original context assets

`ContextOriginalAsset.CreateCopy(bytes, displayName, mimeType)` creates a sealed, memory-only original asset. It copies the input, computes its own SHA256 and bounds supported TXT/Markdown to 64KiB and PNG/JPEG to 2,000,000 bytes. The selected-file reader remains responsible for UTF-8/signature/decoder/dimension validation. The class performs no file, network or native operations.

Each `Retain()` returns an independent disposable lease. `CopyBytes()` returns a working copy, never the shared backing array. The final disposed lease zeroes the shared owned bytes. Callers must dispose every retained lease and clear any working copies they create; Clear cannot retract bytes already copied by a caller.

`RefinementWorkspace.StageSource(scope, input, revision, originalAsset)` retains workspace ownership after matching kind, byte count and original digest. The caller may dispose its reader result after staging. Snapshots contain `OriginalAsset` metadata, never the bytes. Aggregate retained bytes are bounded to 8,000,000/session and 16,000,000/workspace. Remove/Clear/Close/Dispose release workspace leases and invalidate frozen selections.

`RetainOriginalAsset(frozenSelection, sourceId)` obtains an independent lease only for an included, reviewed source under the exact current workspace/session/revision. This is a local ownership API, not send authority. A borrower remains responsible for disposal and for observing the frozen selection's invalidation before any eventual destination operation.

Input/snapshot `OriginalDeliveryRequired` means text conversion is insufficient. `SetOriginalDeliveryRequired(scope,id,required,revision)` changes this choice, invalidates existing selections and resets source review with a new digest. Existing text-only destinations refuse required originals. Optional retained originals generate a visible warning that only reviewed text is included and the original remains local, unattached. Raw images still require reviewed extraction before a text destination can accept them.

Reviewed text/history projects using the root-owned `selected-data` provenance branch. Embedded URLs remain exact untrusted data, never fetched or verified. The explicit HTTPS link path likewise preserves its reviewed query/fragment; credentials and unsupported/nonpublic URL forms remain refused. Existing web/document retrieval contracts are unchanged.

Validation: **58 original-asset/source assertions and 77 workspace/review assertions passed**; both Release builds have 0 warnings/errors. Fixtures use synthetic bytes only. They cover BOM/CRLF preservation, independent copies/leases, final-owner zeroing, current-selection retrieval, cross-chat refusal, Clear/Close/Remove/Dispose, byte budgets, required-original refusal, OCR provenance, changed delivery-choice review and exact selected URL/history preservation. `Evidence/pre-url-fix-tests.log` records the URL refusal reproduced before the selected-data integration.

```powershell
dotnet build tests/Buddy.ContextAssets.Tests/Buddy.ContextAssets.Tests.csproj -c Release
dotnet --roll-forward Major tests/Buddy.ContextAssets.Tests/bin/Release/net8.0/Buddy.ContextAssets.Tests.dll
dotnet build tests/Buddy.ContextWorkspace.Tests/Buddy.ContextWorkspace.Tests.csproj -c Release
dotnet --roll-forward Major tests/Buddy.ContextWorkspace.Tests/bin/Release/net8.0/Buddy.ContextWorkspace.Tests.dll
```

The worker used its cached dependencies, an empty package-source configuration for initial restore, and the installed newer ASP.NET runtime through Major roll-forward. No installation or downloads occurred.

No actual agent, browser, binary attachment transfer, file decoder, local model, WPF/UIA, upload or Submit was exercised. The server artifact belongs to the isolated worker and is not a preview build; the parent must integrate the scoped files, rebuild and rerun affected suites.

The existing local-agent bridge accepts only status/question/deny events and decisions, with 16KiB frames. Its token proves possession of a paired session, not a coding product or exact external chat. No agent-side draft/attachment consumer exists in the repository. A queue and synthetic transport alone would not deliver a draft to a real coding agent. The first concrete target client must be chosen and its supported draft/attachment interface implemented; generic UIA cannot provide original attachment delivery or stable external account/chat identity. No LocalAgent protocol or unsupported provider scaffolding was added.
