using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Buddy.Windows;

internal static class ShellIdentity
{
    internal const string InstalledId="Bhargav2301.Buddy";
    internal static string CurrentId=>PreviewEnvironment.Enabled?InstalledId+".Preview":InstalledId;
    private static readonly Guid AppKey=new("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
    private static readonly Guid StoreId=new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    internal static string ReadProcess(){Marshal.ThrowExceptionForHR(GetCurrentProcessExplicitAppUserModelID(out var value));try{return Marshal.PtrToStringUni(value)??"";}finally{Marshal.FreeCoTaskMem(value);}}
    internal static void Initialize(){Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(CurrentId));if(ReadProcess()!=CurrentId)throw new InvalidOperationException("Buddy's shell identity could not be verified.");}
    internal static void Shortcut(string path){
        var id=StoreId;Marshal.ThrowExceptionForHR(SHGetPropertyStoreFromParsingName(path,IntPtr.Zero,2,ref id,out var store));
        try{Put(store,5,InstalledId);Marshal.ThrowExceptionForHR(store.Commit());}finally{Marshal.FinalReleaseComObject(store);}
    }
    internal static string? ReadShortcut(string path){var id=StoreId;Marshal.ThrowExceptionForHR(SHGetPropertyStoreFromParsingName(path,IntPtr.Zero,0,ref id,out var store));try{return Read(store,5);}finally{Marshal.FinalReleaseComObject(store);}}
    internal static void Window(IntPtr hwnd){
        var id=StoreId;Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(hwnd,ref id,out var store));
        try{Put(store,5,CurrentId);}finally{Marshal.FinalReleaseComObject(store);}
    }
    internal static string? ReadWindow(IntPtr hwnd){var id=StoreId;Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(hwnd,ref id,out var store));try{return Read(store,5);}finally{Marshal.FinalReleaseComObject(store);}}
    private static string? Read(IPropertyStore store,uint pid){var key=new PropertyKey{Id=AppKey,Pid=pid};Marshal.ThrowExceptionForHR(store.GetValue(ref key,out var value));try{return value.Type==31?Marshal.PtrToStringUni(value.Pointer):null;}finally{PropVariantClear(ref value);}}
    private static void Put(IPropertyStore store,uint pid,string text){var key=new PropertyKey{Id=AppKey,Pid=pid};var value=new PropVariant{Type=31,Pointer=Marshal.StringToCoTaskMemUni(text)};try{Marshal.ThrowExceptionForHR(store.SetValue(ref key,ref value));}finally{Marshal.FreeCoTaskMem(value.Pointer);}}
    [StructLayout(LayoutKind.Sequential)]private struct PropertyKey{internal Guid Id;internal uint Pid;}
    [StructLayout(LayoutKind.Explicit,Size=24)]private struct PropVariant{[FieldOffset(0)]internal ushort Type;[FieldOffset(8)]internal IntPtr Pointer;}
    [ComImport,Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]private interface IPropertyStore{
        [PreserveSig]int GetCount(out uint count);[PreserveSig]int GetAt(uint index,out PropertyKey key);[PreserveSig]int GetValue(ref PropertyKey key,out PropVariant value);[PreserveSig]int SetValue(ref PropertyKey key,ref PropVariant value);[PreserveSig]int Commit();
    }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]private static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    [DllImport("shell32.dll")]private static extern int GetCurrentProcessExplicitAppUserModelID(out IntPtr id);
    [DllImport("shell32.dll")]private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd,ref Guid id,[MarshalAs(UnmanagedType.Interface)]out IPropertyStore store);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]private static extern int SHGetPropertyStoreFromParsingName(string path,IntPtr bind,uint flags,ref Guid id,[MarshalAs(UnmanagedType.Interface)]out IPropertyStore store);
    [DllImport("ole32.dll")]private static extern int PropVariantClear(ref PropVariant value);
}
