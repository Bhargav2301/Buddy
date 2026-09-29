using Buddy.Server;
using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Buddy.Windows;

internal sealed class GuidanceOverlay : IDisposable
{
    private readonly List<InkWindow> windows = [];
    private readonly DispatcherTimer expiry = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private DateTimeOffset expires;
    private IntPtr target;
    private Func<bool>? stillValid;
    private bool validating;
    private int revision;
    private GuideInputInvalidation? input;
    internal bool IsVisible => windows.Count > 0;
    internal event Action<ScreenElement?>? TargetChanged;
    internal GuidanceOverlay() { expiry.Tick += async (_, _) => {
        if (DateTimeOffset.UtcNow > expires || Native.GetForegroundWindow() != target && !Native.IsOwnWindow(Native.GetForegroundWindow()) || (InputNative.GetAsyncKeyState(1) & 0x8000) != 0) { Clear(); return; }
        if (validating || stillValid is not { } validate) return;
        validating = true; int current = revision;
        var check = Task.Run(validate);
        try { if (!await check.WaitAsync(TimeSpan.FromMilliseconds(200)) && current == revision) Clear(); }
        catch { if (current == revision) Clear(); }
        finally { // Keep a hung provider from creating an unbounded number of validation workers.
            if (check.IsCompleted) validating = false;
            else _ = check.ContinueWith(_ => expiry.Dispatcher.BeginInvoke(new Action(() => validating = false)), TaskScheduler.Default);
        }
    }; }
    internal void Draw(IntPtr window, ScreenElement element, string primitive, string label, Func<bool>? valid = null)
    {
        Clear(); input = new GuideInputInvalidation(Clear); target = window; stillValid = valid; expires = DateTimeOffset.UtcNow.AddSeconds(15);
        foreach (var monitor in System.Windows.Forms.Screen.AllScreens) {
            var bounds = monitor.Bounds;
            if (!bounds.IntersectsWith(new((int)element.X, (int)element.Y, (int)element.Width, (int)element.Height))) continue;
            var ink = new InkWindow(bounds, element, primitive, label); windows.Add(ink); ink.Show(); ink.Place();
        }
        expiry.Start();
        TargetChanged?.Invoke(IsVisible ? element : null);
    }
    internal void Clear() { revision++; input?.Dispose(); input = null; expiry.Stop(); foreach (var window in windows) window.Close(); windows.Clear(); stillValid = null; TargetChanged?.Invoke(null); }
    public void Dispose() => Clear();

    private sealed class InkWindow : Window
    {
        private readonly System.Drawing.Rectangle screen;
        internal InkWindow(System.Drawing.Rectangle screen, ScreenElement target, string primitive, string label)
        {
            this.screen = screen; Title = "Buddy guidance ink"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false; ShowActivated = false; Focusable = false; IsHitTestVisible = false;
            Content = new Ink(screen, target, primitive, label);
            SourceInitialized += (_, _) => { OverlayNative.Configure(new WindowInteropHelper(this).Handle, true); Place(); };
            if (BuddyTheme.Animate) BeginAnimation(OpacityProperty, new DoubleAnimation(.3, 1, TimeSpan.FromMilliseconds(350)));
        }
        internal void Place() => OverlayNative.SetBounds(this, screen.X, screen.Y, screen.Width, screen.Height);
    }
    private sealed class Ink(System.Drawing.Rectangle screen, ScreenElement target, string primitive, string label) : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc)
        {
            var dpi = VisualTreeHelper.GetDpi(this); double sx = dpi.DpiScaleX, sy = dpi.DpiScaleY;
            var r = new Rect((target.X - screen.X) / sx, (target.Y - screen.Y) / sy, target.Width / sx, target.Height / sy); r.Inflate(5, 5);
            var accent = SystemParameters.HighContrast ? SystemColors.HighlightBrush : new SolidColorBrush(Color.FromRgb(32, 184, 166));
            var white = new Pen(Brushes.White, 7); var pen = new Pen(accent, 3);
            if (primitive == "highlight") { var fill = accent.Clone(); fill.Opacity = .25; dc.DrawRoundedRectangle(fill, pen, r, 8, 8); }
            else if (primitive == "underline") { dc.DrawLine(white, r.BottomLeft, r.BottomRight); dc.DrawLine(pen, r.BottomLeft, r.BottomRight); }
            else { dc.DrawRoundedRectangle(null, white, r, 12, 12); dc.DrawRoundedRectangle(null, pen, r, 12, 12); }
            if (primitive is "arrow" or "ring") {
                var end = new Point(r.Left, r.Top + r.Height / 2); var start = new Point(Math.Max(8, end.X - 80), Math.Max(8, end.Y - 55));
                var path = new StreamGeometry(); using (var c = path.Open()) { c.BeginFigure(start, false, false); c.BezierTo(new(start.X, end.Y), new(end.X - 35, end.Y - 10), end, true, false); }
                dc.DrawGeometry(null, white, path); dc.DrawGeometry(null, pen, path);
                dc.DrawLine(pen, end, new(end.X - 12, end.Y - 8)); dc.DrawLine(pen, end, new(end.X - 12, end.Y + 8));
            }
            var caption = string.Join(' ', label.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(5));
            if (primitive == "badge") caption = "1 · " + caption;
            var text = new FormattedText(caption, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 14, Brushes.White, dpi.PixelsPerDip) { MaxTextWidth = 240 };
            var origin = new Point(Math.Clamp(r.Left, 5, Math.Max(5, ActualWidth - 260)), Math.Max(4, r.Top - text.Height - 16));
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(18, 35, 38)), null, new Rect(origin.X - 5, origin.Y - 4, text.Width + 12, text.Height + 8), 6, 6); dc.DrawText(text, origin);
        }
    }
}
