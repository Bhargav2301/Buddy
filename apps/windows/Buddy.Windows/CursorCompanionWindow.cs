using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Buddy.Windows;

internal enum CompanionMood { Idle, Listening, Looking, Thinking, Speaking, Pointing, Unsure, Error, AgentWorking, Researching, Sleeping }

internal sealed class CursorCompanionWindow : Window, IDisposable
{
    private readonly DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly CursorGlyph glyph = new();
    private readonly Func<bool> suppressed;
    private bool enabled, disposed;
    private OverlayNative.Point last;
    private bool positioned;

    internal CursorCompanionWindow(Func<bool> suppressed)
    {
        this.suppressed = suppressed;
        Title = "Buddy cursor companion"; Width = 46; Height = 52;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent;
        Topmost = true; ShowInTaskbar = false; ShowActivated = false; Focusable = false; IsHitTestVisible = false;
        Content = glyph;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            OverlayNative.Configure(handle, true);
            HwndSource.FromHwnd(handle).AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
            {
                if (msg == 0x0084) { handled = true; return new IntPtr(-1); } // HTTRANSPARENT
                if (msg == 0x0021) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
                return IntPtr.Zero;
            });
        };
        timer.Tick += (_, _) => Follow();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(new Action(() => positioned = false));
    }
    internal void SetEnabled(bool value)
    {
        enabled = value;
        if (enabled) { timer.Start(); Follow(); }
        else { timer.Stop(); Hide(); }
    }
    internal void SetMood(CompanionMood mood) => glyph.SetMood(mood);
    private void Follow()
    {
        if (disposed || !enabled) return;
        if (suppressed() || !OverlayNative.GetCursorPos(out var point)) { if (IsVisible) Hide(); positioned = false; return; }
        if (!IsVisible) { new WindowInteropHelper(this).EnsureHandle(); OverlayNative.Place(this, point); Show(); positioned = false; }
        if (!positioned || point.X != last.X || point.Y != last.Y)
        { OverlayNative.Place(this, point); last = point; positioned = true; }
        glyph.SetBlink(Environment.TickCount64 % 4200 < 140);
    }
    public void Dispose() { if (disposed) return; disposed = true; timer.Stop(); Close(); }
}

internal sealed class CursorGlyph : FrameworkElement
{
    private static readonly Geometry Shape = Geometry.Parse("M 7,5 L 7,37 L 15,29 L 21,44 L 29,40 L 22,27 L 36,27 Z");
    private static readonly Brush Dark = new SolidColorBrush(Color.FromRgb(18, 32, 36));
    private CompanionMood mood;
    private bool blink;
    internal void SetMood(CompanionMood value) { if (mood != value) { mood = value; InvalidateVisual(); } }
    internal void SetBlink(bool value) { if (blink != value) { blink = value; InvalidateVisual(); } }
    protected override void OnRender(DrawingContext drawing)
    {
        var color = mood switch { CompanionMood.Listening => "#9DBAFF", CompanionMood.Thinking or CompanionMood.Researching => "#F7D487", CompanionMood.Speaking => "#C5A7FF", CompanionMood.AgentWorking => "#FFB078", CompanionMood.Error or CompanionMood.Unsure => "#B6BEC5", _ => "#20B8A6" };
        var fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        drawing.DrawGeometry(null, new Pen(Brushes.White, 4), Shape);
        drawing.DrawGeometry(fill, new Pen(Dark, 1.6), Shape);
        if (mood == CompanionMood.Listening) drawing.DrawEllipse(null, new Pen(fill, 2), new(21,25), 20, 24);
        if (blink) { drawing.DrawLine(new Pen(Dark, 1.6), new(12, 23), new(15, 23)); drawing.DrawLine(new Pen(Dark, 1.6), new(19, 23), new(22, 23)); }
        else { drawing.DrawEllipse(Dark, null, new(13.5, 22), 1.6, 2.3); drawing.DrawEllipse(Dark, null, new(20.5, 22), 1.6, 2.3); }
    }
}
