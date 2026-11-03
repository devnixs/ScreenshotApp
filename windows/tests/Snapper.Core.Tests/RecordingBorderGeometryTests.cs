using Snapper.Core.Geometry;
using Snapper.Core.Recording;
namespace Snapper.Core.Tests;

public sealed class RecordingBorderGeometryTests
{
    public static TheoryData<PixelRect, PixelRect, double, bool, PixelRect> Frames => new()
    {
        { new(400,300,600,400),new(0,0,1920,1080),1,false,new(397,297,606,406) },
        { new(0,0,1920,1080),new(0,0,1920,1080),1,false,new(0,0,1920,1080) },
        { new(600,450,900,600),new(0,0,2880,1620),1.5,false,new(595,445,910,610) },
        { new(800,600,1200,800),new(0,0,3840,2160),2,false,new(794,594,1212,812) },
        { new(-2880,0,750,750),new(-2880,-300,2880,1620),1.5,false,new(-2880,-5,755,760) },
        { new(100,-1350,500,300),new(0,-1350,2400,1350),1.25,false,new(96,-1350,508,304) },
        { new(-700,100,1000,600),new(-1000,0,1000,900),1,true,new(-703,97,1006,606) }
    };
    [Theory, MemberData(nameof(Frames))]
    public void OutsetsAndClipsUnlessFollowingWindow(PixelRect recorded, PixelRect monitor, double scale, bool follows, PixelRect expected) =>
        Assert.Equal(expected, RecordingBorderGeometry.WindowFrame(recorded,monitor,scale,follows));
    [Theory, InlineData(1,3), InlineData(1.25,4), InlineData(1.5,5), InlineData(1.75,6), InlineData(2,6)]
    public void CeilingOutsetKeepsUnderlayOutsideCapture(double scale, int expected)
    {
        var outset = RecordingBorderGeometry.OutsetPixels(scale);
        Assert.Equal(expected,outset);
        Assert.True(DisplayGeometry.PixelsToDips(outset,scale) >= RecordingBorderGeometry.StrokeInsetDips + RecordingBorderGeometry.UnderlayWidthDips / 2);
    }
    [Fact] public void StrokeRectIsInsetInDips() => Assert.Equal(new RectD(1.5,1.5,603,403), RecordingBorderGeometry.StrokeRect(new(606,406)));
    [Fact] public void StyleMatchesReference()
    {
        Assert.Equal(2,RecordingBorderGeometry.UnderlayWidthDips);
        Assert.Equal(1,RecordingBorderGeometry.DashWidthDips);
        Assert.Equal(4,RecordingBorderGeometry.DashLengthDips);
        Assert.Equal(4,RecordingBorderGeometry.DashGapDips);
    }
}
