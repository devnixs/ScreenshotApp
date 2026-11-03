using Snapper.Core.Geometry;
namespace Snapper.Core.Annotate.Model;

public sealed record TextEditDraft(AnnotationShape? Original, string Text, PointD Origin, RgbaColor Color, StrokeWidth StrokeWidth, SizeD MeasuredSize);
public abstract record TextCommitDecision
{
    public sealed record Discard : TextCommitDecision;
    public sealed record NoChange : TextCommitDecision;
    public sealed record Add(AnnotationShape Shape) : TextCommitDecision;
    public sealed record Update(AnnotationShape Shape) : TextCommitDecision;
    public sealed record Delete(Guid Id) : TextCommitDecision;
    public static TextCommitDecision Decide(TextEditDraft draft)
    {
        if (string.IsNullOrWhiteSpace(draft.Text)) return draft.Original is { } empty ? new Delete(empty.Id) : new Discard();
        if (draft.Original is { Kind: ShapeKind.Text(var box) } original && box.Text == draft.Text &&
            original.Color == draft.Color && original.StrokeWidth == draft.StrokeWidth) return new NoChange();
        var shape = new AnnotationShape(draft.Original?.Id ?? Guid.NewGuid(),
            new ShapeKind.Text(new(draft.Origin, draft.Text, draft.MeasuredSize)), draft.Color, draft.StrokeWidth);
        return draft.Original is null ? new Add(shape) : new Update(shape);
    }
}
public static class TextCommitDocumentExtensions
{
    public static void Apply(this AnnotationDocument document, TextCommitDecision decision)
    {
        switch (decision)
        {
            case TextCommitDecision.Add(var shape): document.Add(shape); break;
            case TextCommitDecision.Update(var shape): document.Replace(shape); break;
            case TextCommitDecision.Delete(var id): document.Delete(id); break;
        }
    }
}
