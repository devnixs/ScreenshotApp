using Snapper.Core.Recording;
using Snapper.Core.Settings;
namespace Snapper.Core.Tests;
public sealed class RecordingExportPlanTests
{
    [Theory]
    [InlineData(2880,1800,Mp4Quality.High,null)]
    [InlineData(2880,1800,Mp4Quality.Medium,15_552_000)]
    [InlineData(2880,1800,Mp4Quality.Low,7_776_000)]
    [InlineData(1920,1080,Mp4Quality.Medium,6_220_800)]
    [InlineData(1500,750,Mp4Quality.Medium,3_375_000)]
    [InlineData(7680,4320,Mp4Quality.Medium,99_532_800)]
    public void BitratesUseOutputPixels(int w,int h,Mp4Quality q,int? expected) => Assert.Equal(expected,Mp4Bitrate.AverageBitRate(w,h,q));
    [Fact] public void FactorsAndFrameRate()
    {
        Assert.Null(Mp4Bitrate.BitsPerPixelPerFrame(Mp4Quality.High)); Assert.Equal(.10,Mp4Bitrate.BitsPerPixelPerFrame(Mp4Quality.Medium));
        Assert.Equal(.05,Mp4Bitrate.BitsPerPixelPerFrame(Mp4Quality.Low)); Assert.Equal(30,Mp4Bitrate.FramesPerSecond);
    }
    [Theory,InlineData(0,100),InlineData(100,0),InlineData(-1,2)] public void RejectsInvalidDimensions(int w,int h)
    { foreach(var q in Enum.GetValues<Mp4Quality>()) Assert.Throws<ArgumentOutOfRangeException>(()=>Mp4Bitrate.AverageBitRate(w,h,q)); }
    [Fact] public void Mp4PlansIgnoreGifQualityAndScale()
    {
        foreach(var gif in Enum.GetValues<GifQuality>()) foreach(var scale in new[]{1.0,1.5,2.0})
        {
            var high=RecordingExportPlan.Make(RecordingFormat.Mp4,Mp4Quality.High,gif,1000,500,scale);
            Assert.False(high.Converts); Assert.Null(high.Transcode); Assert.Equal(RecordingFormat.Mp4,high.Format);
            Assert.Equal(new RecordingTranscode.Mp4(1_500_000),RecordingExportPlan.Make(RecordingFormat.Mp4,Mp4Quality.Medium,gif,1000,500,scale).Transcode);
            Assert.Equal(new RecordingTranscode.Mp4(750_000),RecordingExportPlan.Make(RecordingFormat.Mp4,Mp4Quality.Low,gif,1000,500,scale).Transcode);
        }
    }
    [Fact] public void PersistedGifTemporarilyUsesMp4Quality()
    {
        var plan=RecordingExportPlan.Make(RecordingFormat.Gif,Mp4Quality.Low,GifQuality.High,1000,500,2);
        Assert.Equal(RecordingFormat.Mp4,plan.Format); Assert.Equal(new RecordingTranscode.Mp4(750_000),plan.Transcode);
    }
    [Fact] public void ExportSettingsAreImmutableSnapshot()
    {
        var settings=new RecordingExportSettings(RecordingFormat.Mp4,Mp4Quality.Medium,GifQuality.Low);
        Assert.Equal(new RecordingExportPlan(RecordingFormat.Mp4,new RecordingTranscode.Mp4(1_500_000)),settings.Plan(1000,500,2));
    }
}
