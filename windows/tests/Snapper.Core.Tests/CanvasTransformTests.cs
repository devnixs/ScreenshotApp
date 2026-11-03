using Snapper.Core.Annotate;
using Snapper.Core.Geometry;
namespace Snapper.Core.Tests;
public sealed class CanvasTransformTests
{
    [Fact] public void ExactFitAndRoundTrips()
    {
        var t = new CanvasTransform(new(400, 300), 1, new(400, 300), 1);
        Assert.Equal(1, t.Scale); Assert.Equal(new(0, 0, 400, 300), t.ImageRectInView); Assert.True(t.IsPixelExact);
        foreach (var p in new PointD[] { new(0, 0), new(400, 300), new(200, 150) }) Assert.Equal(p, t.ViewPoint(t.ImagePoint(p)!.Value));
    }
    [Fact] public void LetterboxesAndClampsBothAxes()
    {
        var t = new CanvasTransform(new(1600, 900), 2, new(400, 400), 2);
        Assert.Equal(.25, t.Scale); Assert.Equal(new(0, 87.5, 400, 225), t.ImageRectInView);
        Assert.Equal(new PointD(800, 450), t.ImagePoint(new(200, 200))); Assert.Equal(new PointD(0, 0), t.ImagePoint(new(0, 87.5)));
        Assert.True(t.IsInsideImage(new(200, 200))); Assert.False(t.IsInsideImage(new(0, 0)));
        Assert.Equal(new PointD(0, 0), t.ClampedImagePoint(new(-20, -20))); Assert.Equal(new PointD(1600, 900), t.ClampedImagePoint(new(500, 500)));
        var r = new RectD(50, 60, 300, 100); Assert.Equal(r, t.ImageRect(t.ViewRect(r))); Assert.Equal(2, t.ViewLength(8));
    }
    [Fact] public void PortraitLetterbox() => Assert.Equal(new(225, 0, 150, 300), new CanvasTransform(new(600, 1200), 1, new(600, 300), 1).ImageRectInView);
    [Theory] [InlineData(1)] [InlineData(2)] public void NeverUpscales(double ppd)
    {
        var t = new CanvasTransform(new(200, 100), ppd, new(800, 800), 1);
        Assert.Equal(1 / ppd, t.Scale); Assert.Equal(new(400, 400), t.ImageRectInView.Center);
        if (ppd == 2) Assert.Equal(new(350, 375, 100, 50), t.ImageRectInView);
    }
    [Fact] public void OffsetSnapsToPhysicalPixels()
    {
        var t = new CanvasTransform(new(300, 150), 1.5, new(801, 601), 1.5);
        Assert.Equal(1 / 1.5, t.Scale); Assert.Equal(451 / 1.5, t.ImageRectInView.X); Assert.Equal(376 / 1.5, t.ImageRectInView.Y); Assert.True(t.IsPixelExact);
    }
    [Fact] public void SameCaptureOnLowerDpiMonitorIsNotPixelExact()
    {
        var t = new CanvasTransform(new(300, 150), 1.5, new(801, 601), 1);
        Assert.Equal(1 / 1.5, t.Scale); Assert.Equal(new(301, 251, 200, 100), t.ImageRectInView); Assert.False(t.IsPixelExact);
    }
    [Fact] public void HundredPercentCaptureOn200PercentMonitor()
    { var t = new CanvasTransform(new(200, 100), 1, new(800, 800), 2); Assert.Equal(1, t.Scale); Assert.False(t.IsPixelExact); }
    [Fact] public void InvalidInputsAreSafe()
    {
        var transforms = new List<CanvasTransform>();
        foreach (var size in new PixelSize[] { new(0, 0), new(-1, 100) }) transforms.Add(new(size, 1, new(100, 100), 1));
        foreach (var size in new SizeD[] { new(0, 0), new(-1, 100), new(double.PositiveInfinity, 100) }) transforms.Add(new(new(100, 100), 1, size, 1));
        foreach (var bad in new[] { 0, -1, double.NaN, double.PositiveInfinity })
        { transforms.Add(new(new(100, 100), bad, new(100, 100), 1)); transforms.Add(new(new(100, 100), 1, new(100, 100), bad)); }
        foreach (var t in transforms)
        {
            Assert.Equal(0, t.Scale); Assert.Equal(default, t.ImageRectInView); Assert.Null(t.ImagePoint(new(0, 0))); Assert.Null(t.ClampedImagePoint(new(0, 0)));
            Assert.Null(t.ImageRect(new(0, 0, 0, 0))); Assert.False(t.IsInsideImage(new(0, 0))); Assert.False(t.IsPixelExact);
        }
    }
}
