using Snapper.Core.Geometry;

namespace Snapper.Core.Annotate.Model;

/// <summary>Pure geometry in image pixels, with a top-left origin and y increasing downward.</summary>
public static class ShapeGeometry
{
    public static RectD NormalizedRect(PointD a, PointD b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));

    public static PointD SquareCorner(PointD anchor, PointD toward)
    {
        var dx = toward.X - anchor.X;
        var dy = toward.Y - anchor.Y;
        var side = Math.Max(Math.Abs(dx), Math.Abs(dy));
        return new(anchor.X + (dx < 0 ? -side : side), anchor.Y + (dy < 0 ? -side : side));
    }

    public static PointD SnappedEndpoint(PointD fixedPoint, PointD moving)
    {
        var dx = moving.X - fixedPoint.X;
        var dy = moving.Y - fixedPoint.Y;
        var length = double.Hypot(dx, dy);
        if (length == 0) return fixedPoint;
        const double step = Math.PI / 4;
        var angle = Math.Round(Math.Atan2(dy, dx) / step, MidpointRounding.AwayFromZero) * step;
        return new(fixedPoint.X + length * Math.Cos(angle), fixedPoint.Y + length * Math.Sin(angle));
    }

    public static double DistanceToSegment(PointD p, PointD a, PointD b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        var squaredLength = dx * dx + dy * dy;
        if (squaredLength == 0) return double.Hypot(p.X - a.X, p.Y - a.Y);
        var t = Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / squaredLength, 0, 1);
        return double.Hypot(p.X - a.X - t * dx, p.Y - a.Y - t * dy);
    }

    public static double DistanceToOutline(PointD p, RectD r)
    {
        r = r.Standardized();
        var dx = Math.Max(Math.Max(r.Left - p.X, 0), p.X - r.Right);
        var dy = Math.Max(Math.Max(r.Top - p.Y, 0), p.Y - r.Bottom);
        if (dx > 0 || dy > 0) return double.Hypot(dx, dy);
        return Math.Min(Math.Min(p.X - r.Left, r.Right - p.X), Math.Min(p.Y - r.Top, r.Bottom - p.Y));
    }

    public static bool TriangleContains(PointD p, PointD a, PointD b, PointD c)
    {
        static double Cross(PointD a, PointD b, PointD c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        // A collapsed head contains only its closed segments, not the entire plane.
        if (Cross(a, b, c) == 0)
            return DistanceToSegment(p, a, b) == 0 || DistanceToSegment(p, b, c) == 0 || DistanceToSegment(p, c, a) == 0;
        var x = Cross(a, b, p);
        var y = Cross(b, c, p);
        var z = Cross(c, a, p);
        return x >= 0 && y >= 0 && z >= 0 || x <= 0 && y <= 0 && z <= 0;
    }
}
