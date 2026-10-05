# Buddy0.4.7 framed Calculator hotfix

This focused candidate follows local-only revision `cebb4650c12de0376dd6cb0bceeee26c32c6a41d`. It retains comparison refinement and bounded task-history reporting from0.4.6. Installed0.4.5 and the sealed0.4.6 candidate remain preserved. Unfinished followup55 work is excluded.

## Observed failure

One approved0.4.6 Calculator request dispatched exactly once. The production verifier did not confirm its foreground window. Read-only metadata showed a Windows ApplicationFrameHost root containing the newly opened CalculatorApp CoreWindow. The top-level process did not match Calculator, so the existing guard correctly refused success. No retry or installation occurred.

## Resulting behavior

The repair binds a single visible direct Calculator child to the same stable Windows frame, validates the fixed signed Windows host separately from the exact signed Calculator package/main process, and rechecks both identities and ownership before accepting the postcondition. An arbitrary descendant, same-name process, unrelated frame, stale handle or replaced package cannot establish success.

The result preserves the real child window/process identity and separately identifies the frame. A reviewed framed launch ends its current action batch before any further capture or action. Exact open-only requests can complete; broader tasks retain a review-needed result with remaining work unperformed. General input/perception support for framed applications is outside this focused repair.

## Validation and publication status

All59 final aggregate commands passed with unchanged build inputs, including243 app-binding checks and36 completion/lifecycle checks. Windows publication and package checks passed. Independent review found no remaining scoped blocker after repairs to UI-thread settlement and full window revalidation.

The exact combined0.4.7 desktop DLL verified the existing Calculator frame, direct child, catalog trust and signed package, then freshly validated the same binding again in877ms. No launch, focus change, field write or profile write occurred. The exact final service DLL also accepted the reported comparison prompt using the existing local models in20.1seconds, similarity0.9005715024022094, with zero field writes or saved conversations.

These read-only and fixture results do not establish a new actual-launch run or installed acceptance. Pure fixtures, read-only existing-window validation and foreground activation are separate evidence. The installed update is authorized after the relevant acceptance gates, but foreground work requires a newly coordinated window. Physical microphone and headphone checks are not part of this fix.

Publication of the prior local0.4.6 commit was rejected twice by automatic approval review; no GitHub update occurred. This repair creates a different source identity, which must be reported separately and must not be published under approval naming only the older commit. No merge or release is requested.
