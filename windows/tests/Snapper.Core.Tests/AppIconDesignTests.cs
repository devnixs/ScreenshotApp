using Snapper.Core.App;
namespace Snapper.Core.Tests;
public sealed class AppIconDesignTests
{
    [Fact] public void SizesAndMetrics()
    {
        Assert.Equal(new[] { 16, 20, 24, 32, 40, 48, 64, 256 }, AppIconDesign.Sizes);
        Assert.Equal(3, AppIconDesign.CornerRadius(16)); Assert.Equal(48, AppIconDesign.CornerRadius(256));
        Assert.Equal(2, AppIconDesign.GlyphInset(16)); Assert.Equal(32, AppIconDesign.GlyphInset(256));
    }
    [Theory] [InlineData(16)] [InlineData(20)] [InlineData(24)] [InlineData(32)] [InlineData(40)] [InlineData(48)] [InlineData(64)] [InlineData(256)]
    public void AllSizesStaySymmetricWithClearCorners(int size)
    {
        var image = AppIconDesign.Render(size); Assert.Equal(size, image.Width); Assert.Equal(size, image.Height);
        foreach (var (x, y) in new[] { (0, 0), (0, size - 1), (size - 1, 0), (size - 1, size - 1) }) Assert.Equal(0u, image.GetPixel(x, y) >> 24);
        Assert.Equal(AppIconDesign.TileArgb, image.GetPixel(size / 2, AppIconDesign.GlyphInset(size) / 2));
        for (var y = 0; y < size; y++)
        for (var x = 0; x < size; x++)
        { Assert.Equal(image.GetPixel(x, y), image.GetPixel(size - 1 - x, y)); Assert.Equal(image.GetPixel(x, y), image.GetPixel(x, size - 1 - y)); }
    }
    [Fact] public void SmallAndLargeGlyphPixelsMatchDesign()
    {
        var small = AppIconDesign.Render(16);
        Assert.Equal(AppIconDesign.GlyphArgb, small.GetPixel(3, 3)); Assert.Equal(AppIconDesign.TileArgb, small.GetPixel(8, 8));
        Assert.Equal(AppIconDesign.TileArgb, small.GetPixel(2, 8)); Assert.Equal(AppIconDesign.TileArgb, small.GetPixel(13, 8));
        var large = AppIconDesign.Render(256);
        Assert.Equal(AppIconDesign.GlyphArgb, large.GetPixel(38, 38)); Assert.Equal(AppIconDesign.TileArgb, large.GetPixel(128, 128));
        var inset = AppIconDesign.GlyphInset(32); var line = TrayGlyphGeometry.Viewfinder(32 - inset * 2).Lines[0];
        Assert.Equal(AppIconDesign.GlyphArgb, AppIconDesign.Render(32).GetPixel((int)(line.X1 + inset), (int)(line.Y1 + inset)));
    }
    [Fact] public void RejectsInvalidSize()
    { Assert.Throws<ArgumentOutOfRangeException>(() => AppIconDesign.Render(0)); Assert.Throws<ArgumentOutOfRangeException>(() => AppIconDesign.Render(257)); }
}
