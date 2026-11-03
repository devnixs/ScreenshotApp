using Snapper.Core.Annotate;
using Snapper.Core.Annotate.Model;
using Snapper.Core.Geometry;
namespace Snapper.Core.Tests;

public sealed class AnnotationExportPlanTests
{
    [Theory] [InlineData(1, 300, 200)] [InlineData(1.5, 450, 300)] [InlineData(2, 600, 400)]
    public void SizeEqualsSourcePixels(double scale, int width, int height)
    {
        var document = new AnnotationDocument(new(width, height), scale);
        var plan = AnnotationExportPlan.Create(document.ImagePixelSize, width, height);
        Assert.Equal(width, plan.PixelWidth); Assert.Equal(height, plan.PixelHeight);
        Assert.Equal(96, plan.Dpi); Assert.Equal(1, plan.PixelsPerDip);
        Assert.Equal(new RectD(0, 0, width, height), plan.ImageRect); Assert.False(plan.DrawSelectionChrome);
    }
    [Fact] public void IndependentOfMonitorAndSelection()
    {
        var id = Guid.NewGuid();
        var expected = new RectD(20, 10, 100, 50);
        var outputs = new List<AnnotationDocument>();
        foreach (var (zoom, dpi) in new[] { (0.25, 1.0), (1.0, 1.5) })
        {
            var document = new AnnotationDocument(new(200, 100), 1);
            var transform = new CanvasTransform(document.ImagePixelSize, 1, new(200 * zoom, 100 * zoom), dpi);
            var rect = Assert.IsType<RectD>(transform.ImageRect(transform.ViewRect(expected)));
            Assert.Equal(expected, rect);
            document.Add(new(id, new ShapeKind.Rectangle(rect), RgbaColor.Red, StrokeWidth.M));
            document.Select(id); outputs.Add(document);
        }
        Assert.Equal(outputs[0].Shapes, outputs[1].Shapes);
        Assert.Equal(AnnotationExportPlan.Create(outputs[0].ImagePixelSize, 200, 100), AnnotationExportPlan.Create(outputs[1].ImagePixelSize, 200, 100));
    }
    [Fact] public void SizeMismatchThrows() => Assert.Equal(AnnotationExportError.SizeMismatch,
        Assert.Throws<AnnotationExportException>(() => AnnotationExportPlan.Create(new(20, 100), 200, 100)).Error);
    [Theory] [InlineData(0, 100)] [InlineData(-1, 100)] [InlineData(100, 0)] [InlineData(100, -1)]
    public void EmptyImageThrows(int width, int height) => Assert.Equal(AnnotationExportError.EmptyImage,
        Assert.Throws<AnnotationExportException>(() => AnnotationExportPlan.Create(new(width, height), width, height)).Error);
    [Fact] public void EmptyDocumentThrowsBeforeMismatch() => Assert.Equal(AnnotationExportError.EmptyImage,
        Assert.Throws<AnnotationExportException>(() => AnnotationExportPlan.Create(new(0, 100), 200, 100)).Error);
    [Theory]
    [InlineData(AnnotationExportError.SizeMismatch, "The annotation dimensions don't match the captured image.")]
    [InlineData(AnnotationExportError.EmptyImage, "The annotated image couldn't be rendered.")]
    [InlineData(AnnotationExportError.RenderFailed, "The annotated image couldn't be rendered.")]
    public void ErrorMessages(AnnotationExportError kind, string expected) => Assert.Equal(expected, new AnnotationExportException(kind).Message);
    [Fact] public void AsyncCompletionDoesNotCleanNewEdits()
    {
        var document = new AnnotationDocument(new(200, 100), 1);
        var shape = new AnnotationShape(new ShapeKind.Rectangle(new(10, 10, 30, 30)), RgbaColor.Red, StrokeWidth.M);
        document.Add(shape);
        var exported = document.Clone();
        Assert.True(AnnotationExportPlan.CanMarkClean(document, exported, false));
        document.Select(shape.Id);
        Assert.True(AnnotationExportPlan.CanMarkClean(document, exported, false));
        Assert.False(AnnotationExportPlan.CanMarkClean(document, exported, true));
        document.BeginEdit();
        Assert.False(AnnotationExportPlan.CanMarkClean(document, exported, false));
        document.CancelEdit();
        document.Replace(shape.WithColor(RgbaColor.Blue));
        Assert.False(AnnotationExportPlan.CanMarkClean(document, exported, false));
        document.Undo();
        Assert.True(AnnotationExportPlan.CanMarkClean(document, exported, false));
    }
}
