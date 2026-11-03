namespace Snapper.Core.Recording;
public readonly record struct AudioFormatChoice(int SampleRate, int Channels, int BitsPerSample, int AacAverageBytesPerSecond)
{
    public int BlockAlign => Channels * BitsPerSample / 8;
    public int PcmAverageBytesPerSecond => SampleRate * BlockAlign;
}
public static class MicrophoneAudioFormat
{
    public static IReadOnlyList<AudioFormatChoice> Candidates(int nativeChannels)
    {
        var channels = nativeChannels == 1 ? 1 : 2;
        return new[] { new AudioFormatChoice(48000, channels, 16, channels == 1 ? 12000 : 24000), new AudioFormatChoice(44100, channels, 16, channels == 1 ? 12000 : 24000) };
    }
}
