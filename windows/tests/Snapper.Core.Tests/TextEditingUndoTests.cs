using Snapper.Core.Annotate.Model;
using Snapper.Core.Tests.Fakes;
namespace Snapper.Core.Tests;
public sealed class TextEditingUndoTests
{
    [Fact] public void AddDiscardNoChangeDeleteHistory()
    {
        var d = new AnnotationDocument(new(100, 100), 2);
        d.Apply(new TextCommitDecision.Discard()); d.Apply(new TextCommitDecision.NoChange());
        Assert.False(d.CanUndo || d.IsDirty);
        d.Apply(TextCommitDecision.Decide(TextCommitDecisionTests.Draft()));
        var original = Assert.Single(d.Shapes); Assert.True(d.IsDirty);
        d.Undo(); Assert.Empty(d.Shapes); Assert.False(d.CanUndo);
        d.Redo(); Assert.Equal(original, Assert.Single(d.Shapes));
        d.MarkClean(); d.Apply(new TextCommitDecision.NoChange()); Assert.False(d.IsDirty);
        d.Apply(new TextCommitDecision.Delete(original.Id)); Assert.Empty(d.Shapes);
        d.Undo(); Assert.Equal(original, Assert.Single(d.Shapes)); Assert.False(d.IsDirty);
    }
    [Fact] public void CompleteSessionAndSelectedWidthAreAtomic()
    {
        var d = new AnnotationDocument(new(100, 100), 2); var original = TextCommitDecisionTests.Original();
        d.Add(original); d.MarkClean();
        d.Apply(TextCommitDecision.Decide(TextCommitDecisionTests.Draft(original, "Hello") with { Color = RgbaColor.Blue, StrokeWidth = StrokeWidth.L, MeasuredSize = new(100, 70) }));
        var updated = Assert.Single(d.Shapes);
        d.Undo(); Assert.Equal(original, Assert.Single(d.Shapes)); Assert.False(d.IsDirty);
        d.Redo(); Assert.Equal(updated, Assert.Single(d.Shapes));
        d.Select(updated.Id); d.Replace(updated.WithStrokeWidth(StrokeWidth.S, d.PixelsPerDip, new FakeTextMeasurer()));
        Assert.Equal(new Snapper.Core.Geometry.SizeD(160, 32), Assert.IsType<ShapeKind.Text>(d.Shapes[0].Kind).Box.MeasuredSize);
        d.Undo(); Assert.Equal(updated, Assert.Single(d.Shapes)); d.Undo(); Assert.Equal(original, Assert.Single(d.Shapes));
    }
    [Fact] public void NormalizedReeditDoesNotDirtyOrCreateHistory()
    {
        var d = new AnnotationDocument(new(100, 100), 1); var original = TextCommitDecisionTests.Original().WithText("A\nB");
        d.Add(original); d.MarkClean();
        d.Apply(TextCommitDecision.Decide(TextCommitDecisionTests.Draft(original, EditorNewlines.ToModel(EditorNewlines.ToEditor("A\nB")))));
        Assert.False(d.IsDirty); d.Undo(); Assert.Empty(d.Shapes);
    }
}
