using Snapper.Core.Annotate.Model;
using Snapper.Core.Geometry;

namespace Snapper.Core.Tests;

public sealed class TextMetricsTests
{
    [Theory] [InlineData(1)] [InlineData(1.5)] [InlineData(2)]
    public void SizesUseCaptureScaleAndCanvasZoom(double scale)
    {
        foreach (var (width, points) in new[] { (StrokeWidth.S, 16), (StrokeWidth.M, 24), (StrokeWidth.L, 36) })
            Assert.Equal(points * scale, TextMetrics.FontPixelSize(width, scale));
        Assert.Equal(24, TextMetrics.EditorFontSize(48, 0.5));
        Assert.Equal(48, TextMetrics.EditorFontSize(48, 1));
        Assert.Equal(0.48, TextMetrics.EditorFontSize(48, 0.01));
    }

    [Fact] public void PlacementClampsTopBottomAndShortImages()
    {
        static PointD Origin(double x, double y) => TextMetrics.NewBoxOrigin(new(x, y), 30, new(100, 80));
        Assert.Equal(new(50, 25), Origin(50, 40)); Assert.Equal(new(99, 0), Origin(99, 2)); Assert.Equal(new(99, 50), Origin(99, 80));
        Assert.True(Origin(100, 80).X < 100); Assert.Equal(new(0, 0), Origin(-1, -10));
        Assert.Equal(0, TextMetrics.NewBoxOrigin(new(10, 5), 30, new(20, 10)).Y);
    }
}
