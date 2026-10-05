# Night Mint native integration

The user selected Buddy Night Mint 1.1 and its brighter text revision. The design handoff, token JSON and CSS were read from the separate design workspace; that workspace was not edited by engineering. The browser design report records 652 contrast measurements and 26 interaction/pixel checks, but those are not native Buddy results.

The native implementation maps the complete role palette into shared WPF brushes and control templates. It keeps primary, secondary, muted, placeholder, disabled and semantic text separate from action fills. Content panels and popup surfaces are opaque; the existing transparent mascot silhouette and desktop guidance/region overlays retain their purposes. Original Buddy artwork is preserved.

New profiles default to Night Mint. Explicit saved Light, Dark or System preferences remain supported; choosing Night Mint in General changes appearance without resetting other preferences. The preview uses its own profile and does not migrate the installed user's data.

Native acceptance includes real WPF control templates, popups, keyboard focus, disabled states, selection, refinement diff decorations and owned-window renders. These renders do not establish wallpaper composition, physical display scaling or assistive-technology acceptance. Final results are recorded in Windows-0.4.4.md and the private preview handoff after execution.

Design input SHA256:

- `handoff.md`: `f328bc5e6ddfb9ed26b2abbf8cd0a04c2cf89ad8dd03ddf034e3fdf03e5266e5`
- `tokens.json`: `207294f66e184a507b7172119af3f14dab17ee79817a0ff992c1b9b54d03665d`
- `night-mint.css`: `d42b886685963f9158e0f81f86714e19c06b9b3ccb7edf8d1a93224d83563d08`
