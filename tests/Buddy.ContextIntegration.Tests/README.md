# Context integration regression fixtures

Pure tests of the production context integration. `BuddySourceRoot` selects the checkout whose Windows files are linked. `BuddyServerAssembly` selects its previously built server DLL; this project never builds into that checkout. Build that server first before claiming an integrated-source pass.

`GeneratePureHost.ps1` extracts the actual MainWindow history-sync declarations/body, arm/consume/revoke bodies, InlinePromptWindow request-token linkage and context Apply seam into this project's `obj` directory. Marker changes fail generation. The synthetic host replaces only dispatcher, window/status, field and model-stream adapters. GuardedEdit, NotchChatSession, refinement request, option plan, workspace and review implementations remain production code. The generated manifest records the external sources, server DLL and generated fragment hashes.

```powershell
dotnet build tests/Buddy.ContextIntegration.Tests/Buddy.ContextIntegration.Tests.csproj -c Release -p:BuddySourceRoot=C:/path/to/selected/Buddy --configfile tests/Buddy.ContextIntegration.Tests/NuGet.Config -p:NuGetAudit=false
dotnet --roll-forward Major tests/Buddy.ContextIntegration.Tests/bin/Release/net8.0/Buddy.ContextIntegration.Tests.dll
```

The explicit empty package-source list prevents package downloads. Major runtime roll-forward is used on the current worker because its installed ASP.NET runtime is newer than the test's net8 target. No runtime or package installation is needed.

Final worker result: **72 assertions passed; build 0 warnings/errors**. The capacity/Clear test first reproduced a real pre-Clear-pair resurrection; its failing log and source hashes are retained in `Evidence/pre-clear-fix-*`. The final run verifies the parent's once-per-observed-pair correction. The first execution also exposed a fixture-only invalid budget-unit string; that was corrected to the production UTF-16 unit without changing production behavior.

Coverage includes 21-pair capacity/review stability, duplicate notices, clear, New Chat, dispatcher marshalling, repeated text, exact one-use local context, cancellation of pending/late injected streams, frozen external options, target/chat/capability mismatch, exact reviewed Apply, cancellation at write, Undo, selected prior response, whole-pair reduction and complete destination budgets.

These tests do not instantiate WPF or validate UI layout, recipient checkbox events, real UIA focus/identity, real clipboard/field writes, original binary-asset transmission, any agent client, inference, network access, native file reads or installed settings. No source/model/network/native acceptance claim follows. Parent integration rerun is still required after copying this project and rebuilding the selected server.
