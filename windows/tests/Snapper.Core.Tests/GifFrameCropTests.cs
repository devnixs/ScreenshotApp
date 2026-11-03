using Snapper.Core.Geometry;
using Snapper.Core.Recording;
namespace Snapper.Core.Tests;
public sealed class GifFrameCropTests
{
    [Fact] public void ApertureRemovesDecoderPadding() => Assert.Equal(new(0,0,1920,1080),GifFrameCrop.CropFor(1920,1088,new(0,0,1920,1080),1920,1080));
    [Fact] public void MissingOrInvalidApertureUsesExpected()
    {
        foreach(var aperture in new PixelRect?[]{null,new(0,0,1920,1100),new(-1,0,1920,1080),new(0,0,0,0)})
            Assert.Equal(new(0,0,1920,1080),GifFrameCrop.CropFor(1920,1088,aperture,1920,1080));
    }
    [Fact] public void ExpectedClampsAndNeverEmpties()
    { Assert.Equal(new(0,0,320,240),GifFrameCrop.CropFor(320,240,null,640,480)); Assert.Equal(new(0,0,1,1),GifFrameCrop.CropFor(320,240,null,0,-1)); }
    [Fact] public void CropKeepsNonzeroApertureOrigin() => Assert.Equal(new(4,2,100,50),GifFrameCrop.CropFor(320,240,new(4,2,100,50),320,240));
    [Fact] public void SignedStrideAndPaddingCopyToTopDown()
    {
        byte[] topDown=[1,2,3,4,5,6,7,8,99,99,99,99,11,12,13,14,15,16,17,18];
        byte[] bottomUp=[11,12,13,14,15,16,17,18,99,99,99,99,1,2,3,4,5,6,7,8];
        Assert.Equal(new byte[]{5,6,7,8,15,16,17,18},GifFrameRows.Copy(topDown,0,12,new(1,0,1,2)));
        Assert.Equal(GifFrameRows.Copy(topDown,0,12,new(0,0,2,2)),GifFrameRows.Copy(bottomUp,12,-12,new(0,0,2,2)));
    }
    [Fact] public void InvalidBuffersRejected()
    { Assert.Throws<ArgumentException>(()=>GifFrameRows.Copy([0,0],0,8,new(0,0,2,1))); Assert.Throws<ArgumentException>(()=>GifFrameRows.Copy(new byte[16],0,4,new(0,0,2,2))); }
}
