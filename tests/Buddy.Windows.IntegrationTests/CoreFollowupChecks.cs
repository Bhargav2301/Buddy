using Buddy.Windows;
using System.IO;
using System.Reflection;
using System.Windows;

// Root-only optional WPF fixture. No real shortcut registration, service,
// microphone, provider, installed-profile load or external application launch.
internal static class CoreFollowupChecks
{
    internal static int Run()
    {
        int checks = 0;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var baseline = new DesktopPreferences { NeuralSpeakerId = 85, NeuralPreset = "F3", VoiceEngine = "piper", RegionSelectionEnabled = true };
        var registered = new Dictionary<int, (uint Key, uint Mod)>();
        var removed = new List<int>(); bool fail = true;
        bool Register(int id, uint key, uint modifiers)
        {
            modifiers &= ~0x4000u;
            if (registered.ContainsKey(id) || registered.Values.Contains((key, modifiers))) return false;
            registered.Add(id, (key, modifiers)); return true;
        }
        void Unregister(int id) { if (!registered.Remove(id)) throw new Exception("Unowned fixture registration"); removed.Add(id); }
        var chat = new ShortcutRegistration((id, mod) => Register(id, 32, mod), Unregister);
        var voice = new ShortcutRegistration((id, mod) => Register(id, 32, mod), Unregister, 3, 7);
        var region = new ShortcutRegistration((id, mod) => Register(id, 82, mod), Unregister, 8, 9);
        voice.TrySet(ShortcutChoice.Find(baseline.VoiceShortcut)); region.TrySet(ShortcutChoice.FindRegion(baseline.RegionShortcut));
        MainWindow? home = null;
        void Check(bool ok, string label) { if (!ok) throw new Exception("FAIL: " + label); checks++; Console.WriteLine("PASS: " + label); }
        FieldInfo Field(string name) => typeof(MainWindow).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
        object? Call(string name, params object[] args) => typeof(MainWindow).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(home, args);
        try {
            home = new MainWindow(false, baseline, mergePreferences: next => fail ? throw new IOException("Owned save refusal") : next);
            Field("shortcut").SetValue(home, chat); Field("voiceShortcut").SetValue(home, voice); Field("regionShortcut").SetValue(home, region);
            try { Call("SavePreferences", baseline with { VoiceShortcut = "Alt + Shift + Space", RegionSelectionEnabled = false }); throw new Exception("Expected refusal"); }
            catch (TargetInvocationException ex) when (ex.InnerException is IOException) { }
            Check(chat.Active is null && chat.ActiveId == 0, "Actual Settings failure rolls back an initially unavailable chat binding");
            Check(voice.Active?.Label == baseline.VoiceShortcut && region.ActiveId == 8 && registered.Count == 2, "Actual Settings failure retains old voice and area leases");
            Check(!removed.Contains(3) && !removed.Contains(8), "Actual failed save never releases or reacquires the old active IDs");
            Check(((DesktopPreferences)Field("desktop").GetValue(home)!).NeuralSpeakerId == 85, "Failed Settings save preserves the selected speaker");
            // Disable area holds for successful Settings calls: this fixture
            // injects registrations but must never install a real input hook.
            var successful = baseline with { RegionSelectionEnabled = false };
            fail = false; Call("SavePreferences", successful);
            var swapped = successful with { Shortcut = baseline.VoiceShortcut, VoiceShortcut = baseline.Shortcut };
            Call("SavePreferences", swapped);
            Check(((ShortcutRegistration)Field("shortcut").GetValue(home)!).Active?.Label == swapped.Shortcut &&
                ((ShortcutRegistration)Field("voiceShortcut").GetValue(home)!).Active?.Label == swapped.VoiceShortcut,
                "Actual Settings commits an exact chat/voice swap using the correct channel objects");
            Field("voiceHeld").SetValue(home, true); Call("ConfigurePtt");
            Check(!(bool)Field("voiceHeld").GetValue(home)!, "Reconfiguring hold-to-talk invalidates a pending held-open continuation");
            Console.WriteLine($"ALL {checks} CORE FOLLOWUP OWNED WPF CHECKS PASSED; injected registrations only.");
            return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally {
            chat.Dispose(); voice.Dispose(); region.Dispose();
            if (home is not null) { Field("shuttingDown").SetValue(home, true); home.Close(); }
            app.Shutdown();
        }
    }
}
