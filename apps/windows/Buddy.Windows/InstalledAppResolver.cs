using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using System.Xml.Linq;
using System.Text;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Core;
using Windows.Management.Deployment;

namespace Buddy.Windows;

internal sealed record VerifiedAppLaunch(string Alias, string Executable, ProcessStartInfo? Start = null,
    string PackageFullName = "", string AppUserModelId = "", AppListEntry? Registration = null,
    string PackageRoot = "", string MainExecutable = "", Package? PackageRegistration = null)
{
    internal bool IsPackaged => PackageFullName.Length != 0 || AppUserModelId.Length != 0 || Registration is not null || PackageRoot.Length != 0 || MainExecutable.Length != 0 || PackageRegistration is not null;
    internal bool SameIdentity(VerifiedAppLaunch other) => Alias == other.Alias &&
        Executable.Equals(other.Executable, StringComparison.OrdinalIgnoreCase) &&
        PackageFullName == other.PackageFullName && AppUserModelId == other.AppUserModelId &&
        PackageRoot.Equals(other.PackageRoot, StringComparison.OrdinalIgnoreCase) &&
        MainExecutable.Equals(other.MainExecutable, StringComparison.OrdinalIgnoreCase) && IsPackaged == other.IsPackaged;
    internal async Task StartAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (IsPackaged) {
            // Launch the exact signed Windows registration, never its manifest
            // executable via CreateProcess. Partial package metadata cannot fall
            // through to the classic executable path.
            if (Registration is null || Start is not null || Registration.AppUserModelId != AppUserModelId ||
                PackageRegistration is null || PackageRegistration.Id.FullName != PackageFullName || PackageRoot.Length == 0 || MainExecutable.Length == 0 ||
                PackageRegistration.IsDevelopmentMode || PackageRegistration.SignatureKind is not (PackageSignatureKind.Store or PackageSignatureKind.System) ||
                !PackageRegistration.Status.VerifyIsOK())
                throw new InvalidOperationException("The exact signed app registration is no longer available; nothing was launched.");
            ct.ThrowIfCancellationRequested();
            if (!await Registration.LaunchAsync().AsTask(ct)) throw new InvalidOperationException("Windows did not accept the requested installed-app launch.");
        } else {
            if (Start is null || Start.UseShellExecute || Start.ArgumentList.Count != 0 || Start.Arguments.Length != 0 || Start.FileName != Executable)
                throw new InvalidOperationException("The launch is not a fixed argument-free executable.");
            ct.ThrowIfCancellationRequested();
            using var process = Process.Start(Start);
        }
        ct.ThrowIfCancellationRequested();
    }
}

internal static class InstalledAppResolver
{
    internal static async Task<VerifiedAppLaunch> ResolveAdditionalAsync(string alias, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (alias is not ("calculator" or "camera" or "spotify")) throw new InvalidOperationException("Unsupported installed-app alias.");
        var candidates = new List<VerifiedAppLaunch>();
        if (alias == "spotify") {
            var locations = new[] {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Spotify", "Spotify.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Spotify", "Spotify.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Spotify", "Spotify.exe")
            };
            foreach (string path in locations.Distinct(StringComparer.OrdinalIgnoreCase).Where(File.Exists)) {
                ct.ThrowIfCancellationRequested(); ValidateSpotify(path);
                candidates.Add(new(alias, path, new(path) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path)! }));
            }
        }
        var (family, publisher, entryId) = RegisteredIdentity(alias);
        var packages = new PackageManager().FindPackagesForUser("", family).Take(3).ToArray();
        if (packages.Length > 1) throw new InvalidOperationException("Multiple registered versions of the requested app were found; no app was chosen or launched.");
        foreach (var package in packages) {
            ct.ThrowIfCancellationRequested();
            if (package.Id.FamilyName != family || package.Id.PublisherId != publisher || package.IsDevelopmentMode || package.IsFramework || package.IsResourcePackage ||
                package.SignatureKind is not (PackageSignatureKind.Store or PackageSignatureKind.System) || !package.Status.VerifyIsOK())
                throw new InvalidOperationException("The requested app registration is not a healthy signed package; nothing was launched.");
            string root = Path.GetFullPath(package.InstalledLocation.Path);
            string windowsApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps") + Path.DirectorySeparatorChar;
            if (!root.StartsWith(windowsApps, StringComparison.OrdinalIgnoreCase) || root[windowsApps.Length..].Contains(Path.DirectorySeparatorChar) ||
                !Path.GetFileName(root).Equals(package.Id.FullName, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The registered app is outside the verified package location.");
            string manifest = Path.Combine(root, "AppxManifest.xml"); ValidateNoLinks(manifest);
            using var reader = XmlReader.Create(manifest, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 262144 });
            var document = XDocument.Load(reader);
            var entries = await package.GetAppListEntriesAsync().AsTask(ct);
            string expectedAumid = family + "!" + entryId;
            var registrations = entries.Where(e => e.AppUserModelId == expectedAumid).Take(2).ToArray();
            var applications = document.Descendants().Where(e => e.Name.LocalName == "Application" && (string?)e.Attribute("Id") == entryId).Take(2).ToArray();
            if (registrations.Length != 1 || applications.Length != 1) throw new InvalidOperationException("The requested app has no unique verified launch registration.");
            string relative = (string?)applications[0].Attribute("Executable") ?? "";
            string executable = RegisteredExecutable(alias, root, relative);
            ValidateNoLinks(executable);
            // The registered Store launch helper need not be the eventual main
            // window process. Only fixed main-executable layouts are accepted.
            var mainCandidates = RegisteredMainExecutables(alias, root).Where(File.Exists).ToArray();
            if (mainCandidates.Length != 1) throw new InvalidOperationException("The signed package has no unique supported main application executable.");
            ValidateNoLinks(mainCandidates[0]);
            candidates.Add(new(alias, executable, PackageFullName: package.Id.FullName, AppUserModelId: expectedAumid, Registration: registrations[0],
                PackageRoot: root, MainExecutable: mainCandidates[0], PackageRegistration: package));
        }
        ct.ThrowIfCancellationRequested();
        if (candidates.Count != 1) throw new InvalidOperationException(candidates.Count == 0
            ? "A verified " + Buddy.Server.AppLaunchIntent.DisplayName(alias) + " installation was not found. No other app or website was opened."
            : "More than one verified installation of this app was found. No installation was chosen or launched.");
        return candidates[0];
    }
    internal static (string Family, string Publisher, string EntryId) RegisteredIdentity(string alias) => alias switch {
        "calculator" => ("Microsoft.WindowsCalculator_8wekyb3d8bbwe", "8wekyb3d8bbwe", "App"),
        "camera" => ("Microsoft.WindowsCamera_8wekyb3d8bbwe", "8wekyb3d8bbwe", "App"),
        "spotify" => ("SpotifyAB.SpotifyMusic_zpdnekdrzrea0", "zpdnekdrzrea0", "Spotify"),
        _ => throw new InvalidOperationException("Unsupported registered app.")
    };
    internal static string RegisteredExecutable(string alias, string root, string relative)
    {
        // Audited exact App Ids are App (Calculator/Camera) and Spotify. Do not
        // accept SpotifyLauncher, SpotifyCli, widgets or arbitrary manifest apps.
        string[] allowed = alias switch {
            "calculator" => ["CalculatorApp.exe"],
            "camera" => ["WindowsCamera.exe"],
            "spotify" => ["Spotify.exe", @"Spotify\Spotify.exe", "SpotifyMigrator.exe"],
            _ => throw new InvalidOperationException("Unsupported registered app.")
        };
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':') || relative.Contains('/') ||
            relative.Split('\\').Any(part => part.Length == 0 || part is "." or "..") || !allowed.Contains(relative, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("The registered app executable does not match its fixed alias.");
        string path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The registered app executable escaped its package.");
        return path;
    }
    internal static IReadOnlyList<string> RegisteredMainExecutables(string alias, string root) => alias switch {
        "calculator" => [RegisteredExecutable(alias, root, "CalculatorApp.exe")],
        "camera" => [RegisteredExecutable(alias, root, "WindowsCamera.exe")],
        "spotify" => [RegisteredExecutable(alias, root, "Spotify.exe"), RegisteredExecutable(alias, root, @"Spotify\Spotify.exe")],
        _ => throw new InvalidOperationException("Unsupported registered app.")
    };
    internal static bool MatchesRegisteredProcess(VerifiedAppLaunch launch, string actualPath, string processPackageFullName) =>
        launch.IsPackaged && launch.PackageFullName.Length > 0 && launch.PackageFullName == processPackageFullName &&
        launch.MainExecutable.Length > 0 && actualPath.Equals(launch.MainExecutable, StringComparison.OrdinalIgnoreCase) &&
        RegisteredMainExecutables(launch.Alias, launch.PackageRoot).Contains(actualPath, StringComparer.OrdinalIgnoreCase);

    internal static string ProcessPackageFullName(uint processId)
    {
        // OS process package identity comes from a query-only handle, not from
        // a process name, executable label or model-produced claim.
        IntPtr process = OpenProcess(0x1000, false, processId);
        if (process == IntPtr.Zero) throw new InvalidOperationException("The foreground process package identity could not be inspected.");
        try {
            uint length = 0;
            if (GetPackageFullName(process, ref length, null) != 122 || length is < 2 or > 1024)
                throw new InvalidOperationException("The foreground process has no verifiable package identity.");
            var name = new StringBuilder((int)length);
            if (GetPackageFullName(process, ref length, name) != 0 || name.Length == 0)
                throw new InvalidOperationException("The foreground process package identity changed or was unavailable.");
            return name.ToString();
        } finally { CloseHandle(process); }
    }
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetPackageFullName(IntPtr process, ref uint length, StringBuilder? name);
    internal static void ValidateSpotify(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !Path.GetFileName(path).Equals("Spotify.exe", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Invalid Spotify path.");
        ValidateNoLinks(path);
        var version = FileVersionInfo.GetVersionInfo(path);
        if (version.ProductName != "Spotify" || !TrustedSignature(path)) throw new InvalidOperationException("Spotify publisher verification failed; nothing was launched.");
        using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
        if (!certificate.GetNameInfo(X509NameType.SimpleName, false).Equals("Spotify AB", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Spotify has an unexpected signing publisher; nothing was launched.");
    }
    private static void ValidateNoLinks(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) throw new InvalidOperationException("The installed app file was not found.");
        for (FileSystemInfo? entry = new FileInfo(path); entry is not null; entry = entry is FileInfo file ? file.Directory : ((DirectoryInfo)entry).Parent)
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("The installed app path contains a link; launch refused.");
    }
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
