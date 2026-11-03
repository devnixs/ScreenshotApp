using Snapper.Core.Annotate.Model;
using Snapper.Core.Geometry;
namespace Snapper.Core.Tests;
public sealed class TextCommitDecisionTests
{
    internal static AnnotationShape Original() => new(new ShapeKind.Text(new(new(10, 20), "Hi", new(30, 28))), RgbaColor.Red, StrokeWidth.M);
    internal static TextEditDraft Draft(AnnotationShape? original = null, string text = "Hi") => new(original, text, new(10, 20), RgbaColor.Red, StrokeWidth.M, new(30, 28));
    [Theory][InlineData("")][InlineData("  \n\t")][InlineData("\n")][InlineData("\r\n")]
    public void NewWhitespaceDiscards(string text) => Assert.IsType<TextCommitDecision.Discard>(TextCommitDecision.Decide(Draft(text: text)));
    [Fact] public void AddPreservesTextAndOrigin()
    {
        var a = Assert.IsType<TextCommitDecision.Add>(TextCommitDecision.Decide(Draft(text: " Hi\n")));
        var box = Assert.IsType<ShapeKind.Text>(a.Shape.Kind).Box;
        Assert.Equal(" Hi\n", box.Text); Assert.Equal(new PointD(10, 20), box.Origin); Assert.NotEqual(Guid.Empty, a.Shape.Id);
    }
    [Theory][InlineData("")][InlineData("\n")]
    public void ExistingEmptyDeletes(string text)
    { var original = Original(); Assert.Equal(new TextCommitDecision.Delete(original.Id), TextCommitDecision.Decide(Draft(original, text))); }
    [Fact] public void UnchangedIgnoresRemeasureAndOrigin()
    {
        Assert.IsType<TextCommitDecision.NoChange>(TextCommitDecision.Decide(Draft(Original()) with { MeasuredSize = new(60, 42), Origin = new(50, 90) }));
    }
    [Fact] public void UpdatesPreserveIdAndDraftMetrics()
    {
        var original = Original(); var draft = Draft(original);
        foreach (var change in new[] { draft with { Color = RgbaColor.Blue }, draft with { StrokeWidth = StrokeWidth.L, MeasuredSize = new(80, 50) }, draft with { Text = "Hello" } })
        {
            var updated = Assert.IsType<TextCommitDecision.Update>(TextCommitDecision.Decide(change)).Shape;
            Assert.Equal(original.Id, updated.Id); var box = Assert.IsType<ShapeKind.Text>(updated.Kind).Box;
            Assert.Equal(change.Text, box.Text); Assert.Equal(change.MeasuredSize, box.MeasuredSize);
            Assert.Equal(change.Color, updated.Color); Assert.Equal(change.StrokeWidth, updated.StrokeWidth);
        }
    }
}
