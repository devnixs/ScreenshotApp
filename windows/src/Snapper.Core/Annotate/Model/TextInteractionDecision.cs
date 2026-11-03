using Snapper.Core.Geometry;
namespace Snapper.Core.Annotate.Model;

public abstract record TextInteractionDecision
{
    public sealed record Commit : TextInteractionDecision;
    public sealed record BeginNew(PointD Point) : TextInteractionDecision;
    public sealed record Edit(Guid Id) : TextInteractionDecision;
    public sealed record None : TextInteractionDecision;
    public static TextInteractionDecision Decide(bool isEditing, AnnotationTool tool, int clickCount, PointD? imagePoint, AnnotationDocument document)
    {
        if (isEditing) return new Commit();
        if (imagePoint is not { } point || !(tool == AnnotationTool.Text || tool == AnnotationTool.Select && clickCount == 2)) return new None();
        var text = document.Shapes.Reverse().FirstOrDefault(s => s.Kind is ShapeKind.Text && s.HitTest(point, document.PixelsPerDip, 0));
        return text is not null ? new Edit(text.Id) : tool == AnnotationTool.Text ? new BeginNew(point) : new None();
    }
}
