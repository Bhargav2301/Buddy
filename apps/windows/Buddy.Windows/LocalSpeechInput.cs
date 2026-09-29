using System.Speech.Recognition;

namespace Buddy.Windows;

internal static class LocalSpeechInput
{
    private static readonly SemaphoreSlim gate = new(1, 1);
    internal static async Task<SpeechRecognitionEngine> Create(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var token = timeout.Token;
        await gate.WaitAsync(token);
        var worker = Task.Run(() => {
            SpeechRecognitionEngine? engine = null;
            try {
                token.ThrowIfCancellationRequested();
                var installed = SpeechRecognitionEngine.InstalledRecognizers();
                if (installed.Count == 0) throw new InvalidOperationException("Install a Windows speech language to use dictation.");
                engine = new(installed.FirstOrDefault(r => r.Culture.Name == System.Globalization.CultureInfo.CurrentUICulture.Name) ?? installed[0]);
                engine.LoadGrammar(new DictationGrammar()); engine.SetInputToDefaultAudioDevice();
                token.ThrowIfCancellationRequested(); return engine;
            } catch { engine?.Dispose(); throw; }
            finally { gate.Release(); }
        }, CancellationToken.None);
        try { return await worker.WaitAsync(token); }
        catch { _ = DisposeLate(worker); throw; }
    }
    private static async Task DisposeLate(Task<SpeechRecognitionEngine> worker)
    {
        try { var engine = await worker.ConfigureAwait(false); engine.Dispose(); } catch { }
    }
    internal static void Stop(SpeechRecognitionEngine? engine)
    {
        if (engine is null) return;
        _ = Task.Run(() => { try { engine.RecognizeAsyncCancel(); } catch { } finally { engine.Dispose(); } });
    }
}
