# Calculator restoration repair, October 6

The user confirmed that Calculator had opened but was minimized during the failed hotfix57 run. The source now handles that state within the existing single owned dispatch. This report supersedes the earlier investigation's unanswered foreground question; the original failure receipts remain unchanged.

## Resulting behavior

Calculator uses the Windows application activation manager once, with the already verified fixed Calculator AppUserModelId and no arguments. Its returned process ID is pinned with a query-only process handle, creation time, exact main executable and full package identity. There is no second launch or fallback route. Other app launches retain their existing route.

Bounded metadata discovery accepts exactly one top-level window for that process. A framed Calculator requires the fixed trusted System32 ApplicationFrameHost and exactly one direct CoreWindow with the returned process identity. Hidden siblings still count as ambiguity. The root must be visible to Windows, uncloaked, on the permitted desktop and non-elevated. A hidden/cloaked child is identity evidence only while its exact root is minimized; this grants no completed foreground receipt or later input authority.

After fresh target validation, Buddy restores a minimized root at most once with synchronous `ShowWindow(SW_RESTORE)`. Its return value is not treated as success. Buddy observes the same window again and makes at most one ordinary `SetForegroundWindow` request if needed. An already visible foreground Calculator needs neither effect. Windows denial ends the attempt; there is no input injection, thread attachment, topmost trick, permission change or retry. The existing strict foreground/package/frame postcondition must still pass and must equal the pinned activation target.

The original operation cancellation/deadline remains in force. Agent/privacy, original source identity, input timestamp, default desktop, elevation, process and window identity are rechecked at boundaries. Input changes latch refusal. Pending native work retains the operation gate and process/host leases until it settles; cancellation prevents later effects but cannot undo an effect already entered. No deadline or input baseline is refreshed after launch.

Full sample revalidation repeats the direct-child inventory and root visibility/cloak/topology/minimized state after child reads. Independent review caught and closed a draft race at this boundary. Fixed activation failures receive bounded, exact-whitelisted report codes; private exception text remains generic.

The API contract is documented by Microsoft: [application activation and returned process ID](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-iapplicationactivationmanager-activateapplication), [ShowWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-showwindow), and [ordinary foreground restrictions](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow). COM identifiers and the method signature were checked against Microsoft's [Windows SDK header](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/ShObjIdl_core.h).

## Validation

- AppBinding: 432 mock/pure checks, including new launch, existing minimized, already foreground, denial, interruption, blocked-operation settlement, immutable targets and within-sample root/sibling changes.
- ComputerUse: 132 mocked/owned-file checks, including one dispatch, cancellation, retained ownership and exact-message privacy.
- RoutineLaunchReport: 82 pure checks, including the new bounded activation outcomes.
- UserFailureBoundary: 56 mocked service/control cases, zero failures; native fallback is a throwing stub.
- These four projects and the Windows Release application compiled with zero warnings/errors. Test executables used the existing runtime through `--roll-forward Major`; no runtime installation occurred.
- Independent source review found no remaining blocker after the sample race fix. These are source, injected and owned-file checks, not native Calculator acceptance.

Changed production files: `CalculatorActivationNative.cs`, `CalculatorTargetBinding.cs`, `CalculatorWindowActivation.cs`, `RoutineAppOpen.cs`, `InstalledAppResolver.cs`, `BoundedComputerUse.cs`, and `RoutineLaunchReport.cs`. Test project links, new lifecycle/binding fixtures, reporting/controller fixtures and a native-refusal boundary stub were updated. Source is isolated in `Buddy-hotfix58`, based on `f8c56b90f6d80479717d0988182ba37492d998f1`; it is uncommitted and version remains 0.4.7. Private evidence is under `validation/hotfix58/activation-*`.

## Remaining native acceptance

No live activation, foreground observation, screenshot, audio, installation or publication ran in this batch. Installed 0.4.5, sealed hotfix57 packages, the used launcher and previous recovery/evidence remain preserved. The last complete installed verification is the earlier investigation's 4,416-file match, not a new live measurement in this batch. The new compiled DLL is a build artifact, not a sealed installer or runnable acceptance package.

A fresh coordinated native window and new one-use attempt are required after packaging the exact reviewed source. Do not reset or reuse the consumed hotfix57 attempt or its deadline. Required evidence:

1. New Calculator launch: OS activation PID, exact window/process/package binding, visible foreground success and one dispatch.
2. Existing minimized Calculator: OS-returned instance identified, same root restored once, no replacement target, strict foreground/frame success. Do not close or minimize unrelated windows to manufacture this state without the user's coordinated action.
3. Already visible Calculator: no unnecessary restore; zero or one normal foreground request as appropriate, no duplicate launch.
4. Foreground refusal or target/input/desktop change: truthful failure, no retry or workaround, no subsequent effect. Injected denial is covered; do not claim a real Windows denial was exercised unless observed under the ordinary rules.
5. Stop/deadline during pending discovery/restoration: no later focus request, all work settled before reuse, and unchanged final verification requirements.
6. Recheck Stop/reopen and the installation acceptance gates with the exact final package before any separately coordinated replacement. Retain a fresh recoverable baseline and current preferences.

Installation remains authorized only after relevant acceptance and coordination; this batch deliberately stops at verified source/builds. Publication remains independently blocked through the previously denied route, and no push, merge or release was attempted.
