using Snapper.Core.Capture;
using Snapper.Core.Geometry;
namespace Snapper.Core.Tests;
public sealed class WindowCaptureCropTests
{
    public static TheoryData<PixelRect,PixelRect,PixelSize,PixelRect?> Cases => new()
    {
        {new(100,100,1014,607),new(107,100,1000,600),new(1014,607),new(7,0,1000,600)},
        {new(-8,-8,1936,1056),new(0,0,1920,1040),new(1936,1056),new(8,8,1920,1040)},
        {new(100,100,1014,607),new(107,100,1000,600),new(1000,600),new(0,0,1000,600)},
        {new(100,100,800,600),new(100,100,800,600),new(800,600),new(0,0,800,600)},
        {new(-2567,300,1214,907),new(-2560,300,1200,900),new(1214,907),new(7,0,1200,900)},
        {new(1906,-540,2028,1214),new(1920,-540,2000,1200),new(2028,1214),new(14,0,2000,1200)},
        {new(100,100,1014,607),new(107,100,1000,600),new(500,300),new(7,0,493,300)},
        {new(0,0,100,100),new(200,200,100,100),new(50,50),null},
        {new(0,0,100,100),new(0,0,100,100),new(0,100),null},
        {new(0,0,100,100),new(0,0,0,100),new(100,100),null},
        {new(100,100,100,100),new(95,95,50,50),new(100,100),new(0,0,45,45)}
    };
    [Theory][MemberData(nameof(Cases))]
    public void CropMatchesVisibleFrame(PixelRect window,PixelRect extended,PixelSize content,PixelRect? expected) =>
        Assert.Equal(expected,WindowCaptureCrop.Compute(window,extended,content));
}
