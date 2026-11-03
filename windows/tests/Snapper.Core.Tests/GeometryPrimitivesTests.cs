using Snapper.Core.Geometry;

namespace Snapper.Core.Tests;

public sealed class GeometryPrimitivesTests
{
    [Fact] public void RectEdgesAndCenter()
    {
        var r = new RectD(10, 20, 30, 40);
        Assert.Equal(10, r.Left); Assert.Equal(20, r.Top);
        Assert.Equal(40, r.Right); Assert.Equal(60, r.Bottom);
        Assert.Equal(new PointD(25, 40), r.Center);
        Assert.Equal(new SizeD(30, 40), r.Size);
    }
    [Fact] public void StandardizesNegativeSizes()
    {
        var r = new RectD(10, 20, 30, 40);
        Assert.Equal(r, new RectD(40, 60, -30, -40).Standardized());
        Assert.Equal(r, r.Standardized());
        Assert.Equal(new RectD(10, 20, 0, 0), new RectD(10, 20, 0, 0).Standardized());
    }
    [Fact] public void RectFactoriesPreserveSpace()
    {
        Assert.Equal(new RectD(10, 20, 30, 40), RectD.FromEdges(10, 20, 40, 60));
        Assert.Equal(new RectD(40, 60, -30, -40), RectD.FromEdges(40, 60, 10, 20));
        Assert.Equal(new RectD(-5, 3, 7, 9), RectD.FromPixelRect(new(-5, 3, 7, 9)));
    }
    [Fact] public void PixelEdgesAreExclusive()
    {
        var r = new PixelRect(-3840, -540, 3840, 2160);
        Assert.Equal(-3840, r.Left); Assert.Equal(-540, r.Top);
        Assert.Equal(0, r.Right); Assert.Equal(1620, r.Bottom);
        Assert.Equal(new PixelSize(3840, 2160), r.Size); Assert.False(r.IsEmpty);
        Assert.Equal(new PixelPoint(-5, 3), new PixelPoint(-5, 3));
    }
    [Fact] public void PixelFactoryNormalizesAndDetectsEmpty()
    {
        Assert.Equal(new PixelRect(5, 8, 5, 12), PixelRect.FromEdges(10, 20, 5, 8));
        Assert.True(new PixelRect(0, 0, 0, 10).IsEmpty);
        Assert.True(new PixelRect(0, 0, 10, 0).IsEmpty);
        Assert.True(new PixelRect(0, 0, -1, 2).IsEmpty);
    }
}
