#if HAS_LIFECYCLE
using Buddy.Server;
using Buddy.Windows;
using System.Diagnostics;
using System.Runtime.CompilerServices;

internal static class LifecycleCases
{
    private static RefinementResult Result(string text) => new(text,"buddy_local","quick","zero-shot",true,.95,70,85,["Reviewed"],[],"Ready for review");
    internal static async Task Run(Func<string,Func<Task>,Task> test,Action<bool,string> check)
    {
        await test("lifecycle: streamed preview without final result cannot enable review",async()=>{
            using var request=new RefinementRequest(TimeSpan.FromSeconds(1)); int previews=0;
            var outcome=await request.Run(PreviewOnly,_=>previews++);
            check(previews==1 && outcome.State==RefinementRequestState.Failed && outcome.ErrorCode=="INCOMPLETE_REFINEMENT" && request.Result is null && !request.IsRunning,"A delta is not a completed refinement.");
        });
        await test("lifecycle: deadline stops an ignored-cancel stream without concurrent disposal",async()=>{
            var stream=new HeldStream(); using var request=new RefinementRequest(TimeSpan.FromMilliseconds(90),heartbeat:TimeSpan.FromMilliseconds(15)); int progress=0,events=0;
            var watch=Stopwatch.StartNew(); var run=request.Run(_=>stream,_=>events++,_=>progress++);
            await stream.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var outcome=await run.WaitAsync(TimeSpan.FromSeconds(2));
            check(outcome.State==RefinementRequestState.TimedOut && outcome.ErrorCode=="REFINE_TIMED_OUT" && outcome.Result is null && !request.IsRunning && watch.Elapsed<TimeSpan.FromSeconds(2),"Timeout must visibly terminate despite uncooperative MoveNext.");
            check(progress>0 && events==0 && stream.Disposals==0,"Elapsed progress is separate from result delivery; pending enumerator must not be concurrently disposed.");
            stream.Release.SetResult(); await stream.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            check(request.Result is null && events==0 && stream.Disposals==1,"Late result must be discarded and enumerator eventually disposed exactly once.");
        });
        await test("lifecycle: Stop and replacement own separate terminal state",async()=>{
            var held=new HeldStream(); using var old=new RefinementRequest(TimeSpan.FromSeconds(2)); int oldEvents=0;
            var previous=old.Run(_=>held,_=>oldEvents++); await held.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2)); old.Cancel();
            var stopped=await previous.WaitAsync(TimeSpan.FromSeconds(2));
            using var current=new RefinementRequest(TimeSpan.FromSeconds(1)); var completed=await current.Run(Finished);
            held.Release.SetResult(); await held.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(2));
            check(stopped.State==RefinementRequestState.Cancelled && old.Result is null && oldEvents==0 && completed.Result?.RefinedPrompt=="current reviewed answer" && current.State==RefinementRequestState.Completed,"Old cleanup cannot overwrite current request state or emit old results.");
        });
        await test("lifecycle: Stop during terminal callback cannot publish an accepted result",async()=>{
            using var request=new RefinementRequest(TimeSpan.FromSeconds(1));
            var outcome=await request.Run(Finished,_=>request.Cancel());
            check(outcome.State==RefinementRequestState.Cancelled && request.Result is null,"Cancellation at the final event must precede Completed publication.");
        });
        await test("lifecycle: unexpected transport cancellation is failure, not a user Stop",async()=>{
            using var request=new RefinementRequest(TimeSpan.FromSeconds(1));
            var outcome=await request.Run(Interrupted);
            check(outcome.State==RefinementRequestState.Failed && outcome.ErrorCode=="REFINE_INTERRUPTED" && outcome.Result is null,"Transport cancellation must leave a truthful terminal failure.");
        });
        await test("lifecycle: service absolute deadline retains inference slot until late worker settles",async()=>{
            using var f=new ModelFixture(); var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var service=new BuddyService(f.Store,new(new HttpClient(f.Model){BaseAddress=new("http://127.0.0.1:11434/")})){RefinementLimits=new(TimeSpan.FromMilliseconds(50),TimeSpan.FromMilliseconds(130))};
            f.Model.BeforeReply=async _=>{entered.TrySetResult();await release.Task;};
            f.Model.Reply=_=>new RefinementContractPlan([new("task",["s0"]),new("subject",["s1"])]);
            var first=service.RefineDetailed(new("Write a poem about a sailing boat."),default);
            try {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
                string firstCode=await Error(first); check(firstCode=="REFINE_TIMED_OUT","Held model response needs a bounded terminal deadline.");
                int before=f.Model.Calls; string nextCode=await Error(service.RefineDetailed(new("Write a poem about a sailing boat."),default));
                check(nextCode=="REFINE_BUSY" && f.Model.Calls==before,"Timeout must not release inference ownership while the old transport is running.");
            } finally {release.TrySetResult();}
            check(await f.Saved()==0,"Timed-out refinement cannot persist conversation/action completion.");
        });
    }
    private static async Task<string> Error(Task<RefinementResult> task)
    {try{await task.WaitAsync(TimeSpan.FromSeconds(2));return "accepted";}catch(BuddyException ex){return ex.Code;}}
    private static async IAsyncEnumerable<RefinementEvent> PreviewOnly([EnumeratorCancellation]CancellationToken ct){await Task.Yield();ct.ThrowIfCancellationRequested();yield return new("delta","preview");}
    private static async IAsyncEnumerable<RefinementEvent> Finished([EnumeratorCancellation]CancellationToken ct){await Task.Yield();ct.ThrowIfCancellationRequested();yield return new("done",Result:Result("current reviewed answer"));}
    private static async IAsyncEnumerable<RefinementEvent> Interrupted([EnumeratorCancellation]CancellationToken ct){await Task.Yield();if(!ct.IsCancellationRequested)throw new OperationCanceledException("owned interrupted transport");yield break;}
    private sealed class HeldStream:IAsyncEnumerable<RefinementEvent>,IAsyncEnumerator<RefinementEvent>
    {
        internal readonly TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously),Release=new(TaskCreationOptions.RunContinuationsAsynchronously),Disposed=new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Disposals; public RefinementEvent Current=>new("done",Result:Result("late stale answer"));
        public IAsyncEnumerator<RefinementEvent> GetAsyncEnumerator(CancellationToken ct=default)=>this;
        public async ValueTask<bool> MoveNextAsync(){Entered.TrySetResult();await Release.Task;return true;}
        public ValueTask DisposeAsync(){Interlocked.Increment(ref Disposals);Disposed.TrySetResult();return ValueTask.CompletedTask;}
    }
}
#endif
