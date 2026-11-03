using Snapper.Core.Capture;
using Snapper.Core.Geometry;
namespace Snapper.Core.Tests;
public sealed class CapturedImageTests
{
    [Fact] public void ExposesNativePixelsAndScale()
    {
        var bytes=new byte[24];var image=new CapturedImage(3,2,bytes,1.5);
        Assert.Equal(new PixelSize(3,2),image.Size);Assert.Same(bytes,image.Bgra);Assert.Equal(1.5,image.ScaleFactor);
    }
    [Theory] [InlineData(0,2,0)] [InlineData(3,0,0)] [InlineData(3,2,23)] [InlineData(3,2,25)]
    public void InvalidDimensionsOrBufferThrow(int w,int h,int length)=>Assert.Throws<ArgumentException>(()=>new CapturedImage(w,h,new byte[length],1));
}
