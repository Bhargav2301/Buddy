using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Buddy.Windows;

// Runs only after a typed confirmation in Settings. No HTTP endpoint can erase data.
internal static class LocalDataDeletion
{
    private const string PipePrefix = "Buddy.Erase.";
    internal static async Task Prepare()
    {
        string channel = PipePrefix + Guid.NewGuid().ToString("N");
        using var pipe = new NamedPipeServerStream(channel, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var parent = Process.GetCurrentProcess();
        var command = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        command.ArgumentList.Add("--delete-local-data-after");
        command.ArgumentList.Add(parent.Id.ToString());
        command.ArgumentList.Add(parent.StartTime.ToUniversalTime().Ticks.ToString());
        command.ArgumentList.Add(channel);
        using var child = Process.Start(command) ?? throw new InvalidOperationException("Could not prepare local data deletion.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try {
            await pipe.WaitForConnectionAsync(timeout.Token);
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pid) || pid != child.Id)
                throw new InvalidOperationException("The deletion helper could not be verified.");
            await pipe.WriteAsync(new byte[] { 0xE1 }, timeout.Token);
            var ack = new byte[1];
            if (await pipe.ReadAsync(ack, timeout.Token) != 1 || ack[0] != 0xA1)
                throw new InvalidOperationException("The deletion helper did not acknowledge the request.");
        } catch {
            if (!child.HasExited) child.Kill();
            throw;
        }
    }

    // Called before logging or WPF initialization, so this process cannot recreate Buddy data.
    internal static int Run(string[] args)
    {
        try {
            if (args.Length != 4 || !int.TryParse(args[1], out int pid) || !long.TryParse(args[2], out long started) ||
                !args[3].StartsWith(PipePrefix, StringComparison.Ordinal) ||
                !Guid.TryParseExact(args[3][PipePrefix.Length..], "N", out _))
                throw new InvalidOperationException("Use Settings → Screen & Privacy to delete local data.");
            using var parent = Process.GetProcessById(pid);
            if (parent.Id == Environment.ProcessId || parent.StartTime.ToUniversalTime().Ticks != started ||
                !string.Equals(parent.MainModule?.FileName, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The Buddy process could not be verified.");
            using (var pipe = new NamedPipeClientStream(".", args[3], PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly)) {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                pipe.ConnectAsync(timeout.Token).GetAwaiter().GetResult();
                if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var server) || server != parent.Id)
                    throw new InvalidOperationException("The deletion request did not come from Buddy.");
                var authorization = new byte[1];
                if (pipe.ReadAsync(authorization, timeout.Token).AsTask().GetAwaiter().GetResult() != 1 || authorization[0] != 0xE1)
                    throw new InvalidOperationException("Deletion was not confirmed.");
                pipe.WriteAsync(new byte[] { 0xA1 }, timeout.Token).AsTask().GetAwaiter().GetResult();
            }
            if (!parent.WaitForExit(120_000)) throw new InvalidOperationException("Buddy has not exited. No data was deleted.");
            using var single = new Mutex(false, "Local\\Buddy.Desktop.v1");
            bool acquired;
            try { acquired = single.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new InvalidOperationException("Buddy restarted before deletion. Quit it and try again from Settings.");
            try { LocalDataTree.Delete(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)); }
            finally { single.ReleaseMutex(); }
            Program.ShowMessage("Buddy's local conversations, memories, prompts, walkthroughs, pairings, settings, keys and logs were deleted.\n\nDownloaded Ollama models remain available. Open Buddy to start fresh.");
            return 0;
        } catch (Exception ex) {
            Program.ShowMessage("Local data deletion did not complete.\n\n" + ex.Message + "\n\nAny remaining data is in %LOCALAPPDATA%\\Buddy.");
            return 1;
        }
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint processId);
}
