using Snapper.Core.Geometry;
using Snapper.Core.Recording;
namespace Snapper.Core.Tests;

public sealed class LetterboxFitTests
{
    [Theory]
    [InlineData(800,600,800,600,0,0,800,600,true)]
    [InlineData(1600,600,800,600,0,150,800,300,false)]
    [InlineData(400,600,800,600,200,0,400,600,false)]
    [InlineData(1200,900,800,600,0,0,800,600,false)]
    [InlineData(1600,1200,800,600,0,0,800,600,false)]
    [InlineData(400,300,800,600,0,0,800,600,false)]
    [InlineData(1201,900,1600,1200,0,0,1600,1199,false)]
    [InlineData(1,1000,800,600,399,0,1,600,false)]
    public void FitsAndCentersWithoutExceedingOutput(int sw,int sh,int ow,int oh,int x,int y,int w,int h,bool identity) =>
        Assert.Equal(new LetterboxPlacement(new(x,y,w,h),identity),LetterboxFit.Fit(new(sw,sh),new(ow,oh)));
    [Theory, InlineData(0,600,800,600), InlineData(800,0,800,600), InlineData(800,600,0,600), InlineData(800,600,800,0), InlineData(-1,600,800,600)]
    public void NonpositiveDimensionsDropFrame(int sw,int sh,int ow,int oh) => Assert.Null(LetterboxFit.Fit(new(sw,sh),new(ow,oh)));
}
