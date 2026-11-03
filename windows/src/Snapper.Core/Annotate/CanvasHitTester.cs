using Snapper.Core.Annotate.Model;
using Snapper.Core.Geometry;
namespace Snapper.Core.Annotate;

public abstract record CanvasHit
{
    public sealed record Handle(Guid ShapeId, ShapeHandle Which) : CanvasHit;
    public sealed record Shape(Guid ShapeId) : CanvasHit;
    public sealed record None : CanvasHit;
}
public static class CanvasHitTester
{
    public const double HandleHitRadiusDips = 6, ShapeHitToleranceDips = 4;
    public static double ImagePixels(double dips, CanvasTransform transform) => transform.Scale > 0 ? dips / transform.Scale : 0;
    public static CanvasHit HitTest(PointD imagePoint, AnnotationDocument document, CanvasTransform transform)
    {
        if (transform.Scale <= 0) return new CanvasHit.None();
        if (document.SelectedShape is { } selected && selected.HandleHitTest(imagePoint, ImagePixels(HandleHitRadiusDips, transform)) is { } handle)
            return new CanvasHit.Handle(selected.Id, handle);
        return document.TopmostShapeAt(imagePoint, ImagePixels(ShapeHitToleranceDips, transform)) is { } shape
            ? new CanvasHit.Shape(shape.Id) : new CanvasHit.None();
    }
}
