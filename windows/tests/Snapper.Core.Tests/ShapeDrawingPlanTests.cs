using Snapper.Core.Annotate;
using Snapper.Core.Annotate.Model;
using Snapper.Core.Geometry;
namespace Snapper.Core.Tests;
public sealed class ShapeDrawingPlanTests
{
    [Theory] [InlineData(StrokeWidth.S, 1, 2)] [InlineData(StrokeWidth.M, 2, 8)]
    public void RectanglePlan(StrokeWidth width, double ppd, double expected)
    {
        var shape = new AnnotationShape(new ShapeKind.Rectangle(new(10, 10, 50, 30)), RgbaColor.Red, width);
        Assert.Equal(new DrawPrimitive.StrokeRect(new(10, 10, 50, 30), expected, RgbaColor.Red), Assert.Single(ShapeDrawingPlan.For(shape, ppd)));
    }
    [Fact] public void ArrowUsesButtShaftEndingAtHeadBase()
    {
        var shape = new AnnotationShape(new ShapeKind.Arrow(new(10, 50), new(90, 50)), RgbaColor.Blue, StrokeWidth.M);
        var plan = ShapeDrawingPlan.For(shape, 1); Assert.Equal(2, plan.Count);
        Assert.Equal(new DrawPrimitive.StrokeLine(new(10, 50), new(70, 50), 4, RgbaColor.Blue), plan[0]);
        var head = Assert.IsType<DrawPrimitive.FillPolygon>(plan[1]); Assert.Equal(new PointD[] { new(90, 50), new(70, 58), new(70, 42) }, head.Points); Assert.Equal(RgbaColor.Blue, head.Color);
    }
    [Fact] public void ShortArrowHasNoZeroLengthShaft()
    {
        var shape = new AnnotationShape(new ShapeKind.Arrow(new(10, 50), new(15, 50)), RgbaColor.Blue, StrokeWidth.M);
        var head = Assert.IsType<DrawPrimitive.FillPolygon>(Assert.Single(ShapeDrawingPlan.For(shape, 1)));
        Assert.Equal(new PointD[] { new(15, 50), new(10, 52), new(10, 48) }, head.Points);
    }
    [Fact] public void ArrowAt150PercentUsesImagePixelMetrics()
    {
        var shape = new AnnotationShape(new ShapeKind.Arrow(new(10, 50), new(90, 50)), RgbaColor.Blue, StrokeWidth.M);
        var shaft = Assert.IsType<DrawPrimitive.StrokeLine>(ShapeDrawingPlan.For(shape, 1.5)[0]); Assert.Equal(6, shaft.Thickness); Assert.Equal(new(60, 50), shaft.To);
    }
    [Fact] public void TextIsDeferredTo0042() => Assert.Empty(ShapeDrawingPlan.For(AnnotationShape.MakeText(new(10, 10), RgbaColor.Red, StrokeWidth.M), 1));
}
