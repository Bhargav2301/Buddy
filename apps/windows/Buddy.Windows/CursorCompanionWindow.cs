using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Automation;
using Buddy.Server;

namespace Buddy.Windows;

internal sealed class CursorCompanionWindow : Window, IDisposable
{
    private readonly DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly CompanionFace glyph = new();
    private readonly Func<bool> suppressed;
    private bool enabled, disposed;
    private readonly CompanionSpring spring = new();
    private long snoozedUntil;
    private bool docked;
    private OverlayNative.Point dockPoint;
    private ScreenElement? groundedTarget;
    private PixelPosition flightFrom;
    private long flightStarted;
    private CompanionMood currentMood;
    private readonly ContextMenu menu = new();
    private bool triangleEnabled, triangleActive;
    private System.Windows.Controls.Button menuButton = null!;
    private Border brainBadge = null!;
    private bool compactPointerMode;
    private readonly CompanionActivity activity = new();
    private readonly TextBlock activityLabel = new() { FontSize = 11, Foreground = BuddyTheme.Ink, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly StackPanel activityPanel = new() { Visibility = Visibility.Collapsed };

    internal CursorCompanionWindow(Func<bool> suppressed, Action<string>? action = null)
    {
        this.suppressed = suppressed;
        Title = "Buddy companion"; Width = 104; Height = 116;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        var button = new System.Windows.Controls.Button { Content = glyph, Padding = new(0), Background = Brushes.Transparent, BorderThickness = new(0), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Open Buddy companion menu" };
        menuButton=button;
        AutomationProperties.SetName(button, "Open Buddy companion menu");
        void Add(string label, Action click) { var item = new MenuItem { Header = label, MinHeight = 44 }; item.Click += (_, _) => click(); menu.Items.Add(item); }
        foreach (var label in new[] { "Voice", "Talk", "Guide", "Select area", "Stop" }) Add(label=="Talk"?"Type":label, () => action?.Invoke(label));
        var optional=new MenuItem{Header="Optional tools",MinHeight=44};
        foreach(var label in new[]{"Refine","Dictate","Agent"}){var item=new MenuItem{Header=label=="Refine"?"Refine source field":label,MinHeight=44};item.Click+=(_,_)=>action?.Invoke(label);optional.Items.Add(item);}menu.Items.Add(optional);
        Add("Home & history",()=>action?.Invoke("Home"));Add("Settings",()=>action?.Invoke("Settings"));
        Add("Dock", () => SetDocked(true)); Add("Follow pointer", () => SetDocked(false)); Add("Snooze 15 minutes", Snooze);
        button.ContextMenu = menu;button.ToolTip="Talk with Buddy; right-click for Type, Guide and optional tools";AutomationProperties.SetName(button,"Talk with Buddy; right-click for tools");
        button.Click += (_, _) => action?.Invoke("Voice");
        var presence = new StackPanel(); button.Width = button.Height = CompanionPresentation.FaceButtonSize(false); presence.Children.Add(button);
        activityPanel.Children.Add(activity); activityPanel.Children.Add(activityLabel);
        presence.Children.Add(new Border { Background = BuddyTheme.Surface, CornerRadius = new(8), Padding = new(4, 2, 4, 2), Child = activityPanel });
        // The activity caption is content, unlike the mascot silhouette: never put light text directly on wallpaper.
        var activitySurface = (Border)presence.Children[presence.Children.Count - 1];
        activitySurface.SetBinding(VisibilityProperty, new System.Windows.Data.Binding(nameof(Visibility)) { Source = activityPanel });
        activityPanel.ToolTip = "Decorative activity, not measured microphone volume.";
        AutomationProperties.SetLiveSetting(activityLabel, AutomationLiveSetting.Polite);
        var brainLabel = new TextBlock { Text = "on this PC", FontSize = 12, Foreground = BuddyTheme.Deep, HorizontalAlignment = HorizontalAlignment.Center };
        AutomationProperties.SetName(brainLabel, "Brain: on this PC"); AutomationProperties.SetLiveSetting(brainLabel, AutomationLiveSetting.Polite);
        brainBadge=new Border { Background = BuddyTheme.Soft, CornerRadius = new(8), Padding = new(6, 2, 6, 2), Child = brainLabel };presence.Children.Add(brainBadge);
        Content = presence;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            OverlayNative.Configure(handle, false, noActivate: true);
            HwndSource.FromHwnd(handle).AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
            {
                if (msg == 0x0021) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
                return IntPtr.Zero;
            });
        };
        timer.Tick += (_, _) => Follow();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(new Action(spring.Reset));
    }
    internal void SetEnabled(bool value)
    {
        enabled = value;
        if (enabled) { timer.Start(); Follow(); }
        else { timer.Stop(); Hide(); }
    }
    internal void SetMood(CompanionMood mood) { currentMood = mood; glyph.SetMood(mood); activity.Mood = mood; RefreshPresentation(); }
    internal void SetCompactPointerMode(bool value)
    {
        compactPointerMode = value; glyph.SetCompactPointer(value);
        if (!value && !triangleEnabled) Triangle(false);
        Width = value ? 88 : 104; Height = 116;
        menuButton.Width = menuButton.Height = CompanionPresentation.FaceButtonSize(value);
        if (value && !triangleActive) glyph.PointAngle(-45);
        RefreshPresentation(); spring.Reset();
    }
    private void RefreshPresentation()
    {
        bool showActivity = compactPointerMode && currentMood is CompanionMood.Listening or CompanionMood.Thinking or CompanionMood.Researching or CompanionMood.Speaking;
        activityPanel.Visibility = showActivity ? Visibility.Visible : Visibility.Collapsed;
        activityLabel.Text = currentMood.ToString();
        AutomationProperties.SetName(activityLabel, $"{currentMood}; decorative activity, not microphone level");
        brainBadge.Visibility = triangleActive || showActivity ? Visibility.Collapsed : Visibility.Visible;
    }
    internal void SetTriangleEnabled(bool enabled) { triangleEnabled=enabled; if(!enabled) Triangle(false); }
    private void Triangle(bool active)
    {
        if(triangleActive==active)return;triangleActive=active;glyph.SetPointer(active);menuButton.IsHitTestVisible=!active;RefreshPresentation();
        if (!active && compactPointerMode) glyph.PointAngle(-45);
        var handle=new WindowInteropHelper(this).Handle;if(handle!=IntPtr.Zero)OverlayNative.Configure(handle,active,noActivate:true);
    }
    internal void PointTo(ScreenElement? target)
    {
        groundedTarget = target; flightFrom = spring.Position; flightStarted = Environment.TickCount64;
        if (target is null) { spring.Reset(); Triangle(false); }
    }
    internal void Snooze() { snoozedUntil = Environment.TickCount64 + 15 * 60 * 1000; Hide(); spring.Reset(); }
    internal void SetDocked(bool value) { docked = value; if (OverlayNative.GetCursorPos(out var point)) dockPoint = point; spring.Reset(); }
    private void Follow()
    {
        if (disposed || !enabled) return;
        if (Environment.TickCount64 < snoozedUntil || suppressed() || OverlayNative.IsFullscreenForeground() || !OverlayNative.GetCursorPos(out var point)) { if (IsVisible) Hide(); spring.Reset(); return; }
        if (menu.IsOpen || IsMouseOver) return;
        var pointer=point;
        bool pointing = groundedTarget is not null && currentMood is CompanionMood.Pointing or CompanionMood.Speaking;
        Triangle((triangleEnabled || compactPointerMode)&&pointing);
        if (pointing) point = new() { X = (int)(groundedTarget!.X + groundedTarget.Width), Y = (int)(groundedTarget.Y + groundedTarget.Height / 2) };
        else if (docked) { point = dockPoint; var work = OverlayNative.WorkArea(point); point.X = (int)(work.Left + work.Width - 16); point.Y = (int)(work.Top + work.Height - 16); }
        if (!IsVisible) { new WindowInteropHelper(this).EnsureHandle(); OverlayNative.Place(this, point); Show(); spring.Reset(); }
        var destination = OverlayNative.Position(this, point);
        glyph.LookToward(pointer.X-(spring.Position.X+Width/2),pointer.Y-(spring.Position.Y+menuButton.Height/2));
        if(triangleActive&&groundedTarget is { } target){double scale=OverlayNative.Scale(new WindowInteropHelper(this).Handle);glyph.PointAngle(Math.Atan2(target.Y+target.Height/2-(destination.Y+menuButton.Height/2*scale),target.X+target.Width/2-(destination.X+Width/2*scale))*180/Math.PI+180);}
        if (pointing) {
            double t = BuddyTheme.Animate ? Math.Clamp((Environment.TickCount64 - flightStarted) / 300.0, 0, 1) : 1;
            double eased = 1 - Math.Pow(1 - t, 3);
            var flight = new PixelPosition(flightFrom.X + (destination.X - flightFrom.X) * eased, flightFrom.Y + (destination.Y - flightFrom.Y) * eased);
            OverlayNative.Move(this, spring.Step(flight, .033, false));
        } else OverlayNative.Move(this, spring.Step(destination, .033, BuddyTheme.Animate));
    }
    public void Dispose() { if (disposed) return; disposed = true; menu.IsOpen = false; timer.Stop(); Close(); }
}
