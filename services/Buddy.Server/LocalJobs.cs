namespace Buddy.Server;

public enum JobState { Planning, AwaitingApproval, Running, Verifying, Completed, Cancelled, Failed, ReviewNeeded }
public record JobReceipt(int Sequence, string Detail, bool Success, DateTimeOffset At);
public record LocalJob(string Id, string Kind, string Goal, string App, JobState State, string Status, DateTimeOffset Started, DateTimeOffset Updated, IReadOnlyList<JobReceipt> Receipts);

// One active job; no replay of unfinished actions after restart. Only observed results become receipts.
public sealed class JobLedger
{
    private readonly List<LocalJob> jobs=[];
    public event Action? Changed;
    public IReadOnlyList<LocalJob> Snapshot => jobs.ToArray();
    public static bool Terminal(JobState state)=>state is JobState.Completed or JobState.Cancelled or JobState.Failed or JobState.ReviewNeeded;
    public void Restore(IEnumerable<LocalJob> saved)
    {
        if(jobs.Count!=0)throw new InvalidOperationException("Job history is already loaded.");
        jobs.AddRange(saved.TakeLast(20).Select(j=>Terminal(j.State)?j:j with {State=JobState.ReviewNeeded,Status="Buddy restarted. Verify the last result; this job was not resumed.",Updated=DateTimeOffset.UtcNow}));
    }
    public string Begin(string kind,string goal,string app)
    {
        if(kind is not ("action" or "notes" or "import"))throw new InvalidOperationException("Unsupported local job.");
        if(jobs.Any(j=>!Terminal(j.State)))throw new InvalidOperationException("Stop or finish the current job before starting another.");
        Security.Text(goal,4000,"Job goal");
        var id=Guid.NewGuid().ToString("N");var now=DateTimeOffset.UtcNow;
        jobs.Add(new(id,kind,Security.Redact(goal),Security.Redact(app),JobState.Planning,"Preparing on this PC",now,now,[]));
        if(jobs.Count>20)jobs.RemoveAt(0);Changed?.Invoke();return id;
    }
    public void Move(string id,JobState next,string status)
    {
        int i=jobs.FindIndex(j=>j.Id==id);if(i<0)return;var job=jobs[i];if(Terminal(job.State))return;
        bool allowed=next is JobState.Cancelled or JobState.Failed or JobState.ReviewNeeded ||
            (job.State,next) is (JobState.Planning,JobState.AwaitingApproval) or (JobState.Running,JobState.AwaitingApproval) or
            (JobState.Running,JobState.Verifying) or (JobState.Verifying,JobState.AwaitingApproval) or (JobState.Verifying,JobState.Completed) ||
            job.Kind!="action"&&job.State==JobState.Planning&&next==JobState.Running;
        if(!allowed)throw new InvalidOperationException("Invalid job transition; approval or verification is required.");
        if(next==JobState.Completed&&(job.Receipts.Count==0||job.Receipts.Any(r=>!r.Success)))throw new InvalidOperationException("Completion requires successful observed receipts.");
        jobs[i]=job with {State=next,Status=Bound(status,1600),Updated=DateTimeOffset.UtcNow};Changed?.Invoke();
    }
    public void Approve(string id)
    {
        int i=jobs.FindIndex(j=>j.Id==id);if(i<0||jobs[i].State!=JobState.AwaitingApproval)throw new InvalidOperationException("There is no current approval to accept.");
        jobs[i]=jobs[i] with {State=JobState.Running,Status="Approved; running the reviewed step or plan",Updated=DateTimeOffset.UtcNow};Changed?.Invoke();
    }
    public void Progress(string id,string status)
    {
        int i=jobs.FindIndex(j=>j.Id==id);if(i<0||Terminal(jobs[i].State))return;
        jobs[i]=jobs[i] with {Status=Bound(status,1600),Updated=DateTimeOffset.UtcNow};Changed?.Invoke();
    }
    public void Receipt(string id,string detail,bool success)
    {
        int i=jobs.FindIndex(j=>j.Id==id);if(i<0||Terminal(jobs[i].State))return;var job=jobs[i];
        if(job.State!=JobState.Running||job.Receipts.Count>=25)throw new InvalidOperationException("A receipt requires a bounded running job.");
        jobs[i]=job with {Receipts=job.Receipts.Append(new(job.Receipts.Count+1,Bound(detail,2400),success,DateTimeOffset.UtcNow)).ToArray(),Updated=DateTimeOffset.UtcNow};Changed?.Invoke();
    }
    public void ClearHistory(){if(jobs.Any(j=>!Terminal(j.State)))throw new InvalidOperationException("Stop the current job first.");jobs.Clear();Changed?.Invoke();}
    private static string Bound(string text,int max){var safe=Security.Redact(text);return safe[..Math.Min(max,safe.Length)];}
}
