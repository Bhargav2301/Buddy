using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Buddy.Server;

var cases=0;var assertions=0;var failed=0;
void Assert(bool condition,string reason){assertions++;if(!condition)throw new Exception(reason);}
void Refuses(Action action){bool refused=false;try{action();}catch(BrowserContextProtocolException){refused=true;}Assert(refused,"Expected a protocol refusal, not success.");}
void Case(string name,Action action){cases++;try{action();Console.WriteLine("PASS "+name);}catch(Exception e){failed++;Console.WriteLine("FAIL "+name+": "+e.GetType().Name+" "+e.Message);}}
var source=Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(a=>a.Key=="BuddySourceRoot").Value!;
var inputs=Directory.GetFiles(Path.Combine(source,"services","Buddy.Server"),"*.cs").Order().ToDictionary(p=>p,p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant());
var registration=new BrowserContextRegistration(1,"register","chatgpt","https://chatgpt.com",61,0,"document-one","generation-one");
byte[] Raw(string text)=>Encoding.UTF8.GetBytes(text);

Case("protocol duplicate property names refused",()=>Refuses(()=>BrowserContextProtocol.ReadRegistration(Raw("{\"version\":1,\"version\":1}"))));
Case("protocol unknown fields refused",()=>Refuses(()=>BrowserContextProtocol.ReadRegistration(Raw("{\"version\":1,\"kind\":\"register\",\"secret\":\"SYNTHETIC\"}"))));
Case("protocol invalid UTF8 refused",()=>Refuses(()=>BrowserContextProtocol.ReadEnvelope(new byte[]{0xff,0x7b,0x7d})));
Case("protocol invalid escaped surrogate refused",()=>Refuses(()=>BrowserContextProtocol.ReadRegistration(Raw("{\"documentId\":\"\\ud800\"}"))));
Case("protocol full-frame bound",()=>Refuses(()=>BrowserContextProtocol.ReadEnvelope(new byte[BrowserContextLimits.MaximumFrameBytes+1])));
Case("real emoji and literal escapes are different exact hashes",()=>Assert(BrowserContextProtocol.TextSha256("🙂")!=BrowserContextProtocol.TextSha256(@"\ud83d\ude42"),"Unicode meaning conflated."));
Case("protocol error omits offending payload",()=>{bool refused=false;try{BrowserContextProtocol.ReadRegistration(Raw("{\"SECRET_SENTINEL\":1}"));}catch(BrowserContextProtocolException e){refused=true;Assert(!e.ToString().Contains("SECRET_SENTINEL"),"Private payload leaked.");}Assert(refused,"Malformed private payload was not refused.");});

foreach(var changed in new[]{registration with{FrameId=1},registration with{TabId=long.MaxValue},registration with{Origin="https://chatgpt.com.attacker.invalid"},registration with{Origin="https://user:password@chatgpt.com"},registration with{Origin="http://chatgpt.com"}})
 Case("registration refuses unsupported frame/origin/unsafe numeric identity "+changed.FrameId+":"+changed.TabId+":"+changed.Origin,()=>{using var b=new BrowserContextBroker();Refuses(()=>b.Register("peer",changed));});

Case("production profile is readiness-only and cannot be enabled by identity evidence",()=>{
 using var f=new Session(false);f.Pair();var reply=f.Send("identity.bind",new BrowserContextIdentityBind(Session.Evidence));
 Assert(reply.Status=="refused"&&reply.Code=="identity_profile_unavailable","Production profile admitted fixture evidence.");
 var s=f.Broker.Snapshots.Single();Assert(!s.ProfileAdmitted&&!s.Capabilities.ReplaceDraft&&s.History is null,"Readiness falsely active.");
});
Case("wrong transport peer cannot use an otherwise valid session token",()=>{
 using var f=new Session();var r=f.Send("identity.bind",new BrowserContextIdentityBind(Session.Evidence),peer:"other-peer");
 Assert(r.Status=="refused"&&r.Code=="session_mismatch","Wrong peer admitted.");Assert(f.Broker.Snapshots.Single().Status=="awaiting-pairing","Unauthenticated request acquired authority.");
});
Case("registration replacement invalidates old connection",()=>{
 using var f=new Session();f.Bind();f.Broker.Register(Session.Peer,registration with{DocumentId="replacement"});
 Assert(f.Send("draft.poll",new BrowserContextEmpty()).Status=="refused","Old session resurrected.");
});
foreach(var field in new[]{"tab","document","generation","account","workspace","chat"})Case("changed "+field+" invalidates authority",()=>{
 using var f=new Session();f.Bind();var changed=field switch{"tab"=>f.Binding with{TabId=62},"document"=>f.Binding with{DocumentId="new-doc"},"generation"=>f.Binding with{Generation="new-generation"},"account"=>f.Binding with{AccountId="new-account"},"workspace"=>f.Binding with{WorkspaceId="new-workspace"},_=>f.Binding with{ConversationId="new-chat"}};
 Assert(f.Send("draft.poll",new BrowserContextEmpty(),changed).Status=="refused","Changed binding accepted.");
 Assert(f.Send("draft.poll",new BrowserContextEmpty()).Status=="refused","Old binding resumed after identity loss.");
});
Case("sequence replay closes authenticated session",()=>{
 using var f=new Session();f.Bind();Assert(f.Send("draft.poll",new BrowserContextEmpty(),sequence:f.Sequence).Code=="sequence_mismatch","Replay accepted.");
 Assert(f.Send("draft.poll",new BrowserContextEmpty()).Status=="refused","Replay failure did not revoke session.");
});
Case("exact selected complete pairs survive multi-chunk history",()=>{
 using var f=new Session();f.Bind();var text=new string('x',34_000)+" Ω🙂";f.Capture([new("u1",text,"a1","Do not publish if any check fails.")]);
 var h=f.Broker.Snapshots.Single().History!;Assert(h.Pairs.Count==1&&h.Pairs[0].UserText==text&&h.Pairs[0].AssistantText=="Do not publish if any check fails.","History changed.");
});
foreach(var bad in new[]{"empty-user","space-assistant","duplicate-id","wrong-count","earlier-unloaded","later-streaming","hash","order","replay-chunk"})Case("history refuses "+bad,()=>{
 using var f=new Session();f.Bind();var pairs=new[]{new BrowserContextHistoryPair(bad=="duplicate-id"?"same":"u1",bad=="empty-user"?"":"Owned user question.",bad=="duplicate-id"?"same":"a1",bad=="space-assistant"?" \r\n":"Owned complete response.")};
 var bytes=JsonSerializer.SerializeToUtf8Bytes(new BrowserContextHistoryDocument(pairs,"","composer-one"),BrowserContextProtocol.Json);
 var coverage=new BrowserContextCoverage(true,bad=="earlier-unloaded",bad=="later-streaming","qa-complete",pairs[0].UserId,pairs[0].AssistantId);
 var begin=f.Send("history.begin",new BrowserContextHistoryBegin("transfer-one",bytes.Length,bad=="hash"?new('0',64):BrowserContextProtocol.Sha256(bytes),bad=="wrong-count"?2:1,coverage));
 var replies=new List<Reply>{begin};
 if(begin.Status=="ok"){
  var chunk=new BrowserContextChunk("transfer-one",bad=="order"?1:0,Convert.ToBase64String(bytes));replies.Add(f.Send("history.chunk",chunk));
  if(bad=="replay-chunk")replies.Add(f.Send("history.chunk",chunk));
  replies.Add(f.Send("history.end",new BrowserContextTransferEnd("transfer-one")));
 }
 Assert(replies.Any(x=>x.Status=="refused"),"Invalid history accepted.");Assert(f.Broker.Snapshots.Single().History is null,"Invalid pair published.");
});
Case("explicit no-Send command surface",()=>{
 using var f=new Session();f.Bind();Assert(f.Send("send",new BrowserContextEmpty()).Code=="unsupported_operation","Send was admitted.");
});
Case("review requires captured original and separate explicit original consent",()=>{
 using var f=new Session();f.Bind();f.Capture();using var a=ContextOriginalAsset.CreateCopy(Raw("Original Ω\r\nbytes"),"owned.txt","text/plain");
 Refuses(()=>f.Broker.PreviewDraft(f.Id,f.Binding,"Different original","Next",[a]));
 var r=f.Broker.PreviewDraft(f.Id,f.Binding,"","Reviewed",[a]);
 Refuses(()=>f.Broker.ApproveDraft(f.Id,r.ReviewId,r.Digest,false));
 Assert(f.Send("draft.poll",new BrowserContextEmpty()).Status=="waiting","Unapproved asset transmitted.");
});
Case("review digest change refuses transfer",()=>{
 using var f=new Session();f.Bind();f.Capture();var r=f.Preview();Refuses(()=>f.Broker.ApproveDraft(f.Id,r.ReviewId,new('0',64),true));
 Assert(f.Send("draft.poll",new BrowserContextEmpty()).Status=="waiting","Changed review emitted offer.");
});
Case("exact original bytes transfer after caller lease is disposed",()=>{
 using var f=new Session();f.Bind();f.Capture();var bytes=Raw("Original\r\nΩ🙂\0NOT OCR");var a=ContextOriginalAsset.CreateCopy(bytes,"original.txt","text/plain");
 var r=f.Preview([a]);a.Dispose();f.Approve(r);var offer=f.Poll();var content=offer.Contents.Single(x=>x.Role=="original");
 var chunk=f.ReadChunk(r.ReviewId,content.ContentId,0);Assert(Convert.FromBase64String(chunk.DataBase64).SequenceEqual(bytes),"Original replaced or lost.");
 Assert(content.Sha256==BrowserContextProtocol.Sha256(bytes)&&chunk.Last,"Original manifest changed.");
});
Case("review offer is one-use and cannot be polled into a second transfer",()=>{
 using var f=new Session();f.Bind();f.Capture();var r=f.Preview();f.Approve(r);f.Poll();Assert(f.Send("draft.poll",new BrowserContextEmpty()).Status=="waiting","Repeated grant returned.");
});
Case("Stop before transfer prevents draft grant",()=>{
 using var f=new Session();f.Bind();f.Capture();var r=f.Preview();f.Approve(r);f.Broker.Cancel(f.Id);
 Assert(f.Send("draft.poll",new BrowserContextEmpty()).Status=="refused","Cancelled review revived.");
});
Case("review owner cancellation revokes old selection",()=>{
 using var f=new Session();f.Bind();f.Capture();using var c=new CancellationTokenSource();var r=f.Broker.PreviewDraft(f.Id,f.Binding,"","Reviewed",[],c.Token);c.Cancel();
 Refuses(()=>f.Broker.ApproveDraft(f.Id,r.ReviewId,r.Digest,true));Assert(f.Broker.Snapshots.Single().History is null,"Cancelled context retained as current.");
});
Case("review expires by monotonic duration even with stationary wall time",()=>{
 using var f=new Session();f.Bind();f.Capture();var r=f.Preview();f.Clock.Milliseconds+=120_000;
 Refuses(()=>f.Broker.ApproveDraft(f.Id,r.ReviewId,r.Digest,true));Assert(f.Broker.Snapshots.Single().Review?.State=="expired","Expired review remained live.");
});
Case("backward wall clock closes authority rather than rebasing",()=>{
 using var f=new Session();f.Bind();f.Clock.Utc=f.Clock.Utc.AddTicks(-1);Refuses(()=>{_=f.Broker.Snapshots;});
 f.Clock.Utc=f.Clock.Utc.AddSeconds(1);Assert(f.Send("draft.poll",new BrowserContextEmpty()).Status=="refused","Clock loss resumed authority.");
});
Case("disconnected transfer cannot publish a stale successful result",()=>{
 using var f=new Session();f.Bind();f.Capture();var r=f.Preview();f.Approve(r);f.Poll();f.Broker.DisconnectPeer(Session.Peer);
 Assert(f.Send("draft.state",new BrowserContextDraftState(r.ReviewId,r.Digest,r.DraftSha256,"composer-one","staged",[])).Status=="refused","Stale success accepted.");
 Assert(f.Broker.Snapshots.Single().Review?.State=="partial-or-unknown","Dispatch uncertainty falsely cleared.");
});
foreach(var state in new[]{"pending","error","removed","wrong-hash"})Case("attachment "+state+" cannot claim complete delivery",()=>{
 using var f=new Session();f.Bind();f.Capture();using var a=ContextOriginalAsset.CreateCopy(Raw("original"),"owned.txt","text/plain");var r=f.Preview([a]);f.Approve(r);var offer=f.Poll();f.ReadAll(offer);
 var asset=offer.Contents.Single(x=>x.Role=="original");var attachment=new BrowserContextAttachmentState(asset.ContentId,asset.Name,asset.MimeType,asset.ByteLength,state=="wrong-hash"?new('0',64):asset.Sha256,state=="wrong-hash"?"ready":state,"receipt-one");
 f.Send("draft.state",new BrowserContextDraftState(r.ReviewId,r.Digest,r.DraftSha256,"composer-one","staged",[attachment]));
 Assert(f.Broker.Snapshots.Single().Review?.State!="staged","Unconfirmed attachment granted success.");
});
Case("successful text-only staging and empty-original Undo use exact one-use chunk",()=>{
 using var f=new Session();f.Bind();f.Capture();var r=f.Preview();f.Approve(r);f.ReadAll(f.Poll());
 Assert(f.Send("draft.state",new BrowserContextDraftState(r.ReviewId,r.Digest,r.DraftSha256,"composer-one","staged",[])).Status=="ok","Exact stage failed.");
 var staged=f.Broker.Snapshots.Single().Review!;Assert(staged.State=="staged"&&staged.ReceiptDigest is not null,"No truthful stage receipt.");
 f.Broker.RequestUndo(f.Id,r.ReviewId,staged.ReceiptDigest!);Assert(f.Send("undo.poll",new BrowserContextEmpty()).Status=="ok","Undo offer unavailable.");
 var chunk=f.ReadChunk(r.ReviewId,"undo-original",0);Assert(chunk.Last&&chunk.DataBase64=="","Empty original corrupted.");
 Assert(f.Send("undo.state",new BrowserContextUndoState(r.ReviewId,staged.ReceiptDigest!,BrowserContextProtocol.TextSha256(""),"composer-one",[])).Status=="ok","Undo not confirmed.");
 Assert(f.Broker.Snapshots.Single().Review?.State=="text-restored","Undo wrong terminal state.");
 Refuses(()=>f.Broker.RequestUndo(f.Id,r.ReviewId,staged.ReceiptDigest!));
});

Case("external production source inventory remains unchanged",()=>{
 var after=Directory.GetFiles(Path.Combine(source,"services","Buddy.Server"),"*.cs").Order().ToDictionary(p=>p,p=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant());
 Assert(inputs.Count==after.Count&&inputs.All(p=>after.TryGetValue(p.Key,out var value)&&value==p.Value),"Source changed during run; receipt not final.");
});
Console.WriteLine(JsonSerializer.Serialize(new{kind="source-receipt",scope="pure injected protocol only; no browser/IPC/model/network",inputs}));
Console.WriteLine($"RESULT cases={cases} assertions={assertions} failed={failed}");return failed==0?0:1;

sealed class TestTime : TimeProvider
{
 public long Milliseconds=1_000;public DateTimeOffset Utc=new(2026,10,7,12,0,0,TimeSpan.Zero);
 public override long TimestampFrequency=>1000;
 public override long GetTimestamp()=>Milliseconds;
 public override DateTimeOffset GetUtcNow()=>Utc;
}
sealed record Reply(string Status,string? Code,JsonElement Payload);
sealed class Session : IDisposable
{
 public const string Peer="qa-owned-peer";
 public static readonly BrowserContextIdentityEvidence Evidence=new("qa-account-signal","qa-workspace-signal","qa-chat-signal","qa-complete","composer-one","qa-v1");
 public readonly TestTime Clock=new();public readonly BrowserContextBroker Broker;
 public readonly BrowserContextRegistrationReply Registration;
 public readonly BrowserContextBinding Binding=new("chatgpt",61,"document-one","generation-one","account-one","workspace-one","chat-one");
 public long Sequence;public string Id=>Registration.ConnectionId;
 public Session(bool admitted=true){Broker=new(admitted?[new("chatgpt","https://chatgpt.com",Evidence,["text/plain","image/png"],new(true,true,true,true,true))]:null,Clock);Registration=Broker.Register(Peer,new(1,"register","chatgpt","https://chatgpt.com",61,0,"document-one","generation-one"));}
 public void Pair()=>Broker.ConfirmPairing(Id,Registration.PairingChallenge,true);
 public void Bind(){Pair();var r=Send("identity.bind",new BrowserContextIdentityBind(Evidence));if(r.Status!="ok")throw new Exception("Fixture binding failed: "+r.Code);}
 public Reply Send(string kind,object payload,BrowserContextBinding? binding=null,string? peer=null,long? sequence=null){
  var frame=new BrowserContextEnvelope(1,Id,Registration.Nonce,sequence??++Sequence,kind,binding??Binding,JsonSerializer.SerializeToElement(payload,BrowserContextProtocol.Json));
  using var reply=JsonDocument.Parse(Broker.HandleUtf8(peer??Peer,BrowserContextProtocol.Serialize(frame)));var json=reply.RootElement;
  return new(json.GetProperty("status").GetString()!,json.GetProperty("code").GetString(),json.GetProperty("payload").Clone());
 }
 public void Capture(BrowserContextHistoryPair[]? pairs=null){
  pairs??=[new("u1","Owned question.","a1","Owned completed answer.")];
  var bytes=JsonSerializer.SerializeToUtf8Bytes(new BrowserContextHistoryDocument(pairs,"","composer-one"),BrowserContextProtocol.Json);
  var replies=new List<Reply>{Send("history.begin",new BrowserContextHistoryBegin("capture",bytes.Length,BrowserContextProtocol.Sha256(bytes),pairs.Length,new(true,false,false,"qa-complete",pairs.FirstOrDefault()?.UserId??"",pairs.LastOrDefault()?.AssistantId??"")))};
  for(int offset=0,index=0;offset<bytes.Length;offset+=BrowserContextLimits.MaximumChunkBytes,index++)replies.Add(Send("history.chunk",new BrowserContextChunk("capture",index,Convert.ToBase64String(bytes,offset,Math.Min(BrowserContextLimits.MaximumChunkBytes,bytes.Length-offset)))));
  replies.Add(Send("history.end",new BrowserContextTransferEnd("capture")));if(replies.Any(r=>r.Status!="ok"))throw new Exception("Fixture capture failed: "+string.Join(',',replies.Select(r=>r.Code)));
 }
 public BrowserContextReviewSnapshot Preview(IReadOnlyList<ContextOriginalAsset>? assets=null)=>Broker.PreviewDraft(Id,Binding,"","Reviewed Ω draft",assets??[]);
 public void Approve(BrowserContextReviewSnapshot r)=>Broker.ApproveDraft(Id,r.ReviewId,r.Digest,true);
 public BrowserContextDraftOffer Poll()=>Send("draft.poll",new BrowserContextEmpty()).Payload.Deserialize<BrowserContextDraftOffer>(BrowserContextProtocol.Json)!;
 public BrowserContextChunkReply ReadChunk(string review,string content,int index)=>Send("draft.chunk.read",new BrowserContextChunkRead(review,content,index)).Payload.Deserialize<BrowserContextChunkReply>(BrowserContextProtocol.Json)!;
 public void ReadAll(BrowserContextDraftOffer offer){foreach(var content in offer.Contents)for(int i=0;;i++){if(ReadChunk(offer.ReviewId,content.ContentId,i).Last)break;}}
 public void Dispose()=>Broker.Dispose();
}
