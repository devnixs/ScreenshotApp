using Snapper.Core.Annotate;
using Snapper.Core.Annotate.Model;
using Snapper.Core.Geometry;
namespace Snapper.Core.Tests;

public sealed class CanvasHitTesterTests
{
    private static CanvasTransform Transform(double scale) => new(new(400, 400), 1 / scale, new(400 * scale, 400 * scale), 1);
    [Theory] [InlineData(.25)] [InlineData(.5)] [InlineData(1)] [InlineData(2)]
    public void DistancesScaleInViewDips(double scale) => Assert.Equal(6 / scale, CanvasHitTester.ImagePixels(6, Transform(scale)));
    [Theory] [InlineData(1, 6, 4)] [InlineData(1.5, 9, 6)] [InlineData(2, 12, 8)]
    public void NativeMixedDpiRadii(double pixelsPerDip, double handle, double slop)
    {
        var t = Transform(1 / pixelsPerDip);
        Assert.Equal(handle, CanvasHitTester.ImagePixels(6, t), 9); Assert.Equal(slop, CanvasHitTester.ImagePixels(4, t), 9);
    }
    [Theory] [InlineData(.25)] [InlineData(.5)] [InlineData(1)] [InlineData(2)]
    public void SelectedHandlesWinOverTopmostAndKeepConstantRadius(double scale)
    {
        var d = new AnnotationDocument(new(400, 400), 1);
        var lower = new AnnotationShape(new ShapeKind.Rectangle(new(100, 100, 150, 150)), RgbaColor.Red, StrokeWidth.M);
        var upper = new AnnotationShape(new ShapeKind.Rectangle(new(100, 100, 170, 170)), RgbaColor.Green, StrokeWidth.M);
        d.Add(lower); d.Add(upper);
        Assert.Equal(new CanvasHit.Shape(upper.Id), CanvasHitTester.HitTest(new(100, 100), d, Transform(scale)));
        d.Select(lower.Id);
        Assert.Equal(new CanvasHit.Handle(lower.Id, ShapeHandle.TopLeft), CanvasHitTester.HitTest(new(100 - 5 / scale, 100), d, Transform(scale)));
        Assert.IsNotType<CanvasHit.Handle>(CanvasHitTester.HitTest(new(100 - 7 / scale, 100), d, Transform(scale)));
        Assert.IsType<CanvasHit.None>(CanvasHitTester.HitTest(new(180, 180), d, Transform(scale)));
    }
    [Fact] public void UnselectedHandlesAreNotHitAndImageEdgeUsesUnclampedPoint()
    {
        var d = new AnnotationDocument(new(400, 400), 1);
        var shape = new AnnotationShape(new ShapeKind.Rectangle(new(0, 20, 200, 200)), RgbaColor.Red, StrokeWidth.S);
        d.Add(shape);
        Assert.IsType<CanvasHit.None>(CanvasHitTester.HitTest(new(-5.5, 20), d, Transform(1)));
        d.Select(shape.Id);
        Assert.Equal(new CanvasHit.Handle(shape.Id, ShapeHandle.TopLeft), CanvasHitTester.HitTest(new(-5.5, 20), d, Transform(1)));
    }
    [Theory] [InlineData(1, 6, 7)] [InlineData(.5, 10, 11)]
    public void RectanglesHitOnlyOutlineAtExpectedTolerance(double scale, double hit, double miss)
    {
        var d = new AnnotationDocument(new(400, 400), 1);
        var shape = new AnnotationShape(new ShapeKind.Rectangle(new(100, 100, 200, 200)), RgbaColor.Red, StrokeWidth.M);
        d.Add(shape); var t = Transform(scale);
        Assert.IsType<CanvasHit.None>(CanvasHitTester.HitTest(new(200, 200), d, t));
        foreach (var sign in new[] { -1, 1 })
        {
            Assert.Equal(new CanvasHit.Shape(shape.Id), CanvasHitTester.HitTest(new(100 + sign * hit, 200), d, t));
            Assert.IsType<CanvasHit.None>(CanvasHitTester.HitTest(new(100 + sign * miss, 200), d, t));
        }
    }
    [Fact] public void ArrowShaftAndFilledHeadAreHit()
    {
        var d = new AnnotationDocument(new(400, 400), 1);
        var shape = new AnnotationShape(new ShapeKind.Arrow(new(50, 100), new(250, 100)), RgbaColor.Red, StrokeWidth.L);
        d.Add(shape); var t = Transform(1); var head = Assert.IsType<ArrowGeometry>(shape.GetArrowGeometry(1));
        Assert.Equal(new CanvasHit.Shape(shape.Id), CanvasHitTester.HitTest(new(100, 100), d, t));
        Assert.Equal(new CanvasHit.Shape(shape.Id), CanvasHitTester.HitTest(new((head.Tip.X + head.BaseLeft.X) / 2, (head.Tip.Y + head.BaseLeft.Y) / 2), d, t));
        Assert.IsType<CanvasHit.None>(CanvasHitTester.HitTest(new(20, 20), d, t));
    }
    [Fact] public void EmptyViewNeverHits()
    {
        var d = new AnnotationDocument(new(400, 400), 1);
        var shape = new AnnotationShape(new ShapeKind.Rectangle(new(0, 0, 200, 200)), RgbaColor.Red, StrokeWidth.M);
        d.Add(shape); d.Select(shape.Id);
        var t = new CanvasTransform(new(400, 400), 1, new(0, 0), 1);
        Assert.Equal(0, CanvasHitTester.ImagePixels(6, t));
        Assert.IsType<CanvasHit.None>(CanvasHitTester.HitTest(new(0, 0), d, t));
    }
}
