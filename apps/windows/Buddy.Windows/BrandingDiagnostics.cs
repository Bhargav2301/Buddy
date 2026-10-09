using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Buddy.Windows;

internal static class BrandingDiagnostics
{
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern IntPtr SendMessageTimeoutW(IntPtr hwnd,uint message,IntPtr wparam,IntPtr lparam,uint flags,uint timeout,out IntPtr result);
    [DllImport("user32.dll")]private static extern bool EnumWindows(EnumCallback callback,IntPtr parameter);
    private delegate bool EnumCallback(IntPtr hwnd,IntPtr parameter);
    [DllImport("user32.dll")]private static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint process);
    [DllImport("user32.dll")]private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]private static extern int GetWindowTextW(IntPtr hwnd,StringBuilder text,int length);
    internal static bool HasMint(IntPtr hwnd,int kind){
        if(SendMessageTimeoutW(hwnd,0x7f,(IntPtr)kind,IntPtr.Zero,2,1500,out var icon)==IntPtr.Zero||icon==IntPtr.Zero)return false;
        using var borrowed=System.Drawing.Icon.FromHandle(icon);using var bitmap=borrowed.ToBitmap();int mint=0;
        for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++){var c=bitmap.GetPixel(x,y);if(c.A>100&&c.G>c.R*1.2&&c.G>c.B&&c.G>100)mint++;}
        return mint>10;
    }
    internal static (IntPtr Handle,int Width,int Height) IconInfo(IntPtr hwnd,int kind){
        if(SendMessageTimeoutW(hwnd,0x7f,(IntPtr)kind,IntPtr.Zero,2,1500,out var icon)==IntPtr.Zero||icon==IntPtr.Zero)return(IntPtr.Zero,0,0);
        using var borrowed=System.Drawing.Icon.FromHandle(icon);return(icon,borrowed.Width,borrowed.Height);
    }
    internal static void InspectRunning(){
        var installed=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","Buddy","Buddy.exe");
        var processes=Process.GetProcessesByName("Buddy").Where(p=>p.Id!=Environment.ProcessId&&p.MainModule?.FileName.Equals(installed,StringComparison.OrdinalIgnoreCase)==true).Select(p=>p.Id).ToHashSet();
        var rows=new List<object>();
        EnumWindows((hwnd,_)=>{GetWindowThreadProcessId(hwnd,out var pid);if(!processes.Contains((int)pid)||!IsWindowVisible(hwnd))return true;
            var title=new StringBuilder(256);GetWindowTextW(hwnd,title,title.Capacity);if(!title.ToString().StartsWith("Buddy",StringComparison.OrdinalIgnoreCase))return true;
            var small=IconInfo(hwnd,0);var large=IconInfo(hwnd,1);
            rows.Add(new{pid,smallIconMint=HasMint(hwnd,0),largeIconMint=HasMint(hwnd,1),smallWidth=small.Width,smallHeight=small.Height,largeWidth=large.Width,largeHeight=large.Height,separateHandles=small.Handle!=large.Handle,appUserModelId=ShellIdentity.ReadWindow(hwnd),physicalTaskbarPixelsInspected=false});return true;},IntPtr.Zero);
        Console.WriteLine(JsonSerializer.Serialize(new{installedProcessCount=processes.Count,visibleBuddyWindows=rows,changedShellState=false}));
    }
}
