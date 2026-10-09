using Buddy.Windows;
using System.Text.Json;

int checks = 0;
void Check(bool value, string label) { if (!value) throw new Exception("FAIL: " + label); checks++; Console.WriteLine("PASS: " + label); }
void Reject(Action action, string label) { try { action(); } catch (InvalidOperationException) { Check(true, label); return; } throw new Exception("FAIL: " + label); }
DesktopPreferences Desired(uint chat = 3, uint voice = 6, bool region = true) => new() {
    Shortcut = ShortcutChoice.Choices.Single(c => c.Modifiers == chat).Label,
    VoiceShortcut = ShortcutChoice.Choices.Single(c => c.Modifiers == voice).Label,
    RegionSelectionEnabled = region
};
ShortcutChoice Choice(uint modifiers) => ShortcutChoice.Choices.Single(c => c.Modifiers == modifiers);

using (var f = new Registry()) {
    f.Chat.TrySet(Choice(3)); f.Voice.TrySet(Choice(6)); f.Region.TrySet(ShortcutChoice.RegionChoices[0]);
    var original = f.Snapshot();
    using (var staged = ShortcutBindingUpdate.Prepare(f.Chat, f.Voice, f.Region, Desired(7, 5))) {
        Check(f.Count == 5, "Both replacements are reserved while all three original leases remain registered");
        Check(f.Chat.Active == Choice(3) && f.Voice.Active == Choice(6), "Preparation does not advertise provisional bindings");
    }
    Check(f.Snapshot() == original, "Failed persistence disposal preserves exact original IDs and values");
    Check(f.Removed.All(id => id is 4 or 7), "Rollback unregisters only the two provisional IDs");
    Check(!f.Removed.Contains(1) && !f.Removed.Contains(3) && !f.Removed.Contains(8), "Rollback never reacquires an old binding");
}
using (var f = new Registry()) {
    f.Voice.TrySet(Choice(6)); var original = f.Snapshot();
    using (ShortcutBindingUpdate.Prepare(f.Chat, f.Voice, f.Region, Desired(3, 5))) {
        Check(f.Chat.Active is null, "Initially unavailable chat remains unavailable until commit");
    }
    Check(f.Snapshot() == original && f.Chat.Active is null && f.Chat.ActiveId == 0, "Failed save removes new registrations when the old channel was unavailable");
}
using (var f = new Registry()) {
    f.Chat.TrySet(Choice(3)); f.Voice.TrySet(Choice(6)); var original = f.Snapshot(); f.Blocked.Add((32, 5));
    Reject(() => ShortcutBindingUpdate.Prepare(f.Chat, f.Voice, f.Region, Desired(7, 5)), "Second channel conflict refuses the entire preparation");
    Check(f.Snapshot() == original && f.Chat.Active == Choice(3) && f.Voice.Active == Choice(6), "Partial conflict releases staging and preserves both originals");
}
using (var f = new Registry()) {
    f.Chat.TrySet(Choice(3)); f.Voice.TrySet(Choice(6)); f.Region.TrySet(ShortcutChoice.RegionChoices[0]); var original = f.Snapshot();
    using (ShortcutBindingUpdate.Prepare(f.Chat, f.Voice, f.Region, Desired(region: false)))
        Check(f.Region.ActiveId == 8 && f.Snapshot() == original, "Disabling area selection retains its lease while disk save is pending");
    Check(f.Region.ActiveId == 8 && f.Snapshot() == original, "Failed area-disable save retains the exact working region binding");
    using (var staged = ShortcutBindingUpdate.Prepare(f.Chat, f.Voice, f.Region, Desired(region: false))) staged.Commit();
    Check(f.Region.Active is null && f.Region.ActiveId == 0 && f.Count == 2, "Successful disable releases only the area binding after commit");
    Check(f.Region.TrySet(ShortcutChoice.RegionChoices[1]), "A disabled area registration remains reusable");
}
using (var f = new Registry()) {
    f.Chat.TrySet(Choice(3)); f.Voice.TrySet(Choice(6)); var original = f.Snapshot(); int calls = f.RegisterCalls;
    ShortcutRegistration? chat, voice;
    using (var swap = ShortcutBindingUpdate.Prepare(f.Chat, f.Voice, null, Desired(6, 3, false))) {
        Check(ReferenceEquals(swap.Chat, f.Voice) && ReferenceEquals(swap.Voice, f.Chat), "Exact swap reassigns channel ownership of existing registrations");
        swap.Commit(); chat = swap.Chat; voice = swap.Voice;
    }
    Check(chat!.Active == Choice(6) && voice!.Active == Choice(3), "Both channels advertise their correct swapped chord");
    Check(f.RegisterCalls == calls && f.Removed.Count == 0 && f.Snapshot() == original, "Exact swap never unregisters or reregisters either chord");
    using (var next = ShortcutBindingUpdate.Prepare(chat, voice, null, Desired(7, 5, false))) { next.Commit(); chat = next.Chat; voice = next.Voice; }
    Check(chat!.ActiveId == 7 && voice!.ActiveId == 4 && f.Count == 2, "Later edits retain each transferred object's disjoint ID pair");
}
foreach (bool chatTakesVoice in new[] { true, false }) using (var f = new Registry()) {
    f.Chat.TrySet(Choice(3)); f.Voice.TrySet(Choice(6)); var original = f.Snapshot();
    var next = chatTakesVoice ? Desired(6, 5, false) : Desired(5, 3, false);
    using (ShortcutBindingUpdate.Prepare(f.Chat, f.Voice, null, next)) { }
    Check(f.Snapshot() == original, "Transferred chord rollback leaves channel owners unchanged: " + chatTakesVoice);
    using var update = ShortcutBindingUpdate.Prepare(f.Chat, f.Voice, null, next); update.Commit();
    Check(update.Chat!.Active!.Label == next.Shortcut && update.Voice!.Active!.Label == next.VoiceShortcut && f.Count == 2, "One channel may take the other's old chord while the other moves: " + chatTakesVoice);
}
using (var f = new Registry()) {
    f.Chat.TrySet(Choice(3)); f.Voice.TrySet(Choice(6)); var original = f.Snapshot(); f.Blocked.Add((82, 7));
    Reject(() => ShortcutBindingUpdate.Prepare(f.Chat, f.Voice, f.Region, Desired(6, 3)), "Area conflict refuses a staged conversation swap");
    Check(f.Snapshot() == original && f.Chat.Active == Choice(3) && f.Voice.Active == Choice(6), "Failed swap never changes the live channel references");
}
using (var f = new Registry()) {
    Reject(() => ShortcutBindingUpdate.Prepare(f.Chat, f.Voice, f.Region, Desired(3, 3)), "Equal chat and voice chords are refused before any registration");
    Reject(() => ShortcutBindingUpdate.Prepare(f.Chat, f.Voice, f.Region, Desired() with { Shortcut = "Win + Space" }), "Unsupported chat chord cannot silently become a different shortcut");
    Reject(() => ShortcutBindingUpdate.Prepare(f.Chat, f.Voice, f.Region, Desired() with { VoiceShortcut = "missing" }), "Unsupported voice chord is refused");
    Reject(() => ShortcutBindingUpdate.Prepare(f.Chat, f.Voice, f.Region, Desired() with { RegionShortcut = "missing" }), "Unsupported enabled region chord is refused");
    Check(f.RegisterCalls == 0 && f.Count == 0, "Invalid choices have no native delegate effects");
    using var staged = ShortcutBindingUpdate.Prepare(f.Chat, f.Voice, f.Region, Desired(region: false));
    Reject(() => f.Chat.TrySet(Choice(5)), "An overlapping direct edit cannot corrupt staged ownership");
    Reject(() => f.Chat.Dispose(), "A pending registration cannot be disposed behind its transaction");
    staged.Dispose(); staged.Dispose();
    Reject(staged.Commit, "A rolled-back transaction cannot later commit");
    Check(f.Count == 0, "Rollback is idempotent and leaves no provisional registration");
}
using (var f = new Registry()) {
    using var staged = ShortcutBindingUpdate.Prepare(f.Chat, f.Voice, null, Desired(region: false)); staged.Commit();
    Reject(staged.Commit, "A committed transaction is single use"); staged.Dispose();
    Check(f.Count == 2, "Disposal after commit retains both active registrations");
    f.Blocked.Add((32, 5)); var original = f.Snapshot();
    Check(!f.Chat.TrySet(Choice(5)) && f.Snapshot() == original, "Legacy single-channel conflict behavior remains intact");
}
Check(ShortcutChoice.Warnings(Choice(3).Label, Choice(2).Label).Contains("ChatGPT"), "Voice Ctrl+Space selection warns about the competing ChatGPT chord");
Check(ShortcutChoice.Warnings(Choice(2).Label, Choice(3).Label).Contains("ChatGPT"), "Chat Ctrl+Space selection retains the conflict warning");
Check(ShortcutChoice.Warnings(Choice(3).Label, Choice(6).Label) == ShortcutChoice.Warning(Choice(3).Label), "Shared general chord warning is not duplicated");

string folder = Path.Combine(Path.GetTempPath(), "Buddy-core53-owned-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
try {
    string path = Path.Combine(folder, "desktop.json");
    var baseline = Desired() with { NeuralSpeakerId = 85, NeuralPreset = "F3", VoiceEngine = "piper", HeadphonesOnly = true, ProtectScreenshots = true,
        AdditionalSettings = new() { ["futureOwnedField"] = JsonSerializer.SerializeToElement(new { value = 42 }) } };
    baseline.Save(path);
    using var f = new Registry(); f.Chat.TrySet(Choice(3)); f.Voice.TrySet(Choice(6)); f.Region.TrySet(ShortcutChoice.RegionChoices[0]);
    var requested = baseline with { CompanionName = "Owned changed name" };
    // Simulate another writer changing only the voice chord before our disk commit.
    (baseline with { VoiceShortcut = Choice(5).Label }).Save(path);
    DesktopPreferences effective;
    using (var staged = ShortcutBindingUpdate.Prepare(f.Chat, f.Voice, f.Region, requested)) { effective = requested.Save(path, baseline); staged.Commit(); }
    Check(effective.CompanionName == requested.CompanionName && effective.VoiceShortcut == Choice(5).Label, "Disk merge keeps both the edited name and newer independent voice choice");
    using (var merged = ShortcutBindingUpdate.Prepare(f.Chat, f.Voice, f.Region, effective)) merged.Commit();
    Check(f.Voice.Active == Choice(5), "Effective merged voice chord, not the stale proposal, becomes active");
    var saved = DesktopPreferences.Load(path);
    Check(saved.NeuralSpeakerId == 85 && saved.NeuralPreset == "F3" && saved.VoiceEngine == "piper", "Transaction preserves the selected local speaker 85/F3");
    Check(saved.HeadphonesOnly && saved.ProtectScreenshots && saved.AdditionalSettings!["futureOwnedField"].GetProperty("value").GetInt32() == 42, "Output/capture policies and unknown settings survive merged save");
    f.Blocked.Add((32, 7)); var original = f.Snapshot();
    var occupied = saved with { Shortcut = Choice(7).Label }; occupied.Save(path);
    Reject(() => ShortcutBindingUpdate.Prepare(f.Chat, f.Voice, f.Region, occupied), "Postcommit merged runtime conflict remains a recoverable warning condition");
    Check(DesktopPreferences.Load(path).Shortcut == Choice(7).Label && f.Snapshot() == original, "Saved preference is retained separately from prior working runtime bindings");
    string before = File.ReadAllText(path);
    try { (occupied with { VoiceShortcut = occupied.Shortcut }).Save(path); throw new Exception("Expected disk rejection"); } catch (InvalidDataException) { Check(File.ReadAllText(path) == before, "Invalid merged equal chords leave the saved file unchanged"); }
} finally {
    string safe = Path.GetFullPath(folder), allowed = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
    if (!safe.StartsWith(allowed, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(safe).StartsWith("Buddy-core53-owned-", StringComparison.Ordinal)) throw new Exception("Unexpected cleanup path");
    Directory.Delete(safe, true);
}
Console.WriteLine($"ALL {checks} CORE FOLLOWUP HEADLESS CHECKS PASSED; injected registrations and owned temporary files only.");

sealed class Registry : IDisposable
{
    private readonly Dictionary<int, (uint Key, uint Mod)> registrations = [];
    internal readonly HashSet<(uint Key, uint Mod)> Blocked = [];
    internal readonly List<int> Removed = [];
    internal readonly ShortcutRegistration Chat, Voice, Region;
    internal int RegisterCalls { get; private set; }
    internal int Count => registrations.Count;
    internal Registry()
    {
        Chat = new((id, mod) => Register(id, 32, mod), Unregister);
        Voice = new((id, mod) => Register(id, 32, mod), Unregister, 3, 7);
        Region = new((id, mod) => Register(id, 82, mod), Unregister, 8, 9);
    }
    private bool Register(int id, uint key, uint mod)
    {
        RegisterCalls++; mod &= ~0x4000u;
        if (Blocked.Contains((key, mod)) || registrations.ContainsKey(id) || registrations.Values.Contains((key, mod))) return false;
        registrations.Add(id, (key, mod)); return true;
    }
    private void Unregister(int id) { if (!registrations.Remove(id)) throw new Exception("Unregistering a lease that is not owned"); Removed.Add(id); }
    internal string Snapshot() => string.Join("|", registrations.OrderBy(x => x.Key).Select(x => $"{x.Key}:{x.Value.Key}:{x.Value.Mod}"));
    public void Dispose() { Chat.Dispose(); Voice.Dispose(); Region.Dispose(); }
}
