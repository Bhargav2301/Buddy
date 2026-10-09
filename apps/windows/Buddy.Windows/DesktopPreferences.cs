using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text;

namespace Buddy.Windows;

internal sealed record DesktopPreferences
{
    internal const int DefaultNeuralSpeakerId = 60; // Owner-selected Voice C, VCTK p232.
    public int SchemaVersion { get; init; } = 2;
    public string Appearance { get; init; } = "Black";
    public bool ReduceMotion { get; init; }
    public string CompanionName { get; init; } = "Buddy";
    public bool OnboardingCompleted { get; init; }
    public bool ShowFieldBadge { get; init; }
    public bool LocalPromptSuggestions { get; init; }
    public bool RegionSelectionEnabled { get; init; }
    public bool RegionVoiceAfterSelection { get; init; }
    public string RegionShortcut { get; init; } = "Ctrl + Alt + Shift + R";
    public bool TrianglePointerEnabled { get; init; }
    public bool CompactPointerMode { get; init; }
    private string islandMode = "Compact";
    public string IslandMode { get => islandMode; init => islandMode = value is "Compact" or "Expanded" ? value : "Hidden"; }
    public bool IslandHideInFullscreen { get; init; }
    public string IslandPlacementMode { get; init; } = "Top";
    public string? IslandPlacementMonitor { get; init; }
    public double IslandPlacementX { get; init; } = .5;
    public double IslandPlacementY { get; init; }
    public bool CoreControlsOnly { get; init; }
    public bool StreamVoiceSentences { get; init; }
    private int inkLifetimeSeconds = 15;
    public int InkLifetimeSeconds { get => inkLifetimeSeconds; init => inkLifetimeSeconds = NormalizeInkLifetime(value); }
    // Disconnected setup drafts only. These never select an active route or store credentials.
    public string ProviderDraft { get; init; } = "";
    public string ProviderModelDraft { get; init; } = "";
    internal static int NormalizeInkLifetime(int seconds) => seconds is 5 or 15 or 30 ? seconds : 15;
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalSettings { get; init; }
    public bool GuideAutoAdvance { get; init; }
    public bool StrictAgentConfirmations { get; init; }
    public bool ShowCompanion { get; init; } = true;
    public bool StartInCompanionMode { get; init; } = true;
    public string Shortcut { get; init; } = "Ctrl + Alt + Space";
    public string VoiceShortcut { get; init; } = "Ctrl + Shift + Space";
    public string RecognitionEngine { get; init; } = "whisper";
    public string WhisperModel { get; init; } = "base.en";
    public string RecognitionLanguage { get; init; } = "";
    public string MicrophoneId { get; init; } = "";
    public string VoiceName { get; init; } = "";
    public string VoiceEngine { get; init; } = "windows";
    public int NeuralSpeakerId { get; init; } = DefaultNeuralSpeakerId;
    public string NeuralPreset { get; init; } = "";
    public int VoiceRate { get; init; } = -1;
    public bool HeadphonesOnly { get; init; }
    public string HeadphoneDeviceId { get; init; } = "";
    public bool ProtectScreenshots { get; init; } = true;
    public bool ShortcutStartsVoice { get; init; }
    public bool ReadVoiceAnswers { get; init; } = true;
    public bool HoldToTalk { get; init; }
    public bool CaptureOnVoice { get; init; } = true;
    public bool RememberTeaching { get; init; }
    public bool AllowWebResearch { get; init; }
    public bool AgentEnabled { get; init; }
    public string BlockedApps { get; init; } = "keepass,1password,bitwarden,lastpass";

    private static string FilePath => Path.Combine(PreviewEnvironment.DataDirectory, "desktop.json");
    internal static DesktopPreferences Load(string? path = null)
    {
        path ??= FilePath;
        if (!File.Exists(path)) return new();
        var json = File.ReadAllText(path);
        var value = JsonSerializer.Deserialize<DesktopPreferences>(json) ?? throw new InvalidDataException("Buddy preferences are empty; the existing file has been preserved.");
        if (value.SchemaVersion > 2) throw new InvalidDataException("These preferences need a newer Buddy version. The existing file has been preserved.");
        using var fields = JsonDocument.Parse(json);
        return value with { SchemaVersion = 2, OnboardingCompleted = fields.RootElement.TryGetProperty(nameof(OnboardingCompleted), out _) ? value.OnboardingCompleted : true };
    }
    // Apply only values edited relative to the visible editor/source baseline.
    internal static DesktopPreferences ApplyChanges(DesktopPreferences current, DesktopPreferences previous, DesktopPreferences proposedValue)
    {
        var saved = JsonSerializer.SerializeToNode(current)!.AsObject();
        var baseline = JsonSerializer.SerializeToNode(previous)!.AsObject();
        var proposed = JsonSerializer.SerializeToNode(proposedValue)!.AsObject();
        foreach (string key in baseline.Select(p => p.Key).Union(proposed.Select(p => p.Key), StringComparer.Ordinal)) {
            baseline.TryGetPropertyValue(key, out var before); proposed.TryGetPropertyValue(key, out var after);
            if (JsonNode.DeepEquals(before, after)) continue;
            if (proposed.ContainsKey(key)) saved[key] = after?.DeepClone(); else saved.Remove(key);
        }
        return saved.Deserialize<DesktopPreferences>() ?? throw new InvalidDataException("Preferences could not be merged; the existing file is unchanged.");
    }
    internal DesktopPreferences Save(string? path = null, DesktopPreferences? previous = null)
    {
        path = Path.GetFullPath(path ?? FilePath);
        string mutexName = "Local\\Buddy.Preferences." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
        using var mutex = new Mutex(false, mutexName);
        bool held = false;
        string? temporary = null;
        try {
            try { held = mutex.WaitOne(TimeSpan.FromSeconds(3)); }
            catch (AbandonedMutexException) { held = true; }
            if (!held) throw new IOException("Another Buddy window is saving preferences. Your edits are retained; try Save changes again.");
            // Validate before replacing; corrupt and newer files are never reset.
            var current = File.Exists(path) ? Load(path) : null;
            var effective = this;
            if (current is not null && previous is not null) effective = ApplyChanges(current, previous, this);
            if (effective.Shortcut == effective.VoiceShortcut) throw new InvalidDataException("Chat and voice shortcuts must differ. The saved preferences were preserved.");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough)) {
                JsonSerializer.Serialize(stream, effective, new JsonSerializerOptions { WriteIndented = true });
                stream.Flush(true);
            }
            File.Move(temporary, path, true); temporary = null;
            return effective;
        } finally {
            if (temporary is not null && File.Exists(temporary)) File.Delete(temporary);
            if (held) mutex.ReleaseMutex();
        }
    }
}

internal sealed record ShortcutChoice(string Label, uint Modifiers)
{
    internal static readonly ShortcutChoice[] RegionChoices = [new("Ctrl + Alt + Shift + R", 7), new("Ctrl + Shift + R", 6), new("Alt + Shift + R", 5)];
    internal static ShortcutChoice FindRegion(string label) => RegionChoices.FirstOrDefault(c => c.Label == label) ?? RegionChoices[0];
    internal static readonly ShortcutChoice[] Choices = [
        new("Ctrl + Space", 0x0002), new("Ctrl + Alt + Space", 0x0003),
        new("Alt + Shift + Space", 0x0005), new("Ctrl + Shift + Space", 0x0006), new("Ctrl + Alt + Shift + Space", 0x0007)
    ];
    internal static ShortcutChoice Find(string label) => Choices.FirstOrDefault(c => c.Label == label) ?? Choices[1];
    internal static string Warning(string label) => label == "Ctrl + Space" ? "Ctrl+Space commonly conflicts with ChatGPT Desktop and input methods; choose another chord if both apps respond." : "Windows+Space is reserved for the input language. Other apps using keyboard hooks may still intercept a registered chord.";
    internal static string Warnings(string chat, string voice) => string.Join(" ", new[] { Warning(chat), Warning(voice) }.Distinct());
    public override string ToString() => Label;
}

// Register the replacement under another ID first: a conflict must not remove
// the working shortcut. Native delegates are supplied only on the UI thread.
internal sealed class ShortcutRegistration(Func<int, uint, bool> register, Action<int> unregister, int firstId = 1, int secondId = 4) : IDisposable
{
    internal int ActiveId { get; private set; }
    internal ShortcutChoice? Active { get; private set; }
    private bool pending;
    private void Release(int id) => unregister(id);
    internal bool TrySet(ShortcutChoice choice)
    {
        if (!TryPrepare(choice, out var change)) return false;
        using (change) change!.Commit();
        return true;
    }
    // A settings transaction keeps the old lease until the disk commit. A failed
    // save releases only provisional IDs, including when Active was unavailable.
    internal bool TryPrepare(ShortcutChoice? choice, out PendingChange? change)
    {
        if (pending) throw new InvalidOperationException("A shortcut update is already pending.");
        change = null;
        int next = choice is null ? 0 : Active == choice ? ActiveId : ActiveId == firstId ? secondId : firstId;
        if (next != 0 && next != ActiveId && !register(next, choice!.Modifiers | 0x4000)) return false;
        pending = true;
        change = new(this, next, choice);
        return true;
    }
    internal sealed class PendingChange(ShortcutRegistration owner, int next, ShortcutChoice? choice) : IDisposable
    {
        private bool finished;
        internal void Commit()
        {
            if (finished) throw new InvalidOperationException("This shortcut update has already finished.");
            int previous = owner.ActiveId;
            owner.ActiveId = next; owner.Active = choice;
            owner.pending = false; finished = true;
            if (previous != 0 && previous != next) owner.Release(previous);
        }
        public void Dispose()
        {
            if (finished) return;
            finished = true; owner.pending = false;
            if (next != 0 && next != owner.ActiveId) owner.Release(next);
        }
    }
    public void Dispose()
    {
        if (pending) throw new InvalidOperationException("Finish the pending shortcut update before disposing its registration.");
        if (ActiveId != 0) unregister(ActiveId); ActiveId = 0; Active = null;
    }
}
