using Snapper.Core.Support.Imaging;
namespace Snapper.Core.Tests;
public sealed class CoverageRasterizerTests
{
    [Fact] public void FullAndHalfRectangles()
    {
        Assert.All(CoverageRasterizer.Coverage(8, 8, CoverageRasterizer.RoundedRect(0, 0, 8, 8, 0)), c => Assert.Equal(1, c));
        Assert.Equal(new[] { 0.5, 0 }, CoverageRasterizer.Coverage(2, 1, CoverageRasterizer.RoundedRect(0, 0, 0.5, 1, 0)));
    }
    [Fact] public void SquareCapsAndClearRows()
    {
        var c = CoverageRasterizer.Coverage(8, 4, CoverageRasterizer.Segment(1, 1.5, 5, 1.5, 1));
        for (var x = 1; x <= 4; x++) Assert.Equal(1, c[8 + x]);
        Assert.Equal(0.5, c[8]); Assert.Equal(0.5, c[13]);
        Assert.All(c[..8], v => Assert.Equal(0, v)); Assert.All(c[16..24], v => Assert.Equal(0, v));
    }
    [Fact] public void DiagonalAndZeroLengthSegmentsHaveSquareCaps()
    {
        var diagonal = CoverageRasterizer.Segment(1, 1, 3, 3, 1);
        Assert.True(diagonal(0.75, 0.75)); Assert.True(diagonal(3.25, 3.25)); Assert.False(diagonal(1, 3));
        Assert.True(CoverageRasterizer.Segment(2, 2, 2, 2, 2)(1.1, 1.1));
    }
    [Fact] public void RingsDiscsAndUnion()
    {
        var disc = CoverageRasterizer.Coverage(8, 8, CoverageRasterizer.Disc(4, 4, 3));
        Assert.Equal(1, disc[36]); Assert.Equal(0, disc[0]);
        var ring = CoverageRasterizer.Coverage(8, 8, CoverageRasterizer.Ring(4, 4, 3, 1));
        Assert.Equal(0, ring[36]); Assert.True(ring[12] > 0);
        var rect = CoverageRasterizer.RoundedRect(0, 0, 2, 2, 0);
        Assert.All(CoverageRasterizer.Coverage(2, 2, CoverageRasterizer.Union(rect, rect)), c => Assert.Equal(1, c));
    }
    [Fact] public void StraightAlphaSourceOver()
    {
        var image = new RgbaImage(1, 1);
        CoverageRasterizer.Paint(image, [1], 0xFF0F6CBD);
        CoverageRasterizer.Paint(image, [0.5], uint.MaxValue);
        Assert.Equal(0xFF87B6DEu, image.GetPixel(0, 0));
        var transparent = new RgbaImage(1, 1);
        CoverageRasterizer.Paint(transparent, [0.25], uint.MaxValue);
        Assert.Equal(0x40FFFFFFu, transparent.GetPixel(0, 0));
    }
    [Fact] public void RejectsMismatchedCoverage() => Assert.Throws<ArgumentException>(() => CoverageRasterizer.Paint(new(1, 1), [], 0));
}
