using Buddy.Server;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static class Program
{
    private static int checks;
    private static int Main()
    {
        try{Ownership();WorkspaceLifecycle();ImagesAndDelivery();Bounds();ExactSelectedUrls();OcrCorrections();Console.WriteLine($"PASS: {checks} original-asset assertions. In-memory synthetic bytes only; no decoder, files, transport or upload.");return 0;}
        catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
    private static void Check(bool value,string label){if(!value)throw new Exception("FAIL: "+label);checks++;Console.WriteLine("PASS: "+label);}
    private static void Reject(Action action,string label)
    {try{action();}catch(Exception e)when(e is InvalidOperationException or ArgumentException or OperationCanceledException){Check(true,label);return;}throw new Exception("FAIL: "+label);}
    private static byte[] Backing(ContextOriginalAsset asset)
    {
        var shared=typeof(ContextOriginalAsset).GetField("shared",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(asset)!;
        return (byte[])shared.GetType().GetField("Bytes",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(shared)!;
    }
    private static RefinementChatScope Scope(string id="a")=>new("manual-external",id);
    private static RefinementWorkspaceSnapshot Stage(RefinementWorkspace w,RefinementChatScope scope,ContextOriginalAsset asset,
        string text="Reviewed text",RefinementSourceKind kind=RefinementSourceKind.LocalTextFile,bool original=false,bool image=false)
    {
        var s=w.StageSource(scope,new(asset.Name,text,kind,OriginalSha256:asset.Sha256,OriginalBytes:asset.ByteCount,
            OriginalDeliveryRequired:original,FullImageRequired:image,ExtractionMethod:kind==RefinementSourceKind.LocalOcr?"local-ocr":"selected-file"),w.Snapshot(scope).Revision,asset);
        var source=s.Sources[^1];return w.ReviewSource(scope,source.Id,source.ReviewDigest,s.Revision);
    }
    private static FrozenRefinementContext Freeze(RefinementWorkspace w,RefinementChatScope scope)
    {var s=w.Snapshot(scope);return w.Freeze(scope,s.Revision,s.Sources.Select(x=>x.Id).ToArray(),[]);}
    private static void Ownership()
    {
        byte[] bytes=[0xef,0xbb,0xbf,0x61,0x0d,0x0a];var expected=bytes.ToArray();
        var asset=ContextOriginalAsset.CreateCopy(bytes,"bom.txt","text/plain");var backing=Backing(asset);
        bytes[3]=0x62;
        Check(asset.CopyBytes().SequenceEqual(expected),"Creation copies original BOM and CRLF bytes without normalizing or adopting caller memory");
        Check(asset.Sha256==Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant(),"Original digest is computed from exact owned bytes");
        var copy=asset.CopyBytes();copy[3]=0x63;
        Check(asset.CopyBytes().SequenceEqual(expected),"Mutating a returned working copy cannot alter the retained original");
        using var second=asset.Retain();asset.Dispose();asset.Dispose();
        Reject(asset.EnsureAvailable,"A disposed lease cannot be read or reused");
        Reject(()=>asset.Retain(),"A disposed lease cannot mint another lease");
        Check(second.CopyBytes().SequenceEqual(expected),"An independently retained lease survives disposal of its original owner");
        Check(backing.Any(b=>b!=0),"Original bytes remain available while a live retained owner exists");
        second.Dispose();Check(backing.All(b=>b==0),"The last owner zeroes the shared original byte array");
        Reject(()=>second.CopyBytes(),"A final disposed lease cannot export bytes");
        Check(!JsonSerializer.Serialize(asset.Info).Contains("Bytes\":"),"Snapshot metadata contains no binary byte-array field");
        foreach(var name in new[]{"../secret.txt","C:\\secret.txt","bad\nname.txt",".."})Reject(()=>ContextOriginalAsset.CreateCopy([1],name,"text/plain"),"Unsafe asset display name is refused");
        Reject(()=>ContextOriginalAsset.CreateCopy([1],"archive.zip","application/zip"),"Unsupported asset MIME type cannot be retained");
        Reject(()=>ContextOriginalAsset.CreateCopy([],"empty.png","image/png"),"An empty original cannot masquerade as a supported asset");
    }
    private static void OcrCorrections()
    {
        using var w=new RefinementWorkspace();var scope=Scope("ocr-correction");var other=Scope("other-ocr");w.Open(scope);w.Open(other);
        using var asset=ContextOriginalAsset.CreateCopy([1,2,3,4],"owned.png","image/png");
        var state=Stage(w,scope,asset,"ALPHA 125",RefinementSourceKind.LocalOcr);var original=state.Sources.Single();var frozen=Freeze(w,scope);
        using(var preview=w.RetainOcrOriginalForReview(scope,original.Id,original.ReviewDigest,state.Revision))
            Check(preview.CopyBytes().SequenceEqual(new byte[]{1,2,3,4})&&w.IsCurrent(frozen),"Opening correction preview retains original bytes without changing reviewed context");
        Reject(()=>w.CorrectOcrText(other,original.Id,original.ReviewDigest,"ALPHA 123",0),"Correction cannot cross chat scopes");
        Reject(()=>w.CorrectOcrText(scope,original.Id,new string('0',64),"ALPHA 123",state.Revision),"Correction refuses a mismatched source digest");
        foreach(string bad in new[]{"", "\0", "\ud800",new string('x',20001)})
            Reject(()=>w.CorrectOcrText(scope,original.Id,original.ReviewDigest,bad,state.Revision),"Invalid correction leaves original context untouched");
        var same=w.CorrectOcrText(scope,original.Id,original.ReviewDigest,original.Text,state.Revision);
        Check(same.Revision==state.Revision&&same.Sources.Single().Reviewed&&!frozen.Invalidated.IsCancellationRequested,"Unchanged text does not revoke a valid review");
        const string exact="ALPHA 123\r\nUnit: µm 👩‍💻\n";
        state=w.CorrectOcrText(scope,original.Id,original.ReviewDigest,exact,state.Revision);var corrected=state.Sources.Single();
        Check(corrected.Text==exact&&!corrected.Reviewed&&corrected.TextSha256!=original.TextSha256,"Correction preserves exact Unicode/newlines and requires fresh review");
        Check(corrected.Id==original.Id&&corrected.OriginalAsset==original.OriginalAsset&&corrected.OriginalSha256==original.OriginalSha256&&corrected.OriginalBytes==original.OriginalBytes,"Correction preserves source identity and original image provenance");
        Check(corrected.Correction==new OcrTextCorrection(original.TextSha256,original.ExtractionMethod)&&corrected.ExtractionMethod=="user-corrected-ocr-v1","Human correction retains first extraction digest and method distinctly");
        Check(frozen.Invalidated.IsCancellationRequested&&!w.IsCurrent(frozen),"Correction immediately invalidates previously prepared context");
        Reject(()=>ContextDeliveryPlan.Project(frozen),"An old draft cannot project text after correction");
        Reject(()=>Freeze(w,scope),"Corrected text cannot be used before separate review");
        Reject(()=>w.ReviewSource(scope,original.Id,original.ReviewDigest,state.Revision),"Original review digest cannot authorize corrected text");
        state=w.ReviewSource(scope,corrected.Id,corrected.ReviewDigest,state.Revision);var fresh=Freeze(w,scope);
        Check(fresh.Sources.Single().Text==exact&&ContextDeliveryPlan.Project(fresh).Ready,"Fresh explicit review admits the corrected text");
        state=w.CorrectOcrText(scope,corrected.Id,corrected.ReviewDigest,"ALPHA 123",state.Revision);
        Check(state.Sources.Single().Correction==corrected.Correction&&fresh.Invalidated.IsCancellationRequested,"Repeated edits retain first OCR provenance and revoke each old generation");
        using(var bytes=w.RetainOcrOriginalForReview(scope,corrected.Id,state.Sources.Single().ReviewDigest,state.Revision))
            Check(bytes.CopyBytes().SequenceEqual(new byte[]{1,2,3,4}),"Repeated text correction never rewrites retained image bytes");
        var stale=state;w.Clear(scope);
        Reject(()=>w.CorrectOcrText(scope,corrected.Id,stale.Sources.Single().ReviewDigest,"late",stale.Revision),"Clear prevents late correction from resurrecting a source");
        state=w.StageSource(scope,new("Unretained OCR","text",RefinementSourceKind.LocalOcr),w.Snapshot(scope).Revision);
        Reject(()=>w.CorrectOcrText(scope,state.Sources[0].Id,state.Sources[0].ReviewDigest,"new",state.Revision),"Correction requires a retained original image");
        w.Clear(scope);state=w.StageSource(scope,new("Plain text","text"),w.Snapshot(scope).Revision);
        Reject(()=>w.CorrectOcrText(scope,state.Sources[0].Id,state.Sources[0].ReviewDigest,"new",state.Revision),"Plain text cannot acquire OCR correction provenance");
        w.Clear(scope);state=Stage(w,scope,asset,"ALPHA 125",RefinementSourceKind.LocalOcr,original:true);
        var requiring=state.Sources.Single();state=w.CorrectOcrText(scope,requiring.Id,requiring.ReviewDigest,"ALPHA 123",state.Revision);
        Check(state.Sources.Single().OriginalDeliveryRequired,"Editing OCR cannot clear the original-attachment requirement");
        for(int i=0;i<6;i++)w.StageSource(scope,new("Large "+i,new string('a',20000)),w.Snapshot(scope).Revision);
        w.StageSource(scope,new("Tail",new string('b',10000)),w.Snapshot(scope).Revision);state=w.Snapshot(scope);var item=state.Sources[0];
        Reject(()=>w.CorrectOcrText(scope,item.Id,item.ReviewDigest,new string('c',20000),state.Revision),"Correction observes total session UTF-8 capacity without dropping other sources");
        Check(w.Snapshot(scope).Revision==state.Revision&&w.Snapshot(scope).Sources.Count==8,"Capacity refusal leaves all prior sources unchanged");
    }
    private static void WorkspaceLifecycle()
    {
        using var w=new RefinementWorkspace();var a=Scope();var b=Scope("b");w.Open(a);w.Open(b);
        var asset=ContextOriginalAsset.CreateCopy([0xef,0xbb,0xbf,0x61],"bom.txt","text/plain");var backing=Backing(asset);
        var staged=w.StageSource(a,new("bom.txt","a",RefinementSourceKind.LocalTextFile),0,asset);asset.Dispose();
        Check(staged.Sources[0].OriginalAsset is {ByteCount:4,MimeType:"text/plain"},"Workspace snapshot preserves original asset metadata while retaining independent ownership");
        Reject(()=>w.Freeze(a,staged.Revision,[staged.Sources[0].Id],[]),"Unreviewed original cannot be selected for byte retrieval");
        var reviewed=w.ReviewSource(a,staged.Sources[0].Id,staged.Sources[0].ReviewDigest,staged.Revision);var frozen=Freeze(w,a);
        using(var retained=w.RetainOriginalAsset(frozen,reviewed.Sources[0].Id))Check(retained.CopyBytes().SequenceEqual(new byte[]{0xef,0xbb,0xbf,0x61}),"Selected TXT original retains exact UTF-8 BOM while reviewed text may omit it");
        Reject(()=>w.RetainOriginalAsset(w.Freeze(b,0,[],[]),reviewed.Sources[0].Id),"Another chat cannot borrow an original by source ID");
        var borrowed=w.RetainOriginalAsset(frozen,reviewed.Sources[0].Id);w.RemoveSource(a,reviewed.Sources[0].Id,reviewed.Revision);
        Check(frozen.Invalidated.IsCancellationRequested&&w.Snapshot(a).Sources.Count==0,"Removing an original invalidates the exact frozen selection");
        Reject(()=>w.RetainOriginalAsset(frozen,reviewed.Sources[0].Id),"Stale selection cannot obtain another original lease");
        Check(borrowed.CopyBytes().Length==4,"An explicit borrowed lease remains caller-owned until disposed");borrowed.Dispose();
        Check(backing.All(x=>x==0),"Removal releases workspace ownership; final borrowed disposal zeroes the bytes");
        foreach(string operation in new[]{"clear","close","dispose"}){
            using var local=new RefinementWorkspace();var scope=Scope(operation);local.Open(scope);
            var original=ContextOriginalAsset.CreateCopy([1,2,3],"note.md","text/markdown");var owned=Backing(original);
            var state=Stage(local,scope,original);original.Dispose();var f=Freeze(local,scope);
            if(operation=="clear")local.Clear(scope);else if(operation=="close")local.Close(scope);else local.Dispose();
            Check(owned.All(x=>x==0)&&f.Invalidated.IsCancellationRequested,operation+" releases original ownership and invalidates old review authority");
            Reject(()=>local.RetainOriginalAsset(f,state.Sources[0].Id),operation+" forbids late retrieval");
        }
        using var wrong=ContextOriginalAsset.CreateCopy([1,2],"note.txt","text/plain");
        Reject(()=>w.StageSource(a,new("note.txt","text",RefinementSourceKind.LocalTextFile,OriginalSha256:new string('0',64)),w.Snapshot(a).Revision,wrong),"Original digest mismatch refuses adoption");
        Reject(()=>w.StageSource(a,new("note.txt","text",RefinementSourceKind.LocalTextFile,OriginalBytes:99),w.Snapshot(a).Revision,wrong),"Original byte-count mismatch refuses adoption");
        Reject(()=>w.StageSource(a,new("note.txt","text",RefinementSourceKind.SelectedText),w.Snapshot(a).Revision,wrong),"A pasted-text kind cannot falsely claim a selected original file");
        Check(w.Snapshot(a).Sources.Count==0,"Rejected adoption leaves the workspace unchanged");
        Reject(()=>w.StageSource(a,new("lost.txt","text",RefinementSourceKind.LocalTextFile,OriginalDeliveryRequired:true),w.Snapshot(a).Revision),"Required original with no retained bytes refuses staging");
    }
    private static void ImagesAndDelivery()
    {
        using var w=new RefinementWorkspace();var scope=Scope();w.Open(scope);
        // Synthetic bytes exercise ownership only. The real reader performs signature/decode checks.
        using var image=ContextOriginalAsset.CreateCopy([137,80,78,71,1,2,3],"chart.png","image/png");
        var state=Stage(w,scope,image,"",RefinementSourceKind.LocalImage,original:true,image:true);
        var f=Freeze(w,scope);var p=ContextDeliveryPlan.Project(f);
        Check(!p.Ready&&p.Reasons.Any(r=>r.Contains("original file")),"Original-only image with empty text is retained and explicitly refused by a text destination");
        using(var original=w.RetainOriginalAsset(f,state.Sources[0].Id))Check(original.Sha256==image.Sha256,"Original-only image remains retrievable from its reviewed selection");
        w.Clear(scope);state=Stage(w,scope,image,"Chart total: 42",RefinementSourceKind.LocalOcr);f=Freeze(w,scope);p=ContextDeliveryPlan.Project(f);
        Check(state.Sources[0].OriginalSha256==image.Sha256&&state.Sources[0].TextSha256!=image.Sha256,"OCR text retains the original image digest separately from extracted text digest");
        Check(p.Ready&&p.Sources[0].Title.StartsWith("Extracted image text")&&p.Warnings.Any(x=>x.Contains("not attached")),"OCR projection labels text conversion and discloses that the original image is not attached");
        using(var original=w.RetainOriginalAsset(f,state.Sources[0].Id))Check(original.CopyBytes().SequenceEqual(image.CopyBytes()),"OCR provenance retains the same original image bytes");
        string oldDigest=state.Sources[0].ReviewDigest;
        state=w.SetOriginalDeliveryRequired(scope,state.Sources[0].Id,true,state.Revision);
        Check(f.Invalidated.IsCancellationRequested&&!state.Sources[0].Reviewed&&state.Sources[0].ReviewDigest!=oldDigest,"Changing original-vs-text delivery invalidates old review and requires fresh review");
        Reject(()=>Freeze(w,scope),"Changed delivery choice cannot reuse prior preview approval");
        state=w.ReviewSource(scope,state.Sources[0].Id,state.Sources[0].ReviewDigest,state.Revision);f=Freeze(w,scope);p=ContextDeliveryPlan.Project(f);
        Check(!p.Ready&&p.Reasons.Any(x=>x.Contains("no verified attachment adapter")),"Requiring the retained OCR original blocks text-only destinations");
        state=w.SetOriginalDeliveryRequired(scope,state.Sources[0].Id,false,state.Revision);
        state=w.ReviewSource(scope,state.Sources[0].Id,state.Sources[0].ReviewDigest,state.Revision);
        Check(ContextDeliveryPlan.Project(Freeze(w,scope)).Ready,"Explicitly reviewing text-only delivery restores OCR text projection");
        w.Clear(scope);using var text=ContextOriginalAsset.CreateCopy(Encoding.UTF8.GetBytes("file body"),"note.md","text/markdown");
        state=Stage(w,scope,text,"file body",original:true);p=ContextDeliveryPlan.Project(Freeze(w,scope));
        Check(!p.Ready&&p.Reasons.Any(x=>x.Contains("original file")),"A required TXT/Markdown original cannot silently degrade into extracted text");
        w.Clear(scope);state=Stage(w,scope,text,"file body");p=ContextDeliveryPlan.Project(Freeze(w,scope));
        Check(p.Ready&&p.Sources[0].Text=="file body"&&p.Warnings.Any(x=>x.Contains("not attached")),"Optional text-file original stays local while exact reviewed text is projected");
    }
    private static void Bounds()
    {
        Reject(()=>ContextOriginalAsset.CreateCopy(new byte[ContextOriginalAsset.MaximumTextBytes+1],"large.txt","text/plain"),"Text originals are bounded to 64KiB");
        Reject(()=>ContextOriginalAsset.CreateCopy(new byte[ContextOriginalAsset.MaximumImageBytes+1],"large.png","image/png"),"Image originals are bounded to 2,000,000 bytes");
        using var image=ContextOriginalAsset.CreateCopy(new byte[ContextOriginalAsset.MaximumImageBytes],"bounded.png","image/png");
        using var w=new RefinementWorkspace();var a=Scope();var b=Scope("b");var c=Scope("c");w.Open(a);w.Open(b);w.Open(c);
        for(int i=0;i<4;i++)Stage(w,a,image,"",RefinementSourceKind.LocalImage,image:true);
        Reject(()=>Stage(w,a,image,"",RefinementSourceKind.LocalImage,image:true),"Per-session original ownership is bounded at 8,000,000 bytes");
        for(int i=0;i<4;i++)Stage(w,b,image,"",RefinementSourceKind.LocalImage,image:true);
        Reject(()=>Stage(w,c,image,"",RefinementSourceKind.LocalImage,image:true),"All sessions share the 16,000,000-byte original budget");
        w.Clear(a);Stage(w,c,image,"",RefinementSourceKind.LocalImage,image:true);
        Check(w.Snapshot(c).Sources.Count==1,"Clear releases retained original budget for a later reviewed source");
    }
    private static void ExactSelectedUrls()
    {
        using var w=new RefinementWorkspace();var scope=Scope();w.Open(scope);
        const string original="Reviewed source: https://example.com/path?q=private-value#anchor\r\nKeep this URL as data.";
        using var asset=ContextOriginalAsset.CreateCopy(Encoding.UTF8.GetBytes(original),"reference.txt","text/plain");
        Stage(w,scope,asset,original);var projected=ContextDeliveryPlan.Project(Freeze(w,scope),8000);
        Check(projected.Ready&&projected.Sources.Single().Text==original,"A reviewed selected TXT URL survives exactly as untrusted data without blocking projection");
        var blocks=RefinementContext.Build(projected.Sources);
        Check(blocks.Ready&&blocks.Blocks.Single().Text.Contains("not fetched",StringComparison.OrdinalIgnoreCase),"Selected text with URLs is labelled as not-fetched data rather than retrieved evidence");
        string block=blocks.Blocks.Single().Text;
        using(var parsed=JsonDocument.Parse(block[(block.IndexOf('\n')+1)..block.LastIndexOf("\nEND_UNTRUSTED_CONTEXT_JSON",StringComparison.Ordinal)])){
            Check(parsed.RootElement.EnumerateObject().Single(x=>x.Name.Equals("Text",StringComparison.OrdinalIgnoreCase)).Value.GetString()==original,
                "The final untrusted JSON block preserves exact selected URL, query, fragment and CRLF characters");
            Check(!parsed.RootElement.EnumerateObject().Any(x=>x.Name.Equals("Url",StringComparison.OrdinalIgnoreCase)||x.Name.Contains("VerifiedUrl",StringComparison.OrdinalIgnoreCase)),
                "Selected data cannot create URL verification or citation metadata");
        }
        w.Clear(scope);const string response="See https://example.com/answer?id=42 for the quoted reference; it was not fetched by Buddy.";
        w.AppendCompletedTurn(scope,"turn","Use the previous response.",response,RefinementTurnOrigin.BuddyCompleted,w.Snapshot(scope).Revision);
        var s=w.Snapshot(scope);var frozen=w.Freeze(scope,s.Revision,[],["turn"],"turn");projected=ContextDeliveryPlan.Project(frozen,8000);
        Check(projected.Ready,"A same-chat assistant response containing a URL remains eligible as selected conversation data");
        using var json=JsonDocument.Parse(projected.Sources.Single().Text);
        Check(json.RootElement.GetProperty("turns")[0].GetProperty("messages")[1].GetProperty("text").GetString()==response,"The selected complete assistant response retains its original URL characters");
        Check(RefinementContext.Build(projected.Sources).Ready,"Selected conversation URLs do not acquire or require verified retrieval status");
    }
}
