# Buddy 0.4.6 focused hotfix56

This candidate is based on published revision `63ca5e9b6b8d2785b6941ce4ad216b627b960d8c`. It repairs reported refinement and Calculator failures. Installed Buddy 0.4.5 remains preserved until a separately coordinated update. Unfinished followup55 changes are excluded.

## Resulting behavior

An explicit comparison previously took the conservative wording path. The configured local model added punctuation, then repeated the original. The request completed normally, but its generic message obscured this cause. A finite source grammar now separates both supplied comparison sides while preserving every word, quantifier and relation in order. Ambiguous scope remains unsupported. No product-specific requirements or features are invented. Source coverage, local preservation assessment, literals, the 0.80 similarity gate, destination budgets, cancellation and field review remain unchanged.

No-change responses distinguish cosmetic output from an assessment that found no useful wording improvement. Rejected inline results retain warnings and budget details.

The installed Calculator resolver reproduced a publisher-verification failure before launch. Windows reports its System32 launcher as validly catalog-signed; Buddy's file-only check fails before reading an embedded certificate. Calculator now uses the existing fixed signed-package resolver: `Microsoft.WindowsCalculator_8wekyb3d8bbwe!App`, publisher `8wekyb3d8bbwe`, main executable `CalculatorApp.exe`. Development, unhealthy, ambiguous, substituted and partial registrations remain refused. The exact package version and main process are revalidated before and after one launch. No general signature fallback, shell, arbitrary executable or weaker permission check is added.

Desktop task history now names the requested supported app and retains an allowlisted failure reason or numeric error code. Its source link opens the selected historical result without replaying an operation. Prompts, arbitrary exception text, paths and stacks are excluded from journal and diagnostic output. The journal remains memory-only.

## Validation

The initial refinement-only preview passed the exact reported prompt using actual local Gemma and all-minilm, with similarity 0.9005715024022094. Production source-field options were bound once; no field writes or saved conversations occurred. The Calculator candidate separately resolved its healthy signed installed package without launching it. Independent source review found no blocking trust or no-replay issue.

The final combined source passed all 58 allowlisted aggregate commands, including launcher negative cases, refinement lifecycle/meaning/options and task-history fixtures. Source/build inputs stayed unchanged throughout. Windows publication and package/runtime checks passed. The exact final 0.4.6 service DLL accepted the reported prompt in 16.8 seconds, with similarity 0.9005715024022094 and zero field writes or saved conversations. The exact final desktop DLL resolved the healthy signed Calculator package without activation. A fresh recoverable installed-app/profile/shortcut snapshot was verified; it will be rechecked before any update. Read-only and synthetic checks do not establish actual Calculator activation, inline Apply/Undo or installed-update acceptance. Foreground work and replacement remain pending coordination. No merge or release is part of this hotfix.

## Evidence boundaries

Private evidence remains outside source under `validation/hotfix56`: model probes, handoffs and intake receipts, source/build snapshots, regression logs, package manifests and recovery/acceptance records. Private prompts, profiles, logs and backups are not committed. The older unsupported server jobs are unrelated to the reported desktop task card; the historical card did not retain its original exception.
