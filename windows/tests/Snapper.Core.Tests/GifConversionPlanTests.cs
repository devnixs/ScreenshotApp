using Snapper.Core.Recording;
using Snapper.Core.Settings;
namespace Snapper.Core.Tests;
public sealed class GifConversionPlanTests
{
    [Theory]
    [InlineData(GifQuality.High,15,false,null)]
    [InlineData(GifQuality.Medium,10,true,null)]
    [InlineData(GifQuality.Low,8,true,640)]
    public void Presets(GifQuality quality,int fps,bool dips,int? cap) =>
        Assert.Equal(new(fps,dips,cap),GifEncodingSettings.ForQuality(quality));
    [Theory]
    [InlineData(GifQuality.High,2880,1800,2,2880,1800)]
    [InlineData(GifQuality.High,2880,1620,1.5,2880,1620)]
    [InlineData(GifQuality.Medium,2880,1800,2,1440,900)]
    [InlineData(GifQuality.Medium,1000,500,1,1000,500)]
    [InlineData(GifQuality.Medium,1001,501,2,501,251)]
    [InlineData(GifQuality.Medium,1500,900,1.5,1000,600)]
    [InlineData(GifQuality.Medium,1001,501,1.25,801,401)]
    [InlineData(GifQuality.Medium,1750,700,1.75,1000,400)]
    [InlineData(GifQuality.Low,2880,1800,2,640,400)]
    [InlineData(GifQuality.Low,1000,500,1,640,320)]
    [InlineData(GifQuality.Low,800,1600,1,320,640)]
    [InlineData(GifQuality.Low,300,200,2,150,100)]
    [InlineData(GifQuality.Low,3840,2160,1.5,640,360)]
    [InlineData(GifQuality.Low,8,8,2,4,4)]
    [InlineData(GifQuality.Medium,2,2,2,1,1)]
    [InlineData(GifQuality.Medium,1,1,1.5,1,1)]
    public void OutputSizes(GifQuality q,int w,int h,double scale,int ew,int eh) =>
        Assert.Equal((ew,eh),GifEncodingSettings.ForQuality(q).OutputPixelSize(w,h,scale));
    [Theory,InlineData(1),InlineData(.75),InlineData(0),InlineData(double.NaN),InlineData(double.PositiveInfinity)]
    public void InvalidOrSmallScaleNeverUpscales(double scale) =>
        Assert.Equal((1000,500),GifEncodingSettings.ForQuality(GifQuality.Medium).OutputPixelSize(1000,500,scale));
    [Theory,InlineData(0,10),InlineData(10,0),InlineData(-1,1)]
    public void InvalidSourceThrows(int w,int h) => Assert.Throws<ArgumentOutOfRangeException>(()=>GifConversionPlan.Make(GifQuality.High,w,h,1));
    [Fact] public void PlanCombinesSettingsAndSize() => Assert.Equal(new(GifEncodingSettings.ForQuality(GifQuality.Medium),1440,900),GifConversionPlan.Make(GifQuality.Medium,2880,1800,2));
}
