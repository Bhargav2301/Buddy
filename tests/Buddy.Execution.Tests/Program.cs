using Buddy.Server;
int count=0;void Check(bool ok,string label){if(!ok)throw new Exception("FAIL: "+label);count++;Console.WriteLine("PASS: "+label);}
var write=new AssistantAction("type",Role:"Edit",Value:"Reviewed draft",Description:"Insert the approved draft");
var navigate=new AssistantAction("open",Value:"notepad",Description:"Open approved Notepad");
Check(ExecutionSpecialists.Role(write)=="Writer"&&ExecutionSpecialists.Role(navigate)=="Navigator","Assignments select distinct writer and navigation capabilities");
using(var run=new ExecutionSpecialists()){
 int active=0,peak=0,effects=0;
 async Task<ActionResult?> Dispatch(AssistantAction action,int sequence,CancellationToken ct){var n=Interlocked.Increment(ref active);peak=Math.Max(peak,n);try{await Task.Delay(25,ct);effects++;return new(sequence,action,true,"Observed fixture effect");}finally{Interlocked.Decrement(ref active);}}
 await Task.WhenAll(run.Dispatch(write,1,t=>Dispatch(write,1,t),default),run.Dispatch(navigate,2,t=>Dispatch(navigate,2,t),default));
 Check(peak==1&&effects==2,"Two real mutation assignments execute serially even when queued together");
 Check(run.Snapshot.Select(j=>j.Specialist).SequenceEqual(new[]{"Writer","Navigator"})&&run.Snapshot.All(j=>j.State=="dispatch receipt returned"),"Execution jobs retain distinct roles and their actual returned receipts");
 int entered=0,readPeak=0;var both=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
 async Task<int> Read(CancellationToken ct){int n=Interlocked.Increment(ref entered);readPeak=Math.Max(readPeak,n);if(n==2)both.TrySetResult();await both.Task.WaitAsync(ct);await Task.Delay(20,ct);Interlocked.Decrement(ref entered);return n;}
 await Task.WhenAll(run.Read("Result verifier",2,Read,default),run.Read("Teacher",2,Read,default));
 Check(readPeak==2,"Read-only result verification and teaching jobs genuinely overlap");
}
using(var run=new ExecutionSpecialists()){
 int effects=0;var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);using var cancel=new CancellationTokenSource();
 var first=run.Dispatch(write,1,async ct=>{entered.TrySetResult();await Task.Delay(30000,ct);effects++;return new(1,write,true,"Unexpected");},cancel.Token);
 await entered.Task;var queued=run.Dispatch(navigate,2,ct=>{effects++;return Task.FromResult<ActionResult?>(new(2,navigate,true,"Unexpected"));},cancel.Token);cancel.Cancel();
 try{await Task.WhenAll(first,queued);}catch(OperationCanceledException){}
 Check(effects==0&&run.Snapshot.All(j=>j.State.StartsWith("cancelled")),"Cancel-all prevents queued mutations and stops the active assignment");
}
using(var run=new ExecutionSpecialists()){
 int called=0;try{await run.Dispatch(write,1,ct=>{called++;throw new TimeoutException("Uncertain effect");},default);}catch(TimeoutException){}
 Check(called==1&&run.Snapshot.Single().State=="failed; no automatic replay","Uncertain dispatch is reported once and never automatically retried");
 var declined=await run.Dispatch(write,2,_=>Task.FromResult<ActionResult?>(null),default);
 Check(declined is null&&run.Snapshot.Last().State=="declined","Declining approval yields no success receipt");
 bool invalid=false;try{await run.Dispatch(write,3,_=>Task.FromResult<ActionResult?>(new(99,write,true,"wrong receipt")),default);}catch(InvalidOperationException){invalid=true;}
 Check(invalid,"A worker cannot substitute another assignment's receipt");
}
using(var run=new ExecutionSpecialists()){
 for(int i=1;i<=25;i++){int sequence=i;await run.Dispatch(write,i,_=>Task.FromResult<ActionResult?>(new(sequence,write,true,"fixture")),default);}
 bool bounded=false;try{await run.Dispatch(write,26,_=>Task.FromResult<ActionResult?>(null),default);}catch(InvalidOperationException){bounded=true;}
 Check(bounded&&run.Snapshot.Count==25,"Action budget remains 25 across specialist jobs");
}
Console.WriteLine($"ALL {count} EXECUTION SPECIALIST CHECKS PASSED");
