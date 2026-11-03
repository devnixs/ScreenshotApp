namespace Snapper.Core.Geometry;

/// <summary>Shared finite-coordinate math. Global values are physical pixels; DIPs are monitor-local.</summary>
public static class DisplayGeometry
{
    public const int BaseDpi = 96;
    private const double Epsilon = 1e-6;

    public static double PixelsToDips(double pixels, int dpi) => pixels * BaseDpi / dpi;
    public static double DipsToPixels(double dips, int dpi) => dips * dpi / BaseDpi;
    public static PointD ToLocalDips(PointD global, DisplayInfo d) =>
        new(PixelsToDips(global.X - d.Bounds.X, d.Dpi), PixelsToDips(global.Y - d.Bounds.Y, d.Dpi));
    public static PointD ToGlobalPixels(PointD localDips, DisplayInfo d) =>
        new(d.Bounds.X + DipsToPixels(localDips.X, d.Dpi), d.Bounds.Y + DipsToPixels(localDips.Y, d.Dpi));

    public static RectD ToLocalDips(RectD global, DisplayInfo d)
    {
        var r = global.Standardized();
        var first = ToLocalDips(new PointD(r.Left, r.Top), d);
        var last = ToLocalDips(new PointD(r.Right, r.Bottom), d);
        return RectD.FromEdges(first.X, first.Y, last.X, last.Y);
    }

    public static RectD ToGlobalPixels(RectD localDips, DisplayInfo d)
    {
        var r = localDips.Standardized();
        var first = ToGlobalPixels(new PointD(r.Left, r.Top), d);
        var last = ToGlobalPixels(new PointD(r.Right, r.Bottom), d);
        return RectD.FromEdges(first.X, first.Y, last.X, last.Y);
    }

    public static SizeD DipSize(RectD global, DisplayInfo d)
    {
        var r = global.Standardized();
        return new(PixelsToDips(r.Width, d.Dpi), PixelsToDips(r.Height, d.Dpi));
    }

    public static PixelRect RoundToGlobalPixels(RectD localDips, DisplayInfo d)
    {
        var r = localDips.Standardized();
        return PixelRect.FromEdges(d.Bounds.X + Round(DipsToPixels(r.Left, d.Dpi)),
            d.Bounds.Y + Round(DipsToPixels(r.Top, d.Dpi)),
            d.Bounds.X + Round(DipsToPixels(r.Right, d.Dpi)),
            d.Bounds.Y + Round(DipsToPixels(r.Bottom, d.Dpi)));
    }

    public static DisplayInfo? PrimaryDisplay(IReadOnlyList<DisplayInfo> displays) =>
        displays.FirstOrDefault(d => d.IsPrimary)
        ?? displays.FirstOrDefault(d => d.Bounds.X == 0 && d.Bounds.Y == 0)
        ?? displays.FirstOrDefault();

    public static DisplayInfo? DisplayContaining(PointD p, IReadOnlyList<DisplayInfo> displays) =>
        displays.FirstOrDefault(d => d.Bounds.Left <= p.X && p.X < d.Bounds.Right && d.Bounds.Top <= p.Y && p.Y < d.Bounds.Bottom)
        ?? displays.FirstOrDefault(d => d.Bounds.Left <= p.X && p.X <= d.Bounds.Right && d.Bounds.Top <= p.Y && p.Y <= d.Bounds.Bottom);

    public static DisplayInfo? NearestDisplay(PointD p, IReadOnlyList<DisplayInfo> displays)
    {
        if (DisplayContaining(p, displays) is { } containing) return containing;
        DisplayInfo? best = null;
        var bestDistance = double.PositiveInfinity;
        foreach (var d in displays)
        {
            var distance = SquaredDistance(p, d.Bounds);
            if (distance < bestDistance) { best = d; bestDistance = distance; }
        }
        return best;
    }

    public static double SquaredDistance(PointD p, PixelRect bounds)
    {
        var nearest = Clamp(p, bounds);
        var x = p.X - nearest.X;
        var y = p.Y - nearest.Y;
        return x * x + y * y;
    }

    public static DisplayInfo? DisplayOverlapping(RectD r, IReadOnlyList<DisplayInfo> displays)
    {
        r = r.Standardized();
        DisplayInfo? best = null;
        double bestArea = 0;
        foreach (var d in displays)
        {
            var width = Math.Max(0, Math.Min(r.Right, d.Bounds.Right) - Math.Max(r.Left, d.Bounds.Left));
            var height = Math.Max(0, Math.Min(r.Bottom, d.Bounds.Bottom) - Math.Max(r.Top, d.Bounds.Top));
            var area = width * height;
            if (area > bestArea) { best = d; bestArea = area; }
        }
        return best;
    }

    public static PointD Clamp(PointD p, PixelRect bounds) =>
        new(Math.Clamp(p.X, bounds.Left, bounds.Right), Math.Clamp(p.Y, bounds.Top, bounds.Bottom));

    public static RectD Clamp(RectD r, PixelRect bounds)
    {
        r = r.Standardized();
        var first = Clamp(new PointD(r.Left, r.Top), bounds);
        var last = Clamp(new PointD(r.Right, r.Bottom), bounds);
        return RectD.FromEdges(first.X, first.Y, last.X, last.Y);
    }

    public static RectD NormalizedRect(PointD a, PointD b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
    public static RectD DragRect(PointD start, PointD current, DisplayInfo d) =>
        NormalizedRect(Clamp(start, d.Bounds), Clamp(current, d.Bounds));

    public static PixelRect PixelAligned(RectD global, DisplayInfo d)
    {
        var r = global.Standardized();
        var (left, right) = AlignedEdges(r.Left - d.Bounds.X, r.Right - d.Bounds.X);
        var (top, bottom) = AlignedEdges(r.Top - d.Bounds.Y, r.Bottom - d.Bounds.Y);
        // Clamp before narrowing to int: even a far-offscreen rect collapses safely to an edge.
        return PixelRect.FromEdges(d.Bounds.X + (int)Math.Clamp(left, 0, d.Bounds.Width),
            d.Bounds.Y + (int)Math.Clamp(top, 0, d.Bounds.Height),
            d.Bounds.X + (int)Math.Clamp(right, 0, d.Bounds.Width),
            d.Bounds.Y + (int)Math.Clamp(bottom, 0, d.Bounds.Height));
    }

    public static PixelRect CaptureRect(RectD selection, DisplayInfo d) => PixelAligned(Clamp(selection, d.Bounds), d);
    public static PixelRect ToMonitorLocal(PixelRect global, DisplayInfo d) =>
        new(global.X - d.Bounds.X, global.Y - d.Bounds.Y, global.Width, global.Height);

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
    private static (double, double) AlignedEdges(double minimum, double maximum)
    {
        if (maximum - minimum <= Epsilon)
        {
            var edge = Math.Round(minimum, MidpointRounding.AwayFromZero);
            return (edge, edge);
        }
        return (Math.Floor(minimum + Epsilon), Math.Ceiling(maximum - Epsilon));
    }
}
