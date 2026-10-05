using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Buddy.Server;

// Default invocation does nothing. Only the root may invoke --owned-ipc; --pure creates no OS pipe.
internal static class Program
{
    private static int checks;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 1 || args[0] is not ("--pure" or "--owned-ipc"))
        { Console.WriteLine("Choose --pure (no IPC) or explicitly authorized --owned-ipc (ephemeral current-user fixture only)."); return 2; }
        try
        {
            if (args[0] == "--pure") await Pure();
            else
            {
                using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                await OwnedIpc(budget.Token).WaitAsync(TimeSpan.FromSeconds(50));
            }
            Console.WriteLine($"PASS {checks} assertions; mode={args[0]}; synthetic session/nonce only, no accounts/hooks/commands/windows/network/audio.");
            return 0;
        }
        catch (Exception ex)
        {
            // Test labels are fixed; never dump envelopes, pairing tokens, or supplied exception details.
            Console.Error.WriteLine("FAIL " + ex.GetType().Name + ": fixture did not complete; no live integration pass is claimed."); return 1;
        }
    }
    private static void Check(bool value, string name)
    { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
    private static void Reject(Action action, string name)
    { try { action(); } catch (InvalidOperationException) { Check(true, name); return; } throw new Exception(name); }
    private static async Task RejectAsync(Func<Task> action, string name, bool cancellation = false)
    {
        try { await action(); }
        catch (OperationCanceledException) when (cancellation) { Check(true, name); return; }
        catch (InvalidOperationException) when (!cancellation) { Check(true, name); return; }
        throw new Exception(name);
    }
    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);
    private static byte[] Decision(string? text = "Answer.", string kind = "answer", string request = "req", string digest = "digest")
        => JsonSerializer.SerializeToUtf8Bytes(new { accepted = true, decision = new { RequestId = request, RequestDigest = digest, Kind = kind, Text = text } });

    private static async Task Pure()
    {
        var broker = new LocalAgentBroker(); await using var server = new LocalAgentPipeServer(broker);
        Check(!server.Started && !server.Running, "constructor never starts a pipe");
        Reject(() => server.Start(false), "listener requires explicit consent");
        LocalAgentPipeClient.ParseAcknowledgement("{\"accepted\":true}"u8.ToArray(), false); Check(true, "exact event acknowledgement");
        foreach (var invalid in new[] { "{}", "null", "{\"accepted\":false}", "{\"accepted\":1}", "{\"accepted\":true,\"unknown\":1}", "{\"accepted\":true,\"accepted\":true}" })
            Reject(() => LocalAgentPipeClient.ParseAcknowledgement(Bytes(invalid), false), "malformed acknowledgement sanitized");
        Check(LocalAgentPipeClient.ParseDecisionResponse(Decision(), "req", "digest")?.Text == "Answer.", "exact decision response");
        Check(LocalAgentPipeClient.ParseDecisionResponse("{\"accepted\":true,\"decision\":null}"u8.ToArray(), "req", "digest") is null, "no pending decision");
        foreach (var invalid in new[] {
            Bytes("{\"accepted\":true}"), Bytes("{\"accepted\":true,\"decision\":{}}"),
            Bytes("{\"accepted\":true,\"decision\":false}"), Decision(null), Decision(""), Decision(" "),
            Decision("Answer.","allow"), Decision("Answer.",request:"different"), Decision("Answer.",digest:"different"),
            Decision(new string('x',2001)), Decision("Bad\0control"),
            Bytes("{\"accepted\":true,\"decision\":{\"RequestId\":\"req\",\"RequestDigest\":\"digest\",\"Kind\":\"answer\",\"Text\":\"ok\",\"Command\":\"ignored data\"}}"),
            Bytes("{\"accepted\":true,\"decision\":{\"RequestId\":\"req\",\"RequestDigest\":\"digest\",\"Kind\":\"answer\",\"Text\":\"ok\",\"Text\":\"shadow\"}}") })
            Reject(() => LocalAgentPipeClient.ParseDecisionResponse(invalid, "req", "digest"), "malformed decision sanitized");
        var lease = broker.Pair("Synthetic fixture", true);
        Reject(() => new LocalAgentPipeClient(server.PipeName, lease with { Token = "short" }), "malformed lease refused before any IPC");
        var first = new LocalAgentEvent(lease.SessionId,1,"started","task","Fixture work.");
        string Handle(LocalAgentEvent ev) => server.Handle(JsonSerializer.SerializeToUtf8Bytes(new { kind="event",token=lease.Token,@event=ev },Json));
        LocalAgentPipeClient.ParseAcknowledgement(Bytes(Handle(first)), false); Check(true,"real serializers interoperate without IPC");
        string digest = LocalAgentBroker.RequestDigest(lease.SessionId,"task","req","question","Which fixture?");
        _=Handle(new(lease.SessionId,2,"question","task","Which fixture?","req",digest));
        broker.Decide(lease.SessionId,"task","req",digest,"answer","Fixture 🚢.\nSecond line.");
        string response=server.Handle(JsonSerializer.SerializeToUtf8Bytes(new { kind="decision",token=lease.Token,sessionId=lease.SessionId,requestId="req",requestDigest=digest }));
        Check(LocalAgentPipeClient.ParseDecisionResponse(Bytes(response),"req",digest)?.Text=="Fixture 🚢.\nSecond line.","Unicode newline response round trip uses actual server/client protocol");
        await server.DisposeAsync();
        Reject(()=>server.Start(true),"disposed server cannot start");
        Check(!server.Running && !broker.Snapshot.Single().Connected,"dispose disconnects leases without listener");
        await LifecyclePublication();
    }
    private static async Task LifecyclePublication()
    {
        // The actual production Start/Dispose methods are exercised with a trusted fake task factory.
        // The factory pauses before task publication: the former unsynchronized Dispose could return here.
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ending=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release=new ManualResetEventSlim(false);
        await using var server=new LocalAgentPipeServer(new LocalAgentBroker(),_=>{
            entered.TrySetResult(); if(!release.Wait(TimeSpan.FromSeconds(3)))throw new TimeoutException(); return ending.Task;
        });
        var starting=Task.Run(()=>server.Start(true)); Task? disposal=null;
        try {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var disposeEntered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            disposal=Task.Run(async()=>{disposeEntered.TrySetResult();await server.DisposeAsync();});
            await disposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(3)); await Task.Delay(30);
            Check(!disposal.IsCompleted,"dispose waits while Start is publishing its listener task");
            release.Set(); await starting.WaitAsync(TimeSpan.FromSeconds(3)); await Task.Delay(30);
            Check(!disposal.IsCompleted,"dispose waits for published listener to finish");
            ending.TrySetResult(); await disposal.WaitAsync(TimeSpan.FromSeconds(3));
            Check(!server.Running,"serialized lifecycle settles without OS pipe");
        } finally {
            release.Set(); ending.TrySetResult();
            await starting.WaitAsync(TimeSpan.FromSeconds(3)); if(disposal is not null)await disposal.WaitAsync(TimeSpan.FromSeconds(3));
        }
    }

    private static async Task OwnedIpc(CancellationToken ct)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
        var broker=new LocalAgentBroker(); await using var server=new LocalAgentPipeServer(broker);
        Check(!server.Started,"owned server remains off before consent"); Reject(()=>server.Start(false),"owned listener rejects absent consent");
        var lease=broker.Pair("Owned ephemeral IPC fixture",true); var client=new LocalAgentPipeClient(server.PipeName,lease);
        server.Start(true); Reject(()=>server.Start(true),"second listener start refused");
        await client.SendAsync(new(lease.SessionId,1,"started","task","Synthetic work."),ct);
        Check(broker.Snapshot.Single().LastSequence==1,"actual current-user pipe delivers first event");
        await RejectAsync(()=>client.SendAsync(new(lease.SessionId,1,"status","task","Replay."),ct),"actual replay refused");
        var wrong=new LocalAgentPipeClient(server.PipeName,lease with {Token=new string('A',64)});
        await RejectAsync(()=>wrong.SendAsync(new(lease.SessionId,2,"status","task","Wrong token."),ct),"actual wrong token refused");
        await RejectAsync(()=>client.SendAsync(new(lease.SessionId,2,"status","other","Wrong task."),ct),"actual wrong task refused");
        await client.SendAsync(new(lease.SessionId,2,"status","task","Still synthetic."),ct);
        var digest=LocalAgentBroker.RequestDigest(lease.SessionId,"task","req","question","Which fixture?");
        await client.SendAsync(new(lease.SessionId,3,"question","task","Which fixture?","req",digest),ct);
        Check(await client.TakeDecisionAsync("req",digest,ct) is null,"actual unanswered question has no fabricated reply");
        broker.Decide(lease.SessionId,"task","req",digest,"answer","Synthetic 🚢 reply.\nSecond line.");
        await RejectAsync(()=>client.TakeDecisionAsync("wrong",digest,ct),"actual reply request binding");
        Check((await client.TakeDecisionAsync("req",digest,ct))?.Text=="Synthetic 🚢 reply.\nSecond line.","actual exact Unicode reply round trip");
        Check(await client.TakeDecisionAsync("req",digest,ct) is null,"actual reply delivered at most once");
        digest=LocalAgentBroker.RequestDigest(lease.SessionId,"task","approval","approval","Synthetic command description; never executed.");
        await client.SendAsync(new(lease.SessionId,4,"approval","task","Synthetic command description; never executed.","approval",digest),ct);
        Reject(()=>broker.Decide(lease.SessionId,"task","approval",digest,"allow"),"no allow decision or executable callback");
        broker.Decide(lease.SessionId,"task","approval",digest,"deny");
        Check((await client.TakeDecisionAsync("approval",digest,ct))?.Kind=="denied","actual denial round trip");
        using(var canceled=new CancellationTokenSource()){
            canceled.Cancel(); await RejectAsync(()=>client.SendAsync(new(lease.SessionId,5,"status","task","Cancelled."),canceled.Token),"pre-canceled client refuses",true);
        }
        Check(broker.Snapshot.Single().LastSequence==4,"pre-cancel does not dispatch a new event");
        broker.Disconnect(lease.SessionId);
        await RejectAsync(()=>client.SendAsync(new(lease.SessionId,5,"status","task","Disconnected."),ct),"actual disconnected token refused");
        var malformed=await Raw(server.PipeName,Bytes("{\"kind\":\"event\",\"token\":\"synthetic\"}\n"),ct);
        Check(malformed.Contains("false",StringComparison.Ordinal),"malformed complete frame receives bounded refusal");
        await Raw(server.PipeName,Bytes(new string('x',16385)+"\n"),ct,allowClosed:true);
        Check(server.Running,"oversized client frame does not terminate listener");
        var watch=Stopwatch.StartNew();
        string empty=await Raw(server.PipeName,Bytes("{\"incomplete\":"),ct,allowClosed:true);
        Check(empty.Length==0&&watch.Elapsed<TimeSpan.FromSeconds(7),"partial frame closes within bounded server timeout");
        var recovery=broker.Pair("Recovery fixture",true); var recovered=new LocalAgentPipeClient(server.PipeName,recovery);
        await recovered.SendAsync(new(recovery.SessionId,1,"started","recovery","Recovered."),ct);
        Check(broker.Snapshot.Single(s=>s.SessionId==recovery.SessionId).LastSequence==1,"listener accepts a fresh session after malformed timeout");
        await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(4),ct);
        Check(!server.Running&&broker.Snapshot.All(s=>!s.Connected),"disposing listener closes all owned sessions");
        await ClientTimeoutAndCancellation(ct);
        await MalformedResponse(ct);
        await DisposeDuringPartialFrame(ct);
    }
    private static async Task<string> Raw(string name,byte[] payload,CancellationToken ct,bool allowClosed=false)
    {
        using var bound=CancellationTokenSource.CreateLinkedTokenSource(ct);bound.CancelAfter(TimeSpan.FromSeconds(7));
        await using var pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(bound.Token);var response=new List<byte>();var one=new byte[1];
        try{
            await pipe.WriteAsync(payload,bound.Token);await pipe.FlushAsync(bound.Token);
            while(response.Count<16385){if(await pipe.ReadAsync(one,bound.Token)==0)break;if(one[0]==(byte)'\n')break;response.Add(one[0]);}
        }catch(IOException)when(allowClosed){}
        finally{Array.Clear(payload);}
        return Encoding.UTF8.GetString(response.ToArray());
    }
    private static async Task ClientTimeoutAndCancellation(CancellationToken ct)
    {
        var broker=new LocalAgentBroker();var lease=broker.Pair("Absent owned fixture",true);
        var client=new LocalAgentPipeClient("Buddy.LocalAgent."+Guid.NewGuid().ToString("N"),lease);
        var watch=Stopwatch.StartNew();
        await RejectAsync(()=>client.SendAsync(new(lease.SessionId,1,"started","task","Absent."),ct),"absent owned server reaches bounded client timeout");
        Check(watch.Elapsed<TimeSpan.FromSeconds(7),"client timeout bounded");
        using var cancel=CancellationTokenSource.CreateLinkedTokenSource(ct);cancel.CancelAfter(TimeSpan.FromMilliseconds(80));watch.Restart();
        await RejectAsync(()=>client.SendAsync(new(lease.SessionId,1,"started","task","Cancelled."),cancel.Token),"client cancellation while connecting",true);
        Check(watch.Elapsed<TimeSpan.FromSeconds(3),"client cancellation prompt");
    }
    private static async Task MalformedResponse(CancellationToken ct)
    {
        string name="Buddy.LocalAgent."+Guid.NewGuid().ToString("N");
        await using var fake=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        using var bound=CancellationTokenSource.CreateLinkedTokenSource(ct);bound.CancelAfter(TimeSpan.FromSeconds(5));
        var serving=Task.Run(async()=>{
            await fake.WaitForConnectionAsync(bound.Token);var one=new byte[1];int count=0;
            while(++count<=16384&&await fake.ReadAsync(one,bound.Token)>0)if(one[0]==(byte)'\n')break;
            await fake.WriteAsync(Decision(null),bound.Token);await fake.WriteAsync("\n"u8.ToArray(),bound.Token);await fake.FlushAsync(bound.Token);
        },bound.Token);
        var lease=new LocalAgentBroker().Pair("Malformed reply fixture",true);var client=new LocalAgentPipeClient(name,lease);
        try{await RejectAsync(()=>client.TakeDecisionAsync("req","digest",bound.Token),"actual malformed reply produces sanitized refusal");}
        finally{await serving.WaitAsync(TimeSpan.FromSeconds(5),ct);}
    }
    private static async Task DisposeDuringPartialFrame(CancellationToken ct)
    {
        var broker=new LocalAgentBroker();await using var server=new LocalAgentPipeServer(broker);server.Start(true);
        await using var raw=new NamedPipeClientStream(".",server.PipeName,PipeDirection.InOut,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
        await raw.ConnectAsync(ct);await raw.WriteAsync("{"u8.ToArray(),ct);await raw.FlushAsync(ct);
        await server.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(4),ct);
        Check(!server.Running,"dispose interrupts incomplete client frame and settles listener");
    }
}
