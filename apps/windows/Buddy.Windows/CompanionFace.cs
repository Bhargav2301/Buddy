using SharpVectors.Converters;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Automation;

namespace Buddy.Windows;

// Exact local vector assets from Foundations 2:104–2:144 and onboarding 5:40.
internal sealed class CompanionFace : Grid
{
    private readonly SvgViewbox body, halo;
    private readonly TextBlock face;
    private readonly bool compact;
    private readonly System.Windows.Threading.DispatcherTimer pulse = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private CompanionMood currentMood;
    internal CompanionFace(bool compact = false)
    {
        this.compact = compact; Width = Height = compact ? 40 : 72; IsHitTestVisible = false;
        halo = Asset("91dcc.svg", 72, 72); halo.Visibility = Visibility.Collapsed; Children.Add(halo);
        body = Asset(compact ? "1cced.svg" : "9a20f.svg", compact ? 40 : 56, compact ? 40 : 56); Children.Add(body);
        face = new TextBlock { Text = "^ ‿ ^", FontFamily = BuddyTheme.Font, FontSize = compact ? 10 : 12, FontWeight = FontWeights.Bold, Foreground = Brushes.White, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Stretch };
        Children.Add(face); SetMood(CompanionMood.Idle);
        pulse.Tick += (_, _) => halo.Opacity = BuddyTheme.Animate ? .6 + .4 * (.5 + .5 * Math.Sin(Environment.TickCount64 * Math.PI / 600)) : 1;
        Loaded += (_, _) => { if (currentMood == CompanionMood.Listening) pulse.Start(); };
        Unloaded += (_, _) => pulse.Stop();
    }
    internal static SvgViewbox Asset(string file, double width, double height) => new() {
        Source = new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Figma", file)),
        Width = width, Height = height, Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
    };
    internal void SetMood(CompanionMood mood)
    {
        currentMood = mood; pulse.Stop(); halo.Opacity = 1;
        if (mood == CompanionMood.Listening && IsLoaded) pulse.Start();
        halo.Visibility = !compact && mood == CompanionMood.Listening ? Visibility.Visible : Visibility.Collapsed;
        var file = compact ? "1cced.svg" : mood == CompanionMood.Error ? "ab83a.svg" : mood == CompanionMood.Sleeping ? "785ba.svg" : "9a20f.svg";
        body.Source = new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Figma", file));
        face.Text = mood switch { CompanionMood.Listening => "• ᴗ •", CompanionMood.Looking => "◉ _ ◉", CompanionMood.Thinking or CompanionMood.Researching => "- _ -", CompanionMood.Speaking => "^ ▽ ^", CompanionMood.Pointing => "^ ω ^", CompanionMood.Unsure => "¯\\_(tsu)_/¯", CompanionMood.Error => "(¬_¬)", CompanionMood.AgentWorking => "fight", CompanionMood.Sleeping => "- ω -", _ => "^ ‿ ^" };
        AutomationProperties.SetName(this, "Buddy · " + mood);
    }
}
