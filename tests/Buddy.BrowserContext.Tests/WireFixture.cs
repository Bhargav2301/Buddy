using Buddy.Server;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

// Test executable only. No production caller, configurable profiles, paths, IPC,
// network, browser, or model. The named CLI mode is the only auto-approval path.
internal static class WireFixture
{
    private const string Peer="owned-cross-language-fixture";
    private const string Original="Owned original \u03a9";
    private const string Replacement="Reviewed prompt \u03a9\r\nKeep 4 numbered checks.";
    private const string AssetText="Original\r\n\u03a9\U0001f642\0bytes";
    private static readonly JsonSerializerOptions Json=new(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase};
    private static readonly BrowserContextRegistration Registration=new(1,"register","chatgpt","https://chatgpt.com",41,0,"doc-qa","qa-generation");
    private static readonly BrowserContextBinding Binding=new("chatgpt",41,"doc-qa","qa-generation","qa-account","qa-workspace","qa-conversation");
    private static readonly BrowserContextIdentityEvidence Evidence=new("qa-account-signal","qa-workspace-signal","qa-chat-signal","qa-complete","composer-1","qa-v1");
    private static readonly BrowserContextHistoryPair Pair=new("u1","Keep 4 numbered checks.","a1","Do not publish without review.");
    internal static int Entry(string[] args)
    {
        try{
            if(args.Length!=1)throw new InvalidOperationException();
            switch(args[0]){
                case "--wire-fixture-info":Console.WriteLine(JsonSerializer.Serialize(new{
                    registration=Registration,binding=Binding,evidence=Evidence,
                    history=new BrowserContextHistoryDocument([Pair],Original,Evidence.ComposerId),
                    replacement=Replacement,original=new{name="owned.txt",mimeType="text/plain",dataBase64=Convert.ToBase64String(Encoding.UTF8.GetBytes(AssetText))},
                    frameBytes=BrowserContextLimits.MaximumFrameBytes,modes=new[]{"--wire-fixture","--wire-fixture-empty-original"}
                },Json));return 0;
                case "--wire-fixture-self-test":SelfTest();return 0;
                case "--wire-fixture":return Run(Console.OpenStandardInput(),Console.OpenStandardOutput(),false);
                case "--wire-fixture-empty-original":return Run(Console.OpenStandardInput(),Console.OpenStandardOutput(),true);
                default:throw new InvalidOperationException();
            }
        }catch{Console.Error.WriteLine("Synthetic wire fixture refused its mode or stream.");return 1;}
    }
    private static int Run(Stream input,Stream output,bool emptyOriginal)
    {
        using var fixture=new Session(emptyOriginal);
        while(ReadFrame(input) is {} frame){
            try{var reply=fixture.Process(frame);try{WriteFrame(output,reply);}finally{CryptographicOperations.ZeroMemory(reply);}}
            finally{CryptographicOperations.ZeroMemory(frame);}
        }
        return 0;
    }
    private sealed class Session : IDisposable
    {
        private readonly BrowserContextBroker broker=new([new("chatgpt","https://chatgpt.com",Evidence,["text/plain"],new(true,true,true,true,true))]);
        private readonly string original;
        private string? connection;
        private bool prepared,undoRequested;
        internal Session(bool emptyOriginal){original=emptyOriginal?"":Original;}
        internal byte[] Process(byte[] frame)
        {
            if(connection is null){
                var input=BrowserContextProtocol.ReadRegistration(frame);
                if(input!=Registration)throw new InvalidOperationException("Fixture registration differs.");
                var registered=broker.Register(Peer,input);connection=registered.ConnectionId;
                broker.ConfirmPairing(connection,registered.PairingChallenge,true);
                return BrowserContextProtocol.Serialize(registered);
            }
            var reply=broker.HandleUtf8(Peer,frame);
            var snapshot=broker.Snapshots.Single();
            if(snapshot.History is {} history&&!prepared){
                if(snapshot.Binding!=Binding||history.OriginalDraft!=original||history.ComposerId!=Evidence.ComposerId||history.Pairs.Count!=1||history.Pairs[0]!=Pair)
                {broker.Cancel(connection);CryptographicOperations.ZeroMemory(reply);throw new InvalidOperationException("Fixture history differs.");}
                byte[] bytes=Encoding.UTF8.GetBytes(AssetText);
                try{
                    using var asset=ContextOriginalAsset.CreateCopy(bytes,"owned.txt","text/plain");
                    var review=broker.PreviewDraft(connection,Binding,original,Replacement,[asset]);
                    broker.ApproveDraft(connection,review.ReviewId,review.Digest,true);prepared=true;
                }finally{CryptographicOperations.ZeroMemory(bytes);}
            }
            snapshot=broker.Snapshots.Single();
            if(snapshot.Review is {State:"staged",ReceiptDigest:{} receipt} staged&&!undoRequested){
                broker.RequestUndo(connection,staged.ReviewId,receipt);undoRequested=true;
            }
            return reply;
        }
        public void Dispose()=>broker.Dispose();
    }
    private static byte[]? ReadFrame(Stream input)
    {
        Span<byte> header=stackalloc byte[4];int first=input.ReadByte();if(first<0)return null;header[0]=(byte)first;
        input.ReadExactly(header[1..]);uint size=BinaryPrimitives.ReadUInt32LittleEndian(header);
        if(size is 0 or >BrowserContextLimits.MaximumFrameBytes)throw new InvalidDataException("Fixture frame size.");
        byte[] bytes=new byte[(int)size];try{input.ReadExactly(bytes);return bytes;}catch{CryptographicOperations.ZeroMemory(bytes);throw;}
    }
    private static void WriteFrame(Stream output,byte[] bytes)
    {
        if(bytes.Length is 0 or >BrowserContextLimits.MaximumFrameBytes)throw new InvalidDataException("Fixture frame size.");
        Span<byte> header=stackalloc byte[4];BinaryPrimitives.WriteUInt32LittleEndian(header,(uint)bytes.Length);
        output.Write(header);output.Write(bytes);output.Flush();
    }
    private static void SelfTest()
    {
        int checks=0;void Check(bool value){if(!value)throw new InvalidOperationException("Fixture self-test.");checks++;}
        using(var stream=new MemoryStream()){WriteFrame(stream,[0x41,0x42]);Check(stream.ToArray().SequenceEqual(new byte[]{2,0,0,0,0x41,0x42}));stream.Position=0;Check(ReadFrame(stream)!.SequenceEqual(new byte[]{0x41,0x42}));Check(ReadFrame(stream)is null);}
        foreach(var malformed in new[]{new byte[]{0,0,0,0},new byte[]{1,0,1,0},new byte[]{2,0,0,0,0x41},new byte[]{1,0}}){
            bool refused=false;try{using var stream=new MemoryStream(malformed);_=ReadFrame(stream);}catch(Exception e)when(e is InvalidDataException or EndOfStreamException){refused=true;}Check(refused);
        }
        foreach(bool empty in new[]{false,true})using(var fixture=new Session(empty)){
            var registration=JsonSerializer.Deserialize<BrowserContextRegistrationReply>(fixture.Process(JsonSerializer.SerializeToUtf8Bytes(Registration,Json)),Json)!;
            long sequence=0;
            JsonElement Call(string kind,object payload,BrowserContextBinding? binding=null){
                var request=new BrowserContextEnvelope(1,registration.ConnectionId,registration.Nonce,++sequence,kind,binding??Binding,JsonSerializer.SerializeToElement(payload,Json));
                using var response=JsonDocument.Parse(fixture.Process(JsonSerializer.SerializeToUtf8Bytes(request,Json)));return response.RootElement.Clone();
            }
            Check(Call("identity.bind",new BrowserContextIdentityBind(Evidence)).GetProperty("status").GetString()=="ok");
            var document=new BrowserContextHistoryDocument([Pair],empty?"":Original,Evidence.ComposerId);byte[] history=JsonSerializer.SerializeToUtf8Bytes(document,Json);
            Check(Call("history.begin",new BrowserContextHistoryBegin("qa-history",history.Length,BrowserContextProtocol.Sha256(history),1,new(true,false,false,Evidence.HistorySignal,"u1","a1"))).GetProperty("status").GetString()=="ok");
            Check(Call("history.chunk",new BrowserContextChunk("qa-history",0,Convert.ToBase64String(history))).GetProperty("status").GetString()=="ok");
            Check(Call("history.end",new BrowserContextTransferEnd("qa-history")).GetProperty("status").GetString()=="ok");
            var offer=Call("draft.poll",new BrowserContextEmpty()).GetProperty("payload").Deserialize<BrowserContextDraftOffer>(Json)!;
            Check(offer.OriginalDraftSha256==BrowserContextProtocol.TextSha256(document.OriginalDraft)&&offer.Contents.Count==2);
            foreach(var manifest in offer.Contents){
                var chunk=Call("draft.chunk.read",new BrowserContextChunkRead(offer.ReviewId,manifest.ContentId,0)).GetProperty("payload").Deserialize<BrowserContextChunkReply>(Json)!;
                Check(chunk.Last&&BrowserContextProtocol.Sha256(Convert.FromBase64String(chunk.DataBase64))==manifest.Sha256);
            }
            var asset=offer.Contents.Single(x=>x.Role=="original");var ready=new[]{new BrowserContextAttachmentState(asset.ContentId,asset.Name,asset.MimeType,asset.ByteLength,asset.Sha256,"ready","qa-upload-id")};
            Check(Call("draft.state",new BrowserContextDraftState(offer.ReviewId,offer.Digest,BrowserContextProtocol.TextSha256(Replacement),Evidence.ComposerId,"staged",ready)).GetProperty("status").GetString()=="ok");
            var undo=Call("undo.poll",new BrowserContextEmpty()).GetProperty("payload").Deserialize<BrowserContextUndoOffer>(Json)!;
            var restored=Call("draft.chunk.read",new BrowserContextChunkRead(offer.ReviewId,"undo-original",0)).GetProperty("payload").Deserialize<BrowserContextChunkReply>(Json)!;
            Check(restored.Last&&Encoding.UTF8.GetString(Convert.FromBase64String(restored.DataBase64))==document.OriginalDraft);
            Check(Call("undo.state",new BrowserContextUndoState(offer.ReviewId,undo.ReceiptDigest,offer.OriginalDraftSha256,Evidence.ComposerId,ready)).GetProperty("status").GetString()=="ok");
        }
        Console.WriteLine($"PASS: {checks} test-only wire-fixture assertions. Synthetic in-memory streams and broker only.");
    }
}
