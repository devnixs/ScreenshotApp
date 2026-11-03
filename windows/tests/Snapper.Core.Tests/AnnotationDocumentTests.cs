using Snapper.Core.Annotate.Model;
using Snapper.Core.Geometry;

namespace Snapper.Core.Tests;

public sealed class AnnotationDocumentTests
{
    private static AnnotationDocument NewDocument() => new(new(2000, 1200), 2);
    private static AnnotationShape Rect(double x = 100) => new(new ShapeKind.Rectangle(new(x, 100, 200, 100)), RgbaColor.Red, StrokeWidth.M);
    private static AnnotationShape Text(string s = "") => AnnotationShape.MakeText(new(10, 20), RgbaColor.Red, StrokeWidth.M)
        .WithText(s).WithMeasuredSize(new(80, 40));
    private static AnnotationShape Moved(AnnotationShape shape, double d = 20) => shape.Moved(d, d);
    private static void Shapes(AnnotationDocument d, params AnnotationShape[] expected) => Assert.Equal(expected, d.Shapes);

    [Fact] public void NewDocumentIsEmptyCleanAndHasNoHistory()
    {
        var d = NewDocument();
        Assert.Empty(d.Shapes); Assert.Null(d.SelectedShapeId); Assert.Null(d.SelectedShape);
        Assert.False(d.CanUndo || d.CanRedo || d.IsDirty || d.IsEditing);
        Assert.Equal(new PixelSize(2000, 1200), d.ImagePixelSize); Assert.Equal(2, d.PixelsPerDip);
    }
    [Fact] public void AddAppendsOnTopAndIsOneUndoStep()
    {
        var d = NewDocument(); var a = Rect(); var b = Rect(200);
        d.Add(a); d.Add(b); Shapes(d, a, b); Assert.Null(d.SelectedShapeId); Assert.True(d.CanUndo);
        d.Undo(); Shapes(d, a); d.Undo(); Assert.Empty(d.Shapes); Assert.False(d.CanUndo);
    }
    [Fact] public void DeleteRemovesShapeAndUndoRestoresAtSameIndex()
    {
        var d = NewDocument(); var a = Rect(); var b = Rect(200); var c = Rect(300);
        d.Add(a); d.Add(b); d.Add(c); d.Delete(b.Id); Shapes(d, a, c);
        d.Undo(); Shapes(d, a, b, c); d.Redo(); Shapes(d, a, c);
    }
    [Fact] public void DeleteOfSelectedShapeClearsSelectionAndUndoReselects()
    {
        var d = NewDocument(); var a = Rect(); var b = Rect(200);
        d.Add(a); d.Add(b); d.Select(b.Id); d.Delete(b.Id); Assert.Null(d.SelectedShapeId);
        d.Undo(); Assert.Equal(b, d.SelectedShape); Shapes(d, a, b);
        d.Redo(); Assert.Null(d.SelectedShape);
    }
    [Fact] public void DeleteOfUnselectedShapeKeepsSelection()
    {
        var d = NewDocument(); var a = Rect(); var b = Rect(200);
        d.Add(a); d.Add(b); d.Select(a.Id); d.Delete(b.Id); Assert.Equal(a, d.SelectedShape);
    }
    [Fact] public void ReplaceKeepsZOrderAndIsUndoable()
    {
        var a = Rect(); var b = Rect(200); var t = Text("Hello");
        AnnotationShape[] changes = [Moved(a), a.WithColor(RgbaColor.Blue), a.WithStrokeWidth(StrokeWidth.L),
            a.Resized(ShapeHandle.BottomRight, new(400, 250), false).Shape];
        foreach (var change in changes)
        {
            var d = NewDocument(); d.Add(a); d.Add(b); d.Replace(change); Shapes(d, change, b);
            d.Undo(); Shapes(d, a, b); d.Redo(); Shapes(d, change, b);
        }
        var document = NewDocument(); document.Add(t);
        var edited = t.WithText("Edited").WithMeasuredSize(new(100, 40));
        document.Replace(edited); document.Undo(); Shapes(document, t); document.Redo(); Shapes(document, edited);
    }
    [Fact] public void ReplaceWithIdenticalValuePushesNothing()
    {
        var d = NewDocument(); var a = Rect();
        d.Add(a); d.MarkClean(); d.Replace(a); Assert.False(d.IsDirty);
        d.Undo(); Assert.Empty(d.Shapes); Assert.False(d.CanUndo);
    }
    [Fact] public void UnknownIdDeleteAndReplaceAreNoOps()
    {
        var d = NewDocument(); var unknown = Rect();
        d.Delete(unknown.Id); d.Replace(unknown); Assert.Empty(d.Shapes); Assert.False(d.CanUndo || d.IsDirty);
        var a = Rect(); d.Add(a); d.Undo(); d.Delete(unknown.Id); d.Replace(unknown);
        Assert.True(d.CanRedo); Assert.False(d.CanUndo || d.IsDirty); d.Redo(); Shapes(d, a);
    }
    [Fact] public void UndoRedoSequence()
    {
        var d = NewDocument(); var a = Rect(); var b = Rect(200); var changed = Moved(b);
        d.Add(a); d.Add(b); d.Replace(changed);
        d.Undo(); Shapes(d, a, b); d.Undo(); Shapes(d, a); d.Undo(); Assert.Empty(d.Shapes);
        d.Redo(); Shapes(d, a); d.Redo(); Shapes(d, a, b); d.Redo(); Shapes(d, a, changed); Assert.False(d.CanRedo);
    }
    [Fact] public void NewEditClearsRedo()
    {
        var d = NewDocument(); var a = Rect(); var b = Rect(200); var c = Rect(300);
        d.Add(a); d.Add(b); d.Undo(); d.Add(c); Assert.False(d.CanRedo); Shapes(d, a, c);
        d.Redo(); Shapes(d, a, c);
    }
    [Fact] public void CoalescedDragIsOneStep()
    {
        var d = NewDocument(); var a = Rect(); d.Add(a); d.BeginEdit();
        for (var distance = 1; distance <= 20; distance++) d.Replace(Moved(a, distance));
        d.CommitEdit(); Shapes(d, Moved(a, 20)); d.Undo(); Shapes(d, a); Assert.True(d.CanUndo);
        d.Undo(); Assert.Empty(d.Shapes); Assert.False(d.CanUndo);
        d.Redo(); d.Redo(); Shapes(d, Moved(a, 20));
    }
    [Fact] public void CreateDragSessionIsOneStep()
    {
        var d = NewDocument(); var prior = Rect(); var a = Rect(200);
        d.Add(prior); d.Select(prior.Id); d.BeginEdit(); d.Add(a);
        for (var distance = 1; distance <= 5; distance++) d.Replace(Moved(a, distance));
        d.Select(a.Id); d.CommitEdit(); d.Undo(); Shapes(d, prior); Assert.Equal(prior, d.SelectedShape);
        d.Redo(); Shapes(d, prior, Moved(a, 5)); Assert.Equal(a.Id, d.SelectedShapeId);
    }
    [Fact] public void NoOpCommitPushesNothingAndKeepsRedo()
    {
        var d = NewDocument(); var a = Rect(); var b = Rect(200);
        d.Add(a); d.Add(b); d.Undo(); d.MarkClean();
        d.BeginEdit(); d.Replace(Moved(a)); d.Replace(a); d.CommitEdit(); Assert.True(d.CanRedo); Assert.False(d.IsDirty);
        d.Undo(); Assert.Empty(d.Shapes); Assert.False(d.CanUndo); d.Redo(); d.Redo(); Shapes(d, a, b);
    }
    [Fact] public void CancelEditRestoresShapesAndSelection()
    {
        var d = NewDocument(); var a = Rect(); var b = Rect(200); var c = Rect(300);
        d.Add(a); d.Add(c); d.Undo(); d.Select(a.Id); d.MarkClean();
        d.BeginEdit(); d.Replace(Moved(a)); d.Add(b); d.Select(b.Id); d.CancelEdit();
        Shapes(d, a); Assert.Equal(a.Id, d.SelectedShapeId); Assert.False(d.IsEditing || d.IsDirty); Assert.True(d.CanRedo);
        d.Undo(); Assert.Empty(d.Shapes); Assert.False(d.CanUndo);
    }
    [Fact] public void UndoRedoUnavailableWhileEditing()
    {
        var d = NewDocument(); var a = Rect(); var b = Rect(200);
        d.Add(a); d.Add(b); d.Undo(); d.BeginEdit(); d.Replace(Moved(a));
        Assert.False(d.CanUndo || d.CanRedo); Assert.True(d.IsEditing);
        d.Undo(); d.Redo(); Shapes(d, Moved(a)); d.CancelEdit(); Assert.True(d.CanUndo && d.CanRedo);
        d.BeginEdit(); d.CommitEdit(); Assert.True(d.CanUndo && d.CanRedo);
    }
    [Fact] public void CommitAndCancelWithoutSessionAreNoOps()
    {
        var d = NewDocument(); d.Undo(); d.Redo(); d.CommitEdit(); d.CancelEdit(); Assert.Empty(d.Shapes);
        Assert.False(d.IsDirty || d.CanUndo || d.CanRedo || d.IsEditing);
        d.Add(Rect()); d.Undo(); d.CommitEdit(); d.CancelEdit(); Assert.True(d.CanRedo); Assert.False(d.CanUndo || d.IsDirty);
    }
    [Fact] public void SelectionIsNotUndoableAndNotDirty()
    {
        var d = NewDocument(); var a = Rect(); var b = Rect(200);
        d.Add(a); d.Add(b); d.MarkClean(); d.Select(a.Id); Assert.Equal(a, d.SelectedShape);
        d.Select(b.Id); d.Select(null); d.Select(Guid.NewGuid()); Assert.False(d.IsDirty); Assert.Null(d.SelectedShapeId);
        d.Undo(); Shapes(d, a); d.Select(a.Id); Assert.True(d.CanRedo);
        d.Undo(); Assert.Empty(d.Shapes); Assert.False(d.CanUndo); Assert.Null(d.SelectedShapeId);
    }
    [Fact] public void RedoRestoresSelectionAtCommit()
    {
        var d = NewDocument(); var a = Rect(); d.BeginEdit(); d.Add(a); d.Select(a.Id); d.CommitEdit();
        d.Undo(); Assert.Empty(d.Shapes); Assert.Null(d.SelectedShapeId); d.Redo(); Shapes(d, a); Assert.Equal(a, d.SelectedShape);
    }
    [Fact] public void TopmostShapeWinsOnOverlap()
    {
        var d = NewDocument(); var a = Rect(); var b = Rect(200); d.Add(a); d.Add(b);
        Assert.Equal(a, d.ShapeWithId(a.Id)); Assert.Null(d.ShapeWithId(Guid.NewGuid()));
        Assert.Equal(b, d.TopmostShapeAt(new(250, 100), 0)); Assert.Equal(a, d.TopmostShapeAt(new(150, 100), 0));
        Assert.Null(d.TopmostShapeAt(new(50, 50), 0)); Assert.Equal(b, d.TopmostShapeAt(new(250, 108), 4));
        Assert.Null(d.TopmostShapeAt(new(250, 109), 4)); d.Delete(b.Id); Assert.Equal(a, d.TopmostShapeAt(new(250, 100), 0));
        var t = Text("Hi"); d.Add(t); Assert.Equal(t, d.TopmostShapeAt(new(30, 30), 0));
        var arrow = new AnnotationShape(new ShapeKind.Arrow(new(0, 0), new(100, 0)), RgbaColor.Blue, StrokeWidth.M);
        d.Add(arrow); Assert.Equal(arrow, d.TopmostShapeAt(new(65, 10), 0));
    }
    [Fact] public void DirtyLifecycle()
    {
        var d = NewDocument(); var a = Rect(); Assert.False(d.IsDirty); d.Add(a); Assert.True(d.IsDirty);
        d.MarkClean(); Assert.False(d.IsDirty); d.Replace(Moved(a)); Assert.True(d.IsDirty);
        d.Undo(); Assert.False(d.IsDirty); d.Redo(); Assert.True(d.IsDirty); d.Undo(); Assert.False(d.IsDirty);
    }
    [Fact] public void EditAfterUndoToCleanIsDirty()
    {
        var d = NewDocument(); var a = Rect(); d.Add(a); d.MarkClean(); d.Replace(Moved(a)); d.Undo(); Assert.False(d.IsDirty);
        d.Replace(Moved(a, 40)); Assert.True(d.IsDirty); Assert.False(d.CanRedo);
        d.MarkClean(); d.Undo(); d.Replace(Moved(a, 60)); Assert.True(d.IsDirty); Assert.False(d.CanRedo);
    }
    [Fact] public void MarkCleanMidHistory()
    {
        var d = NewDocument(); var a = Rect(); var b = Rect(200); d.Add(a); d.Add(b); d.Undo(); d.MarkClean();
        Assert.False(d.IsDirty); d.Redo(); Assert.True(d.IsDirty); d.Undo(); Assert.False(d.IsDirty);
        d.Undo(); Assert.True(d.IsDirty); d.Redo(); Assert.False(d.IsDirty);
    }
    [Fact] public void DirtyWhileEditingWithUncommittedChange()
    {
        var d = NewDocument(); var a = Rect(); d.Add(a); d.MarkClean(); d.BeginEdit(); Assert.False(d.IsDirty);
        d.Replace(Moved(a)); Assert.True(d.IsDirty); d.Replace(a); Assert.False(d.IsDirty);
        d.Replace(Moved(a)); d.CancelEdit(); Assert.False(d.IsDirty);
    }
    [Fact] public void AbandonedNewEmptyTextBoxLeavesNoTrace()
    {
        var d = NewDocument(); var a = Rect(); var t = Text(); d.Add(a); d.Undo(); d.MarkClean();
        d.BeginEdit(); d.Add(t); d.Select(t.Id); d.Replace(t.WithText("")); d.Delete(t.Id); d.CommitEdit();
        Assert.True(d.CanRedo); Assert.False(d.CanUndo || d.IsDirty); Assert.Null(d.SelectedShapeId); d.Redo(); Shapes(d, a);
    }
    [Fact] public void ReEditingTextToEmptyIsOneUndoStep()
    {
        var d = NewDocument(); var t = Text("Hi"); var r = Rect(); d.Add(t); d.Add(r); d.Select(t.Id); d.MarkClean();
        d.BeginEdit(); d.Replace(t.WithText("")); d.Delete(t.Id); d.CommitEdit(); Shapes(d, r); Assert.True(d.IsDirty);
        d.Undo(); Shapes(d, t, r); Assert.Equal(t, d.SelectedShape); Assert.False(d.IsDirty);
        d.Redo(); Shapes(d, r); Assert.Null(d.SelectedShapeId);
    }
    [Fact] public void TextKeystrokesAreOneEdit()
    {
        var d = NewDocument(); var t = Text("Hi"); d.Add(t); d.MarkClean(); d.BeginEdit();
        foreach (var s in new[] { "H", "Hello", "Hello\nworld" }) d.Replace(t.WithText(s));
        d.CommitEdit(); Shapes(d, t.WithText("Hello\nworld")); d.Undo(); Shapes(d, t); Assert.False(d.IsDirty);
        d.Undo(); Assert.Empty(d.Shapes); Assert.False(d.CanUndo);
    }
    [Fact] public void SelectionOnlySessionKeepsSelectionAndRedo()
    {
        var d = NewDocument(); var a = Rect(); var b = Rect(200); d.Add(a); d.Add(b); d.Undo(); d.MarkClean();
        d.BeginEdit(); d.Select(a.Id); d.CommitEdit(); Assert.Equal(a, d.SelectedShape); Assert.False(d.IsDirty); Assert.True(d.CanRedo);
        d.Undo(); Assert.Empty(d.Shapes); Assert.False(d.CanUndo); Assert.Null(d.SelectedShapeId);
    }
    [Fact] public void CloneHasIndependentHistoryAndCleanMarker()
    {
        var original = NewDocument(); var a = Rect(); original.Add(a); original.MarkClean(); var copy = original.Clone();
        copy.BeginEdit(); copy.Replace(Moved(a)); copy.CommitEdit(); Shapes(original, a); Assert.False(original.IsDirty);
        Shapes(copy, Moved(a)); Assert.True(copy.IsDirty); copy.Undo(); copy.Undo(); Assert.Empty(copy.Shapes); Shapes(original, a);
        original.Undo(); Assert.Empty(original.Shapes); Assert.True(original.IsDirty);
    }
    [Fact] public void ConstructorRejectsInvalidSizeOrScale()
    {
        foreach (var size in new[] { new PixelSize(0, 10), new PixelSize(10, 0), new PixelSize(-1, 10), new PixelSize(10, -1) })
            Assert.Throws<ArgumentOutOfRangeException>(() => new AnnotationDocument(size, 1));
        foreach (var scale in new[] { 0, -1, double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            Assert.Throws<ArgumentOutOfRangeException>(() => new AnnotationDocument(new(10, 10), scale));
        foreach (var scale in new[] { 1.0, 1.5, 2.0 }) Assert.Equal(scale, new AnnotationDocument(new(10, 10), scale).PixelsPerDip);
    }
    [Fact] public void AddDuplicateIdThrowsAndChangesNothing()
    {
        var d = NewDocument(); var a = Rect(); d.Add(a); var count = 0; d.Changed += (_, _) => count++;
        Assert.Throws<InvalidOperationException>(() => d.Add(a)); Assert.Equal(0, count); Shapes(d, a);
        d.Undo(); Assert.Empty(d.Shapes); Assert.False(d.CanUndo);
    }
    [Fact] public void NestedBeginEditThrowsAndKeepsOriginalSession()
    {
        var d = NewDocument(); var a = Rect(); d.Add(a); d.BeginEdit(); d.Replace(Moved(a));
        var count = 0; d.Changed += (_, _) => count++;
        Assert.Throws<InvalidOperationException>(d.BeginEdit); Assert.Equal(0, count); Assert.True(d.IsEditing);
        d.CancelEdit(); Shapes(d, a);
    }
    [Fact] public void MarkCleanWhileEditingThrows()
    {
        var d = NewDocument(); d.Add(Rect()); d.BeginEdit(); var count = 0; d.Changed += (_, _) => count++;
        Assert.Throws<InvalidOperationException>(d.MarkClean); Assert.Equal(0, count); d.CommitEdit(); Assert.True(d.IsDirty);
    }
    [Fact] public void ChangedFiresOncePerStateChangingCall()
    {
        var d = NewDocument(); var a = Rect(); var count = 0; d.Changed += (_, _) => count++;
        d.Add(a); Assert.Equal(1, count); d.Select(a.Id); Assert.Equal(2, count);
        d.Select(a.Id); d.Replace(a); d.Delete(Guid.NewGuid()); Assert.Equal(2, count);
        d.Undo(); Assert.Equal(3, count); d.Undo(); Assert.Equal(3, count);
        d.Redo(); Assert.Equal(4, count); d.BeginEdit(); Assert.Equal(5, count);
        for (var distance = 1; distance <= 3; distance++) { d.Replace(Moved(a, distance)); Assert.Equal(5 + distance, count); }
        d.CommitEdit(); Assert.Equal(9, count); d.CommitEdit(); Assert.Equal(9, count);
        d.MarkClean(); Assert.Equal(10, count); d.MarkClean(); Assert.Equal(10, count);
    }
    [Fact] public void ChangedHandlerSeesFinalState()
    {
        var d = NewDocument(); var a = Rect(); d.Add(a); d.Select(a.Id);
        var calls = 0;
        d.Changed += (sender, _) => { Assert.Same(d, sender); calls++; Assert.Empty(d.Shapes); Assert.Null(d.SelectedShapeId); Assert.True(d.CanUndo); };
        d.Delete(a.Id); Assert.Equal(1, calls);
    }
    [Fact] public void CloneDoesNotCopySubscribersOrShareSession()
    {
        var original = NewDocument(); var a = Rect(); original.Add(a); original.MarkClean(); original.BeginEdit(); original.Replace(Moved(a));
        var calls = 0; original.Changed += (_, _) => calls++; var copy = original.Clone(); copy.CommitEdit();
        Assert.Equal(0, calls); Assert.True(original.IsEditing); Assert.False(copy.IsEditing);
        copy.Undo(); Shapes(copy, a); Shapes(original, Moved(a)); original.CancelEdit(); Shapes(original, a);
        Assert.False(original.IsDirty); Assert.False(copy.IsDirty);
        copy.Add(Rect(300)); copy.MarkClean(); Assert.False(original.IsDirty); original.Undo(); Assert.True(original.IsDirty); Assert.False(copy.IsDirty);
    }
    [Fact] public void ShapesSnapshotIsImmutableHistory()
    {
        var d = NewDocument(); var a = Rect(); d.Add(a); var before = d.Shapes;
        d.Replace(Moved(a)); d.Add(Rect(200)); Assert.Equal(new[] { a }, before);
        d.Undo(); d.Undo(); Assert.Equal(before, d.Shapes);
    }
    [Fact] public void TopmostShapeAtForwardsFractionalScale()
    {
        var d = new AnnotationDocument(new(2000, 1200), 1.5); var a = Rect(); d.Add(a);
        const double tolerance = 4;
        var edge = a.StrokePixels(1.5) / 2 + tolerance;
        var boundary = new PointD(150, 100 + edge);
        Assert.True(a.HitTest(boundary, 1.5, tolerance)); Assert.Equal(a, d.TopmostShapeAt(boundary, tolerance));
        var outside = boundary with { Y = boundary.Y + 0.01 };
        Assert.False(a.HitTest(outside, 1.5, tolerance)); Assert.Null(d.TopmostShapeAt(outside, tolerance));
    }
    [Fact] public void NoOpsAndBlockedHistoryDuringSessionDoNotNotify()
    {
        var d = NewDocument(); var count = 0; d.Changed += (_, _) => count++;
        d.Undo(); d.Redo(); d.CommitEdit(); d.CancelEdit(); d.Select(Guid.NewGuid()); d.Replace(Rect()); d.Delete(Guid.NewGuid()); d.MarkClean();
        Assert.Equal(0, count); var a = Rect(); d.Add(a); d.BeginEdit(); var before = count;
        d.Undo(); d.Redo(); d.Replace(a); Assert.Equal(before, count);
        d.CancelEdit(); Assert.Equal(before + 1, count); Shapes(d, a);
    }
    [Fact] public void CloneCopiesRedoIndependently()
    {
        var original = NewDocument(); var a = Rect(); var b = Rect(200); original.Add(a); original.Add(b); original.Undo();
        var copy = original.Clone(); copy.Redo(); Shapes(copy, a, b); Shapes(original, a); Assert.True(original.CanRedo);
        original.Add(Rect(300)); Assert.False(original.CanRedo); copy.Undo(); copy.Redo(); Shapes(copy, a, b);
    }
}
