using Buddy.Server;
using Buddy.Windows;
using System.Text;
using System.Text.Json;

internal static class SelectedContextChecks
{
    internal static int Run()
    {
        int count=0;void Check(bool ok,string label){count++;if(!ok)throw new Exception(label);}
        using var workspace=new RefinementWorkspace();var scope=new RefinementChatScope("manual-external","owned-browser-test");workspace.Open(scope);
        byte[] bytes=Encoding.UTF8.GetBytes("\uFEFFAlpha\r\nhttps://example.invalid/test?q=one#two");
        using var original=ContextOriginalAsset.CreateCopy(bytes,"owned.txt","text/plain");
        var staged=workspace.StageSource(scope,new("owned.txt","Alpha\r\nhttps://example.invalid/test?q=one#two",RefinementSourceKind.LocalTextFile,OriginalSha256:original.Sha256,OriginalBytes:bytes.Length,ExtractionMethod:"strict-utf8-v1",OriginalDeliveryRequired:true),0,original);
        var source=staged.Sources.Single();workspace.ReviewSource(scope,source.Id,source.ReviewDigest,staged.Revision);
        var withTurn=workspace.AppendCompletedTurn(scope,"pair-1","Make it concise.","Keep both requirements and the URL.",RefinementTurnOrigin.AdapterCompleted,workspace.Snapshot(scope).Revision);
        var frozen=workspace.Freeze(scope,withTurn.Revision,[source.Id],["pair-1"],"pair-1");
        using var prepared=new BrowserSelectedContext(workspace,frozen);
        Check(prepared.Originals.Count==1,"explicit original retained");original.Dispose();
        Check(prepared.Originals[0].CopyBytes().SequenceEqual(bytes),"original BOM and CRLF survive caller disposal");
        using var document=JsonDocument.Parse(prepared.Text[(prepared.Text.IndexOf('\n')+1)..]);
        var item=document.RootElement.GetProperty("sources")[0];
        Check(item.GetProperty("Text").GetString()==source.Text,"full selected text exact");
        Check(item.GetProperty("delivery").GetString()!.StartsWith("original attachment requested"),"staging is not attachment success");
        var pair=document.RootElement.GetProperty("completedPairs")[0];
        Check(pair.GetProperty("user").GetProperty("text").GetString()=="Make it concise.","whole user text");
        Check(pair.GetProperty("assistant").GetProperty("text").GetString()=="Keep both requirements and the URL.","whole assistant text");
        Check(pair.GetProperty("selectedPriorResponse").GetBoolean(),"referent remains explicit");
        prepared.ValidateFinalText("Please summarize.\n\n"+prepared.Text);count++;
        foreach(var invalid in new[]{"removed context",prepared.Text+prepared.Text,new string('x',20001)+prepared.Text}){
            try{prepared.ValidateFinalText(invalid);throw new Exception("invalid final draft accepted");}catch(InvalidOperationException){count++;}
        }
        workspace.Clear(scope);
        try{prepared.ValidateFinalText(prepared.Text);throw new Exception("cleared context accepted");}catch(OperationCanceledException){count++;}
        Check(prepared.Originals[0].CopyBytes().SequenceEqual(bytes),"leased original survives workspace clear without becoming approved");
        prepared.Dispose();Check(prepared.Originals.Count==0,"prepared ownership released");
        return count;
    }
}
