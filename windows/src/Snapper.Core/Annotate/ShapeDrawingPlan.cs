using System.Collections.Immutable;
using Snapper.Core.Annotate.Model;
using Snapper.Core.Geometry;
namespace Snapper.Core.Annotate;
public abstract record DrawPrimitive
{
    public sealed record StrokeRect(RectD Rect, double Thickness, RgbaColor Color) : DrawPrimitive;
    public sealed record StrokeLine(PointD From, PointD To, double Thickness, RgbaColor Color) : DrawPrimitive;
    public sealed record FillPolygon(ImmutableArray<PointD> Points, RgbaColor Color) : DrawPrimitive;
}
public static class ShapeDrawingPlan
{
    public static IReadOnlyList<DrawPrimitive> For(AnnotationShape shape, double pixelsPerDip)
    {
        if (shape.Kind is ShapeKind.Rectangle(var r)) return [new DrawPrimitive.StrokeRect(r, shape.StrokePixels(pixelsPerDip), shape.Color)];
        if (shape.GetArrowGeometry(pixelsPerDip) is { } arrow)
        {
            var list = new List<DrawPrimitive>();
            if (arrow.ShaftStart != arrow.BaseCenter) list.Add(new DrawPrimitive.StrokeLine(arrow.ShaftStart, arrow.BaseCenter, shape.StrokePixels(pixelsPerDip), shape.Color));
            list.Add(new DrawPrimitive.FillPolygon([arrow.Tip, arrow.BaseLeft, arrow.BaseRight], shape.Color));
            return list.AsReadOnly();
        }
        return [];
    }
}
