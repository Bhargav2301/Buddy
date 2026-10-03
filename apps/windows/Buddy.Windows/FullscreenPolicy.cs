namespace Buddy.Windows;

internal static class FullscreenPolicy
{
    internal static bool Suppress(string windowClass,long style,bool coversMonitor)
        => coversMonitor && windowClass is not("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
           && (style&0x00C00000)==0; // Ordinary maximized captioned apps are not exclusive fullscreen.
}
