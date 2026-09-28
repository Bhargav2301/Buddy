# Buddy — local PC edition

Windows cursor companion edition · Windows 10/11 x64 and Android 10 or newer

Get versioned downloads from [Buddy Releases](https://github.com/Bhargav2301/Buddy/releases). Use the Windows ZIP and Android APK from the same release. Each release includes a changelog, source commit, and SHA-256 checksums. Draft releases are visible to repository maintainers while device acceptance is pending.

Buddy is a native Windows application and a native Android companion. The AI runs on your Windows PC through Ollama. Android connects to the PC using an encrypted, certificate-pinned connection. No cloud AI API key is required.

This is a first installable alpha, not the full year-long platform in the Nexa specification. Read the feature coverage document for the exact implemented scope. Windows installation, microphone behavior, and phone permissions still need verification on your own devices.

## Cursor companion: start here

Use **`Buddy-Windows-v<version>.zip`** for the Windows application. Extract all files, quit any running Buddy instance from its tray menu, and run **Install-Buddy.cmd** from the extracted folder containing **Buddy.exe**. The source ZIP is for development. The Windows package contains **Cursor-Companion.txt** and **Build-Info.json**, which identifies its version and source commit.

Buddy now includes a small mint pointer with eyes that follows beside your Windows pointer. It is designed to let clicks pass through and leave your original pointer available. The companion runs while Buddy is running; this version does not add Windows login autostart.

| Action | Default control |
|---|---|
| Open a compact chat bar beside the pointer | **Ctrl + Space** |
| Open the separate voice overlay | **Ctrl + Shift + Space** |
| Send typed text / add a new line | **Enter** / **Shift + Enter** |
| Dismiss the bar and cancel listening, speech and its unfinished reply | **Esc** or **×** |
| Stop listening or an answer | **Stop**, or **Ctrl + Alt + Esc** |
| Open history, models, pairing and screen-context tools | **Home** in the bar, or double-click Buddy's tray icon |
| Change the main shortcut, choose chat/voice activation, hide the companion | **Home → Cursor & shortcuts** |

The voice shortcut opens a separate 360 px bubble and starts one utterance. Speak, then pause: Buddy turns the microphone off, sends the transcript to the PC's local AI, and reads sentences as they arrive if **Read voice answers aloud** is enabled. Press the voice shortcut again while listening to cancel; **Finish** submits recognized speech. Changing to another application stops the microphone. Enable **Hold to talk** in Cursor & shortcuts for a short tap to chat and a hold of at least 250 ms to talk; release sends. Windows may need longer to prepare the microphone on its first use. This is not a continuous or wake-word listener.

**Windows + Space normally switches keyboard layouts.** It appears in the shortcut picker as **if available**. If Windows rejects it, Buddy keeps the previous working shortcut. Ctrl + Space is the default, and Ctrl + Alt + Space or Alt + Shift + Space are alternatives. The dedicated voice shortcut is Ctrl + Shift + Space; if another application owns it, use Voice in the bar or tray menu.

When the configured AI model is ready, Buddy can start in companion mode with Home hidden. If Ollama/model setup is incomplete, Home stays open. Turn this behavior off in Cursor & shortcuts if you prefer Home at launch. The companion follows monitor work areas and is designed for different display scales; native monitor/DPI behavior still needs a Windows device check.

The compact bar streams real answers and saves completed turns to the selected conversation. Failed or stopped replies restore your draft. Typed chat captures context only when **Screen** is checked. Voice captures active-window accessibility text and, when safe and available, a redacted memory-only frame; turn this off with **Include active-window context when talking** in Cursor & shortcuts. Frames stay on this PC and never enter stored chat history or web queries. Microphone support depends on an installed Windows speech language and audio device.

## Guide, Agent and internet research (0.3.0 preview)

Read [Windows-Assistant-Preview.md](Windows-Assistant-Preview.md) for supported actions and acceptance limits. Open **Home → Assistant settings** to enable **Internet research** or **Agent mode**; both are off by default. Focus the app you want help with, then summon Buddy. Type a task and choose **Guide** for on-screen directions or **Agent** for a reviewable action plan. Agent changes require **Run this plan** and **Allow this step**. **Esc**, **Ctrl+Alt+Esc**, **Stop**, or mouse movement during execution stops further actions. Try **Home → Try pointing tutorial** first.

## 1. Install on Windows

1. Download the **`Buddy-Windows-v<version>.zip`** from the chosen release and choose **Extract All** into a new folder, such as `Downloads\Buddy-fixed`. Keep all extracted files together. The corrected download contains **Windows-Repair.txt**, **Run-Buddy.cmd**, and **Install-Buddy.cmd**.
2. Double-click **Install-Buddy.cmd** from that folder. Click OK after installation to open Buddy. This installs for your Windows account and creates Start menu and desktop shortcuts; it requires neither administrator rights nor a PowerShell script-policy change. A separate .NET installation is not required. Quit any running Buddy instance before installing an update. Existing conversations in `%LOCALAPPDATA%\Buddy` are retained.
3. To use Buddy without installing shortcuts, double-click **Run-Buddy.cmd** instead. **Buddy.exe** also runs directly; the CMD launcher additionally records .NET startup errors.
4. Open **PC setup & models** in Buddy.
5. Click **Install Ollama**, install it from the official page, and start it. If already installed, use **Start installed Ollama**.
6. Select **qwen3:4b-instruct-2507-q4_K_M**, then click **Download & use chat model**. Wait for **Model ready**. The initial model download is about 2.5 GB; allow several additional GB for the runtime and temporary files.
7. Click **Check connection**, then send a message. A real model response is the setup check.

Alternative setup: run **Setup-Local-AI.ps1** from PowerShell. It installs Ollama through Windows Package Manager when necessary and pulls the selected model. The default is `qwen3:4b-instruct-2507-q4_K_M`.

This build has no publisher certificate. Windows may show an unknown-publisher prompt. Verify the supplied SHA-256 checksum before running files. If your PC's policy blocks unsigned applications or scripts, use your normal administrator-managed installation process; do not disable device protection.

### Repairing the original Windows download

The original ZIP included the wrong `WindowsBase.dll`: a small .NET compatibility facade instead of the Windows Desktop implementation. That could prevent the window from opening with either of the original launch methods. **Windows repair 1** supplies the Desktop implementation and checks required WPF types before starting. Read-aloud now initializes only when requested, so a missing Windows voice cannot prevent the chat window from opening.

Use the updated ZIP and the steps above. Do not mix files from the old and new extracted folders. The native installer replaces the existing installation; you do not need to remove your saved data or reinstall Android.

If Buddy still does not open:

1. Run **Run-Buddy.cmd** in the new extracted folder. Keep any error window open and take a screenshot of its complete message.
2. Press **Win+R**, enter `%LOCALAPPDATA%\Buddy\Logs`, and press Enter.
3. Attach **startup.log** and **host-startup.log** when asking for help. If installation failed, attach **host-installer.log** too. Some files may be absent when Windows blocks execution before the program starts.
4. Include your Windows version and **System type** from **Settings → System → About**. This package targets Windows 10/11 x64. It is not a 32-bit or native ARM64 build.

The startup logs contain runtime information, installation paths and exception details; review them before sharing. The launcher does not record your chat messages. Windows execution and the installer still require confirmation on a Windows PC; packaging checks alone do not establish that every device can run the app.

### Choosing a model

| Model | Use |
|---|---|
| qwen3:4b-instruct-2507-q4_K_M | Starting choice for your PC; actual speed depends on hardware and free memory |
| qwen3:1.7b | Smaller, lighter alternative |
| qwen3:0.6b | Lowest resource option; weaker answers |
| qwen3:8b | Larger option when you have enough free RAM/VRAM |
| gemma3:4b | Optional image understanding model, downloaded separately |

Downloading requires internet. Once installed, text inference can run without internet. Voice depends on installed offline speech language packs. No model is bundled into the ZIP or APK.

## 2. Install on Android and pair

1. Download **`Buddy-Android-v<version>.apk`** to your phone and open it. This is a testing APK for sideloading, not a Google Play release.
2. If Android asks, allow installation from the particular app you used to open the APK. Device or organization policy may prohibit this.
3. Put the PC and phone on the same trusted Wi-Fi network. Keep the PC awake, with Buddy and Ollama running.
4. In Windows Buddy, click **Pair Android phone**.
5. If the PC shows multiple network addresses, choose the Wi-Fi/Ethernet address your phone can reach.
6. On Android, tap **Scan PC code**, scan the QR, check the PC address, then tap **Pair**.
7. You can also paste the full pairing link shown on the PC. There is no API key field.
8. Send a message from Android. Open **History** on either device to continue the same conversation.

Each pairing code expires after five minutes, works once, and closes after five incorrect attempts. Use **Refresh pairing code** on the PC when needed.

### If pairing times out

- Keep both devices on the same Wi-Fi. Guest Wi-Fi can isolate devices from each other.
- Check that the PC network is your trusted **Private** network.
- Allow **Buddy** in Windows Firewall on Private networks. The supplied **Enable-Phone-Access.ps1**, run from the installed app folder, creates a rule for Buddy only: TCP port 47831, Private networks, local subnet. Windows asks for administrator permission for this firewall change.
- If you move Buddy.exe to a different folder, update the firewall rule from the new folder.
- Turn off neither the firewall nor antivirus. Do not forward Buddy's port on your router.
- If the PC address changes, scan a new QR code. This version does not automatically discover a changed address.

For full revocation, open **Pair Android phone → Manage paired phones → Revoke access** on the PC. Android's **Forget this PC** removes the phone's local credentials; it does not revoke a copied token on the PC.

## 3. Everyday use

| Feature | Windows | Android |
|---|---|---|
| Typed chat | Enter a draft, Send or Ctrl+Enter | Enter a draft, tap Send |
| Hybrid voice | Choose Hybrid, Dictate, review, Send | Choose Hybrid, microphone, review, Send |
| Voice mode | Recognized speech sends automatically | Recognized speech sends automatically |
| Stop | Stop button or Ctrl+Alt+Esc | Stop button |
| Prompt refinement | Refine → review → Apply to draft | Refine → review → Apply to draft; Undo is available |
| Conversation history | Left sidebar | History tab |
| Explicit memory | What Buddy remembers | Library → Memory |
| Saved prompts | Saved prompts | Library → Prompts |
| Export | Export conversation → Markdown | Settings → Export current conversation → Android share sheet |

Windows: Ctrl+Space opens the compact chat bar by default. Use Home in the bar or the tray icon to open the full window. Closing Home leaves the companion and phone service running; **Quit Buddy** stops them. Ollama may continue running independently.

Android dictation requires Android 12+ with an available on-device recognizer. On phones without it, use typing or your existing keyboard's microphone. Buddy itself does not fall back to cloud speech. Windows uses an installed Windows speech recognizer; add a Windows speech language if none is present. Read-aloud uses installed device voices; long Android responses are limited to the first 3,900 characters for playback.

## 4. Screen context and images

### Windows

1. Focus the app you want help with, then press Ctrl+Space.
2. Choose **Read screen text**. Review and edit the text, then select **Use this context**.
3. Ask your question and send. The context is included only in that request.
4. For visual questions, first download the optional vision model in PC setup. Use **Capture window**, review the screenshot, then **Attach image**.

Screen text uses Windows UI Automation. Some canvas-based applications expose little or no text. Capture takes a visible rectangle of the previous window, so another window covering it may appear in the preview. Move obstructing windows first. Buddy's own window is excluded from capture where supported.

### Android

- Share selected text or an image to **Buddy** using Android's share sheet, or use the image button in chat.
- In Settings, choose Buddy as your default assistant if supported by your device. Summon it, then choose **Review screen text in Buddy**. Tap **Review** beside the attachment before sending.
- Secure windows and apps may supply no screen context; Buddy does not bypass those protections.

Password nodes and common sensitive text patterns are excluded. Sensitive app/title blocklists provide an additional check, but are not a guarantee of detecting all sensitive information. Review every capture. **Automatic image/OCR redaction is not implemented.** Remove sensitive content yourself or discard the image.

Images are held in memory for the request and are not saved in Buddy's conversation store. The model's written answer may describe an image, and that answer is saved in history. Buddy does not promise immediate zeroization of every managed-memory copy. Ollama is an independent application with its own local runtime behavior.

## 5. Optional Android entry points

- **Floating bubble:** Settings → Show floating Buddy bubble. Grant display-over-other-apps permission, return, and tap the button again. Drag to move; tap to open Buddy. Use Hide bubble or its notification to stop it.
- **Quick Settings:** Edit the Android Quick Settings tiles and add Buddy.
- **Buddy Keyboard:** Enable it in Android input-method settings. It is a basic in-house keyboard. Select text in an app, tap Refine, review the preview, then Apply. It does not press Send. Undo is available for 30 seconds if the replacement has not changed. The globe button opens your keyboard picker.

The keyboard intentionally disables refinement in password fields and recognized sensitive apps. It sends selected text only after an explicit Refine tap. It has no autocorrect, swipe typing, or multilingual key layouts yet; use your regular keyboard when you need those.

## 6. Data and limits

- PC data: `%LOCALAPPDATA%\Buddy`. Conversation data and the TLS certificate are encrypted; Windows DPAPI protects the data-protection key ring for your Windows account.
- Pairing secrets on Android use Android Keystore encryption and are excluded from app backup/device transfer.
- Conversation history lives on the PC. Android holds the open conversation in memory and refreshes while the app is visible. It is not an offline chat-history cache.
- Stop cancels an active response. Unfinished answers are not committed to history and the draft is restored. If a connection drops immediately after the PC finishes, check history before retrying to avoid a duplicate question.
- Maximum combined typed message plus screen text: 20,000 characters. Images: up to 2 MB after compression. Memory and saved prompts: up to 50 items each. The model receives a bounded portion of history and up to 5,000 characters of explicit memories.
- Only one inference runs at a time on the PC. Another device may have to wait.
- LAN sync uses a refresh approximately every six seconds while the app is visible. The specification's two-second sync target has not been met or benchmarked.

## 7. What this version does not do

No autonomous clicking, terminal execution, email sending, cloud connectors, wake word, realtime duplex voice, overlay arrows, routines, billing, browser extension, or independent on-phone LLM is included. It also does not run outside the local network by default. These are substantial later milestones in the supplied specification, not hidden working features.

See `Feature-Coverage.md` and `Validation.md` in the source package for implementation decisions and test evidence.
