using System.Text.Json;

SourceReceipt.Verify();
if (args.Length != 1 || !args[0].StartsWith("--lane=",StringComparison.Ordinal)) throw new ArgumentException("Specify one headless lane; no live, native or IPC mode exists.");
string lane=args[0][7..]; var checks=new Checks();
switch(lane) {
#if HAS_PIPE54
    case "provider": await ProviderCases.Run(checks); break;
#endif
#if HAS_FILE_CONTEXT
    case "provenance": await ProvenanceCases.Run(checks); break;
#endif
#if HAS_CONCEPT54
    case "teaching": TeachingCases.Run(checks); break;
#endif
#if HAS_PLACEMENT54
    case "placement": PlacementCases.Run(checks); break;
#endif
#if HAS_GRAMMAR54
    case "grammar": GrammarCases.Run(checks); break;
#endif
    default: throw new ArgumentException("Unavailable source checkpoint: "+lane);
}
Console.WriteLine(JsonSerializer.Serialize(new{kind="summary",lane,checks.Passed,checks.Failed,model=false,native=false,network=false,ipc=false,installedProfile=false,manualReviewRequired=true}));
return checks.Failed==0?0:1;

internal sealed class Checks {
    internal int Passed{get;private set;} internal int Failed{get;private set;}
    internal void Check(bool value,string id,string expectation) {
        if(value)Passed++;else Failed++;
        Console.WriteLine(JsonSerializer.Serialize(new{kind="boundary_case",id,expectation,passed=value}));
    }
    internal void Rejects<T>(Action f,string id,string expectation) where T:Exception {
        try{f();Check(false,id,expectation);}catch(T){Check(true,id,expectation);}
    }
    internal async Task RejectsAsync<T>(Func<Task> f,string id,string expectation) where T:Exception {
        try{await f();Check(false,id,expectation);}catch(T){Check(true,id,expectation);}
    }
}
