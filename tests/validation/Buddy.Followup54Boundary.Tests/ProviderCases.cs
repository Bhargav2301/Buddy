#if HAS_PIPE54
using Buddy.Server;
using System.Text;
using System.Text.Json;

internal static class ProviderCases {
    private static byte[] Bytes(string value)=>Encoding.UTF8.GetBytes(value);
    internal static async Task Run(Checks c) {
        const string digest="0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
        byte[] Decision(string kind="answer",string id="request-a",string d=digest,string text="Canned reply 😀") => JsonSerializer.SerializeToUtf8Bytes(new{accepted=true,decision=new{RequestId=id,RequestDigest=d,Kind=kind,Text=text}});
        var answer=LocalAgentPipeClient.ParseDecisionResponse(Decision(),"request-a",digest);
        c.Check(answer?.Text=="Canned reply 😀"&&answer.Kind=="answer","PIPE-EXACT-REPLY","Exact Unicode text survives bounded response parsing.");
        c.Check(LocalAgentPipeClient.ParseDecisionResponse(Bytes("{\"accepted\":true,\"decision\":null}"),"request-a",digest) is null,"PIPE-PENDING","A pending reply remains pending, without a fabricated answer.");
        foreach(var (id,payload) in new[]{
            ("WRONG-ID",Decision(id:"request-b")),("WRONG-DIGEST",Decision(d:new string('b',64))),
            ("ALLOW",Decision(kind:"allow")),("EXECUTE",Decision(kind:"execute")),("EMPTY",Decision(text:" ")),
            ("LONG",Decision(text:new string('x',2001))), ("CONTROL",Decision(text:"a\0b")),
            ("EXTRA",Bytes("{\"accepted\":true,\"decision\":null,\"command\":\"run\"}")),
            ("DUPLICATE",Bytes("{\"accepted\":true,\"accepted\":false,\"decision\":null}")),
            ("MALFORMED",Bytes("{\"accepted\":true,\"decision\":")),
            ("WRONG-CASE",Bytes("{\"Accepted\":true,\"decision\":null}"))})
            c.Rejects<InvalidOperationException>(()=>LocalAgentPipeClient.ParseDecisionResponse(payload,"request-a",digest),"PIPE-"+id,"Unbound, executable, malformed or excessive responses are refused.");
        c.Check(LocalAgentPipeClient.ParseDecisionResponse(Decision(kind:"denied"),"request-a",digest)?.Kind=="denied","PIPE-DENIAL","Denial is display data, not approval to execute.");
        c.Rejects<InvalidOperationException>(()=>LocalAgentPipeClient.ParseAcknowledgement(Bytes("{\"accepted\":true,\"decision\":null}"),false),"PIPE-ACK-SHAPE","Event acknowledgements cannot smuggle a decision.");
        var broker=new LocalAgentBroker(); var lease=broker.Pair("Canned local adapter",true);
        int factories=0; var ended=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); CancellationToken owned=default;
        await using var server=new LocalAgentPipeServer(broker,token=>{factories++;owned=token;return ended.Task;});
        c.Check(!server.Started&&!server.Running&&factories==0,"PIPE-OFF-DEFAULT","Construction does not start a listener or injected operation.");
        c.Rejects<InvalidOperationException>(()=>server.Start(false),"PIPE-CONSENT","Explicit local consent is required.");
        c.Check(factories==0,"PIPE-REFUSAL-NO-DISPATCH","Refusing consent invokes no listener factory.");
        server.Start(true);
        c.Check(factories==1&&server.Running,"PIPE-ONE-OWNER","Explicit start owns exactly one injected non-IPC task.");
        c.Rejects<InvalidOperationException>(()=>server.Start(true),"PIPE-NO-RESTART","Repeated start cannot create a competing listener.");
        var dispose=server.DisposeAsync().AsTask();
        c.Check(owned.IsCancellationRequested&&!server.Running&&!dispose.IsCompleted,"PIPE-DRAIN","Disposal cancels promptly but waits for an ignored-cancel injected task to settle.");
        c.Check(!broker.Snapshot.Single().Connected,"PIPE-CLOSE-PAIRING","Dispose revokes paired sessions before the old listener finishes.");
        ended.SetResult(); await dispose.WaitAsync(TimeSpan.FromSeconds(2));
        c.Rejects<InvalidOperationException>(()=>server.Start(true),"PIPE-DISPOSED","Disposed listener cannot restart.");
        var client=new LocalAgentPipeClient(server.PipeName,lease);
        c.Check(!client.ToString().Contains(lease.Token,StringComparison.Ordinal),"PIPE-REDACT","Construction-only client text does not expose the pairing token.");
        c.Rejects<InvalidOperationException>(()=>new LocalAgentPipeClient("external.pipe",lease),"PIPE-NAMESPACE","Arbitrary pipe destinations are not accepted.");
    }
}
#endif
