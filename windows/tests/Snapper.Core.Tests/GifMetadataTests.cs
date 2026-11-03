using System.Text;
using Snapper.Core.Recording;
using Snapper.Core.Settings;
using Snapper.Core.Support;
namespace Snapper.Core.Tests;
public sealed class GifMetadataTests
{
    [Fact] public void LoopExtensionAndDisposalMatchFormat()
    {
        Assert.Equal(Encoding.ASCII.GetBytes("NETSCAPE2.0"),GifMetadata.NetscapeApplicationId.ToArray());
        Assert.Equal(new byte[]{3,1,0,0,0},GifMetadata.LoopForeverData()); Assert.Equal(1,GifMetadata.DisposalDoNotDispose);
        var bytes=GifMetadata.LoopForeverData(); bytes[0]=0; Assert.Equal(3,GifMetadata.LoopForeverData()[0]);
    }
    [Fact] public void RecordingFormatKindsAndFailureTitle()
    {
        Assert.Equal(CaptureKind.RecordingMp4,RecordingFormat.Mp4.ToCaptureKind()); Assert.Equal(CaptureKind.RecordingGif,RecordingFormat.Gif.ToCaptureKind());
        Assert.Throws<ArgumentOutOfRangeException>(()=>((RecordingFormat)9).ToCaptureKind());
        Assert.Equal(new("x","Couldn't create the GIF"),RecordingFailure.GifConversion("x"));
    }
}
