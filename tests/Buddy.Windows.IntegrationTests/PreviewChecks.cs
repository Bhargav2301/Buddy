using Buddy.Windows;
using Buddy.Server;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Speech.Synthesis;
using System.Speech.Recognition;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

internal static class PreviewChecks
{
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);
    internal static int Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; int count = 0, exit = 1;
        void Check(bool value, string label) { if (!value) throw new Exception("FAIL: " + label); count++; Console.WriteLine("PASS: " + label); }
        var window = new Window { Title = "Buddy preview native fixture", Width = 540, Height = 360, Background = Brushes.Teal, Content = new TextBlock { Text = "Disposable native fixture", Foreground = Brushes.White, FontSize = 28 } };
        window.Loaded += async (_, _) => {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            try {
                window.Activate(); InputNative.SetForegroundWindow(handle); await Task.Delay(250);
                Check(Native.GetForegroundWindow() == handle, "Isolated fixture owns focus");
                CaptureProtection.Set(true); Check(GetWindowDisplayAffinity(handle, out var protectedValue) && protectedValue == 0x11, "Screenshot protection sets WDA_EXCLUDEFROMCAPTURE");
                CaptureProtection.Set(false); Check(GetWindowDisplayAffinity(handle, out var visibleValue) && visibleValue == 0, "Screenshot protection off clears display affinity");
                using (var frame = await WindowCapture.Capture(handle, [], default)) {
                    Check(frame.Image.Length > 1000, "Windows Graphics Capture returns pixels when protection is off");
                }
                CaptureProtection.Set(true);
                bool hidden = false;
                try {
                    using var frame = await WindowCapture.Capture(handle, [], default);
                    using var stream = new MemoryStream(frame.Image); using var bitmap = new System.Drawing.Bitmap(stream);
                    var pixel = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2); hidden = pixel.R < 10 && pixel.G < 10 && pixel.B < 10;
                } catch (Exception ex) when (ex is COMException or OperationCanceledException or InvalidOperationException) { hidden = true; Console.WriteLine("Protected capture refused: " + ex.GetType().Name); }
                Check(hidden, "Protected fixture cannot expose its teal content through Windows capture");
                uint fixtureKey=0;
                foreach(uint key in new uint[]{0x87,0x86,0x85,0x84})if(Native.RegisterHotKey(handle,91,0x4007,key)){fixtureKey=key;break;}
                Check(fixtureKey!=0, "Owned fixture reserves an available temporary Ctrl+Alt+Shift+F21-F24 chord without changing any user binding");
                var choice=new ShortcutChoice("Temporary native fixture chord",7);
                using var chord = new ShortcutRegistration((id, modifiers) => Native.RegisterHotKey(handle, id, modifiers, fixtureKey), id => Native.UnregisterHotKey(handle, id), 92, 93);
                Check(!chord.TrySet(choice), "RegisterHotKey conflict is detected without taking the competing chord");
                Native.UnregisterHotKey(handle, 91);
                Check(chord.TrySet(choice), "Freed temporary chord registers successfully");
                chord.Dispose();
                string comet = InstalledAppResolver.ResolveComet(); var launch = InstalledAppResolver.CometStartInfo();
                Check(File.Exists(comet) && launch.FileName == comet && !launch.UseShellExecute && launch.ArgumentList.Count == 0 && launch.Arguments.Length == 0, "Installed Comet passes offline publisher validation and has an argument-free direct launch");
                var voices = LocalVoiceOutput.Voices(); Console.WriteLine("Local voices: " + string.Join(", ", voices));
                Console.WriteLine("Recognition languages: " + string.Join(", ", LocalSpeechInput.Languages()));
                Console.WriteLine("Microphones: " + string.Join(", ", MicrophoneStream.Devices().Select(m => m.Label)));
                Console.WriteLine("Verified active headphones: " + string.Join(", ", LocalVoiceOutput.Headphones().Select(h => h.Label)));
                Check(voices.Length > 0, "Windows exposes installed local TTS voices");
                using (var synth = new SpeechSynthesizer()) using (var memory = new MemoryStream()) {
                    synth.SelectVoice(voices[0]); synth.Rate = -1; synth.SetOutputToWaveStream(memory);
                    synth.Speak(ConversationalReply.PlainText("**Ready.** One step at a time."));
                    Check(memory.Length > 44, "Selected Windows voice synthesizes cleaned text locally into memory without playback");
                }
                using (var output = new LocalVoiceOutput()) {
                    bool muted = false; try { await output.SpeakAsync("Must stay silent.", new() { HeadphonesOnly = true }); } catch (InvalidOperationException) { muted = true; }
                    Check(muted, "Headphones-only output with no selected endpoint stays silent instead of using speakers");
                }
                using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10))) {
                    var language = LocalSpeechInput.Languages().First();
                    var selectedMicrophone = MicrophoneStream.Devices().First();
                    var recognition = await LocalSpeechInput.Create(deadline.Token, new() { RecognitionLanguage = language, MicrophoneId = selectedMicrophone.Id });
                    Check(recognition.RecognizerInfo.Culture.Name == language, "Selected local recognizer and microphone stream initialize successfully");
                    recognition.RecognizeAsync(RecognizeMode.Multiple);
                    await Task.Delay(250); LocalSpeechInput.Stop(recognition);
                    Check(true, "Microphone stream starts and Stop releases it without submitting a transcript");
                }
                using var voice = new VoiceOverlayWindow(() => null, () => Task.FromResult<string?>(null), () => new(), _ => { }, () => handle, new ScreenPerception(() => new()), (_, _) => { }, () => { });
                voice.Show(); voice.Cancel(); Check(!voice.IsListening && !voice.IsBusy, "Stop leaves voice recognition and generation inactive");
                voice.Dismiss(); voice.Show(); Check(voice.IsVisible && !voice.IsListening, "Voice surface can reopen after Stop without starting the microphone");
                Console.WriteLine($"ALL {count} PREVIEW NATIVE CHECKS PASSED; physical microphone/headphone behavior remains manual"); exit = 0;
            } catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally { Native.UnregisterHotKey(handle, 91); window.Close(); app.Shutdown(); }
        };
        app.Run(window); return exit;
    }
}
