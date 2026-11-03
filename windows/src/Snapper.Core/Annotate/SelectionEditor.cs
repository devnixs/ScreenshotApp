using Snapper.Core.Annotate.Model;
using Snapper.Core.Geometry;
namespace Snapper.Core.Annotate;

/// <summary>Select-tool interaction. Previews never mutate the document; release commits at most one edit.</summary>
public sealed class SelectionEditor(AnnotationDocument document)
{
    private ShapeDragSession? session;
    private PointD lastPoint;
    public bool IsDragging => session is not null;
    public AnnotationShape? DragPreview { get; private set; }
    public CanvasHit? DragTarget { get; private set; }
    public bool CanUndo => !IsDragging && document.CanUndo;
    public bool CanRedo => !IsDragging && document.CanRedo;
    public bool CanDelete => !IsDragging && document.SelectedShape is not null;
    public bool PointerPressed(PointD imagePoint, CanvasTransform transform)
    {
        AbortDrag();
        var hit = CanvasHitTester.HitTest(imagePoint, document, transform);
        ShapeDragKind kind;
        switch (hit)
        {
            case CanvasHit.Handle(_, var handle): kind = new ShapeDragKind.Resize(handle); break;
            case CanvasHit.Shape(var id): document.Select(id); kind = new ShapeDragKind.Move(); break;
            default: document.Select(null); return false;
        }
        if (document.SelectedShape is not { } shape) return false;
        session = new(shape, kind, imagePoint, new(0, 0, document.ImagePixelSize.Width, document.ImagePixelSize.Height));
        lastPoint = imagePoint; DragTarget = hit;
        return true;
    }
    public void PointerMoved(PointD imagePoint, bool shiftHeld)
    { if (session is null) return; lastPoint = imagePoint; DragPreview = session.Preview(imagePoint, shiftHeld); }
    public void ShiftChanged(bool shiftHeld) => PointerMoved(lastPoint, shiftHeld);
    public void PointerReleased(PointD imagePoint, bool shiftHeld)
    {
        var replacement = session?.Finish(imagePoint, shiftHeld);
        AbortDrag();
        if (replacement is not null) document.Replace(replacement);
    }
    public void AbortDrag() { session = null; DragPreview = null; DragTarget = null; lastPoint = default; }
    public bool Execute(CanvasKeyCommand command, AnnotationTool currentTool)
    {
        switch (command)
        {
            case CanvasKeyCommand.Deselect: AbortDrag(); document.Select(null); return true;
            case CanvasKeyCommand.DeleteSelection:
                if (CanDelete && document.SelectedShapeId is { } id) document.Delete(id);
                return true;
            case CanvasKeyCommand.Undo:
            case CanvasKeyCommand.Redo:
                if (IsDragging) return true;
                if (command == CanvasKeyCommand.Undo) document.Undo(); else document.Redo();
                if (currentTool != AnnotationTool.Select) document.Select(null);
                return true;
            default: return false;
        }
    }
    public void ToolChanged(AnnotationTool tool) { AbortDrag(); if (tool != AnnotationTool.Select) document.Select(null); }
    public void ApplyStyle(ToolbarStyleEffects effects, Func<AnnotationShape, StrokeWidth, AnnotationShape> withStrokeWidth)
    {
        if (IsDragging || document.SelectedShape is not { } shape) return;
        var replacement = shape;
        if (effects.ApplyColorToSelection is { } color) replacement = replacement.WithColor(color);
        if (effects.ApplyStrokeWidthToSelection is { } width) replacement = withStrokeWidth(replacement, width);
        if (replacement != shape) document.Replace(replacement);
    }
}
