using Buddy.Windows;
using Buddy.Server;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

internal static class NeuralVoiceChecks
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct Entry { public uint Size, Usage, Id; public UIntPtr Heap; public uint Module, Threads, Parent; public int Priority; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name; }
    [DllImport("kernel32.dll")] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint process);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32FirstW(IntPtr handle, ref Entry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32NextW(IntPtr handle, ref Entry entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    internal static int[] Tree(int root)
    {
        var all = new List<(int Id, int Parent)>(); var snapshot = CreateToolhelp32Snapshot(2, 0);
        try { var entry = new Entry { Size = (uint)Marshal.SizeOf<Entry>(), Name = "" }; if (Process32FirstW(snapshot, ref entry)) do { all.Add(((int)entry.Id, (int)entry.Parent)); } while (Process32NextW(snapshot, ref entry)); }
        finally { CloseHandle(snapshot); }
        var ids = new HashSet<int> { root }; bool changed; do { changed = false; foreach (var pair in all) if (ids.Contains(pair.Parent)) changed |= ids.Add(pair.Id); } while (changed); return ids.ToArray();
    }
    private static bool Alive(int id) { try { using var process = Process.GetProcessById(id); return !process.HasExited; } catch (ArgumentException) { return false; } }
    internal static async Task<int> Run(string runtime, string samples)
    {
        int checks = 0;
        void Check(bool condition, string label) { if (!condition) throw new Exception("FAIL: " + label); checks++; Console.WriteLine("PASS: " + label); }
        string script = Path.Combine(AppContext.BaseDirectory, "piper_worker.py");
        var measurements = new List<object>(); Directory.CreateDirectory(samples);
        try {
            Check(new DesktopPreferences().NeuralSpeakerId == 60 && NeuralSpeechSynthesizer.DefaultVoice.Speaker == "p232", "New preferences and UI fallback use owner-selected Voice C");
            Check(NeuralSpeechSynthesizer.Voices.Any(v => v.Id == 82 && v.Speaker == "p227"), "Voice B remains available alongside the selected default");
            using var modelConfig = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(runtime, "models", "en_GB-vctk-medium.onnx.json")));
            string cleaned = SpeechText.Prepare("## **Ready.** [Open Comet Browser](https://example.com) only after you approve. Use `Ctrl+Shift+Space` for voice. [1]");
            Check(cleaned == "Ready. Open Comet Browser only after you approve. Use Control plus Shift plus Space for voice.", "Speech cleanup preserves approval language, removes formatting and speaks shortcut names");
            Check(SpeechText.Prepare("J.A.R.V.I.S. &amp; [[words]]") == "Jarvis & words", "Entity decoding and pronunciation cleanup remove raw phoneme markup");
            Check(ConversationalReply.Sentences(cleaned) == 3, "Cleaned sample remains three sentences with its approval qualification");
            using var neural = new NeuralSpeechSynthesizer(runtime, script);
            string phrase = SpeechText.Prepare("I'm ready. I can open Comet Browser after you approve the plan. Press Ctrl+Shift+Space whenever you'd like to talk.");
            int? firstPid = null;
            for (int i = 0; i < NeuralSpeechSynthesizer.Voices.Length; i++) {
                var voice = NeuralSpeechSynthesizer.Voices[i]; var timer = Stopwatch.StartNew();
                Check(modelConfig.RootElement.GetProperty("speaker_id_map").GetProperty(voice.Speaker!).GetInt32() == voice.Id, voice.Speaker + " uses the installed model's explicit speaker mapping");
                var data = await neural.Render(phrase, voice.Id, -1, default, voice.Preset); timer.Stop();
                using var wave = new WaveFileReader(new MemoryStream(data));
                Check(wave.WaveFormat.SampleRate == 22050 && wave.WaveFormat.BitsPerSample == 16 && wave.WaveFormat.Channels == 1 && wave.TotalTime.TotalSeconds > 3, voice.Label + " generates valid mono 22.05 kHz speech");
                string path = Path.Combine(samples, $"Buddy-Voice-{(voice.Preset == "f3" ? "F3" : ((char)('A' + i)).ToString())}.wav"); await File.WriteAllBytesAsync(path, data);
                using var process = Process.GetProcessById(neural.ProcessId!.Value); process.Refresh();
                var tree = Tree(process.Id); long workingSet = 0, peakSet = 0;
                foreach (int id in tree) { using var member = Process.GetProcessById(id); member.Refresh(); workingSet += member.WorkingSet64; peakSet += member.PeakWorkingSet64; }
                var metrics = new { voice = voice.Label, cold = i == 0, elapsedMs = timer.Elapsed.TotalMilliseconds, durationSeconds = wave.TotalTime.TotalSeconds, realTimeFactor = timer.Elapsed.TotalSeconds / wave.TotalTime.TotalSeconds, workingSetMiB = workingSet / 1048576.0, summedProcessPeaksMiB = peakSet / 1048576.0, workerId = process.Id, processTree = tree, path };
                measurements.Add(metrics); Console.WriteLine(JsonSerializer.Serialize(metrics));
                var choicePath = Path.Combine(samples, "selection-" + voice.Speaker + voice.Preset + ".json");
                new DesktopPreferences { VoiceEngine = "piper", NeuralSpeakerId = voice.Id, NeuralPreset = voice.Preset, VoiceRate = -1 }.Save(choicePath);
                var persisted = DesktopPreferences.Load(choicePath);
                Check(persisted.NeuralSpeakerId == voice.Id && persisted.NeuralPreset == voice.Preset, voice.Speaker + voice.Preset + " selection persists after reload");
                if (firstPid is null) firstPid = process.Id; else Check(voice.Preset == "f3" ? firstPid != process.Id : firstPid == process.Id, voice.Preset == "f3" ? "F3 uses its audition initialization without changing other voices" : "Changing speaker reuses the loaded local model");
                Array.Clear(data);
            }
            async Task<bool> Rejected(string text, int speaker, int rate) { try { await neural.Render(text, speaker, rate, default); return false; } catch (InvalidOperationException) { return true; } }
            Check(await Rejected(new string('x', 1601), 95, -1) && await Rejected("Hello", 999, -1) && await Rejected("Hello", 95, 9), "Oversized text and unsupported voice/rate values fail closed");
            var settingsPath = Path.Combine(samples, "voice-prefs-fixture.json");
            new DesktopPreferences { VoiceEngine = "piper", NeuralSpeakerId = 82, VoiceRate = -2, HeadphonesOnly = true }.Save(settingsPath);
            var saved = DesktopPreferences.Load(settingsPath); Check(saved.VoiceEngine == "piper" && saved.NeuralSpeakerId == 82 && saved.HeadphonesOnly && saved.VoiceRate == -2, "Neural voice, pace and headphone policy persist together");
            // A long warm request is interrupted while the real engine is synthesizing.
            string longText = string.Join(" ", Enumerable.Repeat("Please review the plan carefully before approving any action.", 23));
            using (var cancel = new CancellationTokenSource()) {
                var rendering = neural.Render(longText, 107, -1, cancel.Token); await Task.Delay(80);
                Check(!rendering.IsCompleted, "Cancellation fixture reaches active neural synthesis");
                var workerTree = Tree(neural.ProcessId!.Value);
                var timer = Stopwatch.StartNew(); cancel.Cancel(); bool stopped = false;
                try { await rendering; } catch (OperationCanceledException) { stopped = true; }
                timer.Stop(); Check(stopped && neural.ProcessId is null && timer.ElapsedMilliseconds < 1000, "Cancellation terminates the active worker within one second");
                for (int attempt = 0; attempt < 50 && workerTree.Any(Alive); attempt++) await Task.Delay(20);
                Check(!workerTree.Any(Alive), "Stop terminates both the Python launcher and synthesis child process");
                Console.WriteLine("Cancellation completion ms: " + timer.Elapsed.TotalMilliseconds);
            }
            var recovered = await neural.Render("Ready again.", DesktopPreferences.DefaultNeuralSpeakerId, -1, default);
            Check(recovered.Length > 44 && neural.ProcessId != firstPid, "A new explicit request works after Stop without resuming old speech"); Array.Clear(recovered); neural.Cancel();
            using (var missing = new NeuralSpeechSynthesizer(Path.Combine(samples, "missing-engine"), script)) {
                bool failed = false; try { await missing.Render("Must stay silent.", 95, -1, default); } catch (InvalidOperationException) { failed = true; }
                Check(failed && missing.ProcessId is null, "Missing local voice assets fail without starting an alternative engine");
            }
            using (var output = new LocalVoiceOutput(new NeuralSpeechSynthesizer(runtime, script))) {
                bool muted = false; try { await output.SpeakAsync("Must stay silent.", new() { VoiceEngine = "piper", HeadphonesOnly = true, HeadphoneDeviceId = "" }); } catch (InvalidOperationException) { muted = true; }
                Check(muted, "Piper output without a selected headphone endpoint stays muted");
            }
            // Invoke the production OS-notification callbacks during real synthesis.
            // No physical device is changed, and no playback begins in these checks.
            using var devices = new MMDeviceEnumerator();
            using var endpoint = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var headphones = LocalVoiceOutput.Headphones().FirstOrDefault();
            foreach (string notification in new[] { "removed", "default", "property" }) {
                using var renderer = new NeuralSpeechSynthesizer(runtime, script);
                using var output = new LocalVoiceOutput(renderer);
                var prefs = new DesktopPreferences { VoiceEngine = "piper", NeuralSpeakerId = 85, NeuralPreset = "f3", HeadphonesOnly = headphones is not null, HeadphoneDeviceId = headphones?.Id ?? "" };
                var speaking = output.SpeakAsync(longText, prefs); await Task.Delay(80);
                if (headphones is null && notification != "removed") {
                    // Exercise the production callback with a simulated headphone policy;
                    // this does not claim a physical headset is connected or change any device.
                    typeof(LocalVoiceOutput).GetField("route", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(output, new AudioRouteGuard(endpoint.ID, true, () => output.Cancel()));
                }
                Check(!speaking.IsCompleted && renderer.ProcessId is not null, notification + " callback fixture has an active worker");
                var watch = Stopwatch.StartNew();
                if (notification == "removed") output.OnDeviceRemoved(headphones?.Id ?? endpoint.ID);
                if (notification == "default") output.OnDefaultDeviceChanged(DataFlow.Render, Role.Multimedia, "different-fixture-output");
                if (notification == "property") output.OnPropertyValueChanged(headphones?.Id ?? endpoint.ID, default);
                bool stopped = false; try { await speaking; } catch (OperationCanceledException) { stopped = true; }
                Check(stopped && renderer.ProcessId is null && watch.ElapsedMilliseconds < 1000, notification + " callback cancels neural synthesis without speaker fallback" + (headphones is null ? " (simulated headphone policy where needed)" : ""));
            }
            await File.WriteAllTextAsync(Path.Combine(samples, "benchmark.json"), JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }));
            await File.WriteAllTextAsync(Path.Combine(samples, "sample-text.txt"), phrase);
            Console.WriteLine($"ALL {checks} NEURAL VOICE CHECKS PASSED; listening quality and physical headphone behavior remain user observations"); return 0;
        } catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
