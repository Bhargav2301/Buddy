using Buddy.Browser;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.RegularExpressions;

// Native messaging's stdout is protocol only. No account, prompt or file data
// is logged. The executable never installs itself or starts the Buddy desktop.
internal static partial class Program
{
    internal sealed record Policy(int Version, string ExtensionId, string PipeName);
    [GeneratedRegex("^[a-p]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex ExtensionIdPattern();
    [GeneratedRegex("^Buddy\\.BrowserContext\\.v1\\.[a-f0-9]{24}$", RegexOptions.CultureInvariant)]
    private static partial Regex PipePattern();
    private static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsWindows()) return 2;
        try
        {
            // Chromium supplies the calling extension's origin, then possibly
            // a parent-window argument. No command or path arguments are used.
            if (args.Length is < 1 or > 2) return 2;
            string path = Path.Combine(AppContext.BaseDirectory, "browser-host-policy.json");
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length is <= 0 or > 4096) return 2;
            var policy = await JsonSerializer.DeserializeAsync<Policy>(file, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (policy is null || policy.Version != 1 || !ExtensionIdPattern().IsMatch(policy.ExtensionId ?? "") ||
                policy.PipeName is not string pipeName || !PipePattern().IsMatch(pipeName) || args[0] != "chrome-extension://" + policy.ExtensionId + "/") return 2;
            using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(20));
            using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(3000, lifetime.Token);
            using Stream input = Console.OpenStandardInput(), output = Console.OpenStandardOutput();
            async Task Pump(Stream from,Stream to)
            {
                while(!lifetime.IsCancellationRequested)
                {
                    byte[]? frame=await BrowserNativeTransport.ReadAsync(from,lifetime.Token);
                    if(frame is null)return;
                    try{await BrowserNativeTransport.WriteAsync(to,frame,lifetime.Token);}
                    finally{Array.Clear(frame);}
                }
            }
            // Read the desktop pipe even while no extension request is pending.
            // Buddy Stop/disconnect must close the native port while a content
            // script is awaiting a DOM/attachment observation.
            Task inbound=Pump(input,pipe),outbound=Pump(pipe,output);
            try{await await Task.WhenAny(inbound,outbound);return 0;}
            finally
            {
                lifetime.Cancel();pipe.Dispose();input.Dispose();
                try{await Task.WhenAll(inbound,outbound).WaitAsync(TimeSpan.FromSeconds(1));}
                catch(Exception stopped)when(stopped is IOException or InvalidDataException or ObjectDisposedException or OperationCanceledException or TimeoutException){ }
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or OperationCanceledException or ArgumentException)
        {
            // Fixed exit status only. Never echo protocol exceptions/private data.
            return 3;
        }
    }
}
