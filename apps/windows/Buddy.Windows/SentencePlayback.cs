using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Buddy.Windows;

// At most one future sentence/audio buffer is in flight. Pulling the next model
// sentence overlaps playback of the current one; no completed-answer replay.
internal static class SentencePlayback
{
    internal static string[] Parts(string text) => Regex.Split(text, @"(?<=[.!?])\s+", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)).Where(s => s.Length > 0).ToArray();

    internal static Task Run(IReadOnlyList<string> parts, Func<string, CancellationToken, Task<byte[]>> render,
        Func<byte[], CancellationToken, Task> play, CancellationToken ct) => Run(Enumerate(parts, ct), render, play, ct);

    private static async IAsyncEnumerable<string> Enumerate(IReadOnlyList<string> parts, [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var part in parts) { ct.ThrowIfCancellationRequested(); yield return part; }
        await Task.CompletedTask;
    }

    internal static async Task Run(IAsyncEnumerable<string> parts, Func<string, CancellationToken, Task<byte[]>> render,
        Func<byte[], CancellationToken, Task> play, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var pipeline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        ct = pipeline.Token;
        await using var input = parts.GetAsyncEnumerator(ct);
        Task<byte[]?>? next = null;
        byte[]? current = null;
        async Task<byte[]?> Next()
        {
            byte[]? bytes = null;
            try {
                if (!await input.MoveNextAsync()) return null;
                ct.ThrowIfCancellationRequested();
                bytes = await render(input.Current, ct);
                ct.ThrowIfCancellationRequested();
                return bytes;
            } catch {
                if (bytes is not null) Array.Clear(bytes);
                pipeline.Cancel(); // A late generation/render failure immediately stops current audio.
                throw;
            }
        }
        try {
            next = Next();
            while ((current = await next) is not null) {
                next = null;
                ct.ThrowIfCancellationRequested();
                next = Next();
                ct.ThrowIfCancellationRequested();
                await play(current, ct);
                Array.Clear(current); current = null;
                ct.ThrowIfCancellationRequested();
            }
            ct.ThrowIfCancellationRequested();
        } finally {
            pipeline.Cancel();
            if (current is not null) Array.Clear(current);
            if (next is not null) { try { var unused = await next; if (unused is not null) Array.Clear(unused); } catch { } }
        }
    }
}
