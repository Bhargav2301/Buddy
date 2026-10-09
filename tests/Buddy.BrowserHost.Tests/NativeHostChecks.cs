using Buddy.Browser;
using Buddy.Server;
using Buddy.Windows;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;

internal static class NativeHostChecks
{
    internal static async Task Run(string hostBuild,string outputDirectory)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException();
        string output=Path.GetFullPath(outputDirectory);if(Directory.Exists(output))throw new InvalidOperationException("Choose a new owned fixture directory.");Directory.CreateDirectory(output);
        string runtime=Path.Combine(output,"runtime");Directory.CreateDirectory(runtime);
        foreach(string suffix in new[]{".exe",".dll",".deps.json",".runtimeconfig.json"})File.Copy(Path.Combine(hostBuild,"Buddy.BrowserHost"+suffix),Path.Combine(runtime,"Buddy.BrowserHost"+suffix),false);
        string extension=new('a',32);using var broker=new BrowserContextBroker();using var listener=new BrowserPipeListener(broker);
        await File.WriteAllTextAsync(Path.Combine(runtime,"browser-host-policy.json"),JsonSerializer.Serialize(new{version=1,extensionId=extension,pipeName=listener.PipeName}));
        int checks=0;void Check(bool value,string label){checks++;if(!value)throw new InvalidOperationException(label);}
        Process Start(string origin){var p=new Process{StartInfo=new(Path.Combine(runtime,"Buddy.BrowserHost.exe")){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=runtime}};p.StartInfo.ArgumentList.Add(origin);p.Start();return p;}
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var host=Start("chrome-extension://"+extension+"/");
        var options=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
        async Task<T> RoundTrip<T>(object payload){await BrowserNativeTransport.WriteAsync(host.StandardInput.BaseStream,BrowserContextProtocol.Serialize(payload),timeout.Token);var reply=await BrowserNativeTransport.ReadAsync(host.StandardOutput.BaseStream,timeout.Token);Check(reply is not null,"native reply received");return JsonSerializer.Deserialize<T>(reply!,options)!;}
        try
        {
            var registration=await RoundTrip<BrowserContextRegistrationReply>(new BrowserContextRegistration(1,"register","chatgpt","https://chatgpt.com",7,0,"owned-document","owned-generation"));
            Check(registration.Status=="awaiting-pairing","unpaired native registration");
            var empty=new BrowserContextBinding("chatgpt",7,"owned-document","owned-generation","","","");long sequence=0;
            BrowserContextEnvelope Envelope(string kind,object payload,BrowserContextBinding? binding=null)=>new(1,registration.ConnectionId,registration.Nonce,++sequence,kind,binding??empty,JsonSerializer.SerializeToElement(payload,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase}));
            var readiness=await RoundTrip<BrowserContextReply>(Envelope("readiness.report",new BrowserContextReadiness(1,2,2,false,false,false)));
            Check(readiness.Status=="ok","structural readiness can precede pairing without content");
            broker.ConfirmPairing(registration.ConnectionId,registration.PairingChallenge,true);
            Check(broker.Snapshots.Single().Status=="readiness-only","pairing does not admit unverified production profile");
            var forged=empty with{AccountId="forged-account",WorkspaceId="forged-workspace",ConversationId="forged-chat"};
            var refused=await RoundTrip<BrowserContextReply>(Envelope("identity.bind",new BrowserContextIdentityBind(new("forged-account-signal","forged-workspace-signal","forged-chat-signal","forged-history-signal","forged-composer","forged-capability")),forged));
            Check(refused.Status=="refused"&&refused.Code=="identity_profile_unavailable","forged wire identity cannot admit production");
            Check(broker.Snapshots.Single().History is null&&broker.Snapshots.Single().Binding is null,"no history or bound account produced");
            byte[] oversized=new byte[4];BinaryPrimitives.WriteUInt32LittleEndian(oversized,65_537);
            await host.StandardInput.BaseStream.WriteAsync(oversized,timeout.Token);await host.StandardInput.BaseStream.FlushAsync(timeout.Token);
            await host.WaitForExitAsync(timeout.Token);
            string nativeError=await host.StandardError.ReadToEndAsync(timeout.Token);
            await File.WriteAllTextAsync(Path.Combine(output,"native-exit.json"),JsonSerializer.Serialize(new{exitCode=host.ExitCode,stderr=nativeError}));
            Check(host.ExitCode==3,"oversized native input closes without allocation");
            Check(nativeError.Length==0,"native host does not echo private payload/errors");
            for(int i=0;i<50&&broker.Snapshots.Single().Status!="disconnected";i++)await Task.Delay(10,timeout.Token);
            Check(broker.Snapshots.Single().Status=="disconnected","port exit clears the paired connection");
            using var wrong=Start("chrome-extension://"+new string('b',32)+"/");await wrong.WaitForExitAsync(timeout.Token);Check(wrong.ExitCode==2,"nonallowlisted extension refused before pipe");
            Check(broker.Snapshots.Count==1,"wrong origin made no new broker connection");
            using var idle=Start("chrome-extension://"+extension+"/");
            try
            {
                await BrowserNativeTransport.WriteAsync(idle.StandardInput.BaseStream,BrowserContextProtocol.Serialize(new BrowserContextRegistration(1,"register","chatgpt","https://chatgpt.com",8,0,"owned-idle-document","owned-idle-generation")),timeout.Token);
                Check(await BrowserNativeTransport.ReadAsync(idle.StandardOutput.BaseStream,timeout.Token) is not null,"idle port registered");
                var clock=Stopwatch.StartNew();listener.DisconnectCurrent();
                await idle.WaitForExitAsync(timeout.Token).WaitAsync(TimeSpan.FromSeconds(3));
                Check(clock.Elapsed<TimeSpan.FromSeconds(3),"desktop Stop closes idle native port without a new browser request");
            }
            finally{if(!idle.HasExited){idle.Kill(entireProcessTree:true);await idle.WaitForExitAsync();}}
            var receipt=new{status="passed",checks,scope="Owned native stdio host to actual CurrentUserOnly named pipe; synthetic registration/readiness only. No browser, registry, account, file upload or Send.",hostSha256=BrowserContextProtocol.Sha256(await File.ReadAllBytesAsync(Path.Combine(runtime,"Buddy.BrowserHost.dll"))),extensionInstalled=false,registryChanged=false};
            await File.WriteAllTextAsync(Path.Combine(output,"receipt.json"),JsonSerializer.Serialize(receipt,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine($"PASS: {checks} owned native host/pipe checks.");
        }
        finally
        {
            if(!host.HasExited){host.Kill(entireProcessTree:true);await host.WaitForExitAsync();}
            listener.Dispose();await listener.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
