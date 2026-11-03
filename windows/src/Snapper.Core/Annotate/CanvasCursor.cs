using Snapper.Core.Annotate.Model;
namespace Snapper.Core.Annotate;

public enum CanvasCursorKind { Arrow, Crosshair, Move, ResizeNwse, ResizeNesw, ResizeEndpoint }
public static class CanvasCursor
{
    public static CanvasCursorKind For(AnnotationTool tool, bool insideImage, CanvasHit hit, bool isDragging, CanvasHit? dragTarget)
    {
        if (tool.CreatesShapesByDragging()) return insideImage ? CanvasCursorKind.Crosshair : CanvasCursorKind.Arrow;
        if (tool != AnnotationTool.Select) return CanvasCursorKind.Arrow;
        return (isDragging ? dragTarget ?? hit : hit) switch
        {
            CanvasHit.Handle(_, ShapeHandle.TopLeft or ShapeHandle.BottomRight) => CanvasCursorKind.ResizeNwse,
            CanvasHit.Handle(_, ShapeHandle.TopRight or ShapeHandle.BottomLeft) => CanvasCursorKind.ResizeNesw,
            CanvasHit.Handle(_, ShapeHandle.Start or ShapeHandle.End) => CanvasCursorKind.ResizeEndpoint,
            CanvasHit.Shape => CanvasCursorKind.Move,
            _ => CanvasCursorKind.Arrow
        };
    }
}
