using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Buddy.LocalAgentPipe.Tests")]

namespace Buddy.Server;

/// <summary>Explicitly started, current-Windows-user-only pipe; never installs hooks or runs commands.</summary>
public sealed class LocalAgentPipeServer : IAsyncDisposable
{
    private readonly LocalAgentBroker broker;
    private readonly Func<CancellationToken, Task>? listenerFactory;
    private readonly CancellationTokenSource lifetime = new();
    private readonly object lifecycle = new();
    private Task? listener;
    private bool disposed;
    private int started;
    private int failed;
    public string PipeName { get; } = "Buddy.LocalAgent." + Guid.NewGuid().ToString("N");
    public bool Started => Volatile.Read(ref started) != 0;
    public bool Running => Started && !lifetime.IsCancellationRequested && Volatile.Read(ref failed) == 0;
    public string Status => Volatile.Read(ref failed) != 0 ? "Local listener stopped; disconnect and reopen local sessions." : Running ? "Local listener enabled; individual session pairing is still required." : "Local listener disabled.";
    public LocalAgentPipeServer(LocalAgentBroker broker) : this(broker, null) { }
    // Trusted fixture seam only: pure lifecycle tests inject a task and never create an OS pipe.
    internal LocalAgentPipeServer(LocalAgentBroker broker, Func<CancellationToken, Task>? listenerFactory)
    { this.broker = broker; this.listenerFactory = listenerFactory; }
    public void Start(bool explicitLocalSessionConsent)
    {
        if (!explicitLocalSessionConsent) throw new InvalidOperationException("Enable the local-agent listener explicitly first.");
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This listener requires Windows current-user pipe protection.");
        lock (lifecycle)
        {
            if (disposed || Started) throw new InvalidOperationException("This listener was already started or stopped.");
            Interlocked.Exchange(ref started, 1);
            try { listener = listenerFactory?.Invoke(lifetime.Token) ?? Listen(); }
            catch { Interlocked.Exchange(ref failed, 1); throw; }
        }
    }
    private async Task Listen()
    {
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 16384, 16384);
                await pipe.WaitForConnectionAsync(lifetime.Token).ConfigureAwait(false);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(3));
                try
                {
                    var bytes = new List<byte>(); var one = new byte[1];
                    while (bytes.Count <= 16384)
                    {
                        if (await pipe.ReadAsync(one, deadline.Token).ConfigureAwait(false) == 0) throw LocalAgentBroker.Rejected();
                        if (one[0] == (byte)'\n') break;
                        bytes.Add(one[0]);
                    }
                    if (bytes.Count > 16384) throw LocalAgentBroker.Rejected();
                    deadline.Token.ThrowIfCancellationRequested();
                    string response = Handle(bytes.ToArray());
                    await pipe.WriteAsync(Encoding.UTF8.GetBytes(response + "\n"), deadline.Token).ConfigureAwait(false);
                    await pipe.FlushAsync(deadline.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or InvalidOperationException or JsonException or ArgumentException)
                {
                    // Never reflect supplied text, credentials or exception details to a client or log.
                    if (!deadline.IsCancellationRequested && pipe.IsConnected)
                        try { await pipe.WriteAsync("{\"accepted\":false}\n"u8.ToArray(), deadline.Token).ConfigureAwait(false); }
                        catch (Exception writeError) when (writeError is IOException or OperationCanceledException) { }
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        { Interlocked.Exchange(ref failed, 1); }
        finally { broker.DisconnectAll(); }
    }
    internal string Handle(ReadOnlyMemory<byte> bytes)
    {
        try { return HandleCore(bytes); }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException or OverflowException)
        { throw LocalAgentBroker.Rejected(); }
    }
    private string HandleCore(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > 16384) throw LocalAgentBroker.Rejected();
        using var doc = ProviderTextOnlyValidation.Parse(bytes);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object) throw LocalAgentBroker.Rejected();
        var kind = root.GetProperty("kind").GetString();
        var token = root.GetProperty("token").GetString() ?? "";
        if (kind == "event")
        {
            RequireKeys(root, "kind", "token", "event");
            var ev = root.GetProperty("event");
            RequireKeys(ev, "sessionId", "sequence", "kind", "taskId", "text", "requestId", "requestDigest");
            var input = JsonSerializer.Deserialize<LocalAgentEvent>(ev, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw LocalAgentBroker.Rejected();
            broker.Receive(token, input); return "{\"accepted\":true}";
        }
        if (kind == "decision")
        {
            RequireKeys(root, "kind", "token", "sessionId", "requestId", "requestDigest");
            var decision = broker.TakeDecision(root.GetProperty("sessionId").GetGuid(), token,
                root.GetProperty("requestId").GetString() ?? "", root.GetProperty("requestDigest").GetString() ?? "");
            return JsonSerializer.Serialize(new { accepted = true, decision });
        }
        throw LocalAgentBroker.Rejected();
    }
    private static void RequireKeys(JsonElement value, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Any(x => !allowed.Contains(x.Name, StringComparer.Ordinal))) throw LocalAgentBroker.Rejected();
    }
    public async ValueTask DisposeAsync()
    {
        Task? pending;
        lock (lifecycle) { disposed = true; pending = listener; }
        lifetime.Cancel(); broker.DisconnectAll();
        if (pending is not null) await pending.ConfigureAwait(false);
    }
}
