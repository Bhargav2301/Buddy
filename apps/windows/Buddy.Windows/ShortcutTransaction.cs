namespace Buddy.Windows;

// UI-thread-only registration transaction. The supplied native delegates use
// disjoint ID pairs on the same window; preparation never releases active IDs.
internal sealed class ShortcutBindingUpdate : IDisposable
{
    private readonly List<ShortcutRegistration.PendingChange> changes = [];
    private bool finished;
    internal ShortcutRegistration? Chat { get; private init; }
    internal ShortcutRegistration? Voice { get; private init; }
    internal ShortcutRegistration? Region { get; private init; }

    internal static ShortcutBindingUpdate Prepare(ShortcutRegistration? chat, ShortcutRegistration? voice,
        ShortcutRegistration? region, DesktopPreferences preferences)
    {
        var chatChoice = ShortcutChoice.Choices.FirstOrDefault(c => c.Label == preferences.Shortcut)
            ?? throw new InvalidOperationException("Choose a supported chat shortcut.");
        var voiceChoice = ShortcutChoice.Choices.FirstOrDefault(c => c.Label == preferences.VoiceShortcut)
            ?? throw new InvalidOperationException("Choose a supported voice shortcut.");
        if (chatChoice.Modifiers == voiceChoice.Modifiers) throw new InvalidOperationException("Choose different chat and voice shortcuts.");
        var regionChoice = preferences.RegionSelectionEnabled
            ? ShortcutChoice.RegionChoices.FirstOrDefault(c => c.Label == preferences.RegionShortcut)
                ?? throw new InvalidOperationException("Choose a supported area-selection shortcut.")
            : null;

        // Transfer ownership of an already registered chord between channels.
        // This handles both exact swaps and moving one channel onto the other's
        // old chord without unregistering it or competing with our own lease.
        if (chat is not null && voice is not null &&
            (voice.Active == chatChoice || chat.Active == voiceChoice))
            (chat, voice) = (voice, chat);
        var update = new ShortcutBindingUpdate { Chat = chat, Voice = voice, Region = region };
        try {
            update.Stage(chat, chatChoice, "chat");
            update.Stage(voice, voiceChoice, "voice");
            update.Stage(region, regionChoice, "area-selection");
            return update;
        } catch { update.Dispose(); throw; }
    }
    private void Stage(ShortcutRegistration? registration, ShortcutChoice? choice, string channel)
    {
        if (registration is null) return;
        if (!registration.TryPrepare(choice, out var change))
            throw new InvalidOperationException("That " + channel + " shortcut is in use. Previous bindings remain active.");
        changes.Add(change!);
    }
    internal void Commit()
    {
        if (finished) throw new InvalidOperationException("This shortcut update has already finished.");
        foreach (var change in changes) change.Commit();
        finished = true;
    }
    public void Dispose()
    {
        if (finished) return;
        foreach (var change in changes.AsEnumerable().Reverse()) change.Dispose();
        finished = true;
    }
}
