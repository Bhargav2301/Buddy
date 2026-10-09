using Buddy.Server;
using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Buddy.Windows;

internal sealed record GuidanceMark(ScreenElement Element,string Primitive,string Label,Func<bool>? Valid=null);

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
    private readonly Func<int> lifetimeSeconds;
    private readonly Func<DateTimeOffset> now;
    private readonly Func<IntPtr> foreground;
    private readonly Func<IntPtr, bool> isOwnWindow;
    private readonly Func<bool> pointerDown;
    internal bool IsVisible => windows.Count > 0;
    internal event Action<ScreenElement?>? TargetChanged;
    internal event Action? Invalidated;
    private void Invalidate() { Clear(); Invalidated?.Invoke(); }
    internal GuidanceOverlay(Func<int>? lifetimeSeconds = null, Func<DateTimeOffset>? now = null,
        Func<IntPtr>? foreground = null, Func<IntPtr, bool>? isOwnWindow = null, Func<bool>? pointerDown = null)
    {
        this.lifetimeSeconds = lifetimeSeconds ?? (() => 15);
        this.now = now ?? (() => DateTimeOffset.UtcNow);
        this.foreground = foreground ?? Native.GetForegroundWindow;
        this.isOwnWindow = isOwnWindow ?? Native.IsOwnWindow;
        this.pointerDown = pointerDown ?? (() => (InputNative.GetAsyncKeyState(1) & 0x8000) != 0);
        expiry.Tick += async (_, _) => await CheckValidity();
    }
    internal async Task CheckValidity()
    {
        if (!IsVisible) return;
        var focused = foreground();
        if (now() >= expires || focused != target && !isOwnWindow(focused) || pointerDown()) { Invalidate(); return; }
        if (validating || stillValid is not { } validate) return;
        validating = true; int current = revision;
        var check = Task.Run(validate);
        try { if (!await check.WaitAsync(TimeSpan.FromMilliseconds(200)) && current == revision) Invalidate(); }
        catch { if (current == revision) Invalidate(); }
        finally { // Keep a hung provider from creating an unbounded number of validation workers.
            if (check.IsCompleted) validating = false;
            else _ = check.ContinueWith(_ => expiry.Dispatcher.BeginInvoke(new Action(() => validating = false)), TaskScheduler.Default);
        }
    }
    internal void Draw(IntPtr window, ScreenElement element, string primitive, string label, Func<bool>? valid = null)
        =>DrawMany(window,[new(element,primitive,label,valid)]);
    internal void DrawMany(IntPtr window,IReadOnlyList<GuidanceMark> marks)
    {
        Clear();if(marks.Count is <1 or >4)throw new InvalidOperationException("Choose one to four verified targets.");
        if (validating) throw new InvalidOperationException("The previous target check is still responding. Try again shortly.");
        input = new GuideInputInvalidation(Invalidate); target = window; stillValid = ()=>marks.All(m=>m.Valid?.Invoke()!=false); expires = now().AddSeconds(DesktopPreferences.NormalizeInkLifetime(lifetimeSeconds()));
        foreach(var mark in marks){var element=mark.Element;
        foreach (var monitor in System.Windows.Forms.Screen.AllScreens) {
            var bounds = monitor.Bounds;
            if (!bounds.IntersectsWith(new((int)element.X, (int)element.Y, (int)element.Width, (int)element.Height))) continue;
            var ink = new InkWindow(bounds, element, mark.Primitive, mark.Label); windows.Add(ink); ink.Show(); ink.Place();
        }
        }
        expiry.Start();
        TargetChanged?.Invoke(IsVisible ? marks[0].Element : null);
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
            // Night Mint captions stay opaque throughout their lifetime; pointer motion remains independent.
            if (BuddyTheme.Animate && !BuddyTheme.IsNightMint) BeginAnimation(OpacityProperty, new DoubleAnimation(.3, 1, TimeSpan.FromMilliseconds(350)));
        }
        internal void Place() => OverlayNative.SetBounds(this, screen.X, screen.Y, screen.Width, screen.Height);
    }
    private sealed class Ink(System.Drawing.Rectangle screen, ScreenElement target, string primitive, string label) : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc)
        {
            var dpi = VisualTreeHelper.GetDpi(this); double sx = dpi.DpiScaleX, sy = dpi.DpiScaleY;
            var r = new Rect((target.X - screen.X) / sx, (target.Y - screen.Y) / sy, target.Width / sx, target.Height / sy); r.Inflate(5, 5);
            var accent = BuddyTheme.Deep;
            var white = new Pen(BuddyTheme.FocusGap, 7); var pen = new Pen(accent, 3);
            if (primitive == "highlight") { var fill = accent.Clone(); fill.Opacity = .25; dc.DrawRoundedRectangle(fill, pen, r, 8, 8); }
            else if (primitive == "underline") { dc.DrawLine(white, r.BottomLeft, r.BottomRight); dc.DrawLine(pen, r.BottomLeft, r.BottomRight); }
            else if (primitive is "circle" or "ring") { var center = new Point(r.Left + r.Width / 2, r.Top + r.Height / 2); dc.DrawEllipse(null, white, center, r.Width / 2, r.Height / 2); dc.DrawEllipse(null, pen, center, r.Width / 2, r.Height / 2); }
            else if (primitive is not "label" and not "arrow") { dc.DrawRoundedRectangle(null, white, r, 12, 12); dc.DrawRoundedRectangle(null, pen, r, 12, 12); }
            if (primitive is "arrow" or "ring") {
                var end = new Point(r.Left, r.Top + r.Height / 2); var start = new Point(Math.Max(8, end.X - 80), Math.Max(8, end.Y - 55));
                var path = new StreamGeometry(); using (var c = path.Open()) { c.BeginFigure(start, false, false); c.BezierTo(new(start.X, end.Y), new(end.X - 35, end.Y - 10), end, true, false); }
                dc.DrawGeometry(null, white, path); dc.DrawGeometry(null, pen, path);
                dc.DrawLine(pen, end, new(end.X - 12, end.Y - 8)); dc.DrawLine(pen, end, new(end.X - 12, end.Y + 8));
            }
            var caption = string.Join(' ', label.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(5));
            if (primitive == "badge") caption = "1 · " + caption;
            var text = new FormattedText(caption, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 14, BuddyTheme.Ink, dpi.PixelsPerDip) { MaxTextWidth = 240 };
            var origin = new Point(Math.Clamp(r.Left, 5, Math.Max(5, ActualWidth - 260)), Math.Max(4, r.Top - text.Height - 16));
            dc.DrawRoundedRectangle(BuddyTheme.Surface, new Pen(BuddyTheme.ControlBorder, 1), new Rect(origin.X - 5, origin.Y - 4, text.Width + 12, text.Height + 8), 6, 6); dc.DrawText(text, origin);
        }
    }
}
