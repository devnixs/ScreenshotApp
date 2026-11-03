using Snapper.Core.Recording;
using Snapper.Core.Settings;
namespace Snapper.Core.Tests;
public sealed class MicrophoneCapturePolicyTests
{
    [Fact]
    public void FullDecisionTable()
    {
        var count = 0;
        foreach (var setting in new[] { false, true })
        foreach (var authorization in Enum.GetValues<MicrophoneAuthorization>())
        foreach (var device in new[] { false, true })
        foreach (var format in Enum.GetValues<RecordingFormat>())
        {
            var result = MicrophoneCapturePolicy.ShouldAttemptMicrophone(new(setting, authorization, device, format));
            Assert.Equal(setting && device && format == RecordingFormat.Mp4 && authorization is MicrophoneAuthorization.Authorized or MicrophoneAuthorization.NotDetermined, result);
            if (result) count++;
        }
        Assert.Equal(2, count);
    }
    [Theory]
    [InlineData(false, MicrophoneAuthorization.Authorized, true, RecordingFormat.Mp4, false)]
    [InlineData(true, MicrophoneAuthorization.Denied, true, RecordingFormat.Mp4, false)]
    [InlineData(true, MicrophoneAuthorization.Restricted, true, RecordingFormat.Mp4, false)]
    [InlineData(true, MicrophoneAuthorization.NotDetermined, true, RecordingFormat.Mp4, true)]
    [InlineData(true, MicrophoneAuthorization.Authorized, false, RecordingFormat.Mp4, false)]
    [InlineData(true, MicrophoneAuthorization.Authorized, true, RecordingFormat.Gif, false)]
    public void NamedEdgeCases(bool setting, MicrophoneAuthorization status, bool device, RecordingFormat format, bool expected) =>
        Assert.Equal(expected, MicrophoneCapturePolicy.ShouldAttemptMicrophone(new(setting, status, device, format)));
}
