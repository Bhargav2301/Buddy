using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace Buddy.Windows;

internal sealed class ExecutionBanner : Window
{
    internal ExecutionBanner()
    {
        Title = "Buddy execution indicator"; Height = 40; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        Background = new SolidColorBrush(Color.FromRgb(220, 38, 38)); Foreground = Brushes.White; ShowInTaskbar = false; ShowActivated = false; Topmost = true;
        Content = new TextBlock { Text = "Buddy is controlling · Esc or Ctrl+Alt+Esc to stop", Foreground = Brushes.White, FontFamily = BuddyTheme.Font, FontWeight = FontWeights.SemiBold, FontSize = 14, Margin = new(24, 8, 24, 8) };
        SourceInitialized += (_, _) => OverlayNative.Configure(new WindowInteropHelper(this).Handle, true);
    }
    internal void Open()
    {
        if (!OverlayNative.GetCursorPos(out var point)) return;
        var bounds = OverlayNative.WorkArea(point); var handle = new WindowInteropHelper(this).EnsureHandle(); var scale = OverlayNative.Scale(handle);
        OverlayNative.SetBounds(this, (int)bounds.Left, (int)bounds.Top, (int)bounds.Width, (int)(40 * scale)); Show();
    }
}
