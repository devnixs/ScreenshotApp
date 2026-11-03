using Snapper.Core.Geometry;

namespace Snapper.Core.Annotate.Model;

/// <summary>Single source for bounds, hit-testing and rendering: butt-capped shaft from
/// ShaftStart to BaseCenter, followed by the unstroked filled head Tip, BaseLeft, BaseRight.</summary>
public readonly record struct ArrowGeometry(PointD Tip, PointD BaseCenter, PointD BaseLeft,
    PointD BaseRight, PointD ShaftStart, double StrokePixels)
{
    public static ArrowGeometry Make(PointD start, PointD end, StrokeWidth width, double pixelsPerDip)
    {
        var stroke = width.Points() * pixelsPerDip;
        var length = double.Hypot(end.X - start.X, end.Y - start.Y);
        if (length == 0) return new(start, start, start, start, start, stroke);
        var ux = (end.X - start.X) / length;
        var uy = (end.Y - start.Y) / length;
        var headLength = Math.Min(length, 3 * stroke + 8 * pixelsPerDip);
        var halfWidth = 0.4 * headLength;
        var b = new PointD(end.X - ux * headLength, end.Y - uy * headLength);
        return new(end, b, new(b.X - uy * halfWidth, b.Y + ux * halfWidth),
            new(b.X + uy * halfWidth, b.Y - ux * halfWidth), start, stroke);
    }

    public RectD Bounds
    {
        get
        {
            var dx = Tip.X - ShaftStart.X;
            var dy = Tip.Y - ShaftStart.Y;
            var length = double.Hypot(dx, dy);
            var nx = length > 0 ? -dy / length * StrokePixels / 2 : 0;
            var ny = length > 0 ? dx / length * StrokePixels / 2 : 0;
            var points = new List<PointD> { Tip, BaseLeft, BaseRight };
            if (ShaftStart != BaseCenter)
                foreach (var p in new[] { ShaftStart, BaseCenter })
                {
                    points.Add(new(p.X + nx, p.Y + ny));
                    points.Add(new(p.X - nx, p.Y - ny));
                }
            return RectD.FromEdges(points.Min(p => p.X), points.Min(p => p.Y), points.Max(p => p.X), points.Max(p => p.Y));
        }
    }
}
