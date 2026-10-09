using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Buddy.Browser;

// One request/one response. Frame lengths are checked before any allocation.
public static class BrowserNativeTransport
{
    public const int MaximumFrameBytes = 65_536;
    public static async Task<byte[]?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] header = new byte[4];
        int first = await stream.ReadAsync(header.AsMemory(0, 1), cancellationToken);
        if (first == 0) return null;
        await stream.ReadExactlyAsync(header.AsMemory(1), cancellationToken);
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (count is 0 or > MaximumFrameBytes) throw new InvalidDataException("Browser frame length is invalid.");
        byte[] payload = new byte[count];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return payload;
    }
    public static async Task WriteAsync(Stream stream, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (payload.Length is 0 or > MaximumFrameBytes) throw new InvalidDataException("Browser frame length is invalid.");
        byte[] header = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(header, (uint)payload.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
