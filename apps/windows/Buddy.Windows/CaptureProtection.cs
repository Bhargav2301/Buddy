using System.Windows;
using System.Windows.Interop;

namespace Buddy.Windows;

internal static class CaptureProtection
{
    internal static bool Enabled { get; set; } = true;
    internal static void Apply(IntPtr window)
    {
        if (!Native.SetWindowDisplayAffinity(window, Enabled ? 0x11u : 0u))
            throw new InvalidOperationException("Windows could not apply Buddy screenshot protection.");
    }
    internal static void Set(bool enabled)
    {
        bool previous = Enabled; Enabled = enabled;
        try {
            if (Application.Current is not null)
                foreach (Window window in Application.Current.Windows) {
                    var handle = new WindowInteropHelper(window).Handle;
                    if (handle != IntPtr.Zero) Apply(handle);
                }
        } catch { Enabled = previous; throw; }
    }
}
