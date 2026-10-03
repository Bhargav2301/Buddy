using System.Text.RegularExpressions;

namespace Buddy.Server;

// A lesson is explanatory content, never an action or evidence of a visible control.
// The desktop independently resolves one exact live target before drawing any ink.
public static class GuideLessons
{
    public static GuidePlan Compose(GuidePlan draft, PlanningRequest request)
    {
        if (draft.Summary is null || draft.Summary.Length > 2400 || draft.Steps is null || draft.Steps.Count > 8)
            throw new BuddyException("INVALID_GUIDE", "The lesson must have a bounded explanation and at most eight sections.");
        List<GuideStep> pointers = []; List<GuideLesson> lessons = [];
        foreach (var step in draft.Steps) {
            if (step is null || string.IsNullOrWhiteSpace(step.Instruction) || step.Instruction.Length > 600 || step.Target is null || step.Target.Length > 200 || step.Role is null || step.Role.Length > 80)
                throw new BuddyException("INVALID_GUIDE", "A lesson section was incomplete or too long.");
            // Irrelevant chrome must not survive merely by becoming a text-only lesson.
            if (!GuideSafety.AllowedTarget(new("", step.Target, step.Role, 0, 0, 1, 1), request.Query)) continue;
            lessons.Add(new(step.Instruction, step.Target, step.Role));
            try { GuideSafety.Validate(new("", [step]), request); pointers.Add(step); }
            catch (BuddyException e) when (e.Code == "INVALID_GUIDE") { /* retain explanation, never retain the unverified pointer */ }
        }
        if(draft.Steps.Count>0&&lessons.Count==0) throw new BuddyException("INVALID_GUIDE","All suggested sections were irrelevant window controls. Give a useful explanation of the requested task.");
        return draft with { Steps = pointers, Lessons = lessons };
    }
    public static ScreenElement? Resolve(GuideLesson lesson, ScreenContext context, string query)
    {
        if (GuideSafety.RequestedApp(query) is {} app && !app.Equals(context.App, StringComparison.OrdinalIgnoreCase)) return null;
        if (lesson.Role.Length == 0) return null;
        var exact = context.Elements.Where(e => e.Enabled && e.Width > 0 && e.Height > 0 && e.Name.Equals(lesson.Target, StringComparison.OrdinalIgnoreCase) && e.Role.Equals(lesson.Role, StringComparison.OrdinalIgnoreCase) && GuideSafety.AllowedTarget(e, query)).ToArray();
        return exact.Length == 1 ? exact[0] : null;
    }
    public static bool IsNotepadIntroduction(string query) => GuideSafety.RequestedApp(query) == "notepad" &&
        Regex.IsMatch(query, @"\b(teach|learn|how to use|getting started|basics)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    public static GuidePlan Notepad(PlanningRequest request)
    {
        var editors = request.Context.App.Equals("notepad", StringComparison.OrdinalIgnoreCase) ? request.Context.Elements.Where(e => e.Enabled && e.Role is "Edit" or "Document").ToArray() : [];
        var editor = editors.Length == 1 ? editors[0] : null;
        return new("Notepad is a plain-text editor: enter text, make changes, then save a text file. This introductory lesson leaves the work to you; reviewing a section does not change or save a document.", [], Lessons: [
            new("Start with an empty document, or keep your existing text. Select the editor and type a short practice sentence if you want to try it.", editor?.Name ?? "", editor?.Role ?? ""),
            new("Select a word to replace it, or use the Edit menu to find text and undo a change. Review the result before continuing.", "Edit", "MenuItem"),
            new("When you want to keep your work, use File > Save or Ctrl+S, choose a name and location, and check the saved file. Saving is your choice; Buddy has not saved anything.", "File", "MenuItem")]);
    }
}
