using Snapper.Core.Geometry;

namespace Snapper.Core.Annotate.Model;

public abstract record ShapeDragKind
{
    private ShapeDragKind() { }
    public sealed record Move : ShapeDragKind;
    public sealed record Resize(ShapeHandle Handle) : ShapeDragKind;
}

public sealed record ShapeDragSession(AnnotationShape Original, ShapeDragKind Kind, PointD StartPoint, RectD ImageBounds)
{
    public AnnotationShape Preview(PointD point, bool shiftHeld)
    {
        // Grabbing a handle off-centre and clicking must not snap it to the pointer or create an edit.
        if (point == StartPoint) return Original;
        var p = new PointD(Math.Clamp(point.X, ImageBounds.Left, ImageBounds.Right),
            Math.Clamp(point.Y, ImageBounds.Top, ImageBounds.Bottom));
        return Kind switch
        {
            ShapeDragKind.Move => Original.Moved(p.X - StartPoint.X, p.Y - StartPoint.Y),
            ShapeDragKind.Resize(var handle) => Original.Resized(handle, p, shiftHeld).Shape,
            _ => throw new ArgumentOutOfRangeException(nameof(Kind))
        };
    }
    public AnnotationShape? Finish(PointD point, bool shiftHeld)
    {
        var shape = Preview(point, shiftHeld);
        return shape == Original ? null : shape;
    }
}
