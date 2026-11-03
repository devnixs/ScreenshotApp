using Snapper.Core.Recording;
namespace Snapper.Core.Tests;
public sealed class MicrophoneAudioFormatTests
{
    [Theory] [InlineData(1, 1, 12000)] [InlineData(2, 2, 24000)] [InlineData(6, 2, 24000)] [InlineData(0, 2, 24000)] [InlineData(-1, 2, 24000)]
    public void CandidatesSelectSupportedRatesAndChannels(int native, int channels, int bytes) =>
        Assert.Equal(new[] { new AudioFormatChoice(48000, channels, 16, bytes), new AudioFormatChoice(44100, channels, 16, bytes) }, MicrophoneAudioFormat.Candidates(native));
    [Fact] public void DerivedValues() { var format = MicrophoneAudioFormat.Candidates(2)[0]; Assert.Equal(4, format.BlockAlign); Assert.Equal(192000, format.PcmAverageBytesPerSecond); }
}
