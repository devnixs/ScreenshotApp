using Snapper.Core.Geometry;
using Snapper.Core.Recording;
namespace Snapper.Core.Tests;

public sealed class DecodedVideoFormatChangeTests
{
    [Theory, InlineData(false), InlineData(true)] public void InitialDecoderPaddingPreservesVisibleSize(bool hasAperture) =>
        Assert.True(DecodedVideoFormatChange.CanAccept(false, new(800, 600), new(800, 608), hasAperture ? new(0, 0, 800, 600) : null));
    [Fact] public void ExactInitialFormatIsAccepted() => Assert.True(DecodedVideoFormatChange.CanAccept(false, new(800, 600), new(800, 600), null));
    [Fact] public void LaterFormatChangeIsRejected() => Assert.False(DecodedVideoFormatChange.CanAccept(true, new(800, 600), new(800, 608), new(0, 0, 800, 600)));
    [Theory, InlineData(798, 600), InlineData(800, 598)] public void SmallerDecodedFrameIsRejected(int width, int height) =>
        Assert.False(DecodedVideoFormatChange.CanAccept(false, new(800, 600), new(width, height), null));
    [Fact] public void ChangedVisibleApertureIsRejected() => Assert.False(DecodedVideoFormatChange.CanAccept(false, new(800, 600), new(800, 608), new(0, 0, 800, 608)));
    [Fact] public void EmptyFrameIsRejected() => Assert.False(DecodedVideoFormatChange.CanAccept(false, new(800, 600), new(0, 608), null));
    [Fact] public void NonzeroApertureOriginKeepsRecordedSize() => Assert.True(DecodedVideoFormatChange.CanAccept(false, new(800, 600), new(816, 608), new(8, 4, 800, 600)));
}
