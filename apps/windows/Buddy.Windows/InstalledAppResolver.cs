using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace Buddy.Windows;

internal static class InstalledAppResolver
{
    internal static string ResolveComet()
    {
        var roots = new[] { Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) };
        foreach (var root in roots.Where(r => r.Length > 0)) {
            var path = Path.GetFullPath(Path.Combine(root, "Perplexity", "Comet", "Application", "comet.exe"));
            if (!File.Exists(path)) continue;
            ValidateComet(path); return path;
        }
        throw new InvalidOperationException("A verified Comet installation was not found. Install it yourself, then retry; Buddy will not download or search PATH for an executable.");
    }
    internal static void ValidateComet(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !Path.GetFileName(path).Equals("comet.exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid Comet path.");
        for (var entry = new FileInfo(path) as FileSystemInfo; entry is not null; entry = entry is FileInfo file ? file.Directory : ((DirectoryInfo)entry).Parent)
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Comet path contains a link; launch refused.");
        var version = FileVersionInfo.GetVersionInfo(path);
        if (version.ProductName != "Comet" || !TrustedSignature(path)) throw new InvalidOperationException("Comet publisher verification failed; nothing was launched.");
        using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
        if (!certificate.GetNameInfo(X509NameType.SimpleName, false).Equals("PERPLEXITY AI, INC.", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Comet has an unexpected signing publisher; nothing was launched.");
    }
    internal static ProcessStartInfo CometStartInfo()
    {
        var path = ResolveComet();
        return new(path) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path)! };
    }
    internal static bool MatchesComet(IntPtr window, string executable)
    {
        try { Native.GetWindowThreadProcessId(window, out var id); using var process = Process.GetProcessById((int)id); return string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct FileTrust { public uint Size; [MarshalAs(UnmanagedType.LPWStr)] public string Path; public IntPtr File, Subject; }
    [StructLayout(LayoutKind.Sequential)] private struct TrustData { public uint Size; public IntPtr Policy, Sip; public uint Ui, Revocation, Choice; public IntPtr File; public uint StateAction; public IntPtr State, Url; public uint Flags, Context; }
    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
    private static bool TrustedSignature(string path)
    {
        var file = new FileTrust { Size = (uint)Marshal.SizeOf<FileTrust>(), Path = path };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<FileTrust>());
        Marshal.StructureToPtr(file, pointer, false);
        var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), Ui = 2, Choice = 1, File = pointer, StateAction = 1, Flags = 0x1000 };
        var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
        try { return WinVerifyTrust(new IntPtr(-1), ref action, ref data) == 0; }
        finally { data.StateAction = 2; WinVerifyTrust(new IntPtr(-1), ref action, ref data); Marshal.DestroyStructure<FileTrust>(pointer); Marshal.FreeHGlobal(pointer); }
    }
}
