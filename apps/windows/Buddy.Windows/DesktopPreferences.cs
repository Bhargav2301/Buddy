using System.Text.Json;

namespace Buddy.Windows;

internal sealed record DesktopPreferences
{
    public int SchemaVersion { get; init; } = 2;
    public string Appearance { get; init; } = "System";
    public bool ReduceMotion { get; init; }
    public string CompanionName { get; init; } = "Buddy";
    public bool OnboardingCompleted { get; init; }
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalSettings { get; init; }
    public bool GuideAutoAdvance { get; init; }
    public bool StrictAgentConfirmations { get; init; }
    public bool ShowCompanion { get; init; } = true;
    public bool StartInCompanionMode { get; init; } = true;
    public string Shortcut { get; init; } = "Ctrl + Space";
    public bool ShortcutStartsVoice { get; init; }
    public bool ReadVoiceAnswers { get; init; } = true;
    public bool HoldToTalk { get; init; }
    public bool CaptureOnVoice { get; init; } = true;
    public bool AllowWebResearch { get; init; }
    public bool AgentEnabled { get; init; }
    public string BlockedApps { get; init; } = "keepass,1password,bitwarden,lastpass";

    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Buddy", "desktop.json");
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
    internal static readonly ShortcutChoice[] Choices = [
        new("Ctrl + Space", 0x0002), new("Ctrl + Alt + Space", 0x0003),
        new("Alt + Shift + Space", 0x0005), new("Windows + Space (if available)", 0x0008)
    ];
    public override string ToString() => Label;
}

// Register the replacement under another ID first: a conflict must not remove
// the working shortcut. Native delegates are supplied only on the UI thread.
internal sealed class ShortcutRegistration(Func<int, uint, bool> register, Action<int> unregister) : IDisposable
{
    internal int ActiveId { get; private set; }
    internal ShortcutChoice? Active { get; private set; }
    internal bool TrySet(ShortcutChoice choice)
    {
        if (Active == choice) return true;
        int next = ActiveId == 1 ? 4 : 1;
        if (!register(next, choice.Modifiers | 0x4000)) return false;
        int previous = ActiveId;
        ActiveId = next; Active = choice;
        if (previous != 0) unregister(previous);
        return true;
    }
    public void Dispose() { if (ActiveId != 0) unregister(ActiveId); ActiveId = 0; Active = null; }
}
