using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Buddy.Windows;

// This is deliberately not a general catalog-signature fallback. It accepts
// only the exact System32 ApplicationFrameHost used by Calculator's frame.
// Native contracts: https://learn.microsoft.com/windows/win32/api/wintrust/ns-wintrust-wintrust_catalog_info
// https://learn.microsoft.com/windows/win32/api/wintrust/ns-wintrust-wintrust_data
// https://learn.microsoft.com/windows/win32/api/mscat/nf-mscat-cryptcatadminacquirecontext2
// https://learn.microsoft.com/windows/win32/api/mscat/nf-mscat-cryptcatadminenumcatalogfromhash
// https://learn.microsoft.com/windows/win32/api/wintrust/nf-wintrust-wthelpergetprovcertfromchain
internal static class FixedFrameHostTrust
{
    internal static string ExpectedPath => Path.Combine(Environment.SystemDirectory, "ApplicationFrameHost.exe");
    internal static bool ExpectedPublisher(string publisher) => publisher == "Microsoft Windows";
    internal static IDisposable Acquire(string actualPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!actualPath.Equals(ExpectedPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The application frame host is outside its fixed Windows location.");
        var lease = WindowsRoutineAppBackend.ExecutableLease.Acquire(ExpectedPath, ct);
        try {
            using var member = new FileStream(ExpectedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (member.Length is <= 0 or > 67108864) throw new InvalidOperationException("The fixed frame host file could not be bounded.");
            VerifyCatalog(member.SafeFileHandle, ct);
            ct.ThrowIfCancellationRequested();
            return lease; // Pins the exact no-link host file throughout frame revalidation.
        } catch { lease.Dispose(); throw; }
    }
    private static void VerifyCatalog(SafeFileHandle member, CancellationToken ct)
    {
        IntPtr admin = IntPtr.Zero, catalog = IntPtr.Zero;
        try {
            if (!CryptCATAdminAcquireContext2(out admin, IntPtr.Zero, "SHA256", IntPtr.Zero, 0)) throw Failure();
            uint length = 0;
            if (!CryptCATAdminCalcHashFromFileHandle2(admin, member, ref length, null, 0) || length != 32) throw Failure();
            var hash = new byte[length];
            if (!CryptCATAdminCalcHashFromFileHandle2(admin, member, ref length, hash, 0) || length != hash.Length) throw Failure();
            int lastError = unchecked((int)0x800B0100); // No verified catalog is not success.
            for (int count = 0; count < 8; count++) {
                ct.ThrowIfCancellationRequested();
                // As documented, continue from the previous catalog context; the
                // enumeration owns prior contexts, and the final one is released below.
                catalog = CryptCATAdminEnumCatalogFromHash(admin, hash, length, 0, ref catalog);
                if (catalog == IntPtr.Zero) break;
                var info = new CatalogPath { Size = (uint)Marshal.SizeOf<CatalogPath>(), Path = "" };
                if (!CryptCATCatalogInfoFromContext(catalog, ref info, 0)) { lastError = Marshal.GetLastWin32Error(); continue; }
                string path = Path.GetFullPath(info.Path);
                string catRoot = Path.Combine(Environment.SystemDirectory, "CatRoot") + Path.DirectorySeparatorChar;
                if (!path.StartsWith(catRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetExtension(path).Equals(".cat", StringComparison.OrdinalIgnoreCase)) continue;
                using var catalogLease = WindowsRoutineAppBackend.ExecutableLease.Acquire(path, ct);
                lastError = VerifyMember(admin, member, hash, path, ct);
                if (lastError == 0) return;
            }
            throw new FrameHostTrustException(lastError);
        } finally {
            if (catalog != IntPtr.Zero) CryptCATAdminReleaseCatalogContext(admin, catalog, 0);
            if (admin != IntPtr.Zero) CryptCATAdminReleaseContext(admin, 0);
        }
    }
    private static int VerifyMember(IntPtr admin, SafeFileHandle member, byte[] hash, string catalogPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var pin = GCHandle.Alloc(hash, GCHandleType.Pinned);
        IntPtr pointer = IntPtr.Zero;
        bool marshalled = false;
        try {
            var catalog = new CatalogMember {
                Size = (uint)Marshal.SizeOf<CatalogMember>(), CatalogFile = catalogPath, MemberTag = Convert.ToHexString(hash),
                MemberFile = ExpectedPath, MemberHandle = member.DangerousGetHandle(), Hash = pin.AddrOfPinnedObject(),
                HashLength = (uint)hash.Length, Admin = admin
            };
            pointer = Marshal.AllocHGlobal(Marshal.SizeOf<CatalogMember>());
            Marshal.StructureToPtr(catalog, pointer, false); marshalled = true;
            // UI_NONE, CATALOG, VERIFY; cache-only chain revocation excluding the
            // trust root. Never use HASH_ONLY, CHECK_NONE, or NO_POLICY_USAGE.
            var data = new TrustData { Size = (uint)Marshal.SizeOf<TrustData>(), Ui = 2, Revocation = 1,
                Choice = 2, Subject = pointer, StateAction = 1, Flags = 0x1000 | 0x80 | 0x2000 };
            var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
            try {
                int result = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
                ct.ThrowIfCancellationRequested();
                if (result != 0) return result;
                // Read the certificate actually accepted by this trust state;
                // extracting an unverified certificate from a file is insufficient.
                IntPtr provider = WTHelperProvDataFromStateData(data.State);
                IntPtr signer = provider == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvSignerFromChain(provider, 0, false, 0);
                IntPtr cert = signer == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvCertFromChain(signer, 0);
                if (cert == IntPtr.Zero) return unchecked((int)0x800B010B);
                var accepted = Marshal.PtrToStructure<ProviderCertificate>(cert);
                if (accepted.Size < Marshal.SizeOf<ProviderCertificate>() || accepted.Certificate == IntPtr.Zero || accepted.TestCertificate != 0 || accepted.Error != 0)
                    return unchecked((int)0x800B010B);
                using var certificate = new X509Certificate2(accepted.Certificate);
                return ExpectedPublisher(certificate.GetNameInfo(X509NameType.SimpleName, false)) ? 0 : unchecked((int)0x800B010B);
            } finally { data.StateAction = 2; WinVerifyTrust(new IntPtr(-1), ref action, ref data); }
        } finally {
            if (pointer != IntPtr.Zero) { if (marshalled) Marshal.DestroyStructure<CatalogMember>(pointer); Marshal.FreeHGlobal(pointer); }
            pin.Free();
        }
    }
    private static FrameHostTrustException Failure() => new(Marshal.GetLastWin32Error());
    private sealed class FrameHostTrustException : InvalidOperationException
    {
        internal FrameHostTrustException(int error) : base("The fixed Windows frame host catalog could not be verified (0x" + unchecked((uint)error).ToString("X8") + ").") => HResult = error;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct CatalogPath {
        internal uint Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string Path;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct CatalogMember {
        internal uint Size, Version;
        [MarshalAs(UnmanagedType.LPWStr)] internal string CatalogFile;
        [MarshalAs(UnmanagedType.LPWStr)] internal string MemberTag;
        [MarshalAs(UnmanagedType.LPWStr)] internal string MemberFile;
        internal IntPtr MemberHandle, Hash;
        internal uint HashLength;
        internal IntPtr CatalogContext, Admin;
    }
    [StructLayout(LayoutKind.Sequential)] private struct TrustData {
        internal uint Size; internal IntPtr Policy, Sip; internal uint Ui, Revocation, Choice;
        internal IntPtr Subject; internal uint StateAction; internal IntPtr State, Url;
        internal uint Flags, Context; internal IntPtr SignatureSettings;
    }
    // Only the documented prefix through dwError is read; remaining fields are unused.
    [StructLayout(LayoutKind.Sequential)] private struct ProviderCertificate {
        internal uint Size; internal IntPtr Certificate; internal int Commercial, TrustedRoot, SelfSigned, TestCertificate;
        internal uint RevokedReason, Confidence, Error;
    }
    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptCATAdminAcquireContext2(out IntPtr admin, IntPtr subsystem, string algorithm, IntPtr policy, uint flags);
    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptCATAdminCalcHashFromFileHandle2(IntPtr admin, SafeFileHandle file, ref uint length, [Out] byte[]? hash, uint flags);
    [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = true)] private static extern IntPtr CryptCATAdminEnumCatalogFromHash(IntPtr admin, byte[] hash, uint length, uint flags, ref IntPtr previous);
    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptCATCatalogInfoFromContext(IntPtr catalog, ref CatalogPath info, uint flags);
    [DllImport("wintrust.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptCATAdminReleaseCatalogContext(IntPtr admin, IntPtr catalog, uint flags);
    [DllImport("wintrust.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CryptCATAdminReleaseContext(IntPtr admin, uint flags);
    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)] private static extern int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern IntPtr WTHelperProvDataFromStateData(IntPtr state);
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern IntPtr WTHelperGetProvSignerFromChain(IntPtr provider, uint signer, [MarshalAs(UnmanagedType.Bool)] bool counterSigner, uint counterSignerIndex);
    [DllImport("wintrust.dll", ExactSpelling = true)] private static extern IntPtr WTHelperGetProvCertFromChain(IntPtr signer, uint certificate);
}
