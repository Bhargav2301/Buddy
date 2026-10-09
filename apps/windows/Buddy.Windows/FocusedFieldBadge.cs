using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Automation;

namespace Buddy.Windows;

internal sealed class FocusedFieldBadge : Window, IDisposable
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly FocusedFieldEditor editor;
    private readonly CancellationTokenSource lifetime = new();
    private FieldAnchor? anchor;
    private bool checking, closed, enabled;
    internal FocusedFieldBadge(FocusedFieldEditor editor, Action<string> refine)
    {
        this.editor = editor;
        Title = "Buddy · Refine focused prompt"; Width = Height = 44; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true; Background = Brushes.Transparent; Topmost = true; ShowInTaskbar = false; ShowActivated = false;
        var button = new Button { Content = CompanionFace.Asset("a9b00.svg", 28, 28), Padding = new(0), Background = Brushes.Transparent, BorderThickness = new(0), ToolTip = "Refine this prompt · Ctrl+Alt+R" };
        AutomationProperties.SetName(button, "Refine this prompt on this PC");
        button.Click += (_, _) => { if (anchor is { } field) { Hide(); refine(field.Identity); } };
        Content = button;
        SourceInitialized += (_, _) => {
            var handle = new WindowInteropHelper(this).Handle;
            OverlayNative.Configure(handle, false, noActivate: true);
            HwndSource.FromHwnd(handle).AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) => {
                if (msg == 0x0021) { handled = true; return new IntPtr(3); }
                return IntPtr.Zero;
            });
        };
        timer.Tick += async (_, _) => await Refresh();
    }
    internal void SetEnabled(bool value) { enabled = value; if (value) timer.Start(); else { timer.Stop(); Hide(); } }
    private async Task Refresh()
    {
        if (closed || !enabled) return;
        var window = Native.GetForegroundWindow();
        if (window == IntPtr.Zero || Native.IsOwnWindow(window) || OverlayNative.IsFullscreenForeground()) { Hide(); anchor = null; return; }
        if (anchor?.Window != window) Hide();
        if (checking) return; checking = true;
        try {
            var candidate = await editor.Probe(window, lifetime.Token);
            if (closed || !enabled || Native.GetForegroundWindow() != window) return;
            anchor = candidate;
            if (candidate is null) { Hide(); return; }
            new WindowInteropHelper(this).EnsureHandle();
            OverlayNative.Place(this, new() { X = (int)candidate.Bounds.Right, Y = (int)candidate.Bounds.Bottom });
            Show();
        } catch { if (!closed) { Hide(); anchor = null; } }
        finally { checking = false; }
    }
    public void Dispose() { if (closed) return; closed = true; timer.Stop(); lifetime.Cancel(); Close(); lifetime.Dispose(); }
}
