using System.Windows;
using System.Windows.Media;
using System.Windows.Automation;
using System.Windows.Threading;

namespace Buddy.Windows;

// Decorative activity, never presented as measured microphone amplitude.
internal sealed class CompanionActivity : FrameworkElement
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private CompanionMood mood;
    internal CompanionMood Mood { get => mood; set { mood = value; AutomationProperties.SetName(this, $"Buddy {value} activity; decorative, not microphone level"); InvalidateVisual(); } }
    internal bool Animate { get; set; } = true;
    internal bool MotionEnabled => Animate && BuddyTheme.Animate;
    internal CompanionActivity()
    {
        Width = 40; Height = 20; IsHitTestVisible = false;
        ToolTip = "Decorative activity. These bars do not measure microphone volume.";
        AutomationProperties.SetHelpText(this, "Decorative state indicator, not a measured audio waveform.");
        timer.Tick += (_, _) => InvalidateVisual();
        Loaded += (_, _) => timer.Start();
        Unloaded += (_, _) => timer.Stop();
        Mood = CompanionMood.Idle;
    }
    internal static double HeightFor(int bar,CompanionMood mood,double phase,bool animate)=>mood switch{
        CompanionMood.Listening=>animate?4+12*Math.Abs(Math.Sin(phase+bar*.8)):9,
        CompanionMood.Thinking or CompanionMood.Researching=>animate?4+10*Math.Max(0,Math.Sin(phase-bar*.9)):7,
        CompanionMood.Speaking=>animate?5+10*Math.Abs(Math.Sin(phase*1.3+bar)):10,
        _=>3
    };
    protected override void OnRender(DrawingContext dc){
        var brush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : BuddyTheme.Deep;
        for(int i=0;i<5;i++){double h=HeightFor(i,Mood,Environment.TickCount64/190.0,MotionEnabled);dc.DrawRoundedRectangle(brush,null,new Rect(4+i*7,(20-h)/2,4,h),2,2);}
    }
}
