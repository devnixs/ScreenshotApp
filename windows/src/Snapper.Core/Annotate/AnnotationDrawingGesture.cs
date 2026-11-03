using Snapper.Core.Annotate.Model;
using Snapper.Core.Geometry;
namespace Snapper.Core.Annotate;
public sealed class AnnotationDrawingGesture
{
    private readonly CanvasTransform transform;
    private readonly RgbaColor color;
    private readonly StrokeWidth width;
    public AnnotationTool Tool { get; }
    public PointD Start { get; }
    public PointD Current { get; private set; }
    private AnnotationDrawingGesture(AnnotationTool tool, PointD start, CanvasTransform transform, RgbaColor color, StrokeWidth width)
    { Tool = tool; Start = Current = start; this.transform = transform; this.color = color; this.width = width; }
    public static AnnotationDrawingGesture? Begin(AnnotationTool tool, PointD viewPoint, CanvasTransform transform, RgbaColor color, StrokeWidth strokeWidth) =>
        tool.CreatesShapesByDragging() && transform.IsInsideImage(viewPoint) && transform.ImagePoint(viewPoint) is { } start
            ? new(tool, start, transform, color, strokeWidth) : null;
    public void Update(PointD viewPoint) { if (transform.ClampedImagePoint(viewPoint) is { } point) Current = point; }
    public AnnotationShape? Shape(bool constrained, bool completing)
    {
        var proposed = Tool == AnnotationTool.Rectangle ? constrained ? ShapeGeometry.SquareCorner(Start, Current) : Current
            : AnnotationShape.ArrowFromDrag(Start, Current, constrained).End;
        var dx = proposed.X - Start.X; var dy = proposed.Y - Start.Y; var factor = 1.0;
        if (dx > 0) factor = Math.Min(factor, (transform.ImagePixelSize.Width - Start.X) / dx);
        if (dx < 0) factor = Math.Min(factor, -Start.X / dx);
        if (dy > 0) factor = Math.Min(factor, (transform.ImagePixelSize.Height - Start.Y) / dy);
        if (dy < 0) factor = Math.Min(factor, -Start.Y / dy);
        var end = new PointD(Start.X + dx * factor, Start.Y + dy * factor);
        var minimum = completing ? AnnotationShape.MinimumDragViewDips / transform.Scale : 0;
        return Tool == AnnotationTool.Rectangle ? AnnotationShape.MakeRectangle(Start, end, false, color, width, minimum)
            : AnnotationShape.MakeArrow(Start, end, false, color, width, minimum);
    }
}
