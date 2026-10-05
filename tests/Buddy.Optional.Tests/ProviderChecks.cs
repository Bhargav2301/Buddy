using Buddy.Server;
using System.Text;
using System.Text.Json;

internal static class ProviderChecks
{
    internal static void Run(Action<bool,string> check)
    {
        byte[] Bytes(object value)=>JsonSerializer.SerializeToUtf8Bytes(value);
        void Reject(Action action,string note){try{action();}catch(Exception e)when(e is InvalidOperationException or OperationCanceledException){check(true,note);return;}throw new Exception("FAIL: "+note);}
        check(!ProviderProtocols.Connected,"Optional provider protocols remain disconnected with no transport or credentials");
        foreach(var provider in ProviderProtocols.Providers){var p=ProviderProtocols.Build(provider,"fixture-model","Explain the fixture.");using var json=JsonDocument.Parse(p.Json);check(p.Endpoint.Scheme=="https"&&p.Json.Contains("Explain the fixture.")&&!p.Json.Contains("api_key")&&!p.Json.Contains("audio"),provider+" encodes bounded text-only requests without credentials");}
        var chat=Bytes(new{choices=new[]{new{finish_reason="stop",message=new{role="assistant",content="Keep the original. Check the copy."}}}});
        check(ProviderProtocols.Parse("openai",chat)=="Keep the original. Check the copy."&&ProviderProtocols.Parse("openrouter",chat).Contains("Check the copy"),"OpenAI-compatible adapters preserve the complete qualified answer");
        check(ProviderProtocols.Parse("anthropic",Bytes(new{stop_reason="end_turn",content=new[]{new{type="text",text="Check the copy."}}}))=="Check the copy.","Anthropic adapter accepts complete text blocks");
        check(ProviderProtocols.Parse("gemini",Bytes(new{candidates=new[]{new{finishReason="STOP",content=new{parts=new[]{new{text="Check the copy."}}}}}}))=="Check the copy.","Gemini adapter accepts a completed text candidate");
        Reject(()=>ProviderProtocols.Parse("openai",Bytes(new{choices=new[]{new{finish_reason="length",message=new{content="Unsafe partial"}}}})),"Truncated provider response is refused whole");
        Reject(()=>ProviderProtocols.Parse("anthropic",Bytes(new{stop_reason="tool_use",content=Array.Empty<object>()})),"Provider tool requests never become actions");
        Reject(()=>ProviderProtocols.Parse("gemini",Bytes(new{candidates=new[]{new{finishReason="SAFETY",content=new{parts=new[]{new{text="Blocked"}}}}}})),"Blocked provider output is not surfaced as a valid answer");
        Reject(()=>ProviderProtocols.Parse("openai",new byte[65537]),"Oversized response is bounded before JSON parsing");
        Reject(()=>ProviderProtocols.Build("gemini","../../outside","question"),"Model identifiers cannot replace a provider endpoint path");
        using var cancel=new CancellationTokenSource();cancel.Cancel();Reject(()=>ProviderProtocols.Parse("openai",chat,cancel.Token),"Cancelled protocol parsing yields no answer");
        var realtime=new RealtimeTextProtocol();realtime.Begin("fixture-response");
        var delta=Bytes(new{type="response.output_text.delta",event_id="one",response_id="fixture-response",delta="Keep the original. "});
        check(realtime.Receive(delta) is null&&realtime.Receive(delta) is null,"Realtime deltas stay provisional and duplicate event IDs are ignored");
        realtime.Receive(Bytes(new{type="response.output_text.delta",event_id="two",response_id="fixture-response",delta="Check the copy."}));
        check(realtime.Receive(Bytes(new{type="response.done",response=new{id="fixture-response",status="completed"}}))=="Keep the original. Check the copy.","Realtime emits a complete qualified answer only on matching completed response");
        realtime.Begin("new-response");check(realtime.Receive(delta) is null,"Old response events cannot overwrite a new realtime request");
        check(realtime.Cancel()!.Contains("response.cancel")&&realtime.Receive(delta) is null,"Realtime cancellation clears text and suppresses late output");
        realtime.Begin("fixture-response");Reject(()=>realtime.Receive(Bytes(new{type="response.output_audio.delta",response_id="fixture-response",delta="not audio"})),"Cloud audio events are explicitly disabled");
        realtime.Begin("fixture-response");realtime.Receive(delta);Reject(()=>realtime.Receive(Bytes(new{type="response.done",response=new{id="fixture-response",status="cancelled"}})),"Cancelled realtime completion cannot promote a partial answer");
    }
}
