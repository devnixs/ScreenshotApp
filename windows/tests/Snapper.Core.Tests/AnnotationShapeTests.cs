using Snapper.Core.Annotate.Model;
using Snapper.Core.Geometry;
using Snapper.Core.Tests.Fakes;

namespace Snapper.Core.Tests;

public sealed class AnnotationShapeTests
{
    private static AnnotationShape Shape(ShapeKind kind) => new(kind, RgbaColor.Red, StrokeWidth.M);
    private static AnnotationShape Rectangle => Shape(new ShapeKind.Rectangle(new(100, 100, 200, 100)));
    private static AnnotationShape Arrow => Shape(new ShapeKind.Arrow(new(0, 0), new(100, 0)));
    private static AnnotationShape Text => Shape(new ShapeKind.Text(new(new(10, 20), "Hello", new(50, 30))));
    private static void Near(double actual, double expected) => Assert.InRange(Math.Abs(actual - expected), 0, 1e-9);
    private static void Near(PointD actual, PointD expected) { Near(actual.X, expected.X); Near(actual.Y, expected.Y); }
    private static void Metadata(AnnotationShape actual, AnnotationShape expected)
    { Assert.Equal(expected.Id, actual.Id); Assert.Equal(expected.Color, actual.Color); Assert.Equal(expected.StrokeWidth, actual.StrokeWidth); }

    [Theory]
    [InlineData(StrokeWidth.S, 2, 16, 1)] [InlineData(StrokeWidth.S, 2, 16, 1.5)] [InlineData(StrokeWidth.S, 2, 16, 2)]
    [InlineData(StrokeWidth.M, 4, 24, 1)] [InlineData(StrokeWidth.M, 4, 24, 1.5)] [InlineData(StrokeWidth.M, 4, 24, 2)]
    [InlineData(StrokeWidth.L, 8, 36, 1)] [InlineData(StrokeWidth.L, 8, 36, 1.5)] [InlineData(StrokeWidth.L, 8, 36, 2)]
    public void UnitsAndRecommendedTolerances(StrokeWidth width, double stroke, double font, double scale)
    {
        var shape = Rectangle.WithStrokeWidth(width);
        Assert.Equal(stroke * scale, shape.StrokePixels(scale)); Assert.Equal(font * scale, shape.FontPixelSize(scale));
        Assert.Equal(4, AnnotationShape.HitSlopViewDips); Assert.Equal(6, AnnotationShape.HandleRadiusViewDips);
        Assert.Equal(3, AnnotationShape.MinimumDragViewDips);
    }

    [Fact] public void NormalizationAndKindMutation()
    {
        var expected = new RectD(4, 5, 6, 15);
        Assert.Equal(expected, ShapeGeometry.NormalizedRect(new(10, 20), new(4, 5)));
        var kind = new ShapeKind.Rectangle(new(10, 20, -6, -15));
        Assert.Equal(expected, kind.Rect);
        var shape = Shape(kind);
        Assert.Equal(new ShapeKind.Rectangle(expected), shape.Kind);
        Assert.Equal(new ShapeKind.Rectangle(expected), (shape with { Kind = new ShapeKind.Rectangle(new(10, 20, -6, -15)) }).Kind);
        Assert.Equal(expected, (kind with { Rect = new(10, 20, -6, -15) }).Rect);
    }

    [Theory]
    [InlineData(130, 110, 100, 100, 30)] [InlineData(70, 110, 70, 100, 30)]
    [InlineData(130, 90, 100, 70, 30)] [InlineData(70, 90, 70, 70, 30)]
    [InlineData(110, 140, 100, 100, 40)] [InlineData(100, 130, 100, 100, 30)] [InlineData(130, 100, 100, 100, 30)]
    public void SquareConstraintsEveryQuadrantAndAxis(double x, double y, double left, double top, double side)
    {
        var expected = new RectD(left, top, side, side);
        Assert.Equal(expected, AnnotationShape.RectangleFromDrag(new(100, 100), new(x, y), true));
        var original = Shape(new ShapeKind.Rectangle(new(100, 100, 100, 100)));
        Assert.Equal(new ShapeKind.Rectangle(expected), original.Resized(ShapeHandle.BottomRight, new(x, y), true).Shape.Kind);
    }

    [Theory]
    [InlineData(0, 0)] [InlineData(22, 0)] [InlineData(23, 45)] [InlineData(44, 45)] [InlineData(46, 45)]
    [InlineData(90, 90)] [InlineData(180, 180)] [InlineData(-135, -135)] [InlineData(-22, 0)] [InlineData(-23, -45)] [InlineData(40, 45)]
    public void AngleSnappingPreservesLength(double angle, double snapped)
    {
        foreach (var anchor in new PointD[] { new(0, 0), new(50, 50) })
        {
            var radians = angle * Math.PI / 180; var expected = snapped * Math.PI / 180;
            var actual = ShapeGeometry.SnappedEndpoint(anchor, new(anchor.X + 100 * Math.Cos(radians), anchor.Y + 100 * Math.Sin(radians)));
            Near(actual, new(anchor.X + 100 * Math.Cos(expected), anchor.Y + 100 * Math.Sin(expected)));
            Near(double.Hypot(actual.X - anchor.X, actual.Y - anchor.Y), 100);
            Assert.Equal(anchor, ShapeGeometry.SnappedEndpoint(anchor, anchor));
        }
    }

    [Fact] public void SegmentAndOutlineDistances()
    {
        foreach (var (p, distance) in new (PointD, double)[] { (new(50, 6), 6), (new(-6, 0), 6), (new(-3, 4), 5), (new(103, 4), 5) })
            Near(ShapeGeometry.DistanceToSegment(p, new(0, 0), new(100, 0)), distance);
        Near(ShapeGeometry.DistanceToSegment(new(8, 9), new(5, 5), new(5, 5)), 5);
        var rect = new RectD(10, 20, 20, 30);
        Near(ShapeGeometry.DistanceToOutline(new(20, 30), rect), 10);
        Near(ShapeGeometry.DistanceToOutline(new(7, 16), rect), 5);
        Near(ShapeGeometry.DistanceToOutline(new(10, 40), rect), 0);
    }

    [Fact] public void TriangleEdgesWindingAndDegeneracy()
    {
        foreach (var p in new PointD[] { new(0, 0), new(5, 0), new(2, 2), new(5, 5) })
        {
            Assert.True(ShapeGeometry.TriangleContains(p, new(0, 0), new(10, 0), new(0, 10)));
            Assert.True(ShapeGeometry.TriangleContains(p, new(0, 0), new(0, 10), new(10, 0)));
        }
        Assert.False(ShapeGeometry.TriangleContains(new(8, 8), new(0, 0), new(10, 0), new(0, 10)));
        Assert.False(ShapeGeometry.TriangleContains(new(50, 50), new(0, 0), new(0, 0), new(0, 0)));
        Assert.True(ShapeGeometry.TriangleContains(new(0, 0), new(0, 0), new(0, 0), new(0, 0)));
        Assert.False(ShapeGeometry.TriangleContains(new(20, 0), new(0, 0), new(5, 0), new(10, 0)));
    }

    [Theory] [InlineData(1)] [InlineData(1.5)] [InlineData(2)]
    public void ArrowheadSizesScalesAndDirections(double scale)
    {
        foreach (var (width, length) in new[] { (StrokeWidth.S, 14), (StrokeWidth.M, 20), (StrokeWidth.L, 32) })
        {
            var g = Assert.IsType<ArrowGeometry>(Arrow.WithStrokeWidth(width).GetArrowGeometry(scale));
            Assert.Equal(new(100, 0), g.Tip); Assert.Equal(new(0, 0), g.ShaftStart);
            Near(g.BaseCenter, new(100 - length * scale, 0));
            Near(g.BaseLeft, new(100 - length * scale, 0.4 * length * scale));
            Near(g.BaseRight, new(100 - length * scale, -0.4 * length * scale)); Near(g.StrokePixels, width.Points() * scale);
        }
        var shortArrow = ArrowGeometry.Make(new(0, 0), new(10, 0), StrokeWidth.M, 1);
        Assert.Equal(new(0, 0), shortArrow.BaseCenter); Assert.Equal(new(0, 4), shortArrow.BaseLeft); Assert.Equal(new(0, -4), shortArrow.BaseRight);
        Near(ArrowGeometry.Make(new(0, 0), new(30, 40), StrokeWidth.M, 1).BaseCenter, new(18, 24));
        Near(ArrowGeometry.Make(new(100, 0), new(0, 0), StrokeWidth.M, 1).BaseCenter, new(20, 0));
        Assert.Null(Rectangle.GetArrowGeometry(scale)); Assert.Null(Text.GetArrowGeometry(scale));
    }

    [Theory] [InlineData(1)] [InlineData(1.5)] [InlineData(2)]
    public void PaintedBoundsIncludeStrokeAndHead(double scale)
    {
        Assert.Equal(new(100 - 2 * scale, 100 - 2 * scale, 200 + 4 * scale, 100 + 4 * scale), Rectangle.Bounds(scale));
        Assert.Equal(new(0, -8 * scale, 100, 16 * scale), Arrow.Bounds(scale));
        Assert.Equal(new(10, 20, 50, 30), Text.Bounds(scale));
        var diagonal = Shape(new ShapeKind.Arrow(new(0, 0), new(30, 40))).Bounds(1);
        Near(diagonal.Left, -1.6); Near(diagonal.Top, -1.2); Near(diagonal.Right, 30); Near(diagonal.Bottom, 40);
        Near(Shape(new ShapeKind.Arrow(new(0, 0), new(1, 0))).Bounds(1).Top, -0.4);
    }

    [Fact] public void RectangleOutlineHitsAtEveryScale()
    {
        foreach (var (p, hit) in new (PointD, bool)[] { (new(150, 106), true), (new(150, 107), false), (new(94, 150), true),
            (new(93, 150), false), (new(150, 94), true), (new(200, 150), false) }) Assert.Equal(hit, Rectangle.HitTest(p, 1, 4));
        Assert.True(Rectangle.HitTest(new(150, 112), 2, 8)); Assert.False(Rectangle.HitTest(new(150, 113), 2, 8));
        Assert.True(Rectangle.HitTest(new(150, 109), 1.5, 6)); Assert.False(Rectangle.HitTest(new(150, 110), 1.5, 6));
        Assert.True(Rectangle.HitTest(new(91, 150), 1.5, 6)); Assert.False(Rectangle.HitTest(new(90, 150), 1.5, 6));
    }

    [Fact] public void ArrowStrokeAndFilledHeadHits()
    {
        foreach (var (p, hit) in new (PointD, bool)[] { (new(50, 6), true), (new(50, 7), false), (new(-6, 0), true),
            (new(-7, 0), false), (new(-5, -4), false), (new(106, 0), true), (new(82, 7), true), (new(85, 7), false) })
            Assert.Equal(hit, Arrow.HitTest(p, 1, 4));
        Assert.True(Arrow.HitTest(new(50, 12), 2, 8)); Assert.False(Arrow.HitTest(new(50, 13), 2, 8));
        Assert.True(Arrow.HitTest(new(50, 9), 1.5, 6)); Assert.False(Arrow.HitTest(new(50, 10), 1.5, 6));
    }

    [Fact] public void AuthoritativeTextBoundsAndInclusiveHits()
    {
        foreach (var (p, hit) in new (PointD, bool)[] { (new(10, 20), true), (new(60, 50), true), (new(35, 35), true),
            (new(9, 35), false), (new(61, 35), false), (new(35, 51), false) }) Assert.Equal(hit, Text.HitTest(p, 2, 100));
        var empty = Text.WithMeasuredSize(new(0, 0));
        Assert.True(empty.HitTest(new(10, 20), 1, 100)); Assert.False(empty.HitTest(new(35, 35), 1, 100));
        Assert.Equal(new(10, 20, 0, 0), empty.Bounds(1));
    }

    [Fact] public void HandleOrderOppositesAndNearestHit()
    {
        Assert.Equal(new[] { ShapeHandle.TopLeft, ShapeHandle.TopRight, ShapeHandle.BottomLeft, ShapeHandle.BottomRight }, Rectangle.Handles.Select(h => h.Handle));
        Assert.Equal(new PointD[] { new(100, 100), new(300, 100), new(100, 200), new(300, 200) }, Rectangle.Handles.Select(h => h.Position));
        Assert.Equal(new[] { ShapeHandle.Start, ShapeHandle.End }, Arrow.Handles.Select(h => h.Handle));
        Assert.Equal(new PointD[] { new(0, 0), new(100, 0) }, Arrow.Handles.Select(h => h.Position)); Assert.Empty(Text.Handles);
        foreach (var handle in Enum.GetValues<ShapeHandle>()) Assert.Equal(handle, handle.Opposite().Opposite());
        Assert.Equal(ShapeHandle.BottomRight, ShapeHandle.TopLeft.Opposite()); Assert.Equal(ShapeHandle.BottomLeft, ShapeHandle.TopRight.Opposite());
        Assert.Equal(ShapeHandle.End, ShapeHandle.Start.Opposite());
        Assert.Equal(ShapeHandle.BottomRight, Rectangle.HandleHitTest(new(304, 203), 6));
        Assert.Equal(ShapeHandle.BottomRight, Rectangle.HandleHitTest(new(306, 200), 6)); Assert.Null(Rectangle.HandleHitTest(new(310, 200), 6));
        var tiny = Shape(new ShapeKind.Rectangle(new(0, 0, 4, 4)));
        Assert.Equal(ShapeHandle.BottomRight, tiny.HandleHitTest(new(3, 3), 6)); Assert.Equal(ShapeHandle.TopLeft, tiny.HandleHitTest(new(2, 2), 6));
        Assert.Null(Text.HandleHitTest(new(10, 20), 100));
    }

    [Fact] public void MovementPreservesMetadataAndDoesNotClamp()
    {
        foreach (var original in new[] { Rectangle, Arrow, Text })
        {
            var moved = original.Moved(10, -5); Metadata(moved, original);
            var bounds = original.Bounds(1); Assert.Equal(bounds with { X = bounds.X + 10, Y = bounds.Y - 5 }, moved.Bounds(1));
            Assert.Equal(original, moved.Moved(-10, 5));
        }
        Assert.Equal(new ShapeKind.Arrow(new(-100, -100), new(0, -100)), Arrow.Moved(-100, -100).Kind);
    }

    [Fact] public void RectangleResizeFlipAndContinuedAnchor()
    {
        var original = Shape(new ShapeKind.Rectangle(new(100, 100, 100, 100)));
        var cases = new (ShapeHandle Handle, PointD Point, RectD Rect, ShapeHandle Next)[] {
            (ShapeHandle.TopRight, new(250, 50), new(100, 50, 150, 150), ShapeHandle.TopRight),
            (ShapeHandle.BottomRight, new(50, 60), new(50, 60, 50, 40), ShapeHandle.TopLeft),
            (ShapeHandle.BottomRight, new(50, 300), new(50, 100, 50, 200), ShapeHandle.BottomLeft),
            (ShapeHandle.BottomRight, new(100, 150), new(100, 100, 0, 50), ShapeHandle.BottomRight),
            (ShapeHandle.BottomRight, new(150, 50), new(100, 50, 50, 50), ShapeHandle.TopRight) };
        foreach (var item in cases)
        {
            var result = original.Resized(item.Handle, item.Point, false);
            Assert.Equal(new ShapeKind.Rectangle(item.Rect), result.Shape.Kind); Assert.Equal(item.Next, result.Handle); Metadata(result.Shape, original);
        }
        var step = original.Resized(ShapeHandle.BottomRight, new(50, 60), false);
        step = step.Shape.Resized(step.Handle, new(150, 250), false);
        Assert.Equal(new ShapeKind.Rectangle(new(100, 100, 50, 150)), step.Shape.Kind); Assert.Equal(ShapeHandle.BottomRight, step.Handle);
        step = step.Shape.Resized(step.Handle, new(100, 100), false);
        step = step.Shape.Resized(step.Handle, new(40, 80), true);
        Assert.Equal(new ShapeKind.Rectangle(new(40, 40, 60, 60)), step.Shape.Kind); Assert.Equal(ShapeHandle.TopLeft, step.Handle);
        Assert.Equal(new ShapeKind.Rectangle(new(100, 100, 160, 160)), original.Resized(ShapeHandle.BottomRight, new(260, 180), true).Shape.Kind);
    }

    [Fact] public void ArrowEndpointResizeAndInvalidHandles()
    {
        var arrow = Arrow; var text = Text;
        var result = arrow.Resized(ShapeHandle.End, new(50, 60), false);
        Assert.Equal(new ShapeKind.Arrow(new(0, 0), new(50, 60)), result.Shape.Kind); Assert.Equal(ShapeHandle.End, result.Handle); Metadata(result.Shape, arrow);
        result = arrow.Resized(ShapeHandle.Start, new(30, 65), true);
        var kind = Assert.IsType<ShapeKind.Arrow>(result.Shape.Kind); var c = double.Hypot(70, 65) / Math.Sqrt(2);
        Near(kind.Start, new(100 - c, c)); Assert.Equal(new(100, 0), kind.End); Assert.Equal(ShapeHandle.Start, result.Handle); Metadata(result.Shape, arrow);
        foreach (var (shape, handle) in new[] { (arrow, ShapeHandle.TopLeft), (Rectangle, ShapeHandle.Start), (Rectangle, (ShapeHandle)999), (arrow, (ShapeHandle)999) })
            Assert.Equal((shape, handle), shape.Resized(handle, new(0, 0), true));
        foreach (var handle in Enum.GetValues<ShapeHandle>()) Assert.Equal((text, handle), text.Resized(handle, new(0, 0), false));
    }

    [Fact] public void CreationRejectsSmallDragsAfterConstraints()
    {
        static AnnotationShape? Rect(PointD p, bool constrained = false, double minimum = 3) => AnnotationShape.MakeRectangle(new(10, 10), p, constrained, RgbaColor.Red, StrokeWidth.M, minimum);
        Assert.Null(Rect(new(10, 10))); Assert.Null(Rect(new(12, 40))); Assert.Null(Rect(new(40, 12)));
        Assert.Equal(new ShapeKind.Rectangle(new(10, 10, 30, 30)), Rect(new(40, 40))?.Kind);
        Assert.Equal(new ShapeKind.Rectangle(new(10, 10, 30, 30)), Rect(new(40, 12), true)?.Kind);
        Assert.NotNull(Rect(new(13, 13))); Assert.Null(Rect(new(15, 15), minimum: 6));
        static AnnotationShape? Make(PointD p, bool constrained = false) => AnnotationShape.MakeArrow(new(0, 0), p, constrained, RgbaColor.Red, StrokeWidth.M, 3);
        Assert.Null(Make(new(0, 0))); Assert.Null(Make(new(2, 0))); Assert.NotNull(Make(new(3, 0)));
        Assert.Equal(new ShapeKind.Arrow(new(0, 0), new(30, 40)), Make(new(30, 40))?.Kind);
        var constrained = Assert.IsType<ShapeKind.Arrow>(Assert.IsType<AnnotationShape>(Make(new(100, 10), true)).Kind);
        Assert.Equal(new(0, 0), constrained.Start); Near(constrained.End, new(double.Hypot(100, 10), 0));
        var text = AnnotationShape.MakeText(new(10, 20), RgbaColor.Blue, StrokeWidth.L);
        Assert.Equal(new ShapeKind.Text(new(new(10, 20), "", new(0, 0))), text.Kind); Assert.Equal(RgbaColor.Blue, text.Color); Assert.Equal(StrokeWidth.L, text.StrokeWidth);
        Assert.Null(AnnotationShape.MakeRectangle(new(0, 0), new(0, 0), false, RgbaColor.Red, StrokeWidth.M, 0));
        Assert.Null(AnnotationShape.MakeArrow(new(0, 0), new(0, 0), false, RgbaColor.Red, StrokeWidth.M, 0));
    }

    [Fact] public void CopyMutatorsAndStableIdentity()
    {
        var text = Text;
        foreach (var original in new[] { Rectangle, Arrow, text })
        {
            var colored = original.WithColor(RgbaColor.Blue);
            Assert.Equal(original.Id, colored.Id); Assert.Equal(original.Kind, colored.Kind); Assert.Equal(RgbaColor.Blue, colored.Color); Assert.Equal(original.StrokeWidth, colored.StrokeWidth);
            var widened = original.WithStrokeWidth(StrokeWidth.L);
            Assert.Equal(original.Id, widened.Id); Assert.Equal(original.Kind, widened.Kind); Assert.Equal(original.Color, widened.Color); Assert.Equal(StrokeWidth.L, widened.StrokeWidth);
        }
        foreach (var shape in new[] { Rectangle, Arrow }) { Assert.Equal(shape, shape.WithText("ignored")); Assert.Equal(shape, shape.WithMeasuredSize(new(0, 0))); }
        var edited = text.WithText("Updated\ntext").WithMeasuredSize(new(90, 80)); Metadata(edited, text);
        Assert.Equal(new ShapeKind.Text(new(new(10, 20), "Updated\ntext", new(90, 80))), edited.Kind);
        var first = Rectangle; var second = Rectangle;
        Assert.NotEqual(first.Id, second.Id);
    }

    [Theory] [InlineData(1)] [InlineData(1.5)] [InlineData(2)]
    public void ZeroSizeGeometryRemainsFiniteAndLocal(double scale)
    {
        var zero = Shape(new ShapeKind.Arrow(new(-5, 7), new(-5, 7)));
        var g = Assert.IsType<ArrowGeometry>(zero.GetArrowGeometry(scale));
        foreach (var p in new[] { g.Tip, g.BaseCenter, g.BaseLeft, g.BaseRight, g.ShaftStart })
        { Assert.Equal(new(-5, 7), p); Assert.True(double.IsFinite(p.X) && double.IsFinite(p.Y)); }
        Assert.Equal(new(-5, 7, 0, 0), zero.Bounds(scale)); Assert.True(zero.HitTest(new(-5, 7), scale, 0));
        Assert.False(zero.HitTest(new(500, 500), scale, 4)); Assert.Equal(2, zero.Handles.Count);
        var rect = Shape(new ShapeKind.Rectangle(new(-5, 7, 0, 0)));
        Assert.Equal(4 * scale, rect.Bounds(scale).Width); Assert.True(rect.HitTest(new(-5, 7), scale, 0)); Assert.Equal(4, rect.Handles.Count);
    }

    [Theory] [InlineData(2, 160, 32)] [InlineData(1.5, 120, 24)]
    public void WithStrokeWidthRemeasuresTextThroughInjectedMeasurer(double scale, double width, double height)
    {
        var text = Text;
        var small = text.WithStrokeWidth(StrokeWidth.S, scale, new FakeTextMeasurer());
        Assert.Equal(StrokeWidth.S, small.StrokeWidth); Assert.Equal(text.Id, small.Id); Assert.Equal(text.Color, small.Color);
        Assert.Equal(new(width, height), Assert.IsType<ShapeKind.Text>(small.Kind).Box.MeasuredSize);
        var counter = new CountingMeasurer(); var rectangle = Rectangle;
        Assert.Equal(rectangle.WithStrokeWidth(StrokeWidth.L), rectangle.WithStrokeWidth(StrokeWidth.L, scale, counter)); Assert.Equal(0, counter.Calls);
    }
    private sealed class CountingMeasurer : ITextMeasurer
    {
        public int Calls { get; private set; }
        public SizeD LayoutSize(string text, double fontPixelSize) { Calls++; return new(0, 0); }
        public double LineHeight(double fontPixelSize) { Calls++; return 0; }
    }
}
