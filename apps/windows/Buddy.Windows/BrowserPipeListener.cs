using Buddy.Browser;
using Buddy.Server;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace Buddy.Windows;

// Started only by the browser review window. No listener on normal app startup.
internal sealed class BrowserPipeListener : IDisposable
{
    private readonly BrowserContextBroker broker;
    private readonly CancellationTokenSource lifetime = new();
    private readonly object gate = new();
    private NamedPipeServerStream? current;
    private bool disposed;
    internal string PipeName { get; }
    internal Task Completion { get; }
    internal string Status { get; private set; } = "Waiting for the reviewed native browser host.";
    internal BrowserPipeListener(BrowserContextBroker broker)
    {
        this.broker=broker;
        string package=Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\','/').ToUpperInvariant();
        PipeName="Buddy.BrowserContext.v1."+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName+"|"+package))).ToLowerInvariant()[..24];
        Completion=Task.Run(Run);
    }
    private async Task Run()
    {
        while(!lifetime.IsCancellationRequested)
        {
            string peer=Guid.NewGuid().ToString("N");
            try
            {
                using var pipe=new NamedPipeServerStream(PipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly,65_540,65_540);
                lock(gate){if(disposed)return;current=pipe;}
                await pipe.WaitForConnectionAsync(lifetime.Token);
                using var session=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);session.CancelAfter(TimeSpan.FromMinutes(20));
                bool registered=false;Status="Browser connected. Compare its pairing code before approving.";
                while(!session.IsCancellationRequested)
                {
                    byte[]? frame=await BrowserNativeTransport.ReadAsync(pipe,session.Token);
                    if(frame is null)break;
                    byte[]? reply=null;
                    try
                    {
                        if(!registered){var request=BrowserContextProtocol.ReadRegistration(frame);reply=BrowserContextProtocol.Serialize(broker.Register(peer,request));registered=true;}
                        else reply=broker.HandleUtf8(peer,frame);
                        await BrowserNativeTransport.WriteAsync(pipe,reply,session.Token);
                    }
                    finally{Array.Clear(frame);if(reply is not null)Array.Clear(reply);}
                }
            }
            catch(Exception error) when(error is IOException or InvalidDataException or UnauthorizedAccessException or OperationCanceledException or ObjectDisposedException or InvalidOperationException or System.Text.Json.JsonException or ArgumentException)
            { Status=lifetime.IsCancellationRequested?"Browser session stopped.":"Browser connection ended or was refused. Reconnect and pair again."; }
            finally{broker.DisconnectPeer(peer);lock(gate)current=null;}
        }
    }
    public void Dispose()
    {
        lock(gate){if(disposed)return;disposed=true;lifetime.Cancel();current?.Dispose();}
        _=Completion.ContinueWith(_=>lifetime.Dispose(),TaskScheduler.Default);
    }
    internal void DisconnectCurrent(){lock(gate)current?.Dispose();}
}
