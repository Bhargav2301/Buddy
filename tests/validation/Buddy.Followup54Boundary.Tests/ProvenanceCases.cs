#if HAS_FILE_CONTEXT
using Buddy.Server;
using Microsoft.AspNetCore.DataProtection;
using System.Net;
using System.Text;
using System.Text.Json;

internal static class ProvenanceCases {
    internal static async Task Run(Checks c) {
        using var f=new Fixture(); var chat=await f.Service.CreateConversation("Owned QA54 synthetic state");
        var request=new ChatRequest(chat.Id,"Explain the word pebble.",Guid.NewGuid().ToString(),Context:"CANNED_REVIEWED_FILE_ALPHA 😀",ContextKind:"file",ScreenApp:"ignored-for-file");
        var result=await f.Complete(request);
        c.Check(result.Last().Type=="done"&&result.Last(e=>e.Type=="evidence").Evidence is{File:true,Screen:false,Image:false,App:null},"CONTEXT-FILE-EVIDENCE","A reviewed text attachment is labelled file, never captured screen.");
        using(var body=JsonDocument.Parse(f.Http.Bodies.Single())) {
            string sent=body.RootElement.GetProperty("messages").EnumerateArray().Last().GetProperty("content").GetString()!;
            c.Check(sent.Contains("<untrusted_file_context>",StringComparison.Ordinal)&&sent.Contains(request.Context!,StringComparison.Ordinal)&&!sent.Contains("<untrusted_screen_context>",StringComparison.Ordinal),"CONTEXT-UNTRUSTED","Actual mock engine payload preserves exact reviewed text in untrusted file context.");
        }
        var stored=await f.Store.Read(s=>s.Conversations.Single().Messages);
        c.Check(stored.All(m=>!m.Text.Contains(request.Context!,StringComparison.Ordinal))&&stored.Single(m=>m.Role=="user").ContextFingerprint is{Length:64},"CONTEXT-NO-RAW-PERSIST","Replay binding keeps a digest rather than raw attachment text in saved messages.");
        int calls=f.Http.Bodies.Count; await f.Complete(request);
        c.Check(f.Http.Bodies.Count==calls&&await f.Store.Read(s=>s.Conversations.Single().Messages.Count)==2,"CONTEXT-EXACT-REPLAY","Identical reviewed context replays without inference or duplicate history.");
        foreach(var (id,changed) in new[]{
            ("REPLACED-FILE",request with{Context="CANNED_REVIEWED_FILE_BETA 😀"}),
            ("FILE-SCREEN",request with{ContextKind="screen"}),
            ("FILE-REMOVED",request with{Context=null}),
            ("KIND-UNKNOWN",request with{ContextKind="cloud"}),
            ("FILE-IMAGE",request with{ImageBase64="AQ=="})}) {
            await c.RejectsAsync<BuddyException>(()=>f.Complete(changed),"CONTEXT-"+id,"Changed or invalid reviewed context cannot replay the prior answer.");
            c.Check(f.Http.Bodies.Count==calls,"CONTEXT-"+id+"-NO-MODEL","Context conflict refuses before model dispatch.");
        }
        await f.Store.Update(s=>{
            var messages=s.Conversations.Single().Messages;int i=messages.FindIndex(m=>m.Role=="user");messages[i]=messages[i] with{ContextFingerprint=null};return true;
        });
        await c.RejectsAsync<BuddyException>(()=>f.Complete(request),"CONTEXT-LEGACY-ATTACHMENT","Legacy attachment evidence without a binding digest cannot replay unknown context.");
        var noContext=request with{RequestId=Guid.NewGuid().ToString(),Context=null,ContextKind=null,ScreenApp=null};await f.Complete(noContext);
        calls=f.Http.Bodies.Count;await f.Complete(noContext);
        c.Check(f.Http.Bodies.Count==calls,"CONTEXT-NONE-IDEMPOTENT","Plain text without attachment preserves legacy exact replay.");
        await c.RejectsAsync<BuddyException>(()=>f.Complete(noContext with{Context="new attachment",ContextKind="file"}),"CONTEXT-NONE-TO-FILE","A former plain-text request cannot acquire an attachment on replay.");
        var image=request with{RequestId=Guid.NewGuid().ToString(),Context=null,ContextKind=null,ImageBase64="AQ==",ScreenApp="Owned Image App"};await f.Complete(image);calls=f.Http.Bodies.Count;
        await c.RejectsAsync<BuddyException>(()=>f.Complete(image with{ImageBase64=null,Context="same subject as text"}),"CONTEXT-IMAGE-TO-TEXT","Image and text attachments have distinct request ownership.");
        await c.RejectsAsync<BuddyException>(()=>f.Complete(image with{ImageBase64="Ag=="}),"CONTEXT-IMAGE-REPLACED","Changed image bytes cannot reuse the old answer.");
        c.Check(f.Http.Bodies.Count==calls,"CONTEXT-IMAGE-NO-MODEL","Image replay conflicts cause no new dispatch.");
        var screen=request with{RequestId=Guid.NewGuid().ToString(),ContextKind=null,ScreenApp="Owned Screen App"};await f.Complete(screen);calls=f.Http.Bodies.Count;
        await f.Complete(screen with{ContextKind="screen"});
        c.Check(f.Http.Bodies.Count==calls,"CONTEXT-SCREEN-NORMALIZED","Legacy screen kind and explicit screen kind bind the same effective source.");
        await c.RejectsAsync<BuddyException>(()=>f.Complete(screen with{ScreenApp="Another App"}),"CONTEXT-SCREEN-APP","The claimed screen app is part of reviewed context ownership.");
        using var stop=new CancellationTokenSource(); stop.Cancel();
        await c.RejectsAsync<OperationCanceledException>(()=>f.Complete(screen,stop.Token),"CONTEXT-CANCEL-REPLAY","Precancelled stored replay yields no stale content.");
    }
    private sealed class Fixture:IDisposable {
        private readonly string path=Path.Combine(Path.GetTempPath(),"buddy-qa54-"+Guid.NewGuid().ToString("N"));
        private readonly HttpClient client;
        internal readonly MockHandler Http=new(); internal StateStore Store{get;} internal BuddyService Service{get;}
        internal Fixture(){Store=new(path,new EphemeralDataProtectionProvider());client=new(Http){BaseAddress=new("http://127.0.0.1:11434/")};Service=new(Store,new OllamaEngine(client));}
        internal async Task<List<StreamEvent>> Complete(ChatRequest request,CancellationToken token=default){var events=new List<StreamEvent>();await foreach(var item in Service.Chat(request,token))events.Add(item);return events;}
        public void Dispose(){client.Dispose();Directory.Delete(path,true);}
    }
    private sealed class MockHandler:HttpMessageHandler {
        internal List<string> Bodies{get;}=[];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){
            ct.ThrowIfCancellationRequested();if(request.RequestUri?.AbsolutePath!="/api/chat")throw new InvalidOperationException("Unexpected mock engine route.");
            Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            return new(HttpStatusCode.OK){Content=new StringContent("{\"message\":{\"role\":\"assistant\",\"content\":\"A pebble is a small stone.\"},\"done\":true,\"done_reason\":\"stop\"}\n",Encoding.UTF8,"application/x-ndjson")};
        }
    }
}
#endif
