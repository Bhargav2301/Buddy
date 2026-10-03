using Buddy.Windows;
using NAudio.Wave;
using System.Diagnostics;
using System.IO;

internal static class F3Checks
{
    internal static int Reload(string path)
    {
        var p = DesktopPreferences.Load(path);
        return p.VoiceEngine == "piper" && p.NeuralSpeakerId == 85 && p.NeuralPreset == "f3" && p.VoiceRate == 2 && p.HeadphonesOnly ? 0 : 1;
    }
    internal static async Task<int> Run(string runtime, string audition, string output)
    {
        int count = 0; Directory.CreateDirectory(output);
        void Check(bool value, string text) { if (!value) throw new Exception("FAIL: " + text); count++; Console.WriteLine("PASS: " + text); }
        try {
            Check(new DesktopPreferences().NeuralSpeakerId == 60 && new DesktopPreferences().NeuralPreset == "", "C remains the default without an F3 preset");
            Check(NeuralSpeechSynthesizer.Voices.Any(v => v.Id == 82 && v.Preset == "") && NeuralSpeechSynthesizer.Voices.Any(v => v.Id == 85 && v.Preset == "") && NeuralSpeechSynthesizer.Voices.Any(v => v.Id == 85 && v.Preset == "f3"), "B, original F and F3 remain separate choices");
            var prefs = new DesktopPreferences { VoiceEngine = "piper", NeuralSpeakerId = 85, NeuralPreset = "f3", VoiceRate = 2, HeadphonesOnly = true };
            var path = Path.Combine(output, "f3-persisted.json"); prefs.Save(path);
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--f3-reload"); start.ArgumentList.Add(path);
            using (var child = Process.Start(start)!) { await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); Check(child.ExitCode == 0, "A fresh process reloads the saved F3 choice and other voice preferences"); }
            var reloaded = DesktopPreferences.Load(path);
            string phrase = "I'm ready. I can open Comet Browser after you approve the plan. Press Control plus Shift plus Space whenever you'd like to talk.";
            using var neural = new NeuralSpeechSynthesizer(runtime, Path.Combine(AppContext.BaseDirectory, "piper_worker.py"));
            var audio = await neural.Render(phrase, reloaded, default);
            await File.WriteAllBytesAsync(Path.Combine(output, "F3-runtime.wav"), audio);
            Check(audio.SequenceEqual(await File.ReadAllBytesAsync(audition)), "Fresh production F3 audio is byte-identical to the approved audition, including pace, pauses and volume");
            using (var wav = new WaveFileReader(new MemoryStream(audio))) {
                double peak = 0; int clipped = 0; float[]? frame;
                while ((frame = wav.ReadNextSampleFrame()) != null) { peak = Math.Max(peak, Math.Abs(frame[0])); if (Math.Abs(frame[0]) >= .9999) clipped++; }
                Check(peak is > .89 and < .901 && clipped == 0, "Runtime F3 preserves 0.90 amplitude headroom with no clipping");
            }
            int f3Pid = neural.ProcessId!.Value;
            var original = await neural.Render("Original F remains available.", 85, -1, default);
            Check(original.Length > 44 && neural.ProcessId != f3Pid, "Switching to original F removes the F3 preset worker"); Array.Clear(original);
            var again = await neural.Render(phrase, reloaded with { VoiceRate = -3 }, default);
            Check(again.SequenceEqual(audio), "Returning to F3 restores the exact audition regardless of the general speaking pace"); Array.Clear(again);
            bool rejected = false;
            try { await neural.Render("Must stay silent.", 60, -1, default, "f3"); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "F3 cannot be applied to a different speaker");
            rejected = false;
            try { await neural.Render("Must stay silent.", 85, -1, default, "unknown"); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "Unrecognized preset names fail closed");
            using var cancel = new CancellationTokenSource();
            var rendering = neural.Render(string.Join(" ", Enumerable.Repeat("Please review the plan carefully before approving any action.", 23)), reloaded, cancel.Token);
            await Task.Delay(80); Check(!rendering.IsCompleted, "Stop reaches active F3 synthesis");
            var tree = NeuralVoiceChecks.Tree(neural.ProcessId!.Value); var timer = Stopwatch.StartNew(); cancel.Cancel(); bool stopped = false;
            try { await rendering; } catch (OperationCanceledException) { stopped = true; }
            bool Alive(int pid) { try { using var p = Process.GetProcessById(pid); return !p.HasExited; } catch (ArgumentException) { return false; } }
            for (int i = 0; i < 50 && tree.Any(Alive); i++) await Task.Delay(20);
            Check(stopped && neural.ProcessId == null && !tree.Any(Alive) && timer.ElapsedMilliseconds < 1000, "Stop cancels F3 and terminates its complete worker tree within one second");
            Console.WriteLine("F3 Stop ms: " + timer.Elapsed.TotalMilliseconds); Array.Clear(audio);
            Console.WriteLine($"ALL {count} F3 PRESET CHECKS PASSED"); return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
