using Snapper.Core.Geometry;
using static Snapper.Core.Tests.DisplayFixtures;

namespace Snapper.Core.Tests;

public sealed class DisplayInfoTests
{
    [Theory]
    [InlineData(96, 1)] [InlineData(120, 1.25)] [InlineData(144, 1.5)] [InlineData(168, 1.75)] [InlineData(192, 2)]
    public void ScalesAndDipSizes(int dpi, double scale)
    {
        var d = Single(dpi);
        Assert.Equal(scale, d.Scale);
        Assert.Equal(new SizeD(1920, 1080), d.DipSize);
    }
    [Fact] public void MixedMonitorsHaveIndependentDipSizes()
    {
        Assert.Equal(new SizeD(1920, 1080), R.DipSize);
        Assert.Equal(new SizeD(1920, 1080), L.DipSize);
        Assert.Equal(new SizeD(2048, 1152), A.DipSize);
    }
    [Theory] [InlineData(0)] [InlineData(-96)]
    public void RejectsInvalidDpi(int dpi) => Assert.Throws<ArgumentOutOfRangeException>(() => new DisplayInfo("P", P.Bounds, P.WorkArea, dpi, true));
    [Fact] public void RejectsNullName() => Assert.Throws<ArgumentNullException>(() => new DisplayInfo(null!, P.Bounds, P.WorkArea, 96, true));
    [Fact] public void EqualityIncludesAllFields()
    {
        var copy = new DisplayInfo(P.DeviceName, P.Bounds, P.WorkArea, P.Dpi, P.IsPrimary);
        Assert.Equal(P, copy);
        Assert.Equal(2, new HashSet<DisplayInfo> { P, copy, R }.Count);
        Assert.NotEqual(P, P with { WorkArea = P.Bounds });
        Assert.NotEqual(P, P with { IsPrimary = false });
        Assert.NotEqual(P, P with { Dpi = 144 });
        Assert.NotEqual(P, P with { DeviceName = "other" });
        Assert.NotEqual(P, P with { Bounds = R.Bounds });
    }
    [Fact] public void FixturesDoNotOverlap()
    {
        for (var i = 0; i < All.Length; i++)
            for (var j = i + 1; j < All.Length; j++)
                Assert.Null(DisplayGeometry.DisplayOverlapping(RectD.FromPixelRect(All[i].Bounds), [All[j]]));
    }
}
