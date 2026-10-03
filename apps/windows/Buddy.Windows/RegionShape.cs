namespace Buddy.Windows;

// Physical desktop pixels throughout; WPF/DPI conversion happens only at the input/render boundary.
internal sealed class RegionShape
{
    internal IReadOnlyList<PixelPosition> Points { get; }
    internal PixelBounds Bounds { get; }
    internal RegionShape(IEnumerable<PixelPosition> points, PixelBounds allowed)
    {
        var values = points.ToArray();
        if (values.Length is < 3 or > 512 || values.Any(p => !double.IsFinite(p.X) || !double.IsFinite(p.Y) || !Inside(allowed, p)))
            throw new InvalidOperationException("Draw one bounded area inside the selected window and monitor.");
        double left = values.Min(p => p.X), top = values.Min(p => p.Y), right = values.Max(p => p.X), bottom = values.Max(p => p.Y);
        double area = Math.Abs(values.Select((p, i) => p.X * values[(i + 1) % values.Length].Y - values[(i + 1) % values.Length].X * p.Y).Sum()) / 2;
        if (right - left < 24 || bottom - top < 24 || area < 400 || (right-left)*(bottom-top) > 8_000_000)
            throw new InvalidOperationException("Draw a larger closed area, up to one window on one monitor.");
        Points = Array.AsReadOnly(values); Bounds = new(left, top, right-left, bottom-top);
    }
    internal static bool Inside(PixelBounds b, PixelPosition p) => p.X >= b.Left && p.Y >= b.Top && p.X <= b.Left+b.Width && p.Y <= b.Top+b.Height;
    internal bool Contains(PixelPosition point)
    {
        if (!Inside(Bounds, point)) return false;
        bool inside = false;
        for (int i=0,j=Points.Count-1; i<Points.Count; j=i++) {
            var a=Points[i]; var b=Points[j];
            if (OnSegment(a,b,point)) return true;
            if ((a.Y>point.Y)!=(b.Y>point.Y) && point.X < (b.X-a.X)*(point.Y-a.Y)/(b.Y-a.Y)+a.X) inside=!inside;
        }
        return inside;
    }
    internal bool Contains(PixelBounds box)
    {
        if (box.Width <= 0 || box.Height <= 0) return false;
        PixelPosition[] corners = [new(box.Left,box.Top),new(box.Left+box.Width,box.Top),new(box.Left+box.Width,box.Top+box.Height),new(box.Left,box.Top+box.Height)];
        if (corners.Any(p => !Contains(p))) return false;
        // Reject concave cuts/holes crossing a control even when its four corners happen to be inside.
        for (int i=0;i<Points.Count;i++) {
            var a=Points[i]; var b=Points[(i+1)%Points.Count];
            if (a.X>box.Left && a.X<box.Left+box.Width && a.Y>box.Top && a.Y<box.Top+box.Height) return false;
            for(int e=0;e<4;e++) if (Cross(a,b,corners[e])*Cross(a,b,corners[(e+1)%4]) < -0.0001 && Cross(corners[e],corners[(e+1)%4],a)*Cross(corners[e],corners[(e+1)%4],b) < -0.0001) return false;
        }
        return true;
    }
    private static double Cross(PixelPosition a, PixelPosition b, PixelPosition p) => (b.X-a.X)*(p.Y-a.Y)-(b.Y-a.Y)*(p.X-a.X);
    private static bool OnSegment(PixelPosition a,PixelPosition b,PixelPosition p) => Math.Abs(Cross(a,b,p)) < .0001 && p.X>=Math.Min(a.X,b.X) && p.X<=Math.Max(a.X,b.X) && p.Y>=Math.Min(a.Y,b.Y) && p.Y<=Math.Max(a.Y,b.Y);
    internal static PixelPosition FromLocal(PixelPosition local, PixelBounds surface, double scale) => new(surface.Left + local.X*scale, surface.Top+local.Y*scale);
    internal static PixelPosition ToLocal(PixelPosition physical, PixelBounds surface, double scale) => new((physical.X-surface.Left)/scale,(physical.Y-surface.Top)/scale);
}
