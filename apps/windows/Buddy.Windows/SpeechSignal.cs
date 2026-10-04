namespace Buddy.Windows;

// Conservative speech evidence gate, not a language/identity detector. Native no-speech
// probability and mandatory voice transcript review remain separate protections.
internal static class SpeechSignal
{
    internal const int MaximumSamples=16000*30;
    internal static bool HasSpeech(ReadOnlySpan<float> samples)
    {
        if(samples.Length>MaximumSamples)throw new InvalidOperationException("Record at most 30 seconds at a time.");
        int voiced=0;double minimum=1,maximum=0;
        for(int start=0;start+320<=samples.Length;start+=320){
            double energy=0;int crossings=0;
            for(int i=start;i<start+320;i++){
                if(!float.IsFinite(samples[i]))throw new InvalidDataException("Invalid microphone samples.");
                energy+=samples[i]*samples[i];if(i>start&&Math.Sign(samples[i])!=Math.Sign(samples[i-1]))crossings++;
            }
            double rms=Math.Sqrt(energy/320);
            if(rms<.004||crossings<2||crossings>110)continue;
            voiced++;minimum=Math.Min(minimum,rms);maximum=Math.Max(maximum,rms);
        }
        return voiced>=4&&maximum>=minimum*1.3;
    }
}
