using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace Buddy.Windows;

internal static class AppBranding
{
    private static bool registered;
    private static readonly ConditionalWeakTable<Window,NativeArtwork> artwork=new();
    private sealed class NativeArtwork : IDisposable {
        internal readonly System.Drawing.Icon Small=new(Asset("Buddy.ico"),16,16),Large=new(Asset("Buddy.ico"),48,48);
        public void Dispose(){Small.Dispose();Large.Dispose();}
    }
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr SendMessageW(IntPtr hwnd,uint message,IntPtr type,IntPtr icon);
    internal static void ApplyNative(Window window){
        var hwnd=new WindowInteropHelper(window).Handle;if(hwnd==IntPtr.Zero)return;
        var icons=artwork.GetValue(window,w=>{var icons=new NativeArtwork();w.Closed+=(_,_)=>{icons.Dispose();artwork.Remove(w);};return icons;});
        SendMessageW(hwnd,0x80,IntPtr.Zero,icons.Small.Handle);SendMessageW(hwnd,0x80,(IntPtr)1,icons.Large.Handle);
        ShellIdentity.Window(hwnd);
    }
    internal static string Asset(string name) => Path.Combine(AppContext.BaseDirectory, "Assets", "Branding", name);
    internal static readonly ImageSource Character = Load("Buddy.png");
    internal static readonly ImageSource WindowIcon = Load("Buddy.ico");
    private static ImageSource Load(string name)
    {
        var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(Asset(name)); bitmap.EndInit(); bitmap.Freeze(); return bitmap;
    }
    internal static System.Drawing.Icon TrayIcon() => new(Asset("Buddy.ico"), 32, 32);
    internal static Image Image(double size) => new() { Source = Character, Width = size, Height = size, Stretch = Stretch.Uniform };
    internal static void Apply(Window window)
    {
        window.Icon = WindowIcon;
        window.SourceInitialized+=(_,_)=>ApplyNative(window);
        window.Loaded+=(_,_)=>ApplyNative(window);
        if (registered) return; registered = true;
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) => { if (sender is Window loaded) {loaded.Icon ??= WindowIcon;ApplyNative(loaded);} }));
    }
}
