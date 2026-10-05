using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Buddy.Server;
using Microsoft.Win32.SafeHandles;

namespace Buddy.Windows;

// The caller must obtain an explicit file selection, preview this snapshot, and
// obtain Add consent. Reading does not attach, transmit or persist a resource.
internal sealed record RefinementResourceReadResult(RefinementContextSource Source, int ByteCount, string Sha256);

internal static class RefinementResourceReader
{
    internal const int MaximumBytes = 64 * 1024;
    internal const int MaximumCharacters = 20_000;
    private const uint ReadAttributes = 0x80, GenericRead = 0x80000000, ShareRead = 1, OpenExisting = 3;
    private const uint OpenReparsePoint = 0x00200000, BackupSemantics = 0x02000000;
    private const uint Overlapped = 0x40000000, SequentialScan = 0x08000000;
    private const uint ReparsePoint = 0x400, DirectoryAttribute = 0x10;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static Task<RefinementResourceReadResult> ReadAsync(string selectedPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Path/handle operations are synchronous Win32 calls; keep them off the UI
        // thread as well as using cancellable asynchronous reads for the content.
        return Task.Run(() => ReadSelectedAsync(selectedPath, cancellationToken), cancellationToken);
    }

    private static async Task<RefinementResourceReadResult> ReadSelectedAsync(string selectedPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Local refinement file selection requires Windows.");
        var (path, title, parts) = ValidatePath(selectedPath);
        var directories = new List<SafeFileHandle>();
        byte[]? bytes = null;
        try {
            string drive = path[..3];
            // A mapped drive is also a network path even when it has a drive letter.
            if (GetDriveTypeW(drive) is not (2 or 3 or 5 or 6))
                throw Refused("Choose a file on a local drive, not a network or unavailable drive.");
            string current = drive;
            uint? volume = null;
            for (int i = -1; i < parts.Length - 1; i++) {
                ct.ThrowIfCancellationRequested();
                if (i >= 0) current = Path.Combine(current, parts[i]);
                var handle = Open(current, ReadAttributes, BackupSemantics | OpenReparsePoint);
                directories.Add(handle);
                var info = Information(handle);
                if ((info.Attributes & ReparsePoint) != 0 || (info.Attributes & DirectoryAttribute) == 0)
                    throw Refused("Choose a file without linked folders or reparse points.");
                VerifyPath(handle, current);
                volume ??= info.VolumeSerial;
                if (volume != info.VolumeSerial) throw Refused("The selected file path changed; select it again.");
            }
            ct.ThrowIfCancellationRequested();
            using var file = Open(path, GenericRead, OpenReparsePoint | Overlapped | SequentialScan);
            var before = Information(file);
            if ((before.Attributes & (ReparsePoint | DirectoryAttribute)) != 0 || before.Links != 1)
                throw Refused("Choose one regular text file without links.");
            if (before.VolumeSerial != volume) throw Refused("The selected file path changed; select it again.");
            VerifyPath(file, path);
            long length = ((long)before.SizeHigh << 32) | before.SizeLow;
            if (length > MaximumBytes) throw Refused("The selected file exceeds 64 KiB; choose a smaller file.");
            if (length == 0) throw Refused("The selected file contains no text.");
            bytes = new byte[checked((int)length + 1)];
            using var stream = new FileStream(file, FileAccess.Read, 4096, isAsync: true);
            int used = 0;
            while (used < bytes.Length) {
                int read = await stream.ReadAsync(bytes.AsMemory(used), ct).ConfigureAwait(false);
                if (read == 0) break;
                used += read;
            }
            ct.ThrowIfCancellationRequested();
            if (used != length) throw Refused("The selected file changed while reading; select it again.");
            int offset = used >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf ? 3 : 0;
            string text = StrictUtf8.GetString(bytes, offset, used - offset);
            if (text.Length > MaximumCharacters) throw Refused("The selected file exceeds 20,000 text characters; choose a smaller file.");
            if (string.IsNullOrWhiteSpace(text) || text.Contains('\0')) throw Refused("Choose a nonempty UTF-8 text file without binary content.");
            ct.ThrowIfCancellationRequested();
            var source = new RefinementContextSource("file_" + Guid.NewGuid().ToString("N"), title, text,
                Provenance: "document", Disposition: "reference", Required: false, Url: null);
            return new(source, used, Convert.ToHexString(SHA256.HashData(bytes.AsSpan(0, used))));
        } catch (DecoderFallbackException) {
            throw Refused("The selected file is not valid UTF-8; save a UTF-8 text copy and select it again.");
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            ct.ThrowIfCancellationRequested();
            // Native/IO errors can contain a local path. Never expose that path in
            // the returned source or in an exception intended for the review UI.
            throw Refused("The selected file could not be read safely; choose an accessible local text file.");
        } finally {
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
            for (int i = directories.Count - 1; i >= 0; i--) directories[i].Dispose();
        }
    }

    private static (string Path, string Title, string[] Parts) ValidatePath(string path)
    {
        // Reject URI/UNC/device/drive-relative paths before any filesystem call.
        if (string.IsNullOrWhiteSpace(path) || path.Length > 32_000 || path.Length < 4 ||
            !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] is not ('\\' or '/') || !Path.IsPathFullyQualified(path))
            throw Refused("Choose an absolute local drive path to a .txt or .md file.");
        path = path.Replace('/', '\\');
        string[] parts = path[3..].Split('\\');
        foreach (string part in parts) {
            if (part.Length == 0 || part is "." or ".." || part.EndsWith(' ') || part.EndsWith('.') ||
                part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || ReservedName(part))
                throw Refused("The selected path contains an unsupported name or alternate stream.");
        }
        string title = parts[^1];
        if (title.Length > 160) throw Refused("The filename exceeds 160 characters; choose a shorter filename.");
        foreach (char c in title) {
            if (char.IsControl(c) || c is '\u200e' or '\u200f' or >= '\u202a' and <= '\u202e' or >= '\u2066' and <= '\u2069')
                throw Refused("The filename contains hidden direction or control characters.");
        }
        string extension = Path.GetExtension(title);
        if (!extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".md", StringComparison.OrdinalIgnoreCase))
            throw Refused("Choose a UTF-8 .txt or .md file.");
        return (path, title, parts);
    }

    private static bool ReservedName(string part)
    {
        string stem = part.Split('.')[0];
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("CONIN$", StringComparison.OrdinalIgnoreCase) || stem.Equals("CONOUT$", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("CLOCK$", StringComparison.OrdinalIgnoreCase)) return true;
        return stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
            (stem[3] is >= '1' and <= '9' or '\u00b9' or '\u00b2' or '\u00b3');
    }

    private static SafeFileHandle Open(string path, uint access, uint flags)
    {
        // Refuse delete/write sharing. Held directory handles prevent ancestor
        // replacement/reparse mutation between checking a parent and opening a child.
        var handle = CreateFileW("\\\\?\\" + path, access, ShareRead, IntPtr.Zero, OpenExisting, flags, IntPtr.Zero);
        if (handle.IsInvalid) { handle.Dispose(); throw new IOException("Local file handle could not be opened."); }
        return handle;
    }
    private static HandleInformation Information(SafeFileHandle handle)
    {
        if (GetFileType(handle) != 1 || !GetFileInformationByHandle(handle, out var info))
            throw new IOException("Local disk file information could not be read.");
        return info;
    }
    private static void VerifyPath(SafeFileHandle handle, string expected)
    {
        var buffer = new StringBuilder(32_768);
        uint size = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
        if (size == 0 || size >= buffer.Capacity) throw new IOException("Local file path could not be verified.");
        // Also refuses SUBST/short-name aliases and unexpected redirecting roots.
        string actual = buffer.ToString();
        if (!actual.Equals("\\\\?\\" + expected, StringComparison.OrdinalIgnoreCase))
            throw Refused("Choose the original local file path without aliases or redirection.");
    }
    private static InvalidOperationException Refused(string message) => new(message);

    [StructLayout(LayoutKind.Sequential)]
    private struct HandleInformation
    {
        internal uint Attributes;
        internal System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
        internal uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint sharing, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out HandleInformation info);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetFileType(SafeFileHandle handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint GetDriveTypeW(string rootPath);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint size, uint flags);
}
