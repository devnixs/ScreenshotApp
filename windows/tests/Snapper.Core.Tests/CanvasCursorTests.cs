using Snapper.Core.Annotate;
using Snapper.Core.Annotate.Model;
namespace Snapper.Core.Tests;
public sealed class CanvasCursorTests
{
    [Theory]
    [InlineData(ShapeHandle.TopLeft, CanvasCursorKind.ResizeNwse)]
    [InlineData(ShapeHandle.BottomRight, CanvasCursorKind.ResizeNwse)]
    [InlineData(ShapeHandle.TopRight, CanvasCursorKind.ResizeNesw)]
    [InlineData(ShapeHandle.BottomLeft, CanvasCursorKind.ResizeNesw)]
    [InlineData(ShapeHandle.Start, CanvasCursorKind.ResizeEndpoint)]
    [InlineData(ShapeHandle.End, CanvasCursorKind.ResizeEndpoint)]
    public void SelectedHandleCursor(ShapeHandle handle, CanvasCursorKind expected) => Assert.Equal(expected,
        CanvasCursor.For(AnnotationTool.Select, false, new CanvasHit.Handle(Guid.NewGuid(), handle), false, null));
    [Fact] public void DragUsesPressTimeHandleEvenOutsideImage()
    {
        Assert.Equal(CanvasCursorKind.ResizeNwse, CanvasCursor.For(AnnotationTool.Select, false, new CanvasHit.None(), true, new CanvasHit.Handle(Guid.NewGuid(), ShapeHandle.TopLeft)));
        Assert.Equal(CanvasCursorKind.Move, CanvasCursor.For(AnnotationTool.Select, true, new CanvasHit.Shape(Guid.NewGuid()), false, null));
        Assert.Equal(CanvasCursorKind.Arrow, CanvasCursor.For(AnnotationTool.Select, true, new CanvasHit.None(), false, null));
        Assert.Equal(CanvasCursorKind.Arrow, CanvasCursor.For(AnnotationTool.Text, true, new CanvasHit.Shape(Guid.NewGuid()), false, null));
    }
    [Theory] [InlineData(AnnotationTool.Rectangle)] [InlineData(AnnotationTool.Arrow)]
    public void DrawingOnlyCrosshairsInsideImage(AnnotationTool tool)
    {
        var hit = new CanvasHit.Handle(Guid.NewGuid(), ShapeHandle.TopLeft);
        Assert.Equal(CanvasCursorKind.Crosshair, CanvasCursor.For(tool, true, hit, false, null));
        Assert.Equal(CanvasCursorKind.Arrow, CanvasCursor.For(tool, false, hit, false, null));
    }
}
