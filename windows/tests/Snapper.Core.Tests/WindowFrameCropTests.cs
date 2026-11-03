using Snapper.Core.Geometry;
using Snapper.Core.Recording;
namespace Snapper.Core.Tests;
public sealed class WindowFrameCropTests
{
    public static TheoryData<PixelSize,PixelRect,PixelRect,PixelRect> Cases=>new()
    {
        {new(800,600),new(93,100,814,607),new(100,100,800,600),new(0,0,800,600)},
        {new(814,607),new(93,100,814,607),new(100,100,800,600),new(7,0,800,600)},
        {new(2902,1642),new(-2891,-11,2902,1642),new(-2880,0,2880,1620),new(11,11,2880,1620)},
        {new(700,500),new(93,100,814,607),new(100,100,800,600),new(7,0,693,500)},
        {new(5,5),new(0,0,20,20),new(7,7,10,10),new(0,0,5,5)}
    };
    [Theory,MemberData(nameof(Cases))] public void ReusesScreenshotCropAndFallsBackForInconsistentBounds(PixelSize content,PixelRect raw,PixelRect extended,PixelRect expected)=>
        Assert.Equal(expected,WindowFrameCrop.SourceRect(content,raw,extended));
}
