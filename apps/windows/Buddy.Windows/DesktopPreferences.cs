using System.Text.Json;

namespace Buddy.Windows;

internal sealed record DesktopPreferences
{
    internal const int DefaultNeuralSpeakerId = 60; // Owner-selected Voice C, VCTK p232.
    public int SchemaVersion { get; init; } = 2;
    public string Appearance { get; init; } = "System";
    public bool ReduceMotion { get; init; }
    public string CompanionName { get; init; } = "Buddy";
    public bool OnboardingCompleted { get; init; }
    public bool ShowFieldBadge { get; init; }
    public bool LocalPromptSuggestions { get; init; }
    public bool RegionSelectionEnabled { get; init; }
    public bool RegionVoiceAfterSelection { get; init; }
    public string RegionShortcut { get; init; } = "Ctrl + Alt + Shift + R";
    public bool TrianglePointerEnabled { get; init; }
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
    internal void Save(string? path = null)
    {
        path ??= FilePath;
        if (File.Exists(path)) _ = Load(path); // Never overwrite a corrupt or newer preference file.
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
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
    public override string ToString() => Label;
}

// Register the replacement under another ID first: a conflict must not remove
// the working shortcut. Native delegates are supplied only on the UI thread.
internal sealed class ShortcutRegistration(Func<int, uint, bool> register, Action<int> unregister, int firstId = 1, int secondId = 4) : IDisposable
{
    internal int ActiveId { get; private set; }
    internal ShortcutChoice? Active { get; private set; }
    internal bool TrySet(ShortcutChoice choice)
    {
        if (Active == choice) return true;
        int next = ActiveId == firstId ? secondId : firstId;
        if (!register(next, choice.Modifiers | 0x4000)) return false;
        int previous = ActiveId;
        ActiveId = next; Active = choice;
        if (previous != 0) unregister(previous);
        return true;
    }
    public void Dispose() { if (ActiveId != 0) unregister(ActiveId); ActiveId = 0; Active = null; }
}
