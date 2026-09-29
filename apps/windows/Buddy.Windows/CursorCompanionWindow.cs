using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

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

    internal CursorCompanionWindow(Func<bool> suppressed)
    {
        this.suppressed = suppressed;
        Title = "Buddy companion"; Width = 72; Height = 72;
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
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(new Action(spring.Reset));
    }
    internal void SetEnabled(bool value)
    {
        enabled = value;
        if (enabled) { timer.Start(); Follow(); }
        else { timer.Stop(); Hide(); }
    }
    internal void SetMood(CompanionMood mood) => glyph.SetMood(mood);
    internal void Snooze() { snoozedUntil = Environment.TickCount64 + 15 * 60 * 1000; Hide(); spring.Reset(); }
    internal void SetDocked(bool value) { docked = value; if (OverlayNative.GetCursorPos(out var point)) dockPoint = point; spring.Reset(); }
    private void Follow()
    {
        if (disposed || !enabled) return;
        if (Environment.TickCount64 < snoozedUntil || suppressed() || OverlayNative.IsFullscreenForeground() || !OverlayNative.GetCursorPos(out var point)) { if (IsVisible) Hide(); spring.Reset(); return; }
        if (docked) { point = dockPoint; var work = OverlayNative.WorkArea(point); point.X = (int)(work.Left + work.Width - 16); point.Y = (int)(work.Top + work.Height - 16); }
        if (!IsVisible) { new WindowInteropHelper(this).EnsureHandle(); OverlayNative.Place(this, point); Show(); spring.Reset(); }
        OverlayNative.Move(this, spring.Step(OverlayNative.Position(this, point), .033, BuddyTheme.Animate));
    }
    public void Dispose() { if (disposed) return; disposed = true; timer.Stop(); Close(); }
}
