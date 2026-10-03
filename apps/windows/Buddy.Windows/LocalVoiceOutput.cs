using Buddy.Server;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using System.Speech.Synthesis;

namespace Buddy.Windows;

internal sealed record AudioChoice(string Id, string Label)
{
    public override string ToString() => Label;
}

// Synthesis is entirely local and renders only into memory. WASAPI opens a concrete
// endpoint; it is never allowed to switch to a default device during playback.
internal sealed class LocalVoiceOutput : IDisposable, IMMNotificationClient
{
    private readonly object sync = new();
    private readonly MMDeviceEnumerator devices = new();
    private CancellationTokenSource? current;
    private SpeechSynthesizer? synthesizer;
    private NeuralSpeechSynthesizer? neural;
    private WasapiOut? player;
    private volatile string? endpoint;
    private volatile bool headphonesOnly;
    private bool disposed;
    private volatile AudioRouteGuard? route;
    internal LocalVoiceOutput(NeuralSpeechSynthesizer? neuralVoice = null) { neural = neuralVoice; devices.RegisterEndpointNotificationCallback(this); }
    internal static string[] Voices()
    {
        using var voice = new SpeechSynthesizer();
        return voice.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo.Name).ToArray();
    }
    internal static bool IsHeadphone(MMDevice device)
    {
        try { int form = Convert.ToInt32(device.Properties[PropertyKeys.PKEY_AudioEndpoint_FormFactor].Value); return form is 3 or 5; }
        catch { return false; }
    }
    internal static AudioChoice[] Headphones()
    {
        using var enumeration = new MMDeviceEnumerator();
        var result = new List<AudioChoice>();
        foreach (var device in enumeration.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            using (device) if (IsHeadphone(device)) result.Add(new(device.ID, device.FriendlyName));
        return result.ToArray();
    }
    internal async Task SpeakAsync(string text, DesktopPreferences preferences)
    {
        text = SpeechText.Prepare(text);
        if (text.Length == 0) return;
        if (text.Length > 1600) throw new InvalidOperationException("This text is too long to read aloud. Review the full answer on screen.");
        if (preferences.VoiceEngine is not ("windows" or "piper")) throw new InvalidOperationException("Select an available local voice engine.");
        Cancel(releaseNeural: false);
        using var source = new CancellationTokenSource();
        using var audio = new MemoryStream();
        MMDevice device;
        lock (sync) {
            if (disposed) throw new ObjectDisposedException(nameof(LocalVoiceOutput));
            if (preferences.HeadphonesOnly) {
                if (string.IsNullOrWhiteSpace(preferences.HeadphoneDeviceId)) throw new InvalidOperationException("Select connected headphones in Voice settings; speech stays muted.");
                device = devices.GetDevice(preferences.HeadphoneDeviceId);
                if (device.State != DeviceState.Active || !IsHeadphone(device)) { device.Dispose(); throw new InvalidOperationException("Selected headphones are unavailable; speech stays muted."); }
            } else device = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            current = source; endpoint = device.ID; headphonesOnly = preferences.HeadphonesOnly;
            route = new(endpoint, headphonesOnly, () => Cancel());
        }
        using (device) {
            try {
                if (preferences.VoiceEngine == "piper") {
                    neural ??= new NeuralSpeechSynthesizer();
                    var rendered = await neural.Render(text, preferences, source.Token);
                    try { audio.Write(rendered); } finally { Array.Clear(rendered); }
                } else await Task.Run(async () => {
                    using var synth = new SpeechSynthesizer();
                    var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    synth.SpeakCompleted += (_, e) => { if (e.Error is not null) completed.TrySetException(e.Error); else if (e.Cancelled) completed.TrySetCanceled(); else completed.TrySetResult(); };
                    lock (sync) {
                        source.Token.ThrowIfCancellationRequested(); synthesizer = synth;
                        if (preferences.VoiceName.Length > 0) synth.SelectVoice(preferences.VoiceName);
                        synth.Rate = Math.Clamp(preferences.VoiceRate, -3, 2);
                        synth.SetOutputToWaveStream(audio); synth.SpeakAsync(text);
                    }
                    try { await completed.Task.WaitAsync(source.Token); }
                    finally { lock (sync) { if (ReferenceEquals(synthesizer, synth)) synthesizer = null; } }
                }, source.Token);
                source.Token.ThrowIfCancellationRequested(); audio.Position = 0;
                using var reader = new WaveFileReader(audio);
                using var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 40);
                var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                output.PlaybackStopped += (_, e) => { if (e.Exception is not null) stopped.TrySetException(e.Exception); else stopped.TrySetResult(); };
                lock (sync) {
                    source.Token.ThrowIfCancellationRequested();
                    if (device.State != DeviceState.Active || (preferences.HeadphonesOnly && !IsHeadphone(device))) throw new InvalidOperationException("Audio device changed; speech stopped.");
                    player = output; output.Init(reader); output.Play();
                }
                try { await stopped.Task.WaitAsync(source.Token); }
                finally { lock (sync) { if (ReferenceEquals(player, output)) player = null; output.Stop(); } }
            } finally {
                lock (sync) { if (ReferenceEquals(current, source)) { current = null; endpoint = null; route = null; } }
                if (audio.TryGetBuffer(out var bytes)) Array.Clear(bytes.Array!, bytes.Offset, bytes.Count);
            }
        }
    }
    internal void Cancel(bool releaseNeural = true)
    {
        lock (sync) { current?.Cancel(); try { player?.Stop(); } catch { } try { synthesizer?.SpeakAsyncCancelAll(); } catch { } if (releaseNeural) neural?.Cancel(); }
    }
    // Event-driven cancellation, independent of the WPF dispatcher. Never retry or reroute.
    public void OnDeviceStateChanged(string id, DeviceState state) { if (state != DeviceState.Active) route?.DeviceUnavailable(id); }
    public void OnDeviceRemoved(string id) => route?.DeviceUnavailable(id);
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string id) { if (flow == DataFlow.Render) route?.DefaultRenderChanged(); }
    public void OnPropertyValueChanged(string id, PropertyKey key) => route?.PropertiesChanged(id);
    public void OnDeviceAdded(string id) { }
    public void Dispose() { lock (sync) { if (disposed) return; disposed = true; Cancel(); neural?.Dispose(); devices.UnregisterEndpointNotificationCallback(this); devices.Dispose(); } }
}
