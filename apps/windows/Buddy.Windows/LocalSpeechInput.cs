using System.Speech.Recognition;
using System.Speech.AudioFormat;
using System.Collections.Concurrent;

namespace Buddy.Windows;

internal static class LocalSpeechInput
{
    private static readonly SemaphoreSlim gate = new(1, 1);
    private static readonly ConcurrentDictionary<SpeechRecognitionEngine, MicrophoneStream> inputs = new();
    internal static Func<DesktopPreferences> Preferences { get; set; } = () => new();
    internal static string[] Languages() => SpeechRecognitionEngine.InstalledRecognizers().Select(r => r.Culture.Name).Distinct().ToArray();
    internal static async Task<SpeechRecognitionEngine> Create(CancellationToken ct, DesktopPreferences? preferences = null)
    {
        preferences ??= Preferences();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var token = timeout.Token;
        await gate.WaitAsync(token);
        var worker = Task.Run(() => {
            SpeechRecognitionEngine? engine = null;
            try {
                token.ThrowIfCancellationRequested();
                var installed = SpeechRecognitionEngine.InstalledRecognizers();
                if (installed.Count == 0) throw new InvalidOperationException("Install a Windows speech language to use dictation.");
                var language = preferences.RecognitionLanguage.Length > 0 ? preferences.RecognitionLanguage : System.Globalization.CultureInfo.CurrentUICulture.Name;
                var selected = installed.FirstOrDefault(r => r.Culture.Name == language)
                    ?? throw new InvalidOperationException("Choose an installed speech language in Voice settings; Buddy will not guess a different language.");
                engine = new(selected);
                engine.LoadGrammar(new DictationGrammar());
                var commands = new GrammarBuilder { Culture = selected.Culture };
                commands.Append(new Choices("Open Comet", "Open Comet Browser", "Open Notepad", "Open Calculator", "Open Explorer"));
                engine.LoadGrammar(new Grammar(commands) { Name = "Installed application requests", Weight = .8f });
                var microphone = new MicrophoneStream(preferences.MicrophoneId);
                inputs[engine] = microphone;
                engine.SetInputToAudioStream(microphone, new SpeechAudioFormatInfo(16000, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
                token.ThrowIfCancellationRequested(); return engine;
            } catch { if (engine is not null) { if (inputs.TryRemove(engine, out var microphone)) microphone.Dispose(); engine.Dispose(); } throw; }
            finally { gate.Release(); }
        }, CancellationToken.None);
        try { return await worker.WaitAsync(token); }
        catch { _ = DisposeLate(worker); throw; }
    }
    private static async Task DisposeLate(Task<SpeechRecognitionEngine> worker)
    {
        try { var engine = await worker.ConfigureAwait(false); Stop(engine); } catch { }
    }
    internal static void Stop(SpeechRecognitionEngine? engine)
    {
        if (engine is null) return;
        if (inputs.TryRemove(engine, out var microphone)) microphone.Dispose();
        _ = Task.Run(() => { try { engine.RecognizeAsyncCancel(); } catch { } finally { engine.Dispose(); } });
    }
}
