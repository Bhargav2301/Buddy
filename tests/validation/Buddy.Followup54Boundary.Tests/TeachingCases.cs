#if HAS_CONCEPT54
using Buddy.Server;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class TeachingCases {
    internal static void Run(Checks c) {
        using var input=Assembly.GetExecutingAssembly().GetManifestResourceStream("Qa54Heldouts")!;
        using var document=JsonDocument.Parse(input); int expectedUseful=0,actualUseful=0;
        foreach(var item in document.RootElement.GetProperty("cases").EnumerateArray()) {
            string id=item.GetProperty("id").GetString()!, query=item.GetProperty("query").GetString()!, app=item.GetProperty("app").GetString()!, expected=item.GetProperty("expected").GetString()!;
            var controls=item.GetProperty("observations").EnumerateArray().Select((e,i)=>new ScreenElement("qa54-synthetic-"+i,e.GetProperty("name").GetString()!,e.GetProperty("role").GetString()!,10+i*150,20,120,40,e.GetProperty("enabled").GetBoolean())).ToList();
            var request=new PlanningRequest(query,new(app,"Synthetic fixture, no real window",controls));
            var plan=KnownControlConcepts.TryExplain(request); bool authored=plan?.Summary==ConceptReferences.Origin;
            string prose=string.Join(" ",plan?.Lessons?.Select(x=>x.Instruction)??[]);
            c.Check(plan is null||(plan.Steps?.Count??0)==0&&(plan.Lessons??[]).All(l=>l.Target==""&&l.Role==""),id+"-NO-ACTION","Catalog and clarification never introduce targets or executable steps.");
            if(expected=="authored") { expectedUseful++; if(authored)actualUseful++; }
            if(expected is "unsupported" or "clarification_or_unsupported") c.Check(!authored,id+"-SCOPE","Unsupported conditions, state claims or ambiguous evidence do not silently become a canned concept.");
            if(authored) {
                c.Check(plan!.Sources is {Count:>0}&&plan.Sources.All(s=>s.EvidenceKind=="curated local reference"&&s.Text.Length==0&&s.Title.StartsWith("Local reference (reviewed ",StringComparison.Ordinal)),id+"-PROVENANCE","Static bibliography is labelled local; no freshly fetched evidence is claimed.");
                c.Check(plan.Lessons is {Count:1}&&Regex.Matches(prose,@"[.!?](?:\s|$)").Count==3&&prose.EndsWith('.'),id+"-COMPLETE","All three full concept sentences survive without truncation.");
                c.Check(!Regex.IsMatch(prose,@"\b(?:I|Buddy|we) (?:have |has )?(?:clicked|reloaded|changed|muted|enabled)\b",RegexOptions.IgnoreCase),id+"-NO-COMPLETION","No unperformed action is reported complete.");
                c.Check(Utility(prose,controls[0].Name),id+"-UTILITY","Function, relevant limitation and a concrete non-action comparison are present.");
            }
            Console.WriteLine(JsonSerializer.Serialize(new{kind="heldout_result",id,expected,authored,prose,provenance="synthetic observations; authored local catalog only",model=false}));
        }
        Console.WriteLine(JsonSerializer.Serialize(new{kind="heldout_coverage",expectedUseful,actualUseful,clarificationsCountAsUtility=false,modelUseful=0,modelFailed=5,modelClarifications=4,manualReviewRequired=true}));
        var slider=KnownControlConcepts.TryExplain(new("What does Zoom do?",new("Owned Canvas","Synthetic",[new("qa54-slider","Zoom","Slider",0,0,40,40)])));
        c.Check(slider?.Summary==ConceptReferences.Origin&&Utility(slider.Lessons![0].Instruction,"Zoom"),"CATALOG-SUPPORTED-SLIDER","Separately declared supported role explains magnification without claiming the Zoom application.");
        var fresh=new PlanningRequest("What does Reload do?",new("Comet","Synthetic",[new("qa54-reload","Reload","Button",0,0,40,40)]));
        c.Check(KnownControlConcepts.TryExplain(fresh with{UseWeb=true}) is null,"CATALOG-WEB","An explicit research request is not answered as local authored research.");
        c.Check(KnownControlConcepts.TryExplain(fresh with{Query="Explain Reload after saving all files."}) is null,"CATALOG-PRECONDITION","An additional ordering condition is not discarded.");
        c.Check(KnownControlConcepts.TryExplain(fresh with{Context=fresh.Context with{Elements=[fresh.Context.Elements[0] with{Width=double.NaN}]}}) is null,"CATALOG-INVALID-OBSERVATION","Nonfinite observed geometry does not admit the catalog.");
        var first=KnownControlConcepts.TryExplain(fresh)!; first.Sources!.Clear();
        c.Check(KnownControlConcepts.TryExplain(fresh)!.Sources!.Count>0,"CATALOG-ISOLATED-SOURCES","Caller mutation cannot destroy static references for later requests.");
    }
    private static bool Utility(string value,string name) {
        string p=value.ToLowerInvariant();
        bool Has(params string[] terms)=>terms.Any(p.Contains);
        bool check=Has("compare","review","distinguish","checking");
        return name.ToLowerInvariant() switch {
            "reload"=>Has("page")&&Has("requests","refresh")&&Has("unsaved")&&Has("may","risk","lost")&&check,
            "zoom"=>Has("magnification","magnif")&&Has("current factor","current level")&&Has("surface","scope")&&check&&!Has("zoom application","zoom meeting"),
            "mute"=>Has("silence","silences")&&Has("input")&&Has("output")&&Has("checked state")&&Has("not established","neither","unknown")&&check,
            "word wrap"=>Has("display","visual")&&Has("stored","hard")&&Has("line break")&&Has("current state")&&check,
            _=>false
        };
    }
}
#endif
