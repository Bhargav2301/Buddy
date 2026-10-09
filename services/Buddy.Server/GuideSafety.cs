using System.Text.RegularExpressions;

namespace Buddy.Server;

public static class GuideSafety
{
    public static string? RequestedApp(string query)=>Regex.IsMatch(query,@"\b(?:use)?amoeba\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100))?"amoeba":Regex.IsMatch(query,@"\bnotepad\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100))?"notepad":Regex.IsMatch(query,@"\bcomet(?:\s+browser)?\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100))?"comet":null;
    public static bool IsPointRequest(string query)=>Regex.IsMatch(query,@"\b(point|locate|highlight|underline|circle|where)\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    public static string? RequestedText(string query){
        if(!IsPointRequest(query)||!Regex.IsMatch(query,@"\b(word|text|phrase|document|notepad)\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)))return null;
        var quoted=Regex.Match(query,"[\"“']([^\"”'\\r\\n]{1,80})[\"”']",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
        if(quoted.Success)return quoted.Groups[1].Value;
        var word=Regex.Match(query,@"\b(?:word|text|phrase)\s+(?:is\s+)?([\p{L}\p{N}_-]{1,80})\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
        if(word.Success&&word.Groups[1].Value.ToLowerInvariant() is not("in" or "on" or "at" or "inside" or "from"))return word.Groups[1].Value;
        return null;
    }
    public static bool AllowedTarget(ScreenElement element,string query){
        if(element.Role is "Window" or "TitleBar" or "Pane")return false;
        var chrome=Regex.IsMatch(element.Name,@"^(minimize|maximize|restore|close)( window)?$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
        if(chrome&&!Regex.IsMatch(query,@"\b(minimize|maximize|restore|close)\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100)))return false;
        if(RequestedText(query) is { } text)return element.Role=="Text"&&element.Ref.StartsWith("word:",StringComparison.Ordinal)&&element.Name.Equals(text,StringComparison.OrdinalIgnoreCase);
        return true;
    }
    public static GuidePlan Validate(GuidePlan plan,PlanningRequest request){
        if(plan.Summary is null||plan.Summary.Length>2400||plan.Steps is null||plan.Steps.Count>8)throw new BuddyException("INVALID_GUIDE","A guide needs a bounded explanation and observed steps.");
        foreach(var step in plan.Steps){
            if(RequestedApp(request.Query) is {} app&&!request.Context.App.Equals(app,StringComparison.OrdinalIgnoreCase))throw new BuddyException("INVALID_GUIDE","The selected process is not the requested app. Ask the user to focus that app; do not point elsewhere.");
            if(step is null||step.Instruction is null||step.Instruction.Length is <1 or >600||step.Ref is null||step.Target is null||step.Role is null||step.Expect is {Kind:not("manual" or "visible" or "absent")})throw new BuddyException("INVALID_GUIDE","The guide contains an invalid step.");
            var target=GroundingResolver.Resolve(request.Context.Elements,step.Ref,step.Target,step.Role);
            if(target is null||!AllowedTarget(target,request.Query)||!target.Name.Equals(step.Target,StringComparison.OrdinalIgnoreCase)||!target.Role.Equals(step.Role,StringComparison.OrdinalIgnoreCase))throw new BuddyException("INVALID_GUIDE","Use only exact, observed, goal-relevant controls. Window titles are not document text, and window-management buttons are not setup instructions.");
        }
        return plan;
    }
}

public sealed class GuideReviewProgress
{
    private readonly HashSet<int> observed=[];private readonly HashSet<int> skipped=[];
    public void Advance(int index,bool verified,bool skip){if(verified){observed.Add(index);skipped.Remove(index);}else if(skip){skipped.Add(index);observed.Remove(index);}}
    public bool AllObserved(int count)=>count>0&&skipped.Count==0&&Enumerable.Range(0,count).All(observed.Contains);
    public string EndMessage(int count)=>AllObserved(count)?"All listed step outcomes were observed. Check the overall task result.":skipped.Count>0?"Guide ended with skipped steps. Task completion was not verified.":"Guide reviewed. Task completion was not verified.";
}

public static class NotepadWriting
{
    public static void CheckEmptyEditor(string observedApp,string currentApp,bool ownedPractice,bool readOnly,string currentText){
        if(!observedApp.Equals(currentApp,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("The target application changed; no insertion was dispatched.");
        if(!ownedPractice&&!currentApp.Equals("notepad",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("The draft requires a verified Notepad editor.");
        if(readOnly||currentText.Length!=0)throw new InvalidOperationException("This draft requires an empty writable editor. Existing text was not replaced.");
    }
    public static bool Requested(string query)=>Regex.IsMatch(query,@"\bnotepad\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100))&&Regex.IsMatch(query,@"\b(write|compose|draft|type|insert)\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(100));
    public static AssistantPlan Plan(string text,ScreenContext context){
        if(string.IsNullOrWhiteSpace(text)||text.Length>4000)throw new BuddyException("INVALID_PLAN","The local writing draft is empty or too long; nothing was executed.");
        var actions=new List<AssistantAction>();
        bool active=context.App.Equals("notepad",StringComparison.OrdinalIgnoreCase);
        var editors=context.Elements.Where(e=>e.Enabled&&e.Role is "Edit" or "Document").ToArray();
        if(!active)actions.Add(new("open",Value:"notepad",Description:"Open Notepad; use a new empty document"));
        var editor=active&&editors.Length==1?editors[0]:null;
        if(active&&editor is null)throw new BuddyException("INVALID_PLAN","Focus one empty Notepad editor, then plan again. No text was changed.");
        actions.Add(new("type",Ref:editor?.Ref??"",Target:editor?.Name??"",Role:editor?.Role??"Edit",Value:text,Description:"Insert this reviewed draft only into an empty Notepad editor; existing text is never replaced",RequireEmpty:true));
        return ActionPolicy.Validate(new("Review the local draft below. Approve the plan and the insertion separately; a nonempty or unverifiable editor stops the task.",actions));
    }
}
