using NAudio.Wave;
using System.Collections.Concurrent;

namespace Buddy.Windows;

internal sealed class MicrophoneStream : Stream
{
    private readonly BlockingCollection<byte[]> chunks = new(100);
    private readonly WaveInEvent input;
    private byte[]? pending;
    private int offset;
    private int ended, disposed;
    private long position;
    internal static AudioChoice[] Devices() => Enumerable.Range(0, WaveIn.DeviceCount)
        .Select(i => WaveIn.GetCapabilities(i).ProductName).Distinct()
        .Select(name => new AudioChoice(name, name)).ToArray();
    internal MicrophoneStream(string name)
    {
        int number = -1;
        if (name.Length > 0) {
            var matches = Enumerable.Range(0, WaveIn.DeviceCount).Where(i => WaveIn.GetCapabilities(i).ProductName == name).ToArray();
            if (matches.Length != 1) throw new InvalidOperationException("Selected microphone is missing or ambiguous. Choose it again in Voice settings.");
            number = matches[0];
        }
        input = new WaveInEvent { DeviceNumber = number, WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 40 };
        input.DataAvailable += (_, e) => {
            var bytes = e.Buffer.AsSpan(0, e.BytesRecorded).ToArray();
            try { if (!chunks.TryAdd(bytes)) { Array.Clear(bytes); End(); } }
            catch (InvalidOperationException) { Array.Clear(bytes); }
        };
        input.RecordingStopped += (_, _) => End();
        try { input.StartRecording(); } catch { End(); input.Dispose(); throw; }
    }
    private void End() { if (Interlocked.Exchange(ref ended, 1) == 0) chunks.CompleteAdding(); }
    public override int Read(byte[] buffer, int start, int count)
    {
        if (count == 0) return 0;
        int total = 0;
        while (total < count) {
            if (pending is null) {
                try { pending = chunks.Take(); offset = 0; } catch (InvalidOperationException) { break; }
            }
            int length = Math.Min(count - total, pending.Length - offset);
            Array.Copy(pending, offset, buffer, start + total, length); offset += length; total += length;
            if (offset == pending.Length) { Array.Clear(pending); pending = null; }
        }
        position += total; return total;
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && Interlocked.Exchange(ref disposed, 1) == 0) { End(); input.StopRecording(); input.Dispose(); while (chunks.TryTake(out var bytes)) Array.Clear(bytes); }
        base.Dispose(disposing);
    }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    // SAPI's SpStreamWrapper snapshots Length and reads Position even for live audio.
    // A live source has an unbounded length until Read returns zero after Stop.
    public override long Length => long.MaxValue;
    public override long Position { get => position; set { if (value != position) throw new NotSupportedException(); } }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin)
        => origin == SeekOrigin.Current && offset == 0 || origin == SeekOrigin.Begin && offset == position ? position : throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
