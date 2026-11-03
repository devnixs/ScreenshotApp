using Snapper.Core.Geometry;
using Snapper.Core.Recording;
namespace Snapper.Core.Tests;
public sealed class WindowRecordingConfigurationTests
{
    [Theory,InlineData(800,600,1,800,600),InlineData(1200,900,1.5,1200,900),InlineData(1600,1200,2,1600,1200),InlineData(801,601,1,800,600),InlineData(1,1,2,2,2),InlineData(10240,5760,1,10240,5760)]
    public void FixedOutputUsesStartingPhysicalPixels(int w,int h,double scale,int ew,int eh)
    {
        var c=RecordingConfigurationBuilder.BuildWindow(new(-1500,200,w,h),scale);
        Assert.Equal(ew,c.PixelWidth);Assert.Equal(eh,c.PixelHeight);Assert.Equal(scale,c.Scale);
        Assert.True(c.FollowsWindow);Assert.True(c.ScalesToFit);Assert.True(c.PreservesAspectRatio);Assert.True(c.ShowsCursor);Assert.Equal(30,c.FramesPerSecond);
        Assert.Equal(new PixelRect(0,0,w,h),c.SourceRect);
    }
    [Fact] public void RectangleFlagsRemainUnchanged()
    {
        var display=new DisplayInfo("P",new(0,0,1920,1080),new(0,0,1920,1040),96,true);
        var c=RecordingConfigurationBuilder.Build(display,new(100,100,800,600));
        Assert.False(c.FollowsWindow);Assert.False(c.ScalesToFit);Assert.False(c.PreservesAspectRatio);
    }
}
