using Buddy.Server;

internal static class TeachingCases
{
    private static PlanningRequest Request(string query, string name, string role = "Button") => new(query,
        new("Owned synthetic test app", "Canned context", [new("qa53-control", name, role, 10, 10, 120, 40)]));

    internal static void Run(Checks c)
    {
        var reload = Request("Explain Reload without changing anything, and consider unsaved work.", "Reload");
        var complete = new ConceptualExplanation("Reload requests another copy of the current page.",
            "Unsaved edits can be lost, so no reload is requested here.",
            "Compare the displayed page with the content you intended to retrieve, without activating Reload.");
        var plan = GuideLessons.ComposeExplanation(complete, reload);
        c.Check(plan.Steps is { Count: 0 } && plan.Lessons is { Count: 1 } && plan.Lessons[0].Instruction == string.Join(" ", complete.Purpose, complete.Limitation, complete.ManualCheck),
            "TEACH-POSITIVE-FULL", "Complete authored function, caution and result check survive verbatim without executable steps.");
        c.Rejects<BuddyException>(() => GuideLessons.ComposeExplanation(complete with { ManualCheck = "Check whether Reload is visible and enabled." }, reload),
            "TEACH-AVAILABILITY", "Availability alone cannot substitute for the requested function/outcome check.");
        c.Rejects<BuddyException>(() => GuideLessons.ComposeExplanation(complete with { ManualCheck = "You could simply press Reload to check the result." }, reload),
            "TEACH-MODAL-ACTION", "Optional-adverb action instruction cannot escape a no-change request.");
        c.Rejects<BuddyException>(() => GuideLessons.ComposeExplanation(complete with { Limitation = "The page will open a confirmation dialog." }, reload),
            "TEACH-FUTURE-UI", "Unobserved future UI cannot be presented as established behavior.");
        c.Rejects<BuddyException>(() => GuideLessons.ComposeExplanation(complete with { Purpose = "I have already reloaded your page." }, reload),
            "TEACH-PERFORMED", "A model explanation is not an executed-action receipt.");
        c.Rejects<BuddyException>(() => GuideLessons.ComposeExplanation(complete with { ManualCheck = "Compare the page. Then compare the content." }, reload),
            "TEACH-NO-TRUNCATION", "Extra complete sentences are rejected as a whole, not clipped to fit.");
        c.Rejects<BuddyException>(() => GuideLessons.ComposeExplanation(complete with { Limitation = new string('x', 601) + "." }, reload),
            "TEACH-OVERLONG-QUALIFICATION", "An overlong qualification cannot disappear through truncation.");
        c.Rejects<BuddyException>(() => GuideLessons.ComposeExplanation(complete with { ManualCheck = "The manualCheck should contain visible state." }, reload),
            "TEACH-METADATA", "Internal model schema names do not become user guidance.");

        var mute = Request("Explain Mute without changing it; I cannot tell what audio it affects or its current state.", "Mute", "CheckBox");
        var uncertain = new ConceptualExplanation("Mute generally suppresses some audio, but the affected channel is unknown here.",
            "The label alone does not establish the present state or whether recording is affected.",
            "Compare the visible audio-state description with the channel you intend to check, without changing Mute.");
        c.Check(GuideLessons.ComposeExplanation(uncertain, mute).Steps is { Count: 0 }, "TEACH-MUTE-UNCERTAINTY", "Useful conditional wording may retain unknown channel and state.");
        c.Rejects<BuddyException>(() => GuideLessons.ComposeExplanation(uncertain with { Purpose = "Mute silences the speakers in this application." }, mute),
            "TEACH-MUTE-SCOPE", "A checkbox label cannot establish speaker-only rather than microphone or other scope.");
        c.Rejects<BuddyException>(() => GuideLessons.ComposeExplanation(uncertain with { Limitation = "The checked Mute box confirms that sound is already muted." }, mute),
            "TEACH-MUTE-STATE", "Enabled metadata cannot establish toggle state or completed effect.");

        var zoom = Request("Explain the Zoom slider without changing anything.", "Zoom", "Slider");
        c.Rejects<BuddyException>(() => GuideLessons.ComposeExplanation(new("The Zoom application controls meetings.", "The current value is unknown.", "Compare the displayed text size with your preferred readability."), zoom),
            "TEACH-LABEL-NOT-APP", "A control name is not application identity.");
        var publish = Request("Explain Publish without changing anything; account for private material and audience permission.", "Publish");
        c.Rejects<BuddyException>(() => GuideLessons.ComposeExplanation(new("Publishing makes material visible to an audience.", "The button can have different behavior in different apps.", "Compare the displayed audience with your intended readers."), publish),
            "TEACH-PRIVATE-QUALIFICATION", "A generic caution cannot drop explicitly requested private-material and permission qualifications.");

        var disabled = new PlanningRequest("Explain Download without activating it.", new("Owned synthetic test app", "Canned", [
            new("download", "Download", "Button", 10, 10, 100, 30, false), new("help", "Help", "Button", 10, 60, 100, 30)]));
        c.Check(GuideLessons.EvidenceClarification(disabled) is { Steps.Count: 0 }, "TEACH-DISABLED-UNRELATED", "Unrelated enabled Help does not establish a Download prerequisite or pointer.");
        var duplicate = reload with { Context = reload.Context with { Elements = [reload.Context.Elements[0], reload.Context.Elements[0] with { Ref = "second-reload", Y = 90 }] } };
        c.Check(GuideLessons.EvidenceClarification(duplicate) is { Steps.Count: 0 }, "TEACH-AMBIGUOUS", "Identical names on different controls require clarification, not a guessed pointer.");
    }
}
