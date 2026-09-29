# Buddy Windows MVP preview

This is a testing build from the feature branch, not completed version 0.5.0. Native acceptance remains open; see `MVP-Implementation-Status.md`. The last released ZIP is kept separately.

## Installation and rollback

Extract the entire preview ZIP into a new folder. Verify its SHA-256 file. Quit the running Buddy from the tray, then run `Install-Buddy.cmd` beside `Buddy.exe`. Installation validates the source and staged package before replacing application files. Your conversations, model choices, pairings and shortcuts are retained. Double-click Buddy to open Home even if it is already running; `Open-Buddy-Settings.cmd` opens Settings directly.

The .NET Windows runtime, OCR library/data and Figma assets are included. OCR also needs Microsoft's Visual C++ x64 Runtime. If the dependency probe reports it missing, run `Install-Prerequisites.cmd`, review Microsoft's installer, then retry. The helper downloads from [Microsoft's official x64 permalink](https://aka.ms/vc14/vc_redist.x64.exe), verifies the Microsoft signature, and does not silently accept terms. See [Microsoft's runtime requirements](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist).

To roll back application files, quit Buddy and run `Rollback-Buddy.cmd` from the extracted package. The prior binaries are retained under `%LOCALAPPDATA%\Programs\.Buddy-backup-*`. Rollback preserves current user data; it does not rewind stored data migrations. Clean-machine installation and a real 0.3.1 upgrade/rollback remain acceptance gates.

## Using the preview

- Home has Conversations, Walkthroughs, Memories, Prompts, Devices and Settings. Search, pin or archive conversations; resume saved walkthroughs.
- Settings has nine categories. AI contains local model setup and the intent-check embedding model. Appearance follows Windows or uses light/dark; reduced motion and high contrast are respected.
- The rounded companion has a menu for Talk, Voice, Guide, Agent, Refine, dictation, Home, Settings and Stop. Dock, follow or snooze it from that menu or the tray.
- Your existing main shortcut is retained. Ctrl+Shift+Space opens dedicated voice. Ctrl+Alt+R explicitly reads a supported AI-chat prompt for Refine. Ctrl+Alt+D explicitly captures a supported writable field and caret for dictation. Conflicting shortcuts can be accessed through the tray/menu.
- The Refine badge checks eligibility without reading draft values. Automatic rewrite suggestions remain off. Quick, Guided and Council use local specialist passes. A rewrite that fails fact/intent checks leaves the exact original in place.
- Dictation requires verifiable UIA text and selection information. Empty or inaccessible caret geometry may be unsupported; use dictation in Buddy's own draft in that case. Review field dictation before Insert. Buddy never submits the host form. Changed fields/selections refuse insertion; Undo is guarded for 30 seconds.
- Internet and Agent default off. Enable them in their Settings categories. Sources are clickable, and conversation badges show when screen context or research was used. Raw screen context and screenshots are not stored in conversation history.
- Agent keeps its plan visible and asks initially, then again for consequential/uncertain changes. The red banner appears only during execution. Stop, Esc and pointer movement prevent further dispatch. An accessibility action already handed to another app cannot be retracted; inspect that app if a provider times out.
- Vision controls need exact unique OCR text, independent local boundary evidence and local vision corroboration. Flat controls, unlabeled icons, ambiguous labels and unsupported visual roles abstain. Vision targets are directions only; execution uses Windows accessibility controls.
- Screen & Privacy includes metadata history and **Delete all local Buddy data**. Deletion requires typing DELETE, waits for Buddy to exit, and removes its local profile. Shared Ollama models and app binaries remain.

## Native acceptance

The automated native fixture has been unable to obtain foreground focus in this environment. It does not count skipped capture/actions as passes. From a development checkout, run the following and click **Run native checks** in the disposable fixture window:

```powershell
dotnet run --project tests/Buddy.Windows.IntegrationTests -c Release -- --interactive-fixture
```

The fixture only changes its own test controls. Real microphone, host-field dictation/Refine, screen-reader, mixed-DPI, seven application workflows, 150-case grounding, physical-input stop timing, and clean/upgrade installation still need validation. DaVinci Resolve and FL Studio require runnable installations and disposable sample projects.
