using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace Buddy.Windows;

// Only window navigation crosses this current-user, current-session channel.
internal sealed class DesktopActivation : IDisposable
{
    private readonly CancellationTokenSource stopped = new();
    private NamedPipeServerStream? listener;
    private bool disposed;
    internal Task Completion { get; }
    private static string ChannelName {
        get {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            using var user = WindowsIdentity.GetCurrent(); using var process = Process.GetCurrentProcess();
            return "Buddy.Desktop.Activation.v1." + user.User!.Value + "." + process.SessionId;
        }
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint processId);

    internal DesktopActivation(Func<LaunchDestination, Task> open, string? channel = null, Action<Exception>? report = null)
    {
        var name = channel ?? ChannelName;
        listener = Create(name); // Publish before the first async wait to cover simultaneous launches.
        Completion = Listen(name, open, report);
    }
    private static NamedPipeServerStream Create(string name) => new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
    private async Task Listen(string name, Func<LaunchDestination, Task> open, Action<Exception>? report)
    {
        while (!stopped.IsCancellationRequested) {
            using var pipe = listener!;
            try {
                await pipe.WaitForConnectionAsync(stopped.Token);
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stopped.Token); deadline.CancelAfter(TimeSpan.FromSeconds(5));
                var request = new byte[1];
                if (await pipe.ReadAsync(request, deadline.Token) == 1 && request[0] is (byte)LaunchDestination.Home or (byte)LaunchDestination.Settings) {
                    await open((LaunchDestination)request[0]).WaitAsync(deadline.Token);
                    await pipe.WriteAsync(new byte[] { 1 }, deadline.Token);
                }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
            catch (Exception ex) { report?.Invoke(ex); }
            if (stopped.IsCancellationRequested) break;
            pipe.Dispose(); listener = Create(name);
        }
    }
    internal static async Task<bool> Redirect(LaunchDestination destination, string? channel = null, int timeoutMs = 5000)
    {
        if (destination == LaunchDestination.Background) return true;
        using var timeout = new CancellationTokenSource(timeoutMs);
        using var pipe = new NamedPipeClientStream(".", channel ?? ChannelName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try {
            await pipe.ConnectAsync(timeout.Token);
            if (GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var pid)) AllowSetForegroundWindow(pid);
            await pipe.WriteAsync(new[] { (byte)destination }, timeout.Token);
            var reply = new byte[1]; return await pipe.ReadAsync(reply, timeout.Token) == 1 && reply[0] == 1;
        } catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException) { return false; }
    }
    public void Dispose() { if (disposed) return; disposed = true; stopped.Cancel(); listener?.Dispose(); _ = Completion.ContinueWith(_ => stopped.Dispose(), TaskScheduler.Default); }
}
