# Figma implementation reference

Source: [Buddy Design System + Prototypes](https://www.figma.com/design/JzvFxWvZqgP0d6fNYMgxHa/Buddy-Design-System---Prototypes).
Full connector extraction succeeded on 2026-09-30 after the connected account accepted its invitation. The adjacent text files are the tool's reference output; they are not executable application code.

| Surface | Node |
| --- | --- |
| Onboarding | 5:37 |
| Talk | 5:51 |
| Guide | 5:67 |
| Refine | 5:85 |
| Agent plan | 5:105 |
| Home | 5:127 |
| Foundations | 2:24 |

Original SVG files are bundled under `apps/windows/Buddy.Windows/Assets/Figma`. Their root sizes are preserved. `CompanionFace` loads the 56 px Foundations body variants with the 72 px listening halo; its compact variant uses the prototype's 40 px body. The remaining 8 px status dot, 28 px field badge, 52 px prototype halo and 148 × 64 px sample ring are retained for their respective surfaces. Live guidance geometry is dynamic evidence, not a static picture of a target.

Native adaptation uses Segoe UI Variable as specified by Foundations, a 240 px Home rail, 12 px cards, a 20 px compact chat border, native scrolling and 44 px interactive targets. Primary buttons retain the teal background and use dark text for readable contrast; the prototype's white text on teal does not meet the normal-text contrast target. System high contrast overrides semantic colors. The desktop companion uses the fully specified Foundations face size.

`dist/design-review` contains isolated WPF render fixtures when the Settings integration check runs. They contain no user conversations or screenshots. These fixtures verify initial layout and vector rendering; they do not establish completion of six-surface, screen-reader, or mixed-DPI acceptance.
