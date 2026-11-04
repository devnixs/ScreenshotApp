using Snapper.Core.Geometry;
using Snapper.Core.Recording;
namespace Snapper.Core.Tests;

public sealed class RecordingConfigurationBuilderTests
{
    private static DisplayInfo Display(PixelRect bounds, int dpi = 96) => new("display", bounds, bounds, dpi, true);
    [Fact] public void NativePixelRectangleAndCaptureConstants()
    {
        var c = RecordingConfigurationBuilder.Build(Display(new(0, 0, 1920, 1080)), new(100, 100, 800, 600));
        Assert.Equal(new PixelRect(100, 100, 800, 600), c.SourceRect);
        Assert.Equal(800, c.PixelWidth); Assert.Equal(600, c.PixelHeight); Assert.Equal("display", c.DisplayId);
        Assert.Equal(30, c.FramesPerSecond); Assert.Equal(10_000_000.0 / 30, c.FrameIntervalTicks);
        Assert.True(c.ShowsCursor); Assert.Equal(".mp4", RecordingConfiguration.FileExtension); Assert.Equal("H.264", RecordingConfiguration.VideoCodec);
        Assert.Equal(1, c.Scale); Assert.False(c.FollowsWindow); Assert.False(c.ScalesToFit);
    }
    public static TheoryData<PixelRect, int, RectD, PixelRect> Rectangles => new()
    {
        { new(0,0,1920,1080), 96, new(10,10,101,51), new(10,10,100,50) },
        { new(1920,0,2880,1620), 144, new(2000.75,100.5,301.5,151.5), new(81,101,300,150) },
        { new(1920,0,2880,1620), 144, new(1920,0,2880,1620), new(0,0,2880,1620) },
        { new(-3840,-200,3840,2160), 192, new(-3800,-150,1000,501), new(40,50,1000,500) },
        { new(0,0,3840,2160), 192, new(0,0,3840,2160), new(0,0,3840,2160) },
        { new(0,-1080,1920,1080), 96, new(0,-1080,1920,1080), new(0,0,1920,1080) },
        { new(0,0,1920,1080), 96, new(1800,-20,400,200), new(1800,0,120,180) },
        { new(-100,200,101,51), 96, new(-100,200,101,51), new(0,0,100,50) },
        { new(0,0,1920,1080), 96, new(1919.9,1079.9,0.1,0.1), new(1918,1078,2,2) }
    };
    [Theory, MemberData(nameof(Rectangles))] public void SnapsAndEvenFloorsWithoutScaling(PixelRect bounds, int dpi, RectD selection, PixelRect expected)
    {
        var c = RecordingConfigurationBuilder.Build(Display(bounds, dpi), selection);
        Assert.Equal(expected, c.SourceRect); Assert.Equal(expected.Width, c.PixelWidth); Assert.Equal(expected.Height, c.PixelHeight);
        Assert.Equal(dpi / 96.0, c.Scale); Assert.False(c.FollowsWindow); Assert.False(c.ScalesToFit);
    }
    [Fact] public void OverridesAndFrameBounds()
    {
        var c = RecordingConfigurationBuilder.Build(Display(new(0,0,1920,1080)), new(1800,0,120,180), false, 60);
        Assert.False(c.ShowsCursor); Assert.Equal(10_000_000.0 / 60, c.FrameIntervalTicks);
        Assert.Equal(c.SourceRect, c.SourceBoxForFrame(1920,1080)); Assert.Null(c.SourceBoxForFrame(1280,720));
        Assert.Null(c.SourceBoxForFrame(1920,100));
    }
    [Fact] public void HighBitrateAndFloor()
    {
        Assert.Equal(0.20, RecordingConfigurationBuilder.HighBitsPerPixel);
        Assert.True(RecordingConfigurationBuilder.HighBitsPerPixel > 0.10);
        Assert.Equal(12_441_600, RecordingConfigurationBuilder.Build(Display(new(0,0,1920,1080)), new(0,0,1920,1080)).VideoBitrate);
        Assert.Equal(1_000_000, RecordingConfigurationBuilder.Build(Display(new(0,0,1920,1080)), new(0,0,2,2)).VideoBitrate);
    }
    [Fact] public void ReproducingStartupRectangleKeepsNativeCropAndBitrate()
    {
        // Native WriteSample regression: 800x600 BGRA on the diagnostic 1920x1080/96-DPI display.
        var c = RecordingConfigurationBuilder.Build(Display(new(0, 0, 1920, 1080)), new(100, 100, 800, 600));
        Assert.Equal(new PixelRect(100, 100, 800, 600), c.SourceRect);
        Assert.Equal(800, c.PixelWidth); Assert.Equal(600, c.PixelHeight);
        Assert.Equal(30, c.FramesPerSecond); Assert.Equal(2_880_000, c.VideoBitrate);
    }
    [Fact] public void RejectsImpossibleConfiguration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RecordingConfigurationBuilder.Build(Display(new(0,0,100,100)), new(0,0,10,10), framesPerSecond: 0));
        Assert.Throws<ArgumentException>(() => RecordingConfigurationBuilder.Build(Display(new(0,0,1,100)), new(0,0,1,10)));
    }
}
