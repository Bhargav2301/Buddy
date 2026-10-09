using Buddy.Server;
using System.IO;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Tesseract;

namespace Buddy.Windows;

internal interface ISelectedImageTextExtractor
{
    Task<ContextSourceReadResult> ExtractAsync(ContextSourceReadResult selectedImage, CancellationToken ct);
}

// Explicit local conversion of a selected image, never a screen capture or upload.
// Its output is reviewed again as extracted text; it is not an image attachment.
internal sealed class SelectedImageOcr : ISelectedImageTextExtractor
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Regex PrivateLabel = new(@"\b(password|passcode|otp|secret|api[ _-]?key|card number|credential)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal const string Method = "local-tesseract-5.2-eng-sparse-v1";
    public async Task<ContextSourceReadResult> ExtractAsync(ContextSourceReadResult selectedImage, CancellationToken ct)
    {
        if (selectedImage is not { Kind: ContextSourceKind.LocalImage, Image: { } image })
            throw new InvalidOperationException("Select a PNG or JPEG image before extracting text.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(8));
        var token = deadline.Token;
        try {
            await Gate.WaitAsync(token).ConfigureAwait(false); byte[] pixels;
            try { pixels = image.CopyEncodedBytes(); } catch { Gate.Release(); throw; }
            // Native OCR may finish after cancellation, but owns only its private
            // byte copy; its late result cannot publish itself or dispatch anything.
            var worker = Task.Run(() => {
                try {
                    token.ThrowIfCancellationRequested();
                    using var engine = new TesseractEngine(Path.Combine(AppContext.BaseDirectory, "tessdata"), "eng", EngineMode.LstmOnly);
                    using var decoded = Pix.LoadFromMemory(pixels);
                    if (decoded.Width != image.Info.Width || decoded.Height != image.Info.Height)
                        throw new InvalidOperationException("OCR decoded different image dimensions. Conversion stopped.");
                    using var page = engine.Process(decoded, PageSegMode.SparseText);
                    using var iterator = page.GetIterator(); iterator.Begin();
                    var lines = new List<string>(); int visited = 0, count = 0, omitted = 0;
                    do {
                        token.ThrowIfCancellationRequested();
                        if (++visited > 400) throw new InvalidOperationException("The image exceeds the local OCR line limit. Choose a smaller image.");
                        string? text = iterator.GetText(PageIteratorLevel.TextLine)?.Trim();
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        double confidence = iterator.GetConfidence(PageIteratorLevel.TextLine) / 100d;
                        if (Security.Redact(text) != text || PrivateLabel.IsMatch(text) || !double.IsFinite(confidence) || confidence < .60 || text.Length > 200) { omitted++; continue; }
                        ContextSourceReader.RequireText(text);
                        count += text.Length + 1;
                        if (count > ContextSourceReader.MaximumTextCharacters) throw new InvalidOperationException("The extracted text exceeds 20,000 characters. Choose a smaller image.");
                        lines.Add(text);
                    } while (iterator.Next(PageIteratorLevel.TextLine));
                    token.ThrowIfCancellationRequested(); image.RequireAvailable();
                    if (lines.Count == 0) throw new InvalidOperationException("No readable nonprivate text was found. OCR uses English and may miss visual details.");
                    string method = Method + (omitted > 0 ? "; some lines omitted" : "");
                    return ContextSourceReader.FromOcr(selectedImage, string.Join("\n", lines), method);
                } catch (Exception error) when (error is not OperationCanceledException and not InvalidOperationException and not ObjectDisposedException) {
                    throw new InvalidOperationException("Local image text extraction could not finish. Check the bundled English OCR files and try another image.");
                } finally { CryptographicOperations.ZeroMemory(pixels); Gate.Release(); }
            }, CancellationToken.None);
            // Observe late faults without retaining source data or changing UI state.
            _ = worker.ContinueWith(task => _ = task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return await WaitForWorkerAsync(worker, token).ConfigureAwait(false);
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            throw new InvalidOperationException("Local image text extraction timed out. Nothing was attached or sent.");
        }
    }

    internal static async Task<ContextSourceReadResult> WaitForWorkerAsync(Task<ContextSourceReadResult> worker, CancellationToken ct)
    {
        try { return await worker.WaitAsync(ct).ConfigureAwait(false); }
        catch {
            // An abandoned worker can still succeed after the await is
            // cancelled. Release that result's original-asset lease too.
            _ = worker.ContinueWith(task => {
                if (task.Status == TaskStatus.RanToCompletion) task.Result.Dispose();
                else _ = task.Exception;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
    }
}
