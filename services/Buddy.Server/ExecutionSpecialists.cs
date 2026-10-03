namespace Buddy.Server;

public record ExecutionJob(int Id,string Specialist,string State,int ActionSequence,string Detail);

// Each job owns one bounded capability. Mutations share one lane; independent reads use two.
// Approval and native-scope validation remain inside the supplied desktop dispatch capability.
public sealed class ExecutionSpecialists : IDisposable
{
    private readonly SemaphoreSlim mutations=new(1,1),reads=new(2,2);
    private readonly List<ExecutionJob> jobs=[];private readonly object sync=new();
    private readonly IProgress<ExecutionJob>? progress;private int dispatched;
    public ExecutionSpecialists(IProgress<ExecutionJob>? progress=null)=>this.progress=progress;
    public IReadOnlyList<ExecutionJob> Snapshot {get{lock(sync)return jobs.ToArray();}}
    public static string Role(AssistantAction action)=>action.Kind switch{"type"=>"Writer","read"=>"Reader","wait"=>"Waiter",_=>"Navigator"};
    private int Add(string role,int sequence,string detail){
        ExecutionJob job;lock(sync){if(jobs.Count>=64)throw new InvalidOperationException("The workflow reached its 64-job limit.");job=new(jobs.Count+1,role,"queued",sequence,Security.Redact(detail[..Math.Min(500,detail.Length)]));jobs.Add(job);}progress?.Report(job);return job.Id;
    }
    private void State(int id,string state){ExecutionJob job;lock(sync){job=jobs[id-1] with{State=state};jobs[id-1]=job;}progress?.Report(job);}
    public async Task<ActionResult?> Dispatch(AssistantAction action,int sequence,Func<CancellationToken,Task<ActionResult?>> execute,CancellationToken ct){
        ActionPolicy.Validate(new("One reviewed specialist assignment",[action]));
        if(sequence is <1 or >25||Interlocked.Increment(ref dispatched)>25)throw new InvalidOperationException("The 25-action workflow budget is exhausted.");
        var id=Add(Role(action),sequence,action.Description);bool entered=false;
        try{await mutations.WaitAsync(ct);entered=true;ct.ThrowIfCancellationRequested();State(id,"reviewing / executing approved assignment");var receipt=await execute(ct);ct.ThrowIfCancellationRequested();
            if(receipt is not null&&(receipt.Sequence!=sequence||receipt.Action!=action))throw new InvalidOperationException("The execution specialist returned a receipt for another assignment.");
            State(id,receipt is null?"declined":receipt.Success?"dispatch receipt returned":"not executed; review needed");return receipt;
        }catch(OperationCanceledException){State(id,"cancelled; verify any dispatched effect");throw;}catch{State(id,"failed; no automatic replay");throw;}finally{if(entered)mutations.Release();}
    }
    public async Task<T> Read<T>(string role,int sequence,Func<CancellationToken,Task<T>> inspect,CancellationToken ct){
        if(role is not("Observer" or "Result verifier" or "Teacher"))throw new ArgumentException("Unknown read-only specialist.");
        var id=Add(role,sequence,"Read-only continuation evidence");bool entered=false;
        try{await reads.WaitAsync(ct);entered=true;ct.ThrowIfCancellationRequested();State(id,"running read-only");var result=await inspect(ct);ct.ThrowIfCancellationRequested();State(id,"result returned");return result;}
        catch(OperationCanceledException){State(id,"cancelled");throw;}catch{State(id,"failed");throw;}finally{if(entered)reads.Release();}
    }
    public void Dispose(){mutations.Dispose();reads.Dispose();}
}
