using Snapper.Core.Geometry;
namespace Snapper.Core.Annotate;
public static class TextEditorLayout
{
    public static (double Scale, double OffsetX, double OffsetY) LayerTransform(CanvasTransform t) => (t.Scale, t.ImageRectInView.X, t.ImageRectInView.Y);
    public static PointD TextBoxPosition(PointD origin, PointD inset) => new(origin.X - inset.X, origin.Y - inset.Y);
    public static PointD EditorLocalPoint(PointD imagePoint, PointD origin, PointD inset) => new(imagePoint.X - origin.X + inset.X, imagePoint.Y - origin.Y + inset.Y);
}
