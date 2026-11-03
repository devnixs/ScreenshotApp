using Snapper.Core.Capture;
using Snapper.Core.Geometry;
namespace Snapper.Core.Tests;
public sealed class CaptureCropTests
{
    public static TheoryData<PixelRect, PixelRect, PixelSize, PixelRect?> Cases => new()
    {
        { new(100,200,300,150), new(0,0,1920,1080), new(1920,1080), new(100,200,300,150) },
        { new(2000,50,300,150), new(1920,0,2880,1620), new(2880,1620), new(80,50,300,150) },
        { new(-3800,-700,400,200), new(-3840,-720,3840,2160), new(3840,2160), new(40,20,400,200) },
        { new(10,-10,100,10), new(0,-1440,2560,1440), new(2560,1440), new(10,1430,100,10) },
        { new(-3840,-720,3840,2160), new(-3840,-720,3840,2160), new(3840,2160), new(0,0,3840,2160) },
        { new(1800,100,300,200), new(0,0,1920,1080), new(1920,1080), new(1800,100,120,200) },
        { new(2000,100,300,200), new(0,0,1920,1080), new(1920,1080), null },
        { new(100,100,0,20), new(0,0,1920,1080), new(1920,1080), null },
        { new(1200,700,200,100), new(0,0,1920,1080), new(1280,720), new(1200,700,80,20) },
        { new(0,0,1920,1080), new(0,0,1920,1080), new(2560,1440), new(0,0,1920,1080) },
        { new(0,0,1920,1080), new(0,0,1920,1080), new(0,0), null },
        { new(-10,-10,20,20), new(0,0,1920,1080), new(1920,1080), new(0,0,10,10) }
    };
    [Theory, MemberData(nameof(Cases))]
    public void LocalCropUsesPhysicalPixels(PixelRect selection, PixelRect monitor, PixelSize frame, PixelRect? expected) =>
        Assert.Equal(expected, CaptureCrop.MonitorLocal(selection, monitor, frame));
    [Fact] public void CopiesPitchedRowsAndForcesOpaqueAlpha()
    {
        var input = Enumerable.Range(0,96).Select(i => (byte)i).ToArray();
        var output = new byte[60];
        CaptureCrop.CopyRows(input,32,5,3,output);
        for(var y=0;y<3;y++) for(var x=0;x<20;x++)
            Assert.Equal(x%4==3 ? (byte)255 : input[y*32+x],output[y*20+x]);
    }
    [Theory]
    [InlineData(16,96,60,5,3)] [InlineData(32,83,60,5,3)] [InlineData(32,96,59,5,3)]
    [InlineData(32,96,60,0,3)] [InlineData(32,96,60,5,0)]
    public void InvalidBuffersThrow(int pitch,int input,int output,int width,int height) =>
        Assert.Throws<ArgumentException>(()=>CaptureCrop.CopyRows(new byte[input],pitch,width,height,new byte[output]));
}
