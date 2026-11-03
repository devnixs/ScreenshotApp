using Snapper.Core.Annotate.Model;
using Snapper.Core.Geometry;

namespace Snapper.Core.Tests;

public sealed class ShapeDragSessionTests
{
    private static RectD Bounds => new(0, 0, 100, 100);
    private static AnnotationShape Rectangle => new(new ShapeKind.Rectangle(new(20, 20, 40, 30)), RgbaColor.Red, StrokeWidth.M);

    [Fact] public void MovesClampAndNeverAccumulate()
    {
        var session = new ShapeDragSession(Rectangle, new ShapeDragKind.Move(), new(20, 30), Bounds);
        var point = new PointD(30, 25);
        for (var i = 0; i < 100; i++)
            Assert.Equal(new ShapeKind.Rectangle(new(30, 15, 40, 30)), session.Preview(point, false).Kind);
        Assert.Equal(session.Preview(point, false), session.Preview(point, true));
        Assert.Null(session.Finish(session.StartPoint, false));
        Assert.Equal(new ShapeKind.Rectangle(new(0, -10, 40, 30)), session.Preview(new(-50, -50), false).Kind);
        Assert.Equal(session.Preview(point, false), session.Finish(point, false));
    }

    [Fact] public void EachCornerAnchorsOppositeAndShiftIsReversible()
    {
        var rectangle = Rectangle;
        foreach (var (handle, start) in rectangle.Handles)
        {
            var anchor = rectangle.Handles.Single(h => h.Handle == handle.Opposite()).Position;
            var session = new ShapeDragSession(rectangle, new ShapeDragKind.Resize(handle), start, Bounds);
            var point = new PointD(10, 80);
            var free = session.Preview(point, false);
            Assert.Equal(rectangle.Resized(handle, point, false).Shape, free);
            for (var i = 0; i < 100; i++)
            {
                var square = session.Preview(point, true);
                var r = Assert.IsType<ShapeKind.Rectangle>(square.Kind).Rect;
                Assert.Equal(r.Width, r.Height);
                Assert.Contains(square.Handles, h => h.Position == anchor);
                Assert.Equal(free, session.Preview(point, false));
            }
            Assert.Null(session.Finish(start, false));
        }
    }

    [Fact] public void OffCenterHandleClickDoesNotResize()
    {
        var rectangle = Rectangle;
        var start = new PointD(18, 20);
        var session = new ShapeDragSession(rectangle, new ShapeDragKind.Resize(ShapeHandle.TopLeft), start, Bounds);
        Assert.Equal(rectangle, session.Preview(start, false)); Assert.Null(session.Finish(start, false));
        Assert.Equal(rectangle, session.Preview(start, true)); Assert.Null(session.Finish(start, true));
    }

    [Fact] public void ArrowMovementAndEndpointSnapping()
    {
        var arrow = new AnnotationShape(new ShapeKind.Arrow(new(20, 20), new(50, 50)), RgbaColor.Blue, StrokeWidth.S);
        var move = new ShapeDragSession(arrow, new ShapeDragKind.Move(), new(20, 20), Bounds);
        Assert.Equal(new ShapeKind.Arrow(new(30, 15), new(60, 45)), move.Preview(new(30, 15), false).Kind);
        foreach (var (handle, point) in arrow.Handles)
        {
            var session = new ShapeDragSession(arrow, new ShapeDragKind.Resize(handle), point, Bounds);
            var shape = session.Preview(new(90, 70), true);
            var kind = Assert.IsType<ShapeKind.Arrow>(shape.Kind);
            var angle = Math.Atan2(kind.End.Y - kind.Start.Y, kind.End.X - kind.Start.X) / (Math.PI / 4);
            Assert.InRange(Math.Abs(angle - Math.Round(angle)), 0, 1e-8);
            Assert.Equal(arrow.Handles.Single(h => h.Handle == handle.Opposite()).Position,
                shape.Handles.Single(h => h.Handle == handle.Opposite()).Position);
        }
    }

    [Fact] public void PreviewClampsToBottomRightEdgeInclusive()
    {
        var session = new ShapeDragSession(Rectangle, new ShapeDragKind.Move(), new(20, 30), Bounds);
        Assert.Equal(new ShapeKind.Rectangle(new(100, 90, 40, 30)), session.Preview(new(500, 500), false).Kind);
    }
}
