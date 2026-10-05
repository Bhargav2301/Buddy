#if HAS_CORE53
using Buddy.Windows;

internal static class CoreCases
{
    private sealed class Registrations
    {
        internal readonly Dictionary<int, (uint Modifiers, string Key)> Held = [];
        internal readonly List<int> Released = [];
        internal uint? Refuse;
        internal ShortcutRegistration Channel(int id, int other, string key = "Space") => new((n, m) => {
            m &= ~0x4000u;
            if (m == Refuse || Held.ContainsKey(n) || Held.Values.Contains((m, key))) return false;
            Held[n] = (m, key); return true;
        }, n => { Released.Add(n); Held.Remove(n); }, id, other);
    }
    private static DesktopPreferences Desired(int chat, int voice, bool region = true) => new() {
        Shortcut = ShortcutChoice.Choices[chat].Label,
        VoiceShortcut = ShortcutChoice.Choices[voice].Label,
        RegionSelectionEnabled = region, RegionShortcut = ShortcutChoice.RegionChoices[0].Label
    };

    internal static void Run(Checks c)
    {
        var initial = new Registrations();
        using var noChat = initial.Channel(1, 4);
        using var voiceOnly = initial.Channel(2, 5);
        voiceOnly.TrySet(ShortcutChoice.Choices[3]);
        using (ShortcutBindingUpdate.Prepare(noChat, voiceOnly, null, Desired(1, 3))) {
            c.Check(noChat.Active is null && voiceOnly.Active == ShortcutChoice.Choices[3], "CORE-STAGE-UNBOUND", "Staging an unavailable chat chord does not falsely make it the active binding.");
            c.Check(initial.Held.Count == 2, "CORE-PROVISIONAL", "Provisional chord exists alongside the previously working binding.");
            // Dispose models the owner's save exception before Commit.
        }
        c.Check(initial.Held.Count == 1 && noChat.Active is null && voiceOnly.Active == ShortcutChoice.Choices[3], "CORE-FAILED-SAVE-UNBOUND", "Failed save releases a newly staged chord even when the previous chat binding was null.");

        var registry = new Registrations();
        using var chat = registry.Channel(1, 4); using var voice = registry.Channel(2, 5); using var region = registry.Channel(3, 6, "R");
        chat.TrySet(ShortcutChoice.Choices[1]); voice.TrySet(ShortcutChoice.Choices[3]); region.TrySet(ShortcutChoice.RegionChoices[0]);
        var originalIds = registry.Held.Keys.Order().ToArray();
        using (var rollback = ShortcutBindingUpdate.Prepare(chat, voice, region, Desired(2, 4, false))) {
            c.Check(registry.Released.Count == 0 && originalIds.All(registry.Held.ContainsKey), "CORE-RETAIN-OLD", "Changing chat/voice and disabling region retains all previous registrations until persistence succeeds.");
        }
        c.Check(registry.Held.Keys.Order().SequenceEqual(originalIds) && region.Active is not null, "CORE-ROLLBACK-DISABLE", "Rolling back region disable and two replacements leaves exactly the original working IDs.");

        registry.Refuse = ShortcutChoice.Choices[4].Modifiers;
        c.Rejects<InvalidOperationException>(() => ShortcutBindingUpdate.Prepare(chat, voice, region, Desired(2, 4)), "CORE-PARTIAL-CONFLICT", "Second-channel conflict refuses the complete transaction.");
        c.Check(registry.Held.Keys.Order().SequenceEqual(originalIds) && chat.Active == ShortcutChoice.Choices[1] && voice.Active == ShortcutChoice.Choices[3], "CORE-PARTIAL-ROLLBACK", "Earlier provisional success cannot remain after a later channel conflict.");
        registry.Refuse = null;
        int releases = registry.Released.Count;
        using (var swap = ShortcutBindingUpdate.Prepare(chat, voice, region, Desired(3, 1))) {
            c.Check(ReferenceEquals(swap.Chat, voice) && ReferenceEquals(swap.Voice, chat), "CORE-SWAP-OWNERS", "Exact chord swaps transfer the existing registration objects to the right channels.");
            c.Check(registry.Held.Keys.Order().SequenceEqual(originalIds), "CORE-SWAP-NO-GAP", "A swap does not unregister and reacquire working chords.");
            swap.Commit();
            c.Rejects<InvalidOperationException>(swap.Commit, "CORE-ONE-COMMIT", "A shortcut transaction cannot commit twice.");
        }
        c.Check(registry.Released.Count == releases && registry.Held.Keys.Order().SequenceEqual(originalIds), "CORE-SWAP-DISPOSE", "Disposing committed ownership transfer does not release active bindings.");
        using (var partialMove = ShortcutBindingUpdate.Prepare(chat, voice, region, Desired(3, 2))) {
            c.Check(partialMove.Chat?.Active == ShortcutChoice.Choices[3] && chat.Active == ShortcutChoice.Choices[1], "CORE-PARTIAL-MOVE", "Moving onto another owned chord keeps the original source object unchanged until commit.");
        }
        c.Check(registry.Held.Keys.Order().SequenceEqual(originalIds), "CORE-PARTIAL-MOVE-ROLLBACK", "An abandoned partial move restores every original chord without reacquisition.");
        using (var commit = ShortcutBindingUpdate.Prepare(chat, voice, region, Desired(2, 4, false))) {
            commit.Commit();
            c.Check(chat.Active == ShortcutChoice.Choices[2] && voice.Active == ShortcutChoice.Choices[4] && region.Active is null, "CORE-COMMIT", "Only explicit commit makes replacements active and disables region.");
        }
        c.Check(registry.Held.Count == 2 && registry.Held.Values.All(x => x.Key == "Space"), "CORE-EXACT-AFTER-COMMIT", "Completed transaction retains exactly two selected chat/voice registrations.");
    }
}
#endif
