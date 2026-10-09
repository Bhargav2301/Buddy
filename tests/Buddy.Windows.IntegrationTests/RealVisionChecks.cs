using Buddy.Server;
using Buddy.Windows;
using Microsoft.AspNetCore.DataProtection;
using System.IO;
using System.Text.Json;
using System.Windows;

internal static class RealVisionChecks
{
    internal static async Task<int> Run(string output)
    {
        string root = Path.Combine(Path.GetTempPath(), "Buddy.Vision.Tests." + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var results = new List<object>(); int exit = 0;
        try {
            var store = new StateStore(root, DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(root, "keys"))));
            await store.Update(s => { s.Model = s.VisionModel = "gemma3:4b"; return true; });
            using var trace = new ModelTrace();
            using var client = new System.Net.Http.HttpClient(trace) { BaseAddress = new("http://127.0.0.1:11434/"), Timeout = Timeout.InfiniteTimeSpan };
            var service = new BuddyService(store, new(client));
            foreach (bool button in new[] { true, false }) {
                using var fixture = new System.Drawing.Bitmap(900, 400);
                using (var g = System.Drawing.Graphics.FromImage(fixture)) using (var font = new System.Drawing.Font("Arial", 30)) {
                    g.Clear(System.Drawing.Color.White); g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    if (button) { g.FillRectangle(System.Drawing.Brushes.LightGray, 100, 150, 480, 100); g.DrawRectangle(System.Drawing.Pens.Black, 100, 150, 480, 100); }
                    else g.DrawString("Meeting notes", font, System.Drawing.Brushes.Black, 100, 40);
                    g.DrawString("Export Project", font, System.Drawing.Brushes.Black, 125, 172);
                }
                using var stream = new MemoryStream(); fixture.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                using var frame = new CapturedWindow(stream.ToArray(), new Rect(0, 0, 900, 400), 900, 400);
                var ocr = await LocalOcr.Read(frame, default);
                var evidence = ocr.Text.Single(t => t.Text.Equals("Export Project", StringComparison.OrdinalIgnoreCase));
                bool boundary = VisualControlBoundary.HasBoundary(frame, evidence, default);
                trace.LastContent = null;
                var box = evidence.Bounds; var bytes = frame.ForVision();
                try {
                    Console.WriteLine("Real vision: " + (button ? "button target" : "plain document mention"));
                    var result = await service.ConfirmVisualTarget(new("Export Project", "Button", "disposable-fixture", "Synthetic fixture",
                        Convert.ToBase64String(bytes), [new("fixture-label", evidence.Text, evidence.Confidence, box.X, box.Y, box.Width, box.Height, boundary)]), default);
                    bool passed = button ? result is not null : result is null;
                    if (!passed) exit = 1;
                    results.Add(new { scenario = button ? "button" : "document-mention", ocrConfidence = evidence.Confidence, boundary, result, modelObservation = trace.LastContent, passed });
                    Console.WriteLine((passed ? "PASS" : "FAIL") + ": " + (button ? "OCR-supported button confirmation" : "Document mention rejection") + " · " + trace.LastContent);
                } finally { Array.Clear(bytes); }
                frame.Dispose();
                if (frame.Image.Any(b => b != 0)) throw new Exception("Frame was not cleared.");
            }
        } catch (Exception e) { results.Add(new { error = e.Message }); Console.Error.WriteLine(e); exit = 1; }
        finally {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            await File.WriteAllTextAsync(output, JsonSerializer.Serialize(new { at = DateTimeOffset.UtcNow, fixtureOnly = true, results }, new JsonSerializerOptions { WriteIndented = true }));
            if (Path.GetDirectoryName(Path.GetFullPath(root)) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar) && Path.GetFileName(root).StartsWith("Buddy.Vision.Tests.", StringComparison.Ordinal)) Directory.Delete(root, true);
        }
        return exit;
    }
    private sealed class ModelTrace : System.Net.Http.DelegatingHandler
    {
        internal string? LastContent;
        internal ModelTrace() : base(new System.Net.Http.HttpClientHandler()) { }
        protected override async Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken ct)
        {
            var response = await base.SendAsync(request, ct);
            if (request.RequestUri?.AbsolutePath == "/api/chat") {
                var bytes = await response.Content.ReadAsByteArrayAsync(ct);
                using var json = JsonDocument.Parse(bytes);
                if (json.RootElement.TryGetProperty("message", out var message) && message.TryGetProperty("content", out var content)) LastContent = content.GetString();
            }
            return response;
        }
    }
}
