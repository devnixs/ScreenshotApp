using Snapper.Core.Annotate;
using Snapper.Core.Annotate.Model;
using Snapper.Core.Geometry;
namespace Snapper.Core.Tests;
public sealed class TextEditorLayoutTests
{
    [Theory][InlineData(1, 1, 100, 100)][InlineData(.5, 2, 180, 75)][InlineData(.37, 1, 117, 37)]
    public void LayerUsesCanvasTransform(double expectedScale, double ppp, double width, double height)
    {
        var t = new CanvasTransform(new(100, 100), ppp, new(width, height), 2);
        var (scale, x, y) = TextEditorLayout.LayerTransform(t);
        Assert.Equal(expectedScale, scale, 9); Assert.Equal(t.ImageRectInView.X, x); Assert.Equal(t.ImageRectInView.Y, y);
    }
    [Fact] public void InsetPlacementAndRoundTrip()
    {
        Assert.Equal(new PointD(98, 50), TextEditorLayout.TextBoxPosition(new(100, 50), new(2, 0)));
        Assert.Equal(new PointD(32, 10), TextEditorLayout.EditorLocalPoint(new(130, 60), new(100, 50), new(2, 0)));
        var t = new CanvasTransform(new(200, 100), 2, new(180, 75), 2);
        Assert.Equal(.5, t.Scale); Assert.Equal(new RectD(40, 12.5, 100, 50), t.ImageRectInView);
        var image = t.ImagePoint(t.ViewPoint(new(130, 60)));
        Assert.Equal(new PointD(32, 10), TextEditorLayout.EditorLocalPoint(Assert.IsType<PointD>(image), new(100, 50), new(2, 0)));
    }
    [Theory][InlineData(1, 1, 24)][InlineData(1, .5, 12)][InlineData(1.5, 1, 36)][InlineData(1.5, .5, 18)][InlineData(2, 1, 48)][InlineData(2, .5, 24)]
    public void FontScaleDependsOnCaptureAndCanvas(double ppp, double scale, double expected) => Assert.Equal(expected, TextMetrics.EditorFontSize(TextMetrics.FontPixelSize(StrokeWidth.M, ppp), scale));
}
