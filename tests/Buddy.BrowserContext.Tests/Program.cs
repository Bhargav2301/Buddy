using Buddy.Server;
using System.Text;
using System.Text.Json;

internal static class Program
{
    private static int checks;
    private static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
    private static int Main(string[] args)
    {
        if(args.Length>0)return WireFixture.Entry(args);
        try{Readiness();Wire();Capture();Delivery();Failures();Lifetime();Console.WriteLine($"PASS: {checks} browser-context assertions; synthetic bytes/identities/time only. No browser, native, model, account or uploads.");return 0;}
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    private static void Check(bool value,string label){if(!value)throw new Exception("FAIL: "+label);checks++;Console.WriteLine("PASS: "+label);}
    private static void Reject(Action action,string label){try{action();}catch(Exception e)when(e is BrowserContextProtocolException or OperationCanceledException){Check(true,label);return;}throw new Exception("FAIL: "+label);}
    private static string Status(JsonElement response)=>response.GetProperty("status").GetString()!;
    private static string Code(JsonElement response)=>response.GetProperty("code").GetString()!;
    private static byte[] Serialize<T>(T value)=>JsonSerializer.SerializeToUtf8Bytes(value,Json);
    private static BrowserContextHistoryDocument History(string draft="Original fixture draft",string text="Assistant answer with https://example.com/reference?q=x#anchor")=>
        new([new("u1","User request", "a1",text)],draft,"composer-instance");
    private static void Readiness()
    {
        using var broker=new BrowserContextBroker();
        var input=new BrowserContextRegistration(1,"register","chatgpt","https://chatgpt.com",1,0,"doc-a","generation-a");
        var r=broker.Register("host-peer",input);var empty=new BrowserContextBinding("chatgpt",1,"doc-a","generation-a","","","");
        Check(r.Kind=="registered"&&r.Status=="awaiting-pairing"&&r.Nonce.Length==64,"Registration creates a private nonce and awaits visible pairing");
        Check(!r.ToString().Contains(r.Nonce),"Registration diagnostic output redacts the nonce");
        JsonElement Call(long sequence,string kind,object payload,BrowserContextBinding binding){using var d=JsonDocument.Parse(broker.HandleUtf8("host-peer",Serialize(new BrowserContextEnvelope(1,r.ConnectionId,r.Nonce,sequence,kind,binding,JsonSerializer.SerializeToElement(payload,Json)))));return d.RootElement.Clone();}
        var answer=Call(1,"readiness.report",new BrowserContextReadiness(1,2,2,true,true,true),empty);
        Check(Status(answer)=="ok"&&!answer.GetProperty("payload").GetProperty("profileAdmitted").GetBoolean(),"Page readiness claims cannot admit a production identity profile");
        broker.ConfirmPairing(r.ConnectionId,r.PairingChallenge,true);
        Check(broker.Snapshots[0].Status=="readiness-only"&&broker.Snapshots[0].History is null,"Manual pairing never promotes ChatGPT labels to verified identity or captured history");
        answer=Call(2,"identity.bind",new BrowserContextIdentityBind(Fixture.Evidence),empty with{AccountId="observed-label",WorkspaceId="personal",ConversationId="chat"});
        Check(Status(answer)=="refused"&&Code(answer)=="identity_profile_unavailable","Live ChatGPT history/drafts/assets refuse without a host-admitted identity contract");
        Reject(()=>broker.PreviewDraft(r.ConnectionId,empty,"","proposed",[]),"Readiness-only connections cannot prepare drafts");
        Reject(()=>broker.Register("other",input with{FrameId=1}),"Registration refuses subframes");
        Reject(()=>broker.Register("other",input with{TabId=9_007_199_254_740_992}),"Registration refuses unsafe browser numeric identifiers");
        Reject(()=>broker.Register("other",input with{Origin="http://chatgpt.com"}),"Registration refuses a non-HTTPS origin");
        Reject(()=>broker.Register("other",input with{ProviderId="page-injected",Origin="https://example.com"}),"Page strings cannot register another provider profile");
    }
    private static void Wire()
    {
        const string duplicate="{\"version\":1,\"version\":1,\"kind\":\"register\",\"providerId\":\"chatgpt\",\"origin\":\"https://chatgpt.com\",\"tabId\":1,\"frameId\":0,\"documentId\":\"doc\",\"generation\":\"gen\"}";
        Reject(()=>BrowserContextProtocol.ReadRegistration(Encoding.UTF8.GetBytes(duplicate)),"Duplicate JSON properties cannot override bindings");
        Reject(()=>BrowserContextProtocol.ReadRegistration(Encoding.UTF8.GetBytes(duplicate.Replace("\"version\":1,\"version\":1,","\"version\":1,").Replace("\"gen\"","\"\\uD800\""))),"Unpaired Unicode escapes are rejected before hashing");
        Reject(()=>BrowserContextProtocol.ReadRegistration(new byte[]{0xff,0xfe}),"Invalid UTF-8 frames are refused");
        Reject(()=>BrowserContextProtocol.ReadRegistration(new byte[BrowserContextLimits.MaximumFrameBytes+1]),"Oversized wire frames are refused before deserialization");
        Reject(()=>BrowserContextProtocol.ReadRegistration(Encoding.UTF8.GetBytes(duplicate.Replace("\"version\":1,\"version\":1,","\"version\":1,").Replace("\"frameId\":0,",""))),"Missing binding properties do not receive implicit defaults");
        Reject(()=>BrowserContextProtocol.Serialize(new{data=new string('x',BrowserContextLimits.MaximumFrameBytes)}),"Serialized output independently enforces the frame cap");
        using(var f=new Fixture()){
            var bad=f.Call("readiness.report",new BrowserContextReadiness(1,0,0,false,false,false),binding:f.Empty,peer:"spoofed-host-peer");
            Check(Status(bad)=="refused"&&f.Snapshot.Status=="paired","An envelope cannot spoof the host-owned transport peer or poison the rightful session");
            f.Sequence=0;Check(Status(f.Call("identity.bind",new BrowserContextIdentityBind(Fixture.Evidence)))=="ok","The rightful peer still binds after a foreign-peer attempt");
            var replay=f.Call("draft.poll",new BrowserContextEmpty(),sequence:1);
            Check(Status(replay)=="refused"&&f.Snapshot.Status=="cancelled","A repeated sequence invalidates the current session");
        }
        using(var f=new Fixture()){
            var forged=Encoding.UTF8.GetString(Serialize(new BrowserContextEnvelope(1,f.Registration.ConnectionId,f.Registration.Nonce,1,"identity.bind",f.Binding,
                JsonSerializer.SerializeToElement(new{evidence=Fixture.Evidence,admittedProfile=true},Json))));
            var response=f.Raw(Encoding.UTF8.GetBytes(forged));Check(Status(response)=="refused","Wire payload cannot install an admitted identity profile");
        }
        using(var f=new Fixture()){f.Bind();Check(Status(f.Call("send",new BrowserContextEmpty()))=="refused","No Send or Submit operation exists in the browser protocol");}
        using(var f=new Fixture()){f.Bind();var r=f.Call("private untrusted text",new BrowserContextEmpty());Check(r.GetProperty("kind").GetString()=="invalid"&&!r.GetRawText().Contains("private untrusted text"),"Unsupported operation text is never echoed in a refusal");}
    }
    private static void Capture()
    {
        using(var f=new Fixture()){
            f.Bind();string answer="Quoted code \\n remains literal.\n"+new string('λ',40_000);var h=History("draft 😀",answer);f.Capture(h);
            Check(f.Snapshot.History!.Pairs[0].AssistantText==answer&&f.Snapshot.History.OriginalDraft=="draft 😀","Chunked complete history preserves exact Unicode, literal escapes and captured draft");
            Check(f.Snapshot.History.OriginalDraftSha256==BrowserContextProtocol.TextSha256("draft 😀"),"The host computes the original draft hash from the captured exact text");
            Reject(()=>f.Broker.PreviewDraft(f.Registration.ConnectionId,f.Binding,"guessed empty","replacement",[]),"Draft review cannot substitute a guessed original");
            Check(Status(f.Call("history.begin",f.Begin(h)))=="refused","One invocation cannot silently recapture or replace its completed history");
        }
        using(var f=new Fixture()){
            f.Bind();var h=new BrowserContextHistoryDocument([],"","composer-instance");f.Capture(h);
            Check(f.Snapshot.History!.Pairs.Count==0&&f.Snapshot.History.OriginalDraft=="","A proved empty history can capture an exact empty composer");
        }
        foreach(string body in new[]{"","   ","\t"})using(var f=new Fixture()){
            f.Bind();var h=new BrowserContextHistoryDocument([new("u","request","a",body)],"original","composer-instance");
            Check(Status(f.CaptureResult(h))=="refused"&&f.Snapshot.History is null,"Incomplete or whitespace role bodies cannot become full completed history");
        }
        using(var f=new Fixture()){
            f.Bind();var h=History();var begin=f.Begin(h) with{Coverage=new(true,true,false,"history-proof","u1","a1")};
            Check(Status(f.Call("history.begin",begin))=="refused","Rendered pairs with earlier messages still hidden cannot claim complete history");
        }
        using(var f=new Fixture()){
            f.Bind();var h=History();var b=f.Begin(h);f.Call("history.begin",b);
            Check(Status(f.Call("history.chunk",new BrowserContextChunk(b.TransferId,1,Convert.ToBase64String(Serialize(h)))))=="refused","Out-of-order chunks cancel transfer instead of assembling ambiguous history");
        }
        using(var f=new Fixture()){
            f.Bind();var h=History();var b=f.Begin(h) with{Sha256=new string('0',64)};f.Call("history.begin",b);f.Chunks(h,b);
            Check(Status(f.Call("history.end",new BrowserContextTransferEnd(b.TransferId)))=="refused"&&f.Snapshot.History is null,"A completed transfer must match its declared exact digest");
        }
        using(var f=new Fixture()){
            f.Bind();var h=History() with{ComposerId="different-composer"};Check(Status(f.CaptureResult(h))=="refused","Captured original draft must belong to the bound composer");
        }
    }
    private static void Delivery()
    {
        using var f=new Fixture();f.Bind();f.Capture(History(""));
        byte[] bytes=Enumerable.Range(0,70_000).Select(i=>(byte)(i%251)).ToArray();var asset=ContextOriginalAsset.CreateCopy(bytes,"original.png","image/png");
        var review=f.Preview("Reviewed draft 😀",[asset]);asset.Dispose();
        Check(Status(f.Call("draft.poll",new BrowserContextEmpty()))=="waiting","Draft polling exposes no content before explicit review approval");
        Reject(()=>f.Broker.ApproveDraft(f.Registration.ConnectionId,review.ReviewId,review.Digest,false),"Original-file transfer requires explicit review consent");
        f.Broker.ApproveDraft(f.Registration.ConnectionId,review.ReviewId,review.Digest,true);
        var offer=f.Offer();Check(offer.Contents.Count==2&&offer.Contents[1].Role=="original","Approved offer carries exact original metadata and a separate draft manifest");
        Check(Status(f.Call("draft.poll",new BrowserContextEmpty()))=="waiting","A draft approval is consumed once before any browser side effect");
        var received=f.Download(offer);
        Check(received["asset-0"].SequenceEqual(bytes),"Original bytes survive multi-chunk transfer after caller lease disposal");
        Check(Encoding.UTF8.GetString(received["draft"])=="Reviewed draft 😀","Transferred draft bytes match the exact reviewed candidate");
        var pending=f.Attachments(offer,"pending");f.Call("draft.state",new BrowserContextDraftState(offer.ReviewId,offer.Digest,review.DraftSha256,"composer-instance","pending",pending));
        Check(f.Snapshot.Review!.State=="transferring"&&f.Snapshot.ResultCode=="attachments_pending","Pending attachment state never becomes a confirmed staged draft");
        var ready=f.Attachments(offer,"ready");var response=f.Call("draft.state",new BrowserContextDraftState(offer.ReviewId,offer.Digest,review.DraftSha256,"composer-instance","staged",ready));
        Check(Status(response)=="ok"&&f.Snapshot.Review!.State=="staged"&&f.Snapshot.Review.ReceiptDigest is not null,"Exact draft and correlated ready originals create a receipt without sending");
        var receipt=f.Snapshot.Review!;f.Broker.RequestUndo(f.Registration.ConnectionId,receipt.ReviewId,receipt.ReceiptDigest!);
        var undo=f.Call("undo.poll",new BrowserContextEmpty()).GetProperty("payload").Deserialize<BrowserContextUndoOffer>(Json)!;
        Check(Status(f.Call("undo.poll",new BrowserContextEmpty()))=="waiting","Text Undo approval is one-use");
        var empty=f.Call("draft.chunk.read",new BrowserContextChunkRead(review.ReviewId,"undo-original",0)).GetProperty("payload").Deserialize<BrowserContextChunkReply>(Json)!;
        Check(empty.DataBase64==""&&empty.Last&&undo.OriginalDraft.ByteLength==0,"An empty original draft transfers one manifest-bound empty final chunk");
        response=f.Call("undo.state",new BrowserContextUndoState(review.ReviewId,undo.ReceiptDigest,review.OriginalDraftSha256,"composer-instance",ready));
        Check(Status(response)=="ok"&&f.Snapshot.Review!.State=="text-restored"&&f.Snapshot.ResultCode=="text_restored_originals_remain","Undo confirms exact original text and explicitly retains the unchanged attachment set");
    }
    private static void Failures()
    {
        foreach(string field in new[]{"tab","document","generation","account","workspace","conversation"})using(var f=new Fixture()){
            f.Bind();f.Capture(History());var r=f.Preview("candidate",[]);f.Broker.ApproveDraft(f.Registration.ConnectionId,r.ReviewId,r.Digest,false);_=f.Offer();
            var b=field switch{"tab"=>f.Binding with{TabId=2},"document"=>f.Binding with{DocumentId="other-doc"},"generation"=>f.Binding with{Generation="other-generation"},"account"=>f.Binding with{AccountId="other-account"},"workspace"=>f.Binding with{WorkspaceId="other-workspace"},_=>f.Binding with{ConversationId="other-chat"}};
            var response=f.Call("draft.chunk.read",new BrowserContextChunkRead(r.ReviewId,"draft",0),binding:b);
            Check(Status(response)=="refused"&&f.Snapshot.Review!.State=="partial-or-unknown","Changed "+field+" binding cancels with honest possible-effects status");
            Check(f.Snapshot.History is null&&f.Snapshot.Binding is null,"Identity invalidation clears captured private history and binding");
        }
        using(var f=new Fixture()){
            f.Bind();f.Capture(History());using var cancelled=new CancellationTokenSource();var r=f.Preview("candidate",[],cancelled.Token);
            cancelled.Cancel();Check(f.Snapshot.Status=="cancelled"&&f.Snapshot.Review!.State=="cancelled","Context Clear invalidates an unconsumed draft review and session");
            Reject(()=>f.Broker.ApproveDraft(f.Registration.ConnectionId,r.ReviewId,r.Digest,false),"A cleared selection cannot regain transfer authority");
        }
        foreach(string state in new[]{"pending","error","removed"})using(var f=new Fixture()){
            f.Bind();f.Capture(History());using var asset=ContextOriginalAsset.CreateCopy([1,2,3],"note.txt","text/plain");var r=f.Preview("candidate",[asset]);f.Broker.ApproveDraft(f.Registration.ConnectionId,r.ReviewId,r.Digest,true);var offer=f.Offer();f.Download(offer);
            var response=f.Call("draft.state",new BrowserContextDraftState(r.ReviewId,r.Digest,r.DraftSha256,"composer-instance","staged",f.Attachments(offer,state)));
            Check(Status(response)=="refused"&&f.Snapshot.Review!.State=="partial-or-unknown",state+" originals cannot be reported as staged success");
        }
        using(var f=new Fixture()){
            f.Bind();f.Capture(History());var r=f.Preview("candidate",[]);f.Broker.ApproveDraft(f.Registration.ConnectionId,r.ReviewId,r.Digest,false);var offer=f.Offer();f.Download(offer);
            var response=f.Call("draft.state",new BrowserContextDraftState(r.ReviewId,r.Digest,BrowserContextProtocol.TextSha256("user edited"),"composer-instance","staged",[]));
            Check(Status(response)=="refused"&&f.Snapshot.Review!.ReceiptDigest is null,"A user-edited draft cannot produce an exact staged receipt");
        }
        using(var f=new Fixture()){
            f.Bind();f.Capture(History());var r=f.Preview("candidate",[]);f.Broker.ApproveDraft(f.Registration.ConnectionId,r.ReviewId,r.Digest,false);_=f.Offer();f.Broker.DisconnectPeer("host-peer");
            Check(f.Snapshot.Status=="disconnected"&&f.Snapshot.Review!.State=="partial-or-unknown","Port disconnect never falsely claims an issued staging transaction had no effect");
        }
    }
    private static void Lifetime()
    {
        using(var f=new Fixture()){
            f.Bind();f.Capture(History());var r=f.Preview("candidate",[]);f.Clock.Advance(TimeSpan.FromMinutes(2),wall:false);
            Check(f.Snapshot.Review!.State=="expired","Monotonic review expiry works while wall clock stalls");
            Reject(()=>f.Broker.ApproveDraft(f.Registration.ConnectionId,r.ReviewId,r.Digest,false),"Expired review cannot be approved");
        }
        using(var f=new Fixture()){
            f.Bind();f.Capture(History());_=f.Preview("candidate",[]);f.Clock.Wall=f.Clock.Wall.AddSeconds(-1);
            Reject(()=>_=f.Broker.Snapshots,"A backward wall clock invalidates pending session authority");
        }
        using(var f=new Fixture()){
            f.Bind();var old=f.Registration;var replacement=f.Broker.Register("host-peer",Fixture.RegistrationInput with{DocumentId="replacement-doc",Generation="replacement-generation"});
            Check(replacement.Nonce!=old.Nonce&&f.Broker.Snapshots.Count==1,"A new registration on the same host peer replaces the session nonce and old document");
            Check(Status(f.Call("draft.poll",new BrowserContextEmpty()))=="refused","An old connection cannot resume against the replacement session");
        }
    }
    private sealed class Fixture : IDisposable
    {
        internal static readonly BrowserContextIdentityEvidence Evidence=new("account-proof","workspace-proof","conversation-proof","history-proof","composer-instance","fixture-v1");
        internal static readonly BrowserContextRegistration RegistrationInput=new(1,"register","synthetic","https://synthetic.invalid",1,0,"doc-a","generation-a");
        internal readonly ManualTime Clock=new();
        internal readonly BrowserContextBroker Broker;
        internal readonly BrowserContextRegistrationReply Registration;
        internal readonly BrowserContextBinding Binding=new("synthetic",1,"doc-a","generation-a","account-id","workspace-id","conversation-id");
        internal BrowserContextBinding Empty=>Binding with{AccountId="",WorkspaceId="",ConversationId=""};
        internal long Sequence;
        internal BrowserContextSnapshot Snapshot=>Broker.Snapshots.Single();
        internal Fixture(){Broker=new([new("synthetic","https://synthetic.invalid",Evidence,["text/plain","image/png"],new(true,true,true,true,true))],Clock);Registration=Broker.Register("host-peer",RegistrationInput);Broker.ConfirmPairing(Registration.ConnectionId,Registration.PairingChallenge,true);}
        internal void Bind(){if(Status(Call("identity.bind",new BrowserContextIdentityBind(Evidence)))!="ok")throw new Exception("Fixture bind failed.");}
        internal JsonElement Call(string kind,object payload,BrowserContextBinding? binding=null,string peer="host-peer",long? sequence=null)
        {var data=Serialize(new BrowserContextEnvelope(1,Registration.ConnectionId,Registration.Nonce,sequence??++Sequence,kind,binding??Binding,JsonSerializer.SerializeToElement(payload,Json)));return Raw(data,peer);}
        internal JsonElement Raw(byte[] bytes,string peer="host-peer"){var response=Broker.HandleUtf8(peer,bytes);if(response.Length>65_536)throw new Exception("Oversized response.");using var d=JsonDocument.Parse(response);return d.RootElement.Clone();}
        internal BrowserContextHistoryBegin Begin(BrowserContextHistoryDocument h)
        {var bytes=Serialize(h);return new("history-transfer",bytes.Length,BrowserContextProtocol.Sha256(bytes),h.Pairs.Count,new(true,false,false,"history-proof",h.Pairs.FirstOrDefault()?.UserId??"",h.Pairs.LastOrDefault()?.AssistantId??""));}
        internal void Chunks(BrowserContextHistoryDocument h,BrowserContextHistoryBegin b)
        {var bytes=Serialize(h);for(int offset=0,index=0;offset<bytes.Length;offset+=32_768,index++){var part=bytes.AsSpan(offset,Math.Min(32_768,bytes.Length-offset));if(Status(Call("history.chunk",new BrowserContextChunk(b.TransferId,index,Convert.ToBase64String(part))))!="ok")throw new Exception("Fixture chunk failed.");}}
        internal JsonElement CaptureResult(BrowserContextHistoryDocument h){var b=Begin(h);if(Status(Call("history.begin",b))!="ok")throw new Exception("Fixture begin failed.");Chunks(h,b);return Call("history.end",new BrowserContextTransferEnd(b.TransferId));}
        internal void Capture(BrowserContextHistoryDocument h){if(Status(CaptureResult(h))!="ok")throw new Exception("Fixture history failed.");}
        internal BrowserContextReviewSnapshot Preview(string text,IReadOnlyList<ContextOriginalAsset> assets,CancellationToken ct=default)=>Broker.PreviewDraft(Registration.ConnectionId,Binding,Snapshot.History!.OriginalDraft,text,assets,ct);
        internal BrowserContextDraftOffer Offer()=>Call("draft.poll",new BrowserContextEmpty()).GetProperty("payload").Deserialize<BrowserContextDraftOffer>(Json)!;
        internal Dictionary<string,byte[]> Download(BrowserContextDraftOffer offer)
        {
            var result=new Dictionary<string,byte[]>();foreach(var content in offer.Contents){using var bytes=new MemoryStream();int count=Math.Max(1,(content.ByteLength+32_767)/32_768);
                for(int index=0;index<count;index++){var part=Call("draft.chunk.read",new BrowserContextChunkRead(offer.ReviewId,content.ContentId,index)).GetProperty("payload").Deserialize<BrowserContextChunkReply>(Json)!;bytes.Write(Convert.FromBase64String(part.DataBase64));}
                result.Add(content.ContentId,bytes.ToArray());if(BrowserContextProtocol.Sha256(result[content.ContentId])!=content.Sha256)throw new Exception("Fixture digest differs.");}
            return result;
        }
        internal IReadOnlyList<BrowserContextAttachmentState> Attachments(BrowserContextDraftOffer offer,string state)=>offer.Contents.Where(c=>c.Role=="original").Select(c=>new BrowserContextAttachmentState(c.ContentId,c.Name,c.MimeType,c.ByteLength,c.Sha256,state,state=="ready"?"upload-"+c.ContentId:"")).ToArray();
        public void Dispose()=>Broker.Dispose();
    }
    private sealed class ManualTime : TimeProvider
    {
        internal DateTimeOffset Wall=new(2026,10,7,12,0,0,TimeSpan.Zero);
        private long timestamp;
        public override long TimestampFrequency=>TimeSpan.TicksPerSecond;
        public override DateTimeOffset GetUtcNow()=>Wall;
        public override long GetTimestamp()=>timestamp;
        internal void Advance(TimeSpan duration,bool wall=true){timestamp+=duration.Ticks;if(wall)Wall+=duration;}
    }
}
