using Snapper.Core.Annotate;
using Snapper.Core.Annotate.Model;
using Snapper.Core.Geometry;
namespace Snapper.Core.Tests;

public sealed class SelectionEditorTests
{
    private static CanvasTransform Transform => new(new(100, 100), 1, new(100, 100), 1);
    private static AnnotationShape Rectangle => new(new ShapeKind.Rectangle(new(20, 20, 40, 30)), RgbaColor.Red, StrokeWidth.M);
    private static (AnnotationDocument Document, SelectionEditor Editor, AnnotationShape Shape) Setup()
    {
        var d = new AnnotationDocument(new(100, 100), 1); var shape = Rectangle;
        d.Add(shape); d.MarkClean(); return (d, new SelectionEditor(d), shape);
    }
    private static void PressBody(SelectionEditor editor) => Assert.True(editor.PointerPressed(new(20, 30), Transform));
    [Fact] public void PressSelectsWithoutEditingAndEmptyPressDeselects()
    {
        var (d, editor, shape) = Setup(); PressBody(editor);
        Assert.Equal(shape.Id, d.SelectedShapeId); Assert.True(editor.IsDragging); Assert.False(d.IsDirty);
        Assert.Equal(new CanvasHit.Shape(shape.Id), editor.DragTarget);
        editor.PointerReleased(new(20, 30), false); d.Undo(); Assert.Empty(d.Shapes);
        d.Redo(); d.Select(shape.Id);
        Assert.False(editor.PointerPressed(new(90, 90), Transform)); Assert.Null(d.SelectedShapeId); Assert.False(editor.IsDragging);
    }
    [Fact] public void MovePreviewsOriginalAndCommitsExactlyOneUndoStep()
    {
        var (d, editor, shape) = Setup(); PressBody(editor);
        for (var i = 0; i < 100; i++) editor.PointerMoved(new(30, 25), false);
        var moved = Assert.IsType<AnnotationShape>(editor.DragPreview);
        Assert.Equal(new ShapeKind.Rectangle(new(30, 15, 40, 30)), moved.Kind);
        Assert.Equal(shape, d.Shapes.Single()); Assert.False(d.IsDirty);
        editor.PointerReleased(new(30, 25), false);
        Assert.False(editor.IsDragging); Assert.Null(editor.DragPreview); Assert.Null(editor.DragTarget);
        Assert.Equal(moved, d.Shapes.Single()); Assert.True(d.IsDirty);
        d.Undo(); Assert.Equal(shape, d.Shapes.Single()); Assert.False(d.IsDirty);
        d.Redo(); Assert.Equal(moved, d.Shapes.Single());
        d.Undo(); d.Undo(); Assert.Empty(d.Shapes); Assert.False(d.CanUndo);
    }
    [Fact] public void ClickAndReturnToOriginCommitNothing()
    {
        var (d, editor, shape) = Setup(); PressBody(editor);
        editor.PointerMoved(new(80, 80), false); editor.PointerReleased(new(20, 30), false);
        Assert.False(d.IsDirty); Assert.Equal(shape, d.Shapes.Single());
        d.Undo(); Assert.Empty(d.Shapes);
        editor.PointerReleased(new(0, 0), false); Assert.Empty(d.Shapes);
    }
    [Fact] public void OffCentreHandleClickDoesNotSnapOrBecomeDirty()
    {
        var (d, editor, shape) = Setup(); d.Select(shape.Id);
        Assert.True(editor.PointerPressed(new(18, 20), Transform));
        Assert.Equal(new CanvasHit.Handle(shape.Id, ShapeHandle.TopLeft), editor.DragTarget);
        editor.PointerMoved(new(18, 20), true); Assert.Equal(shape, editor.DragPreview);
        editor.PointerReleased(new(18, 20), true); Assert.False(d.IsDirty);
    }
    [Fact] public void ResizeShiftUpdatesAtLastPointerWithoutMovement()
    {
        var (d, editor, shape) = Setup(); d.Select(shape.Id);
        Assert.True(editor.PointerPressed(new(60, 50), Transform));
        editor.PointerMoved(new(70, 60), true);
        var square = Assert.IsType<ShapeKind.Rectangle>(editor.DragPreview?.Kind).Rect;
        Assert.Equal(50, square.Width); Assert.Equal(50, square.Height); Assert.Equal(20, square.X); Assert.Equal(20, square.Y);
        editor.ShiftChanged(false);
        Assert.Equal(new ShapeKind.Rectangle(new(20, 20, 50, 40)), editor.DragPreview?.Kind);
        editor.PointerReleased(new(70, 60), false); d.Undo(); Assert.Equal(shape, d.Shapes.Single());
    }
    [Fact] public void ArrowEndpointShiftSnapsAroundFixedEnd()
    {
        var d = new AnnotationDocument(new(100, 100), 1);
        var arrow = new AnnotationShape(new ShapeKind.Arrow(new(20, 20), new(60, 50)), RgbaColor.Blue, StrokeWidth.S);
        d.Add(arrow); d.Select(arrow.Id); var editor = new SelectionEditor(d);
        Assert.True(editor.PointerPressed(new(60, 50), Transform)); editor.PointerMoved(new(80, 60), true);
        var kind = Assert.IsType<ShapeKind.Arrow>(editor.DragPreview?.Kind);
        Assert.Equal(new PointD(20, 20), kind.Start);
        var steps = Math.Atan2(kind.End.Y - kind.Start.Y, kind.End.X - kind.Start.X) / (Math.PI / 4);
        Assert.InRange(Math.Abs(steps - Math.Round(steps)), 0, 1e-8);
    }
    [Fact] public void CapturedPointerOutsideWindowClampsGrabbedPoint()
    {
        var (_, editor, _) = Setup(); PressBody(editor); editor.PointerMoved(new(-5000, 9000), false);
        Assert.Equal(new ShapeKind.Rectangle(new(0, 90, 40, 30)), editor.DragPreview?.Kind);
    }
    [Fact] public void LostCaptureAbortKeepsSelectionAndOriginal()
    {
        var (d, editor, shape) = Setup(); PressBody(editor); editor.PointerMoved(new(70, 70), false); editor.AbortDrag();
        Assert.False(editor.IsDragging); Assert.Null(editor.DragPreview); Assert.Null(editor.DragTarget);
        Assert.Equal(shape.Id, d.SelectedShapeId); Assert.Equal(shape, d.Shapes.Single()); Assert.False(d.IsDirty);
    }
    [Fact] public void EscapeAlwaysConsumesAndAbortsAndDeselects()
    {
        var (d, editor, shape) = Setup(); PressBody(editor); editor.PointerMoved(new(70, 70), false);
        Assert.True(editor.Execute(CanvasKeyCommand.Deselect, AnnotationTool.Select));
        Assert.False(editor.IsDragging); Assert.Null(d.SelectedShapeId); Assert.Equal(shape, d.Shapes.Single()); Assert.False(d.IsDirty);
        Assert.True(editor.Execute(CanvasKeyCommand.Deselect, AnnotationTool.Select));
    }
    [Fact] public void DeleteIsUndoableButNoSelectionAndDraggingAreNoOps()
    {
        var (d, editor, shape) = Setup(); Assert.False(editor.CanDelete);
        Assert.True(editor.Execute(CanvasKeyCommand.DeleteSelection, AnnotationTool.Select)); Assert.Equal(shape, d.Shapes.Single());
        PressBody(editor); Assert.False(editor.CanDelete);
        editor.Execute(CanvasKeyCommand.DeleteSelection, AnnotationTool.Select); Assert.Equal(shape, d.Shapes.Single());
        editor.PointerReleased(new(20, 30), false); Assert.True(editor.CanDelete);
        editor.Execute(CanvasKeyCommand.DeleteSelection, AnnotationTool.Select); Assert.Empty(d.Shapes); Assert.Null(d.SelectedShapeId);
        editor.Execute(CanvasKeyCommand.Undo, AnnotationTool.Select); Assert.Equal(shape, d.Shapes.Single()); Assert.Equal(shape.Id, d.SelectedShapeId);
        editor.Execute(CanvasKeyCommand.Redo, AnnotationTool.Select); Assert.Empty(d.Shapes);
    }
    [Fact] public void UndoRedoAvailabilityAndCommandsAreGuardedDuringDrag()
    {
        var (d, editor, shape) = Setup(); d.Replace(shape.WithColor(RgbaColor.Green)); d.Undo();
        Assert.True(editor.CanUndo); Assert.True(editor.CanRedo);
        PressBody(editor); Assert.False(editor.CanUndo); Assert.False(editor.CanRedo); Assert.False(editor.CanDelete);
        Assert.True(editor.Execute(CanvasKeyCommand.Undo, AnnotationTool.Select)); Assert.True(editor.Execute(CanvasKeyCommand.Redo, AnnotationTool.Select));
        Assert.Equal(shape, d.Shapes.Single()); Assert.True(editor.IsDragging);
    }
    [Theory] [InlineData(AnnotationTool.Select, true)] [InlineData(AnnotationTool.Rectangle, false)] [InlineData(AnnotationTool.Arrow, false)] [InlineData(AnnotationTool.Text, false)]
    public void HistorySelectionIsKeptOnlyForSelectTool(AnnotationTool tool, bool selected)
    {
        var (d, editor, shape) = Setup(); d.Select(shape.Id); d.Replace(shape.WithColor(RgbaColor.Blue));
        editor.Execute(CanvasKeyCommand.Undo, tool);
        Assert.Equal(selected ? shape.Id : (Guid?)null, d.SelectedShapeId);
        editor.Execute(CanvasKeyCommand.Redo, tool);
        Assert.Equal(selected ? shape.Id : (Guid?)null, d.SelectedShapeId);
    }
    [Fact] public void ToolChangeAbortsAndClearsExceptForSelect()
    {
        var (d, editor, shape) = Setup(); PressBody(editor); editor.PointerMoved(new(80, 80), false);
        editor.ToolChanged(AnnotationTool.Select); Assert.False(editor.IsDragging); Assert.Equal(shape.Id, d.SelectedShapeId);
        PressBody(editor); editor.ToolChanged(AnnotationTool.Rectangle);
        Assert.False(editor.IsDragging); Assert.Null(d.SelectedShapeId); Assert.Equal(shape, d.Shapes.Single()); Assert.False(d.IsDirty);
    }
    [Fact] public void ColorAndWidthTogetherAreOneEditAndDelegateMeasuresWidth()
    {
        var (d, editor, shape) = Setup(); d.Select(shape.Id); var calls = 0;
        editor.ApplyStyle(new(ApplyColorToSelection: RgbaColor.Green, ApplyStrokeWidthToSelection: StrokeWidth.L), (s, w) => { calls++; return s.WithStrokeWidth(w); });
        Assert.Equal(1, calls); var changed = d.Shapes.Single(); Assert.Equal(RgbaColor.Green, changed.Color); Assert.Equal(StrokeWidth.L, changed.StrokeWidth); Assert.Equal(shape.Kind, changed.Kind);
        d.Undo(); Assert.Equal(shape, d.Shapes.Single()); Assert.False(d.IsDirty);
        d.Redo(); Assert.Equal(changed, d.Shapes.Single());
    }
    [Fact] public void EmptySameAndUnselectedStyleEffectsDoNotCreateEdits()
    {
        var (d, editor, shape) = Setup();
        editor.ApplyStyle(new(ApplyColorToSelection: RgbaColor.Green), (s, w) => s.WithStrokeWidth(w)); Assert.Equal(shape, d.Shapes.Single());
        d.Select(shape.Id);
        editor.ApplyStyle(new(), (s, w) => throw new InvalidOperationException("Should not measure"));
        editor.ApplyStyle(new(ApplyColorToSelection: RgbaColor.Red), (s, w) => s.WithStrokeWidth(w));
        Assert.False(d.IsDirty); d.Undo(); Assert.Empty(d.Shapes);
    }
    [Fact] public void StyleIsIgnoredDuringDrag()
    {
        var (d, editor, shape) = Setup(); PressBody(editor);
        editor.ApplyStyle(new(ApplyColorToSelection: RgbaColor.Green, ApplyStrokeWidthToSelection: StrokeWidth.L), (s, w) => throw new InvalidOperationException("Should not measure"));
        Assert.Equal(shape, d.Shapes.Single()); Assert.False(d.IsDirty);
    }
    [Fact] public void ColorOnlyIsOneEditWithoutGeometryChange()
    {
        var (d, editor, shape) = Setup(); d.Select(shape.Id);
        editor.ApplyStyle(new(ApplyColorToSelection: RgbaColor.Green), (s, w) => s.WithStrokeWidth(w));
        Assert.Equal(shape.Kind, d.Shapes.Single().Kind); Assert.Equal(RgbaColor.Green, d.Shapes.Single().Color);
        d.Undo(); Assert.Equal(shape, d.Shapes.Single()); Assert.False(d.IsDirty);
    }
    [Fact] public void WindowsKeepIndependentSelectionsDragsAndHistories()
    {
        var (a, first, _) = Setup(); var (b, second, other) = Setup();
        PressBody(first); Assert.False(second.IsDragging); Assert.Null(b.SelectedShapeId);
        first.PointerReleased(new(30, 25), false); first.Execute(CanvasKeyCommand.DeleteSelection, AnnotationTool.Select); first.Execute(CanvasKeyCommand.Undo, AnnotationTool.Select);
        Assert.Equal(other, b.Shapes.Single()); Assert.False(b.IsDirty); Assert.Null(b.SelectedShapeId);
        PressBody(second); Assert.False(first.IsDragging);
    }
}
