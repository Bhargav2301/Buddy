using Buddy.Windows;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

// Exercises the app's real tray handlers and native message hook in this process.
// It does not click Explorer's tray, inject keyboard input, capture a user app,
// submit conversation text or start microphone capture.
internal static class TrayRoutingChecks
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    private static T Read<T>(object instance, string name) => (T)instance.GetType().GetField(name, Private)!.GetValue(instance)!;
    private static void Write(object instance, string name, object value) => instance.GetType().GetField(name, Private)!.SetValue(instance, value);
    private static object? Call(object instance, string name, params object[] values) => instance.GetType().GetMethod(name, Private)!.Invoke(instance, values);
    internal static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var home = new MainWindow(startService: false);
        Write(home, "desktop", new DesktopPreferences { OnboardingCompleted = true, ShowCompanion = false, ShowFieldBadge = false, StartInCompanionMode = false, CaptureOnVoice = false, AllowWebResearch = false, AgentEnabled = false, HeadphonesOnly = true, HoldToTalk = false });
        int checks = 0, exit = 1;
        void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); checks++; Console.WriteLine("PASS: " + label); }
        home.Loaded += async (_, _) => {
            try {
                await (Task)Call(home, "Start")!;
                var tray = Read<System.Windows.Forms.NotifyIcon>(home, "tray");
                var quick = Read<QuickChatWindow>(home, "quick");
                var voice = Read<VoiceOverlayWindow>(home, "voiceOverlay");
                Check(tray.Visible && tray.ContextMenuStrip is not null && quick is not null && voice is not null, "Production startup creates the tray and both interaction surfaces");
                void Menu(string text) => tray.ContextMenuStrip!.Items.Cast<System.Windows.Forms.ToolStripItem>().Single(i => i.Text == text).PerformClick();
                home.Close();
                Check(!home.IsVisible && tray.Visible, "Closing Home hides the window and leaves its tray available");
                Menu("Open Buddy Home");
                Check(home.IsVisible && home.WindowState == WindowState.Normal, "Production tray Home command reopens hidden Home");
                home.Hide();
                typeof(System.Windows.Forms.NotifyIcon).GetMethod("OnDoubleClick", Private)!.Invoke(tray, [EventArgs.Empty]);
                Check(home.IsVisible, "Production tray double-click handler restores Home");
                home.Hide(); Menu("Settings");
                Check(home.IsVisible && home.VisibleHomeSection == "Settings", "Production tray Settings command opens embedded settings");
                Menu("Quick chat");
                var draft = Read<TextBox>(quick!, "draft");
                Check(quick!.IsVisible && !voice!.IsListening && !draft.IsReadOnly && draft.IsKeyboardFocusWithin, "Tray Quick chat opens a focused editable draft with microphone off");
                quick.Dismiss(); Menu("Voice");
                Check(voice!.IsVisible && voice.IsListening && !quick.IsVisible, "Tray Voice routes to the separate voice surface");
                Call(home, "Cancel"); // Before the dispatcher can run deferred microphone initialization.
                Check(!voice.IsListening && !voice.IsBusy && !quick.IsBusy, "Home Stop cancels pending voice startup and both interaction surfaces");
                voice.Dismiss();
                var hwnd = new WindowInteropHelper(home).Handle;
                Read<ShortcutRegistration>(home, "shortcut")?.Dispose(); Read<ShortcutRegistration>(home, "voiceShortcut")?.Dispose();
                // Real registrations use uncommon F23/F24 fixture keys so the running installed app
                // and other applications keep their actual Space shortcuts. No key is injected.
                var chatChord = new ShortcutRegistration((id, modifiers) => Native.RegisterHotKey(hwnd, id, modifiers, 0x86), id => Native.UnregisterHotKey(hwnd, id), 101, 102);
                var voiceChord = new ShortcutRegistration((id, modifiers) => Native.RegisterHotKey(hwnd, id, modifiers, 0x87), id => Native.UnregisterHotKey(hwnd, id), 103, 104);
                Write(home, "shortcut", chatChord); Write(home, "voiceShortcut", voiceChord);
                Check(chatChord.TrySet(ShortcutChoice.Find("Ctrl + Alt + Shift + Space")) && voiceChord.TrySet(ShortcutChoice.Find("Alt + Shift + Space")), "Windows independently registers two fixture chords without taking installed Space shortcuts");
                SendMessage(hwnd, 0x0312, (IntPtr)chatChord.ActiveId, IntPtr.Zero);
                Check(quick.IsVisible && !voice.IsListening && draft.IsKeyboardFocusWithin, "Native WM_HOTKEY chat message routes to typed chat");
                SendMessage(hwnd, 0x0312, (IntPtr)voiceChord.ActiveId, IntPtr.Zero);
                Check(voice.IsVisible && voice.IsListening && !quick.IsVisible, "Native WM_HOTKEY voice message routes independently");
                SendMessage(hwnd, 0x0312, (IntPtr)2, IntPtr.Zero);
                Check(!voice.IsListening && !voice.IsBusy, "Native Stop message cancels queued voice startup synchronously");
                voice.Dismiss(); Menu("Open Buddy Home");
                Check(home.IsVisible && !voice.IsListening, "Tray reopens Home after Stop without restarting voice");
                await Task.Delay(200);
                Check(!voice.IsListening && Read<object?>(voice, "recognizer") is null, "Cancelled deferred microphone callbacks remain inactive");
                Console.WriteLine($"ALL {checks} TRAY/ROUTING CHECKS PASSED; Explorer tray clicks, physical key presses and third-party hooks remain manual"); exit = 0;
            } catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { await home.Quit(); }
        };
        app.Run(home); return exit;
    }
}
