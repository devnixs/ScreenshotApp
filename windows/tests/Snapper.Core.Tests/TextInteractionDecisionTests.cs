using Snapper.Core.Annotate;
using Snapper.Core.Annotate.Model;
using Snapper.Core.Geometry;
namespace Snapper.Core.Tests;
public sealed class TextInteractionDecisionTests
{
    [Fact] public void TextReentryTopmostDrawingAndCommitRouting()
    {
        var d = new AnnotationDocument(new(100, 100), 2); var first = TextCommitDecisionTests.Original();
        var second = first with { Id = Guid.NewGuid(), Color = RgbaColor.Blue };
        d.Add(first); d.Add(second);
        TextInteractionDecision Decide(AnnotationTool tool, int clicks = 1, bool editing = false, PointD? point = null) =>
            TextInteractionDecision.Decide(editing, tool, clicks, point, d);
        foreach (var clicks in new[] { 1, 2 }) Assert.Equal(new TextInteractionDecision.Edit(second.Id), Decide(AnnotationTool.Text, clicks, point: new(20, 30)));
        Assert.Equal(new TextInteractionDecision.Edit(second.Id), Decide(AnnotationTool.Select, 2, point: new(20, 30)));
        foreach (var tool in new[] { AnnotationTool.Rectangle, AnnotationTool.Arrow, AnnotationTool.Select })
            Assert.IsType<TextInteractionDecision.None>(Decide(tool, point: new(20, 30)));
        foreach (var tool in new[] { AnnotationTool.Rectangle, AnnotationTool.Arrow })
            Assert.IsType<TextInteractionDecision.None>(Decide(tool, 2, point: new(20, 30)));
        Assert.Equal(new TextInteractionDecision.BeginNew(new(90, 90)), Decide(AnnotationTool.Text, point: new(90, 90)));
        Assert.IsType<TextInteractionDecision.None>(Decide(AnnotationTool.Text));
        Assert.IsType<TextInteractionDecision.Commit>(Decide(AnnotationTool.Text, editing: true));
        Assert.IsType<TextInteractionDecision.Commit>(Decide(AnnotationTool.Rectangle, editing: true, point: new(20, 30)));
    }
    [Fact] public void HitTestingIsTextOnlyWithoutSlop()
    {
        var d = new AnnotationDocument(new(100, 100), 1); var text = TextCommitDecisionTests.Original(); d.Add(text);
        d.Add(new(new ShapeKind.Rectangle(new(0, 0, 100, 100)), RgbaColor.Red, StrokeWidth.M));
        Assert.Equal(new TextInteractionDecision.Edit(text.Id), TextInteractionDecision.Decide(false, AnnotationTool.Text, 1, new(10, 20), d));
        Assert.IsType<TextInteractionDecision.BeginNew>(TextInteractionDecision.Decide(false, AnnotationTool.Text, 1, new(9.99, 20), d));
    }
}
