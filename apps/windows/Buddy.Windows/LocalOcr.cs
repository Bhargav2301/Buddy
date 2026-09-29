using Buddy.Server;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Tesseract;

namespace Buddy.Windows;

internal sealed record OcrText(string Ref, string Text, double Confidence, System.Windows.Rect Bounds);
internal sealed record OcrObservation(List<OcrText> Text, List<System.Windows.Rect> PrivateBounds);

internal static class LocalOcr
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly Regex PrivateLabel = new(@"\b(password|passcode|otp|secret|api[ _-]?key|card number|credential)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    internal static void ProbeDependencies()
    {
        try { using var engine = new TesseractEngine(Path.Combine(AppContext.BaseDirectory, "tessdata"), "eng", EngineMode.LstmOnly); }
        catch (Exception ex) { throw new InvalidOperationException("The bundled OCR engine could not load. Verify the package and Microsoft Visual C++ 2015–2022 x64 Runtime, then retry.", ex); }
    }

    internal static async Task<OcrObservation> Read(CapturedWindow frame, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct); deadline.CancelAfter(TimeSpan.FromSeconds(8));
        var token = deadline.Token;
        try {
            await Gate.WaitAsync(token);
            byte[] pixels;
            try { pixels = frame.Image.ToArray(); } catch { Gate.Release(); throw; }
            var worker = Task.Run(() => {
                try {
                    token.ThrowIfCancellationRequested();
                    using var engine = new TesseractEngine(Path.Combine(AppContext.BaseDirectory, "tessdata"), "eng", EngineMode.LstmOnly);
                    using var image = Pix.LoadFromMemory(pixels);
                    using var page = engine.Process(image, PageSegMode.SparseText);
                    using var iterator = page.GetIterator(); iterator.Begin();
                    var lines = new List<OcrText>(); var masks = new List<System.Windows.Rect>(); int visited = 0;
                    do {
                        token.ThrowIfCancellationRequested();
                        if (++visited > 400) throw new InvalidOperationException("The visible text exceeds the OCR privacy budget. Use a smaller window.");
                        var text = iterator.GetText(PageIteratorLevel.TextLine)?.Trim();
                        if (string.IsNullOrWhiteSpace(text) || !iterator.TryGetBoundingBox(PageIteratorLevel.TextLine, out var box)) continue;
                        if (box.Width <= 0 || box.Height <= 0 || box.X1 < 0 || box.Y1 < 0 || box.X2 > frame.PixelWidth || box.Y2 > frame.PixelHeight)
                            throw new InvalidOperationException("OCR returned unusable coordinates. Capture stopped.");
                        double scaleX = frame.Bounds.Width / frame.PixelWidth, scaleY = frame.Bounds.Height / frame.PixelHeight;
                        var bounds = new System.Windows.Rect(frame.Bounds.X + box.X1 * scaleX, frame.Bounds.Y + box.Y1 * scaleY, box.Width * scaleX, box.Height * scaleY);
                        if (Security.Redact(text) != text || PrivateLabel.IsMatch(text)) { masks.Add(bounds); continue; }
                        double confidence = iterator.GetConfidence(PageIteratorLevel.TextLine) / 100d;
                        if (double.IsFinite(confidence) && confidence >= .60 && text.Length <= 200) lines.Add(new("ocr" + visited, text, Math.Clamp(confidence, 0, 1), bounds));
                    } while (iterator.Next(PageIteratorLevel.TextLine));
                    return new OcrObservation(lines, masks);
                } finally { CryptographicOperations.ZeroMemory(pixels); Gate.Release(); }
            }, CancellationToken.None);
            return await worker.WaitAsync(token);
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            throw new InvalidOperationException("Local OCR timed out. No image was sent to the model.");
        }
    }
}
