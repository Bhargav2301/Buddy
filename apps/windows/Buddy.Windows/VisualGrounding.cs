using Buddy.Server;
using System.Security.Cryptography;

namespace Buddy.Windows;

internal sealed class VisualGrounding(ScreenPerception perception, Func<BuddyService?> service)
{
    internal async Task<GroundedTarget?> Resolve(ScreenSnapshot snapshot, GuideStep step, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(step.Target) || service() is not { } host) return null;
        using var frame = await perception.Frame(snapshot, ct);
        if (frame is null) return null;
        var candidates = frame.Text.Where(t => t.Confidence >= .85 && t.Text.Equals(step.Target, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length != 1) return null;
        var candidate = candidates[0];
        bool boundary = await Task.Run(() => VisualControlBoundary.HasBoundary(frame, candidate, ct), ct);
        if (!step.Role.Equals("Text", StringComparison.OrdinalIgnoreCase) && !boundary) return null;
        var bytes = frame.ForVision();
        VisionConfirmation? verified;
        try {
            // Model evidence uses image-pixel coordinates; UI placement uses physical desktop coordinates.
            double scale = Math.Min(1, 1280d / Math.Max(frame.PixelWidth, frame.PixelHeight));
            var evidence = new VisionEvidence(candidate.Ref, candidate.Text, candidate.Confidence,
                (candidate.Bounds.X - frame.Bounds.X) * frame.PixelWidth / frame.Bounds.Width * scale, (candidate.Bounds.Y - frame.Bounds.Y) * frame.PixelHeight / frame.Bounds.Height * scale,
                candidate.Bounds.Width * frame.PixelWidth / frame.Bounds.Width * scale, candidate.Bounds.Height * frame.PixelHeight / frame.Bounds.Height * scale, boundary);
            verified = await host.ConfirmVisualTarget(new(step.Target, step.Role, snapshot.Context.App, snapshot.Context.Title, Convert.ToBase64String(bytes), [evidence]), ct);
        } finally { CryptographicOperations.ZeroMemory(bytes); }
        if (verified is null) return null;
        // A slow vision response is not permission to point at stale pixels.
        var current = await perception.Capture(snapshot.Window, ct);
        if (current.Context.App != snapshot.Context.App || current.Context.Title != snapshot.Context.Title) return null;
        using var fresh = await perception.Frame(current, ct);
        if (fresh is null || fresh.Bounds != frame.Bounds) return null;
        var matches = fresh.Text.Where(t => t.Confidence >= .85 && t.Text.Equals(candidate.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1 || matches[0].Bounds != candidate.Bounds) return null;
        if (!step.Role.Equals("Text", StringComparison.OrdinalIgnoreCase) && !await Task.Run(() => VisualControlBoundary.HasBoundary(fresh, matches[0], ct), ct)) return null;
        var b = candidate.Bounds;
        return new(new(candidate.Ref, candidate.Text, "Text", b.X, b.Y, b.Width, b.Height), "ocr+local-vision", Math.Min(candidate.Confidence, verified.Confidence), false);
    }
    internal async Task<bool> StillVisible(ScreenSnapshot snapshot, ScreenElement target, string role, CancellationToken ct)
    {
        using var fresh = await perception.Frame(snapshot, ct);
        if (fresh is null) return false;
        var matches = fresh.Text.Where(t => t.Confidence >= .85 && t.Text.Equals(target.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1 || matches[0].Bounds != new System.Windows.Rect(target.X, target.Y, target.Width, target.Height)) return false;
        return role.Equals("Text", StringComparison.OrdinalIgnoreCase) || await Task.Run(() => VisualControlBoundary.HasBoundary(fresh, matches[0], ct), ct);
    }
}
