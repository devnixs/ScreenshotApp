using Snapper.Core.Annotate;
using Snapper.Core.Annotate.Model;
using Snapper.Core.Geometry;
namespace Snapper.Core.Tests;
public sealed class AnnotationDrawingGestureTests
{
    private static CanvasTransform Transform => new(new(1600, 900), 2, new(400, 400), 2);
    private static AnnotationDrawingGesture Gesture(AnnotationTool tool = AnnotationTool.Rectangle, PointD? start = null) =>
        AnnotationDrawingGesture.Begin(tool, Transform.ViewPoint(start ?? new(100, 100)), Transform, RgbaColor.Red, StrokeWidth.M)!;
    [Fact] public void MarginsAndPassiveToolsNeverBegin()
    {
        foreach (var tool in Enum.GetValues<AnnotationTool>()) Assert.Null(AnnotationDrawingGesture.Begin(tool, new(0, 0), Transform, RgbaColor.Red, StrokeWidth.M));
        foreach (var tool in new[] { AnnotationTool.Select, AnnotationTool.Text }) Assert.Null(AnnotationDrawingGesture.Begin(tool, new(200, 200), Transform, RgbaColor.Red, StrokeWidth.M));
    }
    [Theory] [InlineData(1, 200)] [InlineData(1, 800)] [InlineData(2, 200)] [InlineData(2, 800)] [InlineData(1.5, 400)]
    public void ThresholdsUseViewDipsAtAnyZoom(double ppd, double view)
    {
        var t = new CanvasTransform(new(800, 800), ppd, new(view, view), 1);
        foreach (var tool in new[] { AnnotationTool.Rectangle, AnnotationTool.Arrow })
        {
            var origin = new PointD(t.ImageRectInView.X, t.ImageRectInView.Y);
            var g = AnnotationDrawingGesture.Begin(tool, origin, t, RgbaColor.Red, StrokeWidth.M)!;
            Assert.Null(g.Shape(false, true)); g.Update(new(origin.X + 2, origin.Y + 1)); Assert.Null(g.Shape(false, true));
            g.Update(new(origin.X + 20, origin.Y + 2)); Assert.Equal(tool == AnnotationTool.Arrow, g.Shape(false, true) is not null);
            g.Update(new(origin.X + 20, origin.Y + 20)); Assert.NotNull(g.Shape(false, true));
        }
    }
    [Fact] public void ShiftUpdatesWithoutMovementAndCompletionIsOneUndo()
    {
        var g = Gesture(); g.Update(Transform.ViewPoint(new(300, 200)));
        Assert.Equal(new ShapeKind.Rectangle(new(100, 100, 200, 100)), g.Shape(false, false)!.Kind);
        var square = g.Shape(true, true)!; Assert.Equal(new ShapeKind.Rectangle(new(100, 100, 200, 200)), square.Kind);
        Assert.Equal(new ShapeKind.Rectangle(new(100, 100, 200, 100)), g.Shape(false, false)!.Kind);
        var d = new AnnotationDocument(new(1600, 900), 2); d.Add(square); Assert.Single(d.Shapes); d.Undo(); Assert.Empty(d.Shapes); Assert.False(d.CanUndo);
        d.Redo(); Assert.Equal(square, Assert.Single(d.Shapes)); Assert.Equal(AnnotationTool.Rectangle, g.Tool);
    }
    [Fact] public void OutsidePointsAndConstraintsStayInsideImage()
    {
        foreach (var tool in new[] { AnnotationTool.Rectangle, AnnotationTool.Arrow })
        {
            var g = Gesture(tool, new(1500, 800)); g.Update(new(900, 900)); Assert.Equal(new(1600, 900), g.Current);
            foreach (var constrained in new[] { false, true })
                foreach (var (_, p) in g.Shape(constrained, true)!.Handles)
                { Assert.InRange(p.X, 0, 1600); Assert.InRange(p.Y, 0, 900); }
        }
        var square = Gesture(start: new(1500, 100)); square.Update(Transform.ViewPoint(new(1600, 500)));
        Assert.Equal(new ShapeKind.Rectangle(new(1500, 100, 100, 100)), square.Shape(true, true)!.Kind);
        var arrow = Gesture(AnnotationTool.Arrow, new(1500, 700)); arrow.Update(Transform.ViewPoint(new(1600, 850)));
        var end = Assert.IsType<ShapeKind.Arrow>(arrow.Shape(true, true)!.Kind).End;
        Assert.Equal(1600, end.X, 8); Assert.Equal(800, end.Y, 8);
        arrow = Gesture(AnnotationTool.Arrow); arrow.Update(Transform.ViewPoint(new(300, 250)));
        var a = Assert.IsType<ShapeKind.Arrow>(arrow.Shape(true, true)!.Kind); Assert.Equal(a.End.X - a.Start.X, a.End.Y - a.Start.Y, 8);
    }
    [Fact] public void ConstraintsStayInsideEveryCorner()
    {
        foreach (var start in new PointD[] { new(100, 100), new(1500, 100), new(100, 800), new(1500, 800) })
            foreach (var current in new PointD[] { new(-500, -500), new(2500, -500), new(-500, 1500), new(2500, 1500) })
                foreach (var tool in new[] { AnnotationTool.Rectangle, AnnotationTool.Arrow })
                {
                    var g = Gesture(tool, start); g.Update(Transform.ViewPoint(current));
                    if (g.Shape(true, true) is not { } shape) continue;
                    foreach (var (_, p) in shape.Handles) { Assert.InRange(p.X, -1e-8, 1600 + 1e-8); Assert.InRange(p.Y, -1e-8, 900 + 1e-8); }
                }
    }
}
