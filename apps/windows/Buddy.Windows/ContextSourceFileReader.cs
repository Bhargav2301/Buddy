using System.Runtime.InteropServices;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Buddy.Windows;

// TXT/MD/PNG/JPEG counterpart of RefinementResourceReader's held-handle boundary.
// No path following, ancestor replacement, write sharing or network drive reads.
internal static class ContextSourceFileReader
{
    private const uint ReadAttributes = 0x80, GenericRead = 0x80000000, ShareRead = 1, OpenExisting = 3;
    private const uint OpenReparsePoint = 0x00200000, BackupSemantics = 0x02000000;
    private const uint Overlapped = 0x40000000, SequentialScan = 0x08000000;
    private const uint ReparsePoint = 0x400, DirectoryAttribute = 0x10;
    internal static Task<(string Name, byte[] Bytes)> ReadSelectedBytesAsync(string path, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.Run(() => Read(path, ct), ct);
    }
    private static async Task<(string Name, byte[] Bytes)> Read(string selectedPath, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        var (path, title, parts) = ValidatePath(selectedPath);
        bool text = Path.GetExtension(title).Equals(".txt", StringComparison.OrdinalIgnoreCase) || Path.GetExtension(title).Equals(".md", StringComparison.OrdinalIgnoreCase);
        int maximum = text ? 65536 : ContextSourceReader.MaximumImageBytes, minimum = text ? 1 : 12;
        var directories = new List<SafeFileHandle>(); byte[]? bytes = null;
        try {
            string drive = path[..3];
            if (GetDriveTypeW(drive) is not (2 or 3 or 5 or 6)) throw Refused("Choose a file on an available local drive.");
            string current = drive; uint? volume = null;
            for (int i = -1; i < parts.Length - 1; i++) {
                ct.ThrowIfCancellationRequested();
                if (i >= 0) current = Path.Combine(current, parts[i]);
                var handle = Open(current, ReadAttributes, BackupSemantics | OpenReparsePoint); directories.Add(handle);
                var info = Information(handle);
                if ((info.Attributes & ReparsePoint) != 0 || (info.Attributes & DirectoryAttribute) == 0)
                    throw Refused("Choose a file without linked folders or reparse points.");
                VerifyPath(handle, current); volume ??= info.VolumeSerial;
                if (volume != info.VolumeSerial) throw Refused("The selected file path changed. Select it again.");
            }
            ct.ThrowIfCancellationRequested();
            using var file = Open(path, GenericRead, OpenReparsePoint | Overlapped | SequentialScan);
            var before = Information(file);
            if ((before.Attributes & (ReparsePoint | DirectoryAttribute)) != 0 || before.Links != 1)
                throw Refused("Choose one regular local file without links.");
            if (before.VolumeSerial != volume) throw Refused("The selected file path changed. Select it again.");
            VerifyPath(file, path);
            long length = ((long)before.SizeHigh << 32) | before.SizeLow;
            if (length < minimum || length > maximum) throw Refused(text ? "Choose a nonempty UTF-8 text file no larger than 64 KiB." : "Choose a nonempty PNG or JPEG image no larger than 2 MB.");
            bytes = new byte[checked((int)length)];
            using var stream = new FileStream(file, FileAccess.Read, 4096, isAsync: true); int used = 0;
            while (used < bytes.Length) {
                int read = await stream.ReadAsync(bytes.AsMemory(used), ct).ConfigureAwait(false);
                if (read == 0) break; used += read;
            }
            byte[] extra = new byte[1];
            int trailing = await stream.ReadAsync(extra, ct).ConfigureAwait(false);
            CryptographicOperations.ZeroMemory(extra); ct.ThrowIfCancellationRequested();
            var after = Information(file);
            if (used != length || trailing != 0 || before.IndexHigh != after.IndexHigh || before.IndexLow != after.IndexLow ||
                before.SizeHigh != after.SizeHigh || before.SizeLow != after.SizeLow || before.VolumeSerial != after.VolumeSerial ||
                before.Links != after.Links || before.Written.dwHighDateTime != after.Written.dwHighDateTime || before.Written.dwLowDateTime != after.Written.dwLowDateTime)
                throw Refused("The selected file changed while reading. Select it again.");
            VerifyPath(file, path); var owned = bytes; bytes = null; return (title, owned);
        } catch (Exception error) when (error is IOException or UnauthorizedAccessException) {
            ct.ThrowIfCancellationRequested(); throw Refused("The selected file could not be read safely. Choose an accessible local file.");
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
            throw Refused("Choose an absolute local drive path to a TXT, Markdown, PNG or JPEG file.");
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
        if (!extension.Equals(".txt", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".md", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".png", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            throw Refused("Choose a TXT, Markdown, PNG or JPEG file.");
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
