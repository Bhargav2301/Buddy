using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Buddy.Windows;

internal sealed record NeuralVoiceChoice(int Id, string Label, string? Speaker = null, string Preset = "")
{
    public override string ToString() => Label;
}

// A fixed, optional local engine, separate from the action/tool runner.
// Private text goes only through redirected stdin; audio goes only through stdout.
internal sealed class NeuralSpeechSynthesizer : IDisposable
{
    internal static readonly NeuralVoiceChoice[] Voices = [
        new(95, "Voice A · VCTK p226", "p226"), new(82, "Voice B · VCTK p227", "p227"), new(60, "Voice C · VCTK p232", "p232"),
        new(107, "Voice D · Female · VCTK p225", "p225"), new(90, "Voice E · Female · VCTK p228", "p228"), new(85, "Voice F · Female · VCTK p229", "p229"),
        new(85, "Voice F3 · Light and flowing", "p229", "f3")
    ];
    internal static NeuralVoiceChoice DefaultVoice => Voices.Single(v => v.Id == DesktopPreferences.DefaultNeuralSpeakerId);
    internal static string DefaultRoot => Path.Combine(AppContext.BaseDirectory, "local-voice");
    internal static bool Available => FilesAvailable(DefaultRoot, Path.Combine(AppContext.BaseDirectory, "piper_worker.py"));
    private static bool FilesAvailable(string root, string script) => File.Exists(Path.Combine(root, "runtime", "Scripts", "python.exe")) && File.Exists(Path.Combine(root, "models", "en_GB-vctk-medium.onnx")) && File.Exists(Path.Combine(root, "models", "en_GB-vctk-medium.onnx.json")) && File.Exists(script);
    private readonly string root, script;
    private readonly object sync = new();
    private readonly SemaphoreSlim serial = new(1, 1);
    private readonly System.Threading.Timer idle;
    private Process? worker;
    private string workerPreset = "";
    private bool disposed;
    private bool rendering;
    private DateTime idleSince;
    internal int? ProcessId { get { lock (sync) { return worker is { HasExited: false } ? worker.Id : null; } } }
    internal NeuralSpeechSynthesizer(string? runtimeRoot = null, string? workerScript = null)
    {
        root = runtimeRoot ?? DefaultRoot; script = workerScript ?? Path.Combine(AppContext.BaseDirectory, "piper_worker.py");
        idle = new(_ => { lock (sync) { if (!rendering && DateTime.UtcNow - idleSince >= TimeSpan.FromSeconds(60)) Stop(null); } }, null, Timeout.Infinite, Timeout.Infinite);
    }
    internal Task<byte[]> Render(string text, DesktopPreferences preferences, CancellationToken cancellation)
        => Render(text, preferences.NeuralSpeakerId, preferences.VoiceRate, cancellation, preferences.NeuralPreset);
    internal async Task<byte[]> Render(string text, int speaker, int rate, CancellationToken cancellation, string preset = "")
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 1600 || !Voices.Any(v => v.Id == speaker && v.Preset == preset) || rate is < -3 or > 2) throw new InvalidOperationException("Voice text, speaker, preset or pace is outside the supported limits.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        await serial.WaitAsync(deadline.Token);
        Process? process = null; byte[]? audio = null;
        try {
            lock (sync) {
                ObjectDisposedException.ThrowIf(disposed, this);
                deadline.Token.ThrowIfCancellationRequested();
                idle.Change(Timeout.Infinite, Timeout.Infinite);
                rendering = true;
                if (worker is not null && workerPreset != preset) Stop(worker);
                if (worker is null || worker.HasExited) {
                    worker?.Dispose(); worker = null;
                    if (!FilesAvailable(root, script)) throw new InvalidOperationException("Piper is unavailable. Select a Windows voice or restore the approved local voice files.");
                    var start = new ProcessStartInfo(Path.Combine(root, "runtime", "Scripts", "python.exe")) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardInputEncoding = new UTF8Encoding(false), WorkingDirectory = root };
                    start.ArgumentList.Add("-I"); start.ArgumentList.Add("-B"); start.ArgumentList.Add(script); start.ArgumentList.Add(root);
                    if (preset.Length > 0) start.ArgumentList.Add(preset);
                    worker = Process.Start(start) ?? throw new InvalidOperationException("The local voice worker could not start.");
                    workerPreset = preset;
                    // Drain diagnostics without retaining private text or blocking a full pipe.
                    _ = Drain(worker.StandardError);
                }
                process = worker;
            }
            using var stop = deadline.Token.Register(() => Stop(process));
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { text, speaker, rate, preset }).AsMemory(), deadline.Token);
            await process.StandardInput.FlushAsync(deadline.Token);
            var headerSize = new byte[4]; await process.StandardOutput.BaseStream.ReadExactlyAsync(headerSize, deadline.Token);
            int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(headerSize);
            if (length is < 2 or > 4096) throw new InvalidDataException("Invalid local voice response.");
            var header = new byte[length]; await process.StandardOutput.BaseStream.ReadExactlyAsync(header, deadline.Token);
            using var response = JsonDocument.Parse(header);
            if (response.RootElement.TryGetProperty("error", out _)) throw new InvalidOperationException("Local neural speech failed. Check the approved voice files; speech stayed muted.");
            int bytes = response.RootElement.GetProperty("audioBytes").GetInt32();
            if (bytes is < 44 or > 12 * 1024 * 1024) throw new InvalidDataException("Local voice audio exceeded its limit.");
            audio = new byte[bytes]; await process.StandardOutput.BaseStream.ReadExactlyAsync(audio, deadline.Token);
            deadline.Token.ThrowIfCancellationRequested();
            var result = audio; audio = null; return result;
        } catch (Exception ex) when (deadline.IsCancellationRequested) {
            Stop(process);
            if (cancellation.IsCancellationRequested) throw new OperationCanceledException("Speech stopped.", ex, cancellation);
            throw new TimeoutException("Local speech took too long and was stopped.", ex);
        } catch (IOException ex) { Stop(process); throw new InvalidOperationException("The local voice worker stopped unexpectedly. Speech stayed muted; select a Windows voice or review the local voice installation.", ex); }
        catch { Stop(process); throw; }
        finally {
            if (audio is not null) Array.Clear(audio);
            lock (sync) { rendering = false; if (!disposed && ReferenceEquals(worker, process) && worker is not null) { idleSince = DateTime.UtcNow; idle.Change(TimeSpan.FromSeconds(60), Timeout.InfiniteTimeSpan); } }
            serial.Release();
        }
    }
    private static async Task Drain(StreamReader reader)
    {
        try { var buffer = new char[512]; while (await reader.ReadAsync(buffer) > 0) Array.Clear(buffer); }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
    }
    private void Stop(Process? expected)
    {
        lock (sync) {
            if (expected is not null && !ReferenceEquals(expected, worker)) return;
            var old = worker; worker = null;
            if (old is null) return;
            try { if (!old.HasExited) old.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            finally { old.Dispose(); }
        }
    }
    internal void Cancel() => Stop(null);
    public void Dispose() { lock (sync) { if (disposed) return; disposed = true; idle.Dispose(); Stop(null); } }
}
