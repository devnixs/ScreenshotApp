using Snapper.Core.Annotate;
using Snapper.Core.Geometry;
namespace Snapper.Core.Tests;
public sealed class AnnotationWindowSizingTests
{
    private static AnnotationWindowPlacement Size(PixelSize image, double ppd, PixelRect workArea, double scale = 1) => AnnotationWindowSizing.Frame(image, ppd, workArea, scale, new(16, 80), new(480, 300));
    [Fact] public void LargeImageCappedAt80PercentOn100Percent()
    { var p = Size(new(3840, 2160), 1, new(0, 0, 1920, 1040)); Assert.Equal(new(1336, 752), p.CanvasDip); Assert.Equal(new(284, 104, 1352, 832), p.WindowRect); }
    [Fact] public void LargeImageOn150PercentMonitor()
    { var p = Size(new(2880, 1620), 1.5, new(0, 0, 2880, 1680), 1.5); Assert.Equal(new(1450, 816), p.CanvasDip); Assert.Equal(new(341, 168, 2199, 1344), p.WindowRect); }
    [Fact] public void SmallImageNotUpscaledOn200Percent()
    { var p = Size(new(400, 300), 2, new(0, 0, 3840, 2160), 2); Assert.Equal(new(480, 300), p.CanvasDip); Assert.Equal(new(1424, 700, 992, 760), p.WindowRect); }
    [Fact] public void ExactNaturalSizeWhenItFits() => Assert.Equal(new(1200, 800), Size(new(1200, 800), 1, new(0, 0, 2560, 1400)).CanvasDip);
    [Fact] public void FallbackMonitorWithDifferentScaleUsesImageDips()
    { var p = Size(new(2880, 1620), 1.5, new(0, 0, 1920, 1040)); Assert.Equal(new(1336, 752), p.CanvasDip); Assert.Equal(new(284, 104, 1352, 832), p.WindowRect); }
    [Fact] public void NegativeOriginMonitor() => Assert.Equal(new(-1636, -196, 1352, 832), Size(new(3840, 2160), 1, new(-1920, -300, 1920, 1040)).WindowRect);
    [Fact] public void PortraitHeightLimited() => Assert.Equal(new(480, 752), Size(new(1000, 3000), 1, new(0, 0, 1920, 1040)).CanvasDip);
    [Fact] public void TinyWorkArea()
    {
        var p = Size(new(5120, 2880), 1, new(0, 0, 640, 400)); Assert.Equal(new(480, 300), p.CanvasDip); Assert.Equal(new(72, 10, 496, 380), p.WindowRect);
        p = Size(new(5120, 2880), 1, new(0, 0, 320, 240)); Assert.Equal(new(304, 160), p.CanvasDip); Assert.Equal(new(0, 0, 320, 240), p.WindowRect);
    }
    [Fact] public void Cascade()
    {
        var frame = new PixelRect(341, 168, 2199, 1344); var wa = new PixelRect(0, 0, 2880, 1680);
        Assert.Equal(frame, AnnotationWindowSizing.Cascaded(frame, [], wa, 36));
        Assert.Equal(frame with { X = 377, Y = 204 }, AnnotationWindowSizing.Cascaded(frame, [new(341, 168)], wa, 36));
        Assert.Equal(frame with { X = 413, Y = 240 }, AnnotationWindowSizing.Cascaded(frame, [new(341, 168), new(377, 204)], wa, 36));
        var edge = new PixelRect(wa.Right - frame.Width, wa.Bottom - frame.Height, frame.Width, frame.Height);
        Assert.Equal(edge, AnnotationWindowSizing.Cascaded(edge, [new(edge.X, edge.Y)], wa, 36));
        frame = new(-1636, -196, 1352, 832); wa = new(-1920, -300, 1920, 1040);
        Assert.Equal(frame with { X = frame.X + 24, Y = frame.Y + 24 }, AnnotationWindowSizing.Cascaded(frame, [new(frame.X, frame.Y)], wa, 24));
    }
}
